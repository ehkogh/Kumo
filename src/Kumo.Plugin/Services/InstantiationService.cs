using System.Collections;
using Kumo.Plugin.Configuration;
using Kumo.Plugin.Runtime;
using Photon.Hive.Plugin;

namespace Kumo.Plugin.Services;

public sealed class InstantiationService
{
    private readonly RoomState _state;
    private readonly KumoOptions _options;
    private readonly IPluginHost _host;
    private readonly IPluginLogger _log;

    public InstantiationService(RoomState state, KumoOptions options, IPluginHost host, IPluginLogger log)
    {
        _state = state;
        _options = options;
        _host = host;
        _log = log;
    }

    public bool Handle(IRaiseEventCallInfo info)
    {
        if (!_state.Actors.TryGetValue(info.ActorNr, out var actor) ||
            !info.Request.Parameters.TryGetValue(245, out var raw) ||
            raw is not Hashtable data)
        {
            info.Fail("Invalid instantiate payload.");
            return false;
        }

        if (data[0] is not string prefab || string.IsNullOrWhiteSpace(prefab))
        {
            info.Fail("Invalid instantiate prefab.");
            return false;
        }

        var viewIds = data[4] as int[] ?? Array.Empty<int>();
        var mainViewId = data[7] is int main ? main : 0;

        if (_options.StrictLegacyViewLayout)
        {
            var start = info.ActorNr * 100000;
            var allowed = new HashSet<int> { start + 1, start + 2, start + 3, start + 4 };
            if (mainViewId != start + 1 || viewIds.Any(id => !allowed.Contains(id)))
            {
                Reject(info, "Invalid legacy view layout.");
                return false;
            }

            if (!string.Equals(prefab, "VRCPlayer", StringComparison.Ordinal))
            {
                Reject(info, "Only the player prefab is allowed in strict legacy mode.");
                return false;
            }
        }
        else
        {
            if (mainViewId != 0 && !BelongsToActor(info.ActorNr, mainViewId))
            {
                Reject(info, "Invalid main view owner.");
                return false;
            }

            if (viewIds.Any(id => !BelongsToActor(info.ActorNr, id)))
            {
                Reject(info, "Invalid view owner.");
                return false;
            }
        }

        if (string.Equals(prefab, "VRCPlayer", StringComparison.Ordinal))
        {
            if (actor.Instantiated)
            {
                info.Fail("Already instantiated.");
                return false;
            }

            actor.Instantiated = true;
        }

        info.Request.Cache = CacheOperations.AddToRoomCache;
        data.Remove((byte)1);
        data.Remove((byte)2);
        info.Continue();
        return true;
    }

    private static bool BelongsToActor(int actorNumber, int viewId) =>
        viewId > 0 && viewId / 100000 == actorNumber;

    private void Reject(IRaiseEventCallInfo info, string reason)
    {
        _log.Warn($"Kumo: actor {info.ActorNr} instantiate rejected: {reason}");
        info.Fail(reason);
        _host.RemoveActor(info.ActorNr, reason);
    }
}
