using System.Collections.Concurrent;
using Kumo.Plugin.Protocol;
using Kumo.Plugin.Runtime;
using Photon.Hive.Plugin;

namespace Kumo.Plugin.Services;

/// <summary>
/// Networked object ownership, which the server arbitrates (the state manager's
/// <c>SetOwner</c>).
/// </summary>
/// <remarks>
/// A client takes an object by raising <see cref="EventCodes.OwnershipTransfer"/>
/// <c>[viewId, newOwner]</c>. Relayed as is, the other clients drop it (the code is server-only
/// for them), every client keeps believing it owns the object, all of them claim and serialize
/// it in a loop, and nobody accepts anybody's sync (no <c>_onDeserialization</c>). The server
/// records the owner and announces it as actor 0.
/// </remarks>
public sealed class OwnershipService
{
    private readonly IPluginHost _host;
    private readonly IPluginLogger _log;
    private readonly RoomState _state;
    private readonly ConcurrentDictionary<int, int> _owners = new();
    private int _logged;

    public OwnershipService(RoomState state, IPluginHost host, IPluginLogger log)
    {
        _state = state;
        _host = host;
        _log = log;
    }

    public IReadOnlyDictionary<int, int> Owners => _owners;

    public void Handle(IRaiseEventCallInfo info)
    {
        info.Cancel();
        if (!TryRead(info.Request.Data, out var view, out var owner))
        {
            _log.Warn($"Kumo: malformed ownership request from actor {info.ActorNr}: {info.Request.Data?.GetType().FullName}");
            return;
        }

        // A player may take an object for itself; giving it to someone else needs that someone to
        // be in the room.
        if (owner != info.ActorNr && !_state.Actors.ContainsKey(owner)) return;

        _owners[view] = owner;
        Announce(_state.Actors.Keys.ToList(), view, owner);
        if (Interlocked.Increment(ref _logged) <= 20)
            _log.Info($"Kumo: view {view} now owned by actor {owner} (asked by {info.ActorNr})");
    }

    /// <summary>Objects owned by someone who left go back to the master.</summary>
    public void OnLeave(int actor, int master)
    {
        foreach (var pair in _owners.Where(p => p.Value == actor).ToArray())
        {
            _owners[pair.Key] = master;
            Announce(_state.Actors.Keys.Where(a => a != actor).ToList(), pair.Key, master);
        }
    }

    /// <summary>Tells a joiner who owns what.</summary>
    public void SendAll(int actor)
    {
        foreach (var pair in _owners)
            Announce(new List<int> { actor }, pair.Key, pair.Value);
    }

    // [viewId, owner] arrives as an int[] or an object[] of integers, depending on the client.
    private static bool TryRead(object? data, out int view, out int owner)
    {
        view = owner = 0;
        var items = data switch
        {
            int[] ints => ints.Cast<object>().ToArray(),
            object[] objects => objects,
            System.Collections.IList list => list.Cast<object>().ToArray(),
            _ => null,
        };
        if (items is not { Length: >= 2 }) return false;
        try
        {
            view = Convert.ToInt32(items[0]);
            owner = Convert.ToInt32(items[1]);
            return true;
        }
        catch (Exception) { return false; }
    }

    private void Announce(List<int> targets, int view, int owner) =>
        _host.BroadcastEvent(targets, 0, EventCodes.OwnershipTransfer, PhotonPayload.Wrap(0, new[] { view, owner }), 0);
}
