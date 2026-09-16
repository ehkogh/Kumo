using Kumo.Plugin.Protocol;
using Kumo.Plugin.Runtime;
using Photon.Hive.Plugin;

namespace Kumo.Plugin.Services;

public sealed class InteractionPermissionService
{
    private readonly RoomState _state;
    private readonly IPluginHost _host;

    public InteractionPermissionService(RoomState state, IPluginHost host)
    {
        _state = state;
        _host = host;
    }

    public bool Update(int actorNumber, object? value)
    {
        if (!_state.Actors.TryGetValue(actorNumber, out var actor) || value is not string[] userIds)
            return false;

        actor.InteractionActors.Clear();
        foreach (var userId in userIds)
        {
            var target = _state.Actors.Values.FirstOrDefault(candidate => candidate.UserId == userId);
            if (target is not null)
                actor.InteractionActors.Add(target.ActorNumber);
        }

        Announce();
        return true;
    }

    public void Announce()
    {
        var permissions = _state.Actors.Values
            .OrderBy(actor => actor.ActorNumber)
            .Select(actor => actor.InteractionActors.Concat(new[] { actor.ActorNumber }).Distinct().ToArray())
            .ToArray();

        _host.BroadcastEvent(0, 0, 0, EventCodes.InteractionPermissions, PhotonPayload.Wrap(0, permissions), 0);
    }
}
