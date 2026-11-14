using System.Collections;
using Kumo.Plugin.Protocol;
using Kumo.Plugin.Runtime;
using Photon.Hive.Plugin;

namespace Kumo.Plugin.Services;

/// <summary>
/// Shared objects (event 74): portals, stickers, props and prints share the outer event code,
/// but each object type has its own action and property layout.
/// <para>
/// The sender leaves out what only the server can vouch for. A receiving 2026 client refuses a
/// sticker Create without keys 9, 11 and 128 ("Required Properties: 1,0,2,11,4,5,9,128,129,130"),
/// none of which the sender includes; VRChat's server adds them after reading the spawn token
/// (131). Kumo adds them here, as worked out from the receiving client's own complaints ("the
/// property type map requires type ...", "from a player that doesn't exist"): 9 a boolean (true),
/// 11 the placing actor number, 128 an int (0). With these, stickers show for everyone (2026-10-19).
/// The token is not checked yet.
/// </para>
/// </summary>
public sealed class SharingService
{
    private const byte KeyAction = 1;
    private const byte KeyType = 0;
    private const byte KeyObjectId = 2;
    private const byte KeyPosition = 4;
    private const byte KeyRotation = 5;
    private const byte KeyRequestedDuration = 6;
    /// <summary>A boolean (the client's type map says so); most likely "the server checked the token".</summary>
    private const byte KeyVerified = 9;
    /// <summary>An Int32: the placing player's actor number. With anything else the receiver says "Received a StickerEvent from a player that doesn't exist".</summary>
    private const byte KeyOwnerActor = 11;

    /// <summary>An Int32 (type map); meaning unknown, 0 works.</summary>
    private const byte KeyStickerInt = 128;

    /// <summary>A portal's instance short name (from GET /instances/{location}/shortName); only portals carry it.</summary>
    private const byte KeyPortalShortName = 132;
    private const byte KeyPortalMarker = 128;
    private const byte KeyPortalStatic = 129;
    private const byte KeyPortalByte130 = 130;
    private const byte KeyPortalDuration = 131;
    private const byte KeyPortalSpawnId = 135;
    private const byte PropType = 11;
    private const byte KeyPropServerField = 133;
    private const byte CreateStaticPortal = 129;
    private const byte Destroy = 1;

    private readonly IPluginHost _host;
    private readonly IPluginLogger _log;
    private readonly Dictionary<string, object> _portalTimers = new(StringComparer.Ordinal);
    private readonly object _portalTimersLock = new();
    private bool _stopped;

    public SharingService(IPluginHost host, IPluginLogger log)
    {
        _host = host;
        _log = log;
    }

    public void Prepare(IRaiseEventCallInfo info)
    {
        if (info.Request.Data is not IDictionary data) return;
        if (IsAction(data[KeyType], 0) && IsAction(data[KeyAction], Destroy))
        {
            if (data[KeyPortalMarker] is string { Length: > 0 } destroyedId)
                CancelPortalTimer(destroyedId);
            return;
        }
        var staticPortalRequest = IsAction(data[KeyAction], CreateStaticPortal) && data.Contains(KeyPortalShortName);
        if (!staticPortalRequest && !IsAction(data[KeyAction], 0)) return;

        if (staticPortalRequest)
        {
            // The client sends action 129 with only the marker and destination strings. Its
            // receive handler recognizes actions 0..3; action 0 with flag 129=1 selects the
            // registered static marker instead of decoding coordinates (2026.4 client RVAs
            // 0x178BFC0, 0x160B520, 0x160BAB0).
            if (data[KeyPortalMarker] is not string { Length: > 0 } marker) return;
            data[KeyAction] = (byte)0;
            data[KeyPortalStatic] = (byte)1;
            if (!data.Contains(KeyPosition)) data[KeyPosition] = new byte[12];
            if (!data.Contains(KeyRotation)) data[KeyRotation] = new byte[12];
            if (!data.Contains(KeyPortalByte130)) data[KeyPortalByte130] = (byte)0;
            if (!data.Contains(KeyPortalDuration)) data[KeyPortalDuration] = GetPortalDuration(data);
            if (!data.Contains(KeyPortalSpawnId)) data[KeyPortalSpawnId] = marker;
        }

        if (data[KeyObjectId] is null or "") data[KeyObjectId] = Guid.NewGuid().ToString();
        if (!data.Contains(KeyOwnerActor)) data[KeyOwnerActor] = info.ActorNr;
        if (IsAction(data[KeyType], 0) && data.Contains(KeyPortalShortName))
        {
            // A dynamic Create supplies coordinates but omits key 128. The receiver requires
            // a nonempty string for its portal identity before it can create the object.
            // Static portal requests already supply their registered marker string there.
            if (!data.Contains(KeyPortalMarker) || data[KeyPortalMarker] is not string { Length: > 0 })
                data[KeyPortalMarker] = Guid.NewGuid().ToString("N");
            if (!data.Contains(KeyPortalStatic)) data[KeyPortalStatic] = (byte)0;
            if (!data.Contains(KeyPortalByte130)) data[KeyPortalByte130] = (byte)0;
            var duration = data[KeyPortalDuration] is byte { } suppliedDuration && suppliedDuration > 0
                ? suppliedDuration
                : GetPortalDuration(data);
            data[KeyPortalDuration] = duration;
            if (data[KeyPortalMarker] is string { Length: > 0 } portalId)
                SchedulePortalExpiry(portalId, duration, info.ActorNr);
        }
        else
        {
            if (!data.Contains(KeyVerified)) data[KeyVerified] = true;
            if (IsAction(data[KeyType], PropType))
            {
                // The prop receiver requires two server-supplied fields in addition to the
                // inventory id (132): "Required Properties: 1,0,2,11,4,5,9,128,130,129,131,132,133".
                // Neither is in its key->type map and no prop handler reads them (2026.4 client:
                // odd5ab's type map, .cctor 0x12E44B0, has 128-130, 132 and 134-136 only), so they only
                // need to be present. Keep the prop's own id in 128 intact.
                if (!data.Contains(KeyPropServerField)) data[KeyPropServerField] = "";
                if (!data.Contains(KeyPortalDuration)) data[KeyPortalDuration] = (byte)0;
            }
            else if (!data.Contains(KeyStickerInt)) data[KeyStickerInt] = 0;
        }

        // The placer gets the server's version back too, as the confirmation: a portal stays at
        // "Loading" until it arrives and removes itself after about three seconds without it
        // (2026-11-12). Relayed to others only, as Photon does by default, it never came.
        info.Request.ReceiverGroup = ReciverGroup.All;
        info.Request.Actors = null!;

        _log.Info($"Kumo: shared object create from actor {info.ActorNr}, sent to all: " +
                  string.Join(", ", data.Keys.Cast<object>().Select(k => $"{k}={data[k]?.GetType().Name}")));
    }

    public void Stop()
    {
        lock (_portalTimersLock)
        {
            _stopped = true;
            foreach (var timer in _portalTimers.Values)
                _host.StopTimer(timer);
            _portalTimers.Clear();
        }
    }

    private void CancelPortalTimer(string portalId)
    {
        lock (_portalTimersLock)
        {
            if (_portalTimers.TryGetValue(portalId, out var timer))
            {
                _portalTimers.Remove(portalId);
                _host.StopTimer(timer);
            }
        }
    }

    private void SchedulePortalExpiry(string portalId, byte durationSeconds, int ownerActor)
    {
        lock (_portalTimersLock)
        {
            if (_stopped) return;
            if (_portalTimers.TryGetValue(portalId, out var oldTimer))
            {
                _portalTimers.Remove(portalId);
                _host.StopTimer(oldTimer);
            }

            object? timer = null;
            timer = _host.CreateOneTimeTimer(() =>
            {
                lock (_portalTimersLock)
                {
                    if (_stopped || !_portalTimers.TryGetValue(portalId, out var current) ||
                        !ReferenceEquals(current, timer)) return;
                    _portalTimers.Remove(portalId);
                    _host.StopTimer(current);
                }

                // The client's countdown only updates its text. It waits for this event 74
                // Destroy action to remove the portal identified by key 128.
                _host.BroadcastEvent(
                    ReciverGroup.All, ownerActor, 0, EventCodes.ItemSpawn,
                    PhotonPayload.Wrap(ownerActor, new Dictionary<byte, object>
                    {
                        [KeyType] = (byte)0,
                        [KeyAction] = Destroy,
                        [KeyPortalMarker] = portalId,
                    }),
                    CacheOperations.DoNotCache);
                _log.Info($"Kumo: portal expired after {durationSeconds}s; sent Destroy to all.");
            }, durationSeconds * 1000);
            _portalTimers[portalId] = timer;
        }
    }

    private static bool IsAction(object? action, byte expected) => action switch
    {
        byte b => b == expected,
        int i => i == expected,
        short s => s == expected,
        _ => false,
    };

    private static byte GetPortalDuration(IDictionary data)
    {
        var requested = data.Contains(KeyRequestedDuration) ? data[KeyRequestedDuration] : null;
        var seconds = requested switch
        {
            byte b => b,
            short s => s,
            int i => i,
            _ => 30,
        };
        return (byte)Math.Min(255, Math.Max(1, seconds));
    }
}
