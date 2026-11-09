using System.Collections;
using System.Collections.Concurrent;
using Kumo.Plugin.Protocol;
using Photon.Hive.Plugin;
using Newtonsoft.Json;

namespace Kumo.Plugin.Services;

/// <summary>
/// Decides what happens to every event a client raises: relay it, act on it, or drop it.
/// </summary>
/// <remarks>
/// Only codes in <see cref="EventCodes"/> pass. In discovery mode (<c>DiscoveryMode="true"</c>),
/// unknown codes inside the client's own range are relayed too, and every unmapped code has its
/// first payloads logged, so a session keeps working while the 2026 table is mapped. Discovery
/// mode is temporary; once the table is complete it goes.
/// </remarks>
public sealed class EventRouter
{
    /// <summary>How many payloads of each unmapped code to log per room.</summary>
    private const int SamplesPerCode = 5;

    private readonly IPluginHost _host;
    private readonly IPluginLogger _log;
    private readonly SharingService _sharing;
    private readonly PropertyService _properties;
    private readonly ProfileRefreshService _profiles;
    private readonly ModerationService _moderation;
    private readonly InterestService _interest;
    private readonly InteractionPermissionService _interactions;
    private readonly InstantiationService _instantiation;
    private readonly RateLimitService _rateLimits;
    private readonly PersistenceService _persistence;
    private readonly OwnershipService _ownership;
    private readonly ContentTokenService _tokens;
    private readonly bool _discovery;

    /// <summary>Diagnostics: see <see cref="Configuration.KumoOptions.TraceViews"/>.</summary>
    public int[] TraceViews { get; set; } = Array.Empty<int>();
    private readonly ConcurrentDictionary<byte, int> _seen = new();

    // Discovery mode: events and bytes per code, logged every TrafficWindow. Telling talking from
    // silence this way is how a feature riding an existing code (voice) gets found.
    private static readonly TimeSpan TrafficWindow = TimeSpan.FromSeconds(10);
    private readonly ConcurrentDictionary<byte, (int Events, long Bytes)> _traffic = new();
    private DateTime _trafficSince = DateTime.UtcNow;

    /// <summary>A player's world has loaded (event 25): what is sent then (purchases).</summary>
    public Action<int>? WorldLoaded { get; set; }

    public EventRouter(
        IPluginHost host,
        IPluginLogger log,
        PropertyService properties,
        ProfileRefreshService profiles,
        ModerationService moderation,
        InterestService interest,
        InteractionPermissionService interactions,
        InstantiationService instantiation,
        RateLimitService rateLimits,
        SharingService sharing,
        PersistenceService persistence,
        OwnershipService ownership,
        bool discovery)
    {
        _persistence = persistence;
        _ownership = ownership;
        _tokens = new ContentTokenService(host, log);
        _host = host;
        _log = log;
        _properties = properties;
        _profiles = profiles;
        _moderation = moderation;
        _interest = interest;
        _interactions = interactions;
        _instantiation = instantiation;
        _rateLimits = rateLimits;
        _sharing = sharing;
        _discovery = discovery;
    }

    public void Handle(IRaiseEventCallInfo info)
    {
        info.Request.Cache = CacheOperations.DoNotCache;
        var code = info.Request.EvCode;

        // Discovery mode logs the first payloads of every code, known ones too: a feature that
        // does not work may be riding on a code that is relayed fine but carries something new.
        if (_discovery && IsMapped(code)) Sample(code, info, "relayed (mapped)");
        if (_discovery) CountTraffic(code, info);

        switch (code)
        {
            // Server-only: a client raising one is impersonating the server.
            case 0:
            case EventCodes.ExecutiveMessage:
            case EventCodes.RateLimitTick:
            case EventCodes.SerializationRecovery:
            case EventCodes.PlayerFlag26:
            case EventCodes.PlayerRestored:
                Sample(code, info, "refused (server event)");
                info.Fail("Unauthorized.");
                return;

            case EventCodes.Movement:
            case EventCodes.Sync:
            case EventCodes.Unknown23:
            case EventCodes.AvatarParameters:
            case EventCodes.Voice:
            case EventCodes.PastEvents:
            case EventCodes.SyncEvents:
            case EventCodes.InitialSyncFinished:
            case EventCodes.ProcessEvent:
            case EventCodes.Udon:
            case EventCodes.AdditionalSync:
            case EventCodes.Emoji:
            case EventCodes.Chatbox:
            case EventCodes.ChatboxTyping:
                info.Continue();
                return;

            case EventCodes.WorldMetadata:
                // For the server's state manager; clients have no handler for it.
                info.Cancel();
                _persistence.OnWorldMetadata(info.ActorNr, info.Request.Data);
                WorldLoaded?.Invoke(info.ActorNr);
                return;

            case EventCodes.UdonSyncControl:
            case EventCodes.UdonVariableSync:
                if (TraceViews.Length > 0 && info.Request.Data is byte[] { Length: >= 4 } traced && TraceViews.Contains(BitConverter.ToInt32(traced, 0)))
                    _log.Info($"Kumo: trace ev {code} from {info.ActorNr}: {Convert.ToBase64String(traced)}");
                _persistence.OnBunch(info.ActorNr, code, info.Request.Data);
                info.Continue();
                return;

            case EventCodes.OwnershipTransfer:
                _ownership.Handle(info);
                return;

            case EventCodes.ItemSpawn:
                _sharing.Prepare(info);
                info.Continue();
                return;

            case EventCodes.UserUpdate:
                // An avatar switch (or another profile change) the API already has.
                info.Cancel();
                _profiles.Refresh(info.ActorNr);
                return;

            case EventCodes.AvatarFile:
                // A content token request, for the server only (ContentTokenService).
                info.Cancel();
                _profiles.Refresh(info.ActorNr);
                _tokens.Answer(info.ActorNr, info.Request.Data);
                return;

            case EventCodes.RateLimits:
                // The client asking for the table: answer it, relay nothing.
                _rateLimits.SendConfiguration(info.ActorNr);
                info.Cancel();
                return;

            case EventCodes.Interest:
                _interest.Handle(info);
                return;

            case EventCodes.ExecutiveAction:
                _moderation.Handle(info);
                return;

            case EventCodes.Properties:
                HandleProperties(info);
                return;

            case EventCodes.InteractionPermissions:
                if (info.Request.Parameters.TryGetValue(245, out var permissionData))
                    _interactions.Update(info.ActorNr, permissionData);
                info.Cancel();
                return;

            case EventCodes.Instantiate:
                _instantiation.Handle(info);
                return;
        }

        if (code >= 200)
        {
            info.Continue();
            return;
        }

        if (_discovery && code <= EventCodes.DiscoveryRange)
        {
            Sample(code, info, "relayed (discovery mode)");
            info.Continue();
            return;
        }

        Sample(code, info, "dropped");
        info.Cancel();
    }

    private void HandleProperties(IRaiseEventCallInfo info)
    {
        if (info.Request.Data is not Hashtable properties)
        {
            info.Fail("Invalid properties.");
            return;
        }

        if (!_properties.TryPrepare(info.ActorNr, properties, out var prepared, out var error))
        {
            info.Fail(error);
            return;
        }

        _host.SetProperties(info.ActorNr, prepared, null, true);
        _properties.Broadcast(info.ActorNr, prepared);
        info.Cancel();
    }

    private static bool IsMapped(byte code) => code switch
    {
        EventCodes.Movement or EventCodes.Sync or EventCodes.Unknown23 or EventCodes.AvatarParameters or EventCodes.Voice
            or EventCodes.UdonSyncControl or EventCodes.UdonVariableSync
            or EventCodes.PastEvents or EventCodes.SyncEvents or EventCodes.InitialSyncFinished or EventCodes.ProcessEvent
            or EventCodes.Udon or EventCodes.AdditionalSync or EventCodes.ItemSpawn or EventCodes.Emoji or EventCodes.Chatbox
            or EventCodes.ChatboxTyping or EventCodes.AvatarFile or EventCodes.WorldMetadata or EventCodes.OwnershipTransfer
            or EventCodes.RateLimits or EventCodes.Interest or EventCodes.ExecutiveAction or EventCodes.Properties
            or EventCodes.InteractionPermissions or EventCodes.Instantiate or EventCodes.UserUpdate => true,
        _ => code >= 200,
    };

    private void CountTraffic(byte code, IRaiseEventCallInfo info)
    {
        var size = info.Request.Data is byte[] bytes ? bytes.Length : 0;
        _traffic.AddOrUpdate(code, (1, size), (_, t) => (t.Events + 1, t.Bytes + size));

        var now = DateTime.UtcNow;
        if (now - _trafficSince < TrafficWindow) return;
        _trafficSince = now;
        var summary = string.Join(", ", _traffic.OrderBy(t => t.Key)
            .Select(t => $"{t.Key}: {t.Value.Events}ev/{t.Value.Bytes}B"));
        _traffic.Clear();
        _log.Info($"Kumo: traffic last {TrafficWindow.TotalSeconds:0}s (byte[] payloads only count bytes): {summary}");
    }

    /// <summary>Logs the first few payloads of each code, then only counts them.</summary>
    private void Sample(byte code, IRaiseEventCallInfo info, string outcome)
    {
        var count = _seen.AddOrUpdate(code, 1, (_, n) => n + 1);
        if (count > SamplesPerCode)
            return;

        var payload = info.Request.Data is byte[] bytes
            ? Convert.ToBase64String(bytes, 0, Math.Min(bytes.Length, 512)) + (bytes.Length > 512 ? $"… ({bytes.Length} bytes)" : "")
            : JsonConvert.SerializeObject(info.Request.Data);
        _log.Info($"Kumo: event {code} from actor {info.ActorNr} {outcome} [{count}/{SamplesPerCode}]: {payload}");
    }
}
