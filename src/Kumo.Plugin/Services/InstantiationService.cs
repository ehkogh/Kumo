using System.Collections;
using Kumo.Plugin.Runtime;
using Photon.Hive.Plugin;

namespace Kumo.Plugin.Services;

public sealed class InstantiationService
{
    private readonly RoomState _state;
    private readonly IPluginHost _host;
    private readonly IPluginLogger _log;

    public InstantiationService(RoomState state, IPluginHost host, IPluginLogger log)
    {
        _state = state;
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

        if (Field(data, 0) is not string prefab || string.IsNullOrWhiteSpace(prefab))
        {
            info.Fail("Invalid instantiate prefab.");
            return false;
        }

        var viewIds = Field(data, 4) as int[] ?? Array.Empty<int>();
        var mainViewId = Field(data, 7) is int main ? main : 0;

        var start = info.ActorNr * 100000;
        var allowed = new HashSet<int> { start + 1, start + 2, start + 3, start + 4 };
        if (mainViewId != start + 1 || viewIds.Any(id => !allowed.Contains(id)))
        {
            Reject(info, $"Invalid view layout (prefab '{prefab}', main view {mainViewId}, views [{string.Join(",", viewIds)}], "
                       + $"keys [{string.Join(",", data.Keys.Cast<object>())}]).");
            return false;
        }

        if (!string.Equals(prefab, "VRCPlayer", StringComparison.Ordinal))
        {
            Reject(info, $"Only the player prefab is allowed (got '{prefab}', main view {mainViewId}, views [{string.Join(",", viewIds)}], "
                       + $"keys [{string.Join(",", data.Keys.Cast<object>())}]).");
            return false;
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

    /// <summary>
    /// The client keys the instantiate data with bytes. A Hashtable never matches an int key to a
    /// byte one, so <c>data[0]</c> read nothing and every player's instantiate was refused.
    /// </summary>
    private static object? Field(Hashtable data, byte key) => data[key] ?? data[(int)key];

    private void Reject(IRaiseEventCallInfo info, string reason)
    {
        _log.Warn($"Kumo: actor {info.ActorNr} instantiate rejected: {reason}");
        info.Fail(reason);
        _host.RemoveActor(info.ActorNr, reason);
    }
}
