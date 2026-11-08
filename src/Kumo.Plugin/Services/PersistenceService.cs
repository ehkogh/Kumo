using System.Collections;
using Kumo.Plugin.Protocol;
using Kumo.Plugin.Runtime;
using Newtonsoft.Json;
using Photon.Hive.Plugin;

namespace Kumo.Plugin.Services;

/// <summary>
/// Player persistence (PlayerData, PlayerObjects): the part VRChat's room plugin does in
/// <c>VRC.Core.Networking.Codec.StateManagement.PersistenceObjectStateManager</c>.
/// </summary>
/// <remarks>
/// <para>
/// A player's persistent state is ordinary network sync: Udon variable bunches (events 10 and
/// 11) on the player's persistence objects, views <c>actor × MaximumViewIDs + id</c> with
/// <c>id</c> in [FirstPlayerPersistenceID, MaxPlayerPersistenceID) (100..10000 in practice).
/// PlayerData itself is one such object, "/Player Data Object".
/// </para>
/// <para>
/// Join: the client raises <see cref="EventCodes.WorldMetadata"/> with its persistence objects
/// (path, component info). The server answers with the stored bunches in
/// <see cref="EventCodes.SerializationRecovery"/>, view ids rewritten to the player's current
/// actor, then <see cref="EventCodes.PlayerRestored"/>; only then does Udon get
/// <c>_onPlayerRestored</c>, which worlds wait for. A late joiner is also sent the other players'
/// current persistence objects. Everything the server sends here goes out as actor 0: the client
/// drops these events from anyone else.
/// </para>
/// <para>
/// While the player is in the room, the latest complete bunch set per object is kept and written
/// to <see cref="IPersistenceStore"/> (debounced, and on leave and room close).
/// </para>
/// </remarks>
public sealed class PersistenceService
{
    private static readonly TimeSpan SaveDelay = TimeSpan.FromSeconds(5);

    private readonly IPluginHost _host;
    private readonly IPluginLogger _log;
    private readonly RoomState _state;
    private readonly IPersistenceStore _store;
    private readonly Dictionary<int, PlayerSession> _sessions = new();
    private int _recoveryId;
    private object? _timer;

    public PersistenceService(RoomState state, IPluginHost host, IPluginLogger log, IPersistenceStore store)
    {
        _state = state;
        _host = host;
        _log = log;
        _store = store;
    }

    public void Start() => _timer ??= _host.CreateTimer(SaveDirty, 1000, 1000);

    /// <summary>Event 25: restore the player and announce it.</summary>
    public void OnWorldMetadata(int actor, object? data)
    {
        if (!_state.Actors.TryGetValue(actor, out var actorState)) return;

        WorldMetadata metadata;
        try
        {
            metadata = data is object[] { Length: >= 3 } parts
                ? parts[2] switch
                {
                    byte[] bytes => WorldMetadata.FromEvent25(bytes),
                    string encoded => WorldMetadata.FromEvent25(encoded),
                    var other => throw new InvalidDataException($"unexpected element {other?.GetType().Name}"),
                }
                : throw new InvalidDataException($"unexpected payload {data?.GetType().Name}");
        }
        catch (Exception ex)
        {
            _log.Warn($"Kumo: unreadable world metadata from actor {actor}: {ex.Message}; restoring without data");
            Broadcast(_state.Actors.Keys.ToList(), EventCodes.PlayerRestored, actor);
            return;
        }

        var session = new PlayerSession(actor, actorState.UserId, metadata);
        var stored = Load(actorState.UserId);
        var restored = session.Restore(stored);

        PlayerSession[] others;
        lock (_sessions)
        {
            others = _sessions.Values.Where(s => s.Actor != actor && _state.Actors.ContainsKey(s.Actor)).ToArray();
            _sessions[actor] = session;
        }

        var everyone = _state.Actors.Keys.ToList();
        if (restored.Count > 0) SendRecovery(everyone, restored);
        Broadcast(everyone, EventCodes.PlayerRestored, actor);

        // The late joiner has never seen the others' persistent state.
        var current = others.SelectMany(o => o.Current()).ToList();
        if (current.Count > 0) SendRecovery(new List<int> { actor }, current);
        foreach (var other in others)
            Broadcast(new List<int> { actor }, EventCodes.PlayerRestored, other.Actor);

        _log.Info($"Kumo: restored actor {actor} ({actorState.UserId}) with {restored.Count} stored object(s) " +
                  $"of {metadata.PlayerPersistence.Count}; sent it {current.Count} object(s) of {others.Length} earlier player(s)");
    }

    /// <summary>Events 10 and 11: keep the latest state of the sender's persistence objects.</summary>
    public void OnBunch(int actor, byte code, object? data)
    {
        if (data is not byte[] bytes || bytes.Length < 8) return;
        PlayerSession? session;
        lock (_sessions) _sessions.TryGetValue(actor, out session);
        session?.Record(code, bytes);
    }

    public void OnLeave(int actor)
    {
        PlayerSession? session;
        lock (_sessions)
        {
            if (!_sessions.TryGetValue(actor, out session)) return;
            _sessions.Remove(actor);
        }
        Save(session);
    }

    public void OnClose()
    {
        if (_timer is not null) _host.StopTimer(_timer);
        _timer = null;
        PlayerSession[] all;
        lock (_sessions) all = _sessions.Values.ToArray();
        foreach (var session in all) Save(session);
    }

    private void SaveDirty()
    {
        PlayerSession[] due;
        lock (_sessions) due = _sessions.Values.Where(s => s.DirtySince is { } t && DateTime.UtcNow - t >= SaveDelay).ToArray();
        foreach (var session in due) Save(session);
    }

    private string WorldId => _state.GameId.Split(':')[0];

    private StoredPlayer? Load(string userId)
    {
        try { return _store.Load(WorldId, userId); }
        catch (Exception ex)
        {
            _log.Warn($"Kumo: could not load persistence for {userId} in {WorldId}: {ex.Message}");
            return null;
        }
    }

    private void Save(PlayerSession session)
    {
        var snapshot = session.TakeSnapshot();
        if (snapshot is null) return;
        try { _store.Save(WorldId, session.UserId, snapshot); }
        catch (Exception ex) { _log.Warn($"Kumo: could not save persistence for {session.UserId} in {WorldId}: {ex.Message}"); }
    }

    // Event 24: [id, part count, part index, gzip(WorldMetadata)]; one part.
    private void SendRecovery(List<int> targets, List<ObjectMetadataPair> objects)
    {
        var payload = new WorldMetadata { Objects = objects }.ToEvent24();
        var id = Interlocked.Increment(ref _recoveryId);
        Broadcast(targets, EventCodes.SerializationRecovery, new object[] { id, 1, 0, payload });
    }

    private void Broadcast(List<int> targets, byte code, object data) =>
        _host.BroadcastEvent(targets, 0, code, PhotonPayload.Wrap(0, data), 0);

    /// <summary>One player's persistence while in the room.</summary>
    private sealed class PlayerSession
    {
        private readonly WorldMetadata _template;
        private readonly Dictionary<(int View, byte Code), StoredObject> _objects = new();
        private readonly Dictionary<(int View, byte Code), List<ObjectBunch>> _pending = new();
        private readonly object _gate = new();

        public PlayerSession(int actor, string userId, WorldMetadata template)
        {
            Actor = actor;
            UserId = userId;
            _template = template;
        }

        public int Actor { get; }
        public string UserId { get; }
        public DateTime? DirtySince { get; private set; }

        private int Stride => _template.MaximumViewIds > 0 ? _template.MaximumViewIds : 100000;

        private ObjectMetadata? TemplateFor(int relativeView) =>
            _template.PlayerPersistence.FirstOrDefault(p => p.ViewId == relativeView)?.Metadata;

        /// <summary>Takes stored objects that still match this world's persistence objects.</summary>
        public List<ObjectMetadataPair> Restore(StoredPlayer? stored)
        {
            if (stored is null) return new List<ObjectMetadataPair>();
            lock (_gate)
            {
                foreach (var o in stored.Objects)
                {
                    var template = TemplateFor(o.View);
                    if (template is null || template.DoNotPersist) continue;
                    if (o.Path is not null && template.InitialPath is not null && o.Path != template.InitialPath) continue;
                    _objects[(o.View, o.Code)] = o;
                }
                return CurrentLocked();
            }
        }

        /// <summary>The objects as they are now, view ids and senders for the current actor.</summary>
        public List<ObjectMetadataPair> Current()
        {
            lock (_gate) return CurrentLocked();
        }

        private List<ObjectMetadataPair> CurrentLocked()
        {
            var result = new List<ObjectMetadataPair>();
            foreach (var o in _objects.Values)
            {
                var template = TemplateFor(o.View);
                if (template is null) continue;
                var view = Actor * Stride + o.View;
                var bunches = o.Bunches.Select(b => new ObjectBunch
                {
                    Time = b.Time,
                    Sender = Actor,
                    Index = b.Index,
                    Data = WithView(Convert.FromBase64String(b.Data), view),
                }).ToList();
                result.Add(new ObjectMetadataPair { ViewId = view, Metadata = template.CloneWithPayload(o.Code, o.Is8Bit, bunches) });
            }
            return result;
        }

        // 32-bit bunches: [view i32][time i32][index u8][count u8][data]; 8-bit ones are single.
        public void Record(byte code, byte[] data)
        {
            var view = BitConverter.ToInt32(data, 0);
            if (view / Stride != Actor) return;
            var relative = view % Stride;
            if (relative < _template.FirstPlayerPersistenceId || relative >= _template.MaxPlayerPersistenceId) return;
            var template = TemplateFor(relative);
            if (template is null || template.DoNotPersist) return;

            var is8Bit = code == EventCodes.UdonSyncControl;
            var index = is8Bit || data.Length < 10 ? (byte)0 : data[8];
            var count = is8Bit || data.Length < 10 ? (byte)1 : Math.Max((byte)1, data[9]);
            var bunch = new ObjectBunch { Time = BitConverter.ToInt32(data, 4), Sender = Actor, Index = index, Data = data };

            lock (_gate)
            {
                var key = (relative, code);
                if (index == 0) _pending[key] = new List<ObjectBunch>();
                if (!_pending.TryGetValue(key, out var parts)) return;
                parts.Add(bunch);
                if (parts.Count < count) return;
                _pending.Remove(key);

                _objects[key] = new StoredObject
                {
                    View = relative,
                    Code = code,
                    Is8Bit = is8Bit,
                    Path = template.InitialPath,
                    Bunches = parts.Select(p => new StoredBunch { Time = p.Time, Index = p.Index, Data = Convert.ToBase64String(p.Data) }).ToList(),
                };
                DirtySince ??= DateTime.UtcNow;
            }
        }

        public StoredPlayer? TakeSnapshot()
        {
            lock (_gate)
            {
                if (DirtySince is null) return null;
                DirtySince = null;
                return new StoredPlayer { Objects = _objects.Values.ToList() };
            }
        }

        private static byte[] WithView(byte[] data, int view)
        {
            var copy = (byte[])data.Clone();
            BitConverter.GetBytes(view).CopyTo(copy, 0);
            return copy;
        }
    }
}

/// <summary>A player's stored persistence in one world.</summary>
public sealed class StoredPlayer
{
    public List<StoredObject> Objects { get; set; } = new();
}

public sealed class StoredObject
{
    /// <summary>The view id relative to the player (100 for "/Player Data Object").</summary>
    public int View { get; set; }
    public byte Code { get; set; }
    public bool Is8Bit { get; set; }
    public string? Path { get; set; }
    public List<StoredBunch> Bunches { get; set; } = new();
}

public sealed class StoredBunch
{
    public int Time { get; set; }
    public byte Index { get; set; }
    /// <summary>The bunch as the client sent it, header included (base64).</summary>
    public string Data { get; set; } = string.Empty;
}

public interface IPersistenceStore
{
    StoredPlayer? Load(string worldId, string userId);
    void Save(string worldId, string userId, StoredPlayer player);
}

/// <summary>One JSON file per world and user under a directory.</summary>
public sealed class FilePersistenceStore : IPersistenceStore
{
    private readonly string _root;

    public FilePersistenceStore(string root) => _root = root;

    public StoredPlayer? Load(string worldId, string userId)
    {
        var path = PathFor(worldId, userId);
        return File.Exists(path) ? JsonConvert.DeserializeObject<StoredPlayer>(File.ReadAllText(path)) : null;
    }

    public void Save(string worldId, string userId, StoredPlayer player)
    {
        var path = PathFor(worldId, userId);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonConvert.SerializeObject(player, Formatting.Indented));
        if (File.Exists(path)) File.Delete(path);
        File.Move(temp, path);
    }

    private string PathFor(string worldId, string userId) =>
        Path.Combine(_root, Safe(worldId), Safe(userId) + ".json");

    private static string Safe(string s)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(s.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
    }
}
