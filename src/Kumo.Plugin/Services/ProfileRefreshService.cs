using Kumo.Plugin.Api;
using Kumo.Plugin.Models;
using Kumo.Plugin.Runtime;
using Newtonsoft.Json;
using Photon.Hive.Plugin;

namespace Kumo.Plugin.Services;

/// <summary>
/// Re-reads a player's record from the API when their client says it changed, and broadcasts
/// the result if it did.
/// </summary>
/// <remarks>
/// Every broadcast makes every other client treat the player's avatar as newly chosen and restart
/// its download. With two 2026 clients in a room that became a loop: each update made the other
/// client raise the update event again, about three times a second, so neither avatar ever
/// finished loading. So the API is asked at most once a second per player, and nothing is
/// broadcast unless the record actually changed.
/// </remarks>
public sealed class ProfileRefreshService
{
    private static readonly TimeSpan MinimumInterval = TimeSpan.FromSeconds(1);

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

        // Too soon: run once when the interval is up instead of dropping it, so a real change
        // that lands inside a burst is still picked up. One deferred refresh per player at most.
        var now = DateTime.UtcNow;
        var wait = actor.LastProfileRefresh + MinimumInterval - now;
        if (wait > TimeSpan.Zero)
        {
            if (!actor.ProfileRefreshPending)
            {
                actor.ProfileRefreshPending = true;
                _host.CreateOneTimeTimer(() =>
                {
                    actor.ProfileRefreshPending = false;
                    Refresh(actorNumber);
                }, (int)Math.Ceiling(wait.TotalMilliseconds));
            }
            return;
        }
        actor.LastProfileRefresh = now;

        var current = _host.GameActors.FirstOrDefault(candidate => candidate.ActorNr == actorNumber);
        if (current is null)
            return;

        var updated = _api.GetUser(actor.UserId);
        if (updated is null || !updated.Valid)
            return;

        if (string.Equals(updated.Ip, "notset", StringComparison.OrdinalIgnoreCase))
            updated.Ip = actor.IpAddress;

        var changed = !SameRecord(actor.Identity, updated);
        actor.Identity = updated;
        actor.IpAddress = updated.Ip;
        if (!changed)
            return;

        if (!_properties.TryPrepare(actorNumber, current.Properties.GetProperties(), out var prepared, out _))
            return;

        _host.SetProperties(actorNumber, prepared, null, true);
        _properties.Broadcast(actorNumber, prepared);
    }

    /// <summary>Whether the parts of a record that end up in the player's properties are unchanged.</summary>
    private static bool SameRecord(JoinValidationResponse before, JoinValidationResponse after) =>
        JsonConvert.SerializeObject(before.User) == JsonConvert.SerializeObject(after.User) &&
        JsonConvert.SerializeObject(before.AvatarDict) == JsonConvert.SerializeObject(after.AvatarDict) &&
        JsonConvert.SerializeObject(before.FavatarDict) == JsonConvert.SerializeObject(after.FavatarDict);
}
