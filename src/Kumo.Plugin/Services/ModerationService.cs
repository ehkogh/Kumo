using Kumo.Plugin.Models;
using Kumo.Plugin.Protocol;
using Kumo.Plugin.Runtime;
using Photon.Hive.Plugin;

namespace Kumo.Plugin.Services;

public sealed class ModerationService
{
    private readonly RoomState _state;
    private readonly IPluginHost _host;
    private readonly IPluginLogger _log;

    public ModerationService(RoomState state, IPluginHost host, IPluginLogger log)
    {
        _state = state;
        _host = host;
        _log = log;
    }

    public bool Handle(IRaiseEventCallInfo info)
    {
        if (!_state.Actors.TryGetValue(info.ActorNr, out var source) ||
            !TryGetEventData(info, out var data) ||
            !TryGetByte(data, ExecutiveActionFields.Type, out var type))
        {
            info.Fail("Invalid moderation payload.");
            return false;
        }

        switch (type)
        {
            case ExecutiveActionTypes.RequestPlayerModerations:
                HandleModerationSnapshot(info.ActorNr, data);
                break;

            case ExecutiveActionTypes.Kick:
                if (!CanModerate(source))
                    return Fail(info, "Not allowed to kick.");
                if (!TryFindTarget(data, out var kickTarget))
                    return Fail(info, "Could not find target user.");
                SendExecutiveMessage(kickTarget.ActorNumber, GetString(data, ExecutiveActionFields.MainProperty));
                break;

            case ExecutiveActionTypes.Warn:
                if (!CanModerate(source))
                    return Fail(info, "Not allowed to warn.");
                if (!TryFindTarget(data, out var warnTarget))
                    return Fail(info, "Could not find target user.");
                SendWarn(
                    warnTarget.ActorNumber,
                    GetString(data, ExecutiveActionFields.Heading),
                    GetString(data, ExecutiveActionFields.Message));
                break;

            case ExecutiveActionTypes.MicOff:
                if (!CanModerate(source))
                    return Fail(info, "Not allowed to disable another player's microphone.");
                if (!TryFindTarget(data, out var micTarget))
                    return Fail(info, "Could not find target user.");
                SendMicOff(micTarget.ActorNumber);
                break;

            case ExecutiveActionTypes.MuteUser:
                if (!TryFindTarget(data, out var muteTarget) ||
                    !TryGetBoolean(data, ExecutiveActionFields.MainProperty, out var muted))
                    return Fail(info, "Invalid mute request.");
                UpdateModeration(source, muteTarget, muted: muted, blocked: null);
                SendModerationPair(source.ActorNumber, muteTarget.ActorNumber);
                break;

            case ExecutiveActionTypes.BlockUser:
                if (!TryFindTarget(data, out var blockTarget) ||
                    !TryGetBoolean(data, ExecutiveActionFields.MainProperty, out var blocked))
                    return Fail(info, "Invalid block request.");
                UpdateModeration(source, blockTarget, muted: null, blocked: blocked);
                SendModerationPair(source.ActorNumber, blockTarget.ActorNumber);
                break;

            default:
                _log.Warn($"Kumo: unsupported executive action {type} from actor {info.ActorNr}.");
                return Fail(info, "Unknown executive action type.");
        }

        info.Cancel();
        return true;
    }

    public void RequestExistingForActor(int actorNumber)
    {
        var users = _state.Actors.Values
            .Where(actor => actor.ActorNumber != actorNumber)
            .Select(actor => ShortUserId(actor.UserId))
            .ToArray();

        var data = PhotonPayload.Wrap(0, new Dictionary<byte, object>
        {
            [ExecutiveActionFields.Type] = ExecutiveActionTypes.RequestPlayerModerations,
            [ExecutiveActionFields.MainProperty] = users,
        });

        _host.BroadcastEvent(new List<int> { actorNumber }, 0, EventCodes.ExecutiveAction, data, 0);
    }

    public void RequestNewActorFromOthers(int actorNumber)
    {
        if (!_state.Actors.TryGetValue(actorNumber, out var joined))
            return;

        var shortId = ShortUserId(joined.UserId);
        foreach (var actor in _state.Actors.Values.Where(actor => actor.ActorNumber != actorNumber))
        {
            var data = PhotonPayload.Wrap(0, new Dictionary<byte, object>
            {
                [ExecutiveActionFields.Type] = ExecutiveActionTypes.RequestPlayerModerations,
                [ExecutiveActionFields.MainProperty] = new[] { shortId },
            });

            _host.BroadcastEvent(new List<int> { actor.ActorNumber }, 0, EventCodes.ExecutiveAction, data, 0);
        }
    }

    private void HandleModerationSnapshot(int sourceActor, Dictionary<byte, object> data)
    {
        if (!_state.Actors.TryGetValue(sourceActor, out var source) ||
            !TryGetStringMatrix(data, ExecutiveActionFields.MainProperty, out var matrix) ||
            matrix.Length < 2)
        {
            _log.Warn($"Kumo: actor {sourceActor} sent moderation lists Kumo could not read " +
                      $"({(data.TryGetValue(ExecutiveActionFields.MainProperty, out var raw) ? raw?.GetType().FullName : "missing")}).");
            return;
        }

        var blockedUsers = new HashSet<string>(matrix[0]);
        var mutedUsers = new HashSet<string>(matrix[1]);

        foreach (var target in _state.Actors.Values.Where(actor => actor.ActorNumber != sourceActor))
        {
            var shortId = ShortUserId(target.UserId);
            var blocked = blockedUsers.Contains(shortId);
            var muted = mutedUsers.Contains(shortId);

            UpdateModeration(source, target, muted, blocked);
            SendReply(sourceActor, target.ActorNumber, blocked, muted);
            _log.Info($"Kumo: moderation reply to actor {sourceActor} about actor {target.ActorNumber} (blocked={blocked}, muted={muted}).");
        }
    }

    private bool CanModerate(ActorState actor)
    {
        var user = actor.Identity.User;
        return user.Tags.Contains("admin_moderator") ||
               actor.UserId == _state.WorldAuthor ||
               actor.UserId == _state.InstanceCreator;
    }

    private bool TryFindTarget(Dictionary<byte, object> data, out ActorState target)
    {
        var userId = GetString(data, ExecutiveActionFields.TargetUser);
        target = _state.Actors.Values.FirstOrDefault(actor => actor.UserId == userId)!;
        return target is not null;
    }

    private static void UpdateModeration(
        ActorState source,
        ActorState target,
        bool? muted,
        bool? blocked)
    {
        if (!source.Moderations.TryGetValue(target.ActorNumber, out var moderation))
        {
            moderation = new UserModeration
            {
                ActorNumber = target.ActorNumber,
                UserId = target.UserId,
            };
            source.Moderations[target.ActorNumber] = moderation;
        }

        if (muted.HasValue)
            moderation.IsMuted = muted.Value;
        if (blocked.HasValue)
            moderation.IsBlocked = blocked.Value;
    }

    private void SendModerationPair(int sourceActor, int targetActor)
    {
        if (!_state.Actors.TryGetValue(sourceActor, out var source) ||
            !_state.Actors.TryGetValue(targetActor, out var target))
            return;

        source.Moderations.TryGetValue(targetActor, out var sourceModeration);
        target.Moderations.TryGetValue(sourceActor, out var targetModeration);

        SendReply(
            targetActor,
            sourceActor,
            sourceModeration?.IsBlocked ?? false,
            sourceModeration?.IsMuted ?? false);
        SendReply(
            sourceActor,
            targetActor,
            targetModeration?.IsBlocked ?? false,
            targetModeration?.IsMuted ?? false);
    }

    private void SendReply(int recipient, int targetActor, bool blocked, bool muted)
    {
        var data = PhotonPayload.Wrap(0, new Dictionary<byte, object>
        {
            [ExecutiveActionFields.Type] = ExecutiveActionTypes.ReplyPlayerModerations,
            [ExecutiveActionFields.TargetUser] = targetActor,
            [ExecutiveActionFields.BlockedUsers] = blocked,
            [ExecutiveActionFields.MutedUsers] = muted,
        });

        _host.BroadcastEvent(new List<int> { recipient }, 0, EventCodes.ExecutiveAction, data, 0);
    }

    private void SendExecutiveMessage(int actorNumber, string message)
    {
        _host.BroadcastEvent(
            new List<int> { actorNumber },
            0,
            EventCodes.ExecutiveMessage,
            PhotonPayload.Wrap(actorNumber, message),
            0);
        _host.RemoveActor(actorNumber, message);
    }

    private void SendWarn(int actorNumber, string heading, string message)
    {
        var data = PhotonPayload.Wrap(0, new Dictionary<byte, object>
        {
            [ExecutiveActionFields.Type] = ExecutiveActionTypes.Warn,
            [ExecutiveActionFields.Heading] = heading,
            [ExecutiveActionFields.Message] = message,
        });

        _host.BroadcastEvent(new List<int> { actorNumber }, 0, EventCodes.ExecutiveAction, data, 0);
    }

    private void SendMicOff(int actorNumber)
    {
        var data = PhotonPayload.Wrap(0, new Dictionary<byte, object>
        {
            [ExecutiveActionFields.Type] = ExecutiveActionTypes.MicOff,
        });

        _host.BroadcastEvent(new List<int> { actorNumber }, 0, EventCodes.ExecutiveAction, data, 0);
    }

    private static string ShortUserId(string userId)
    {
        var value = userId.StartsWith("usr_", StringComparison.Ordinal) ? userId.Substring(4) : userId;
        return value.Length <= 6 ? value : value.Substring(0, 6);
    }

    private static bool TryGetEventData(IRaiseEventCallInfo info, out Dictionary<byte, object> data)
    {
        if (info.Request.Parameters.TryGetValue(245, out var value) && value is Dictionary<byte, object> dictionary)
        {
            data = dictionary;
            return true;
        }

        data = null!;
        return false;
    }

    private static bool TryGetByte(Dictionary<byte, object> data, byte key, out byte value)
    {
        if (data.TryGetValue(key, out var raw))
        {
            try
            {
                value = Convert.ToByte(raw);
                return true;
            }
            catch
            {
            }
        }

        value = 0;
        return false;
    }

    private static bool TryGetBoolean(Dictionary<byte, object> data, byte key, out bool value)
    {
        if (data.TryGetValue(key, out var raw) && raw is bool boolean)
        {
            value = boolean;
            return true;
        }

        value = false;
        return false;
    }

    private static bool TryGetStringMatrix(
        Dictionary<byte, object> data,
        byte key,
        out string[][] value)
    {
        // The block and mute lists. Photon may hand them over as string[][] or as object[] of
        // string[] (empty lists especially); requiring string[][] meant no reply was ever sent.
        if (data.TryGetValue(key, out var raw) && raw is Array outer)
        {
            value = outer.Cast<object?>()
                .Select(row => row is Array inner ? inner.Cast<object?>().Select(v => v?.ToString() ?? "").ToArray() : Array.Empty<string>())
                .ToArray();
            return true;
        }

        value = Array.Empty<string[]>();
        return false;
    }

    private static string GetString(Dictionary<byte, object> data, byte key) =>
        data.TryGetValue(key, out var raw) ? raw?.ToString() ?? string.Empty : string.Empty;

    private static bool Fail(IRaiseEventCallInfo info, string message)
    {
        info.Fail(message);
        return false;
    }
}
