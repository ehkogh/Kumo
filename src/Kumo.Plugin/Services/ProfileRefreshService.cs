using Kumo.Plugin.Api;
using Kumo.Plugin.Runtime;
using Photon.Hive.Plugin;

namespace Kumo.Plugin.Services;

public sealed class ProfileRefreshService
{
    private readonly RoomState _state;
    private readonly IKumoApiClient _api;
    private readonly IPluginHost _host;
    private readonly PropertyService _properties;

    public ProfileRefreshService(
        RoomState state,
        IKumoApiClient api,
        IPluginHost host,
        PropertyService properties)
    {
        _state = state;
        _api = api;
        _host = host;
        _properties = properties;
    }

    public void Refresh(int actorNumber)
    {
        if (!_state.Actors.TryGetValue(actorNumber, out var actor))
            return;

        var current = _host.GameActors.FirstOrDefault(candidate => candidate.ActorNr == actorNumber);
        if (current is null)
            return;

        var updated = _api.GetUser(actor.UserId);
        if (updated is null)
            return;

        if (string.Equals(updated.Ip, "notset", StringComparison.OrdinalIgnoreCase))
            updated.Ip = actor.IpAddress;

        actor.Identity = updated;
        actor.IpAddress = updated.Ip;

        if (!_properties.TryPrepare(actorNumber, current.Properties.GetProperties(), out var prepared, out _))
            return;

        _host.SetProperties(actorNumber, prepared, null, true);
        _properties.Broadcast(actorNumber, prepared);
    }
}
