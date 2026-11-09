using Kumo.Plugin.Api;
using Kumo.Plugin.Protocol;
using Kumo.Plugin.Runtime;
using Photon.Hive.Plugin;

namespace Kumo.Plugin.Services;

/// <summary>
/// World (udon) purchases, event 51. The 2026 client fires a world's <c>_onPurchasesLoaded</c>
/// only from this server event, never from its own licenses, so without it no world sees what
/// anyone bought. Payload <c>[int actor, bool, Dictionary&lt;string, object&gt;[] products]</c>
/// (decoded from the client's handler, 2026.4.1p1); the bool's meaning and the records' keys are
/// not confirmed, the records are the products as the API shows them. Sent when a player's world
/// has loaded (event 25): theirs to everyone, and everyone else's to them.
/// </summary>
public sealed class PurchaseService
{
    private readonly RoomState _state;
    private readonly IKumoApiClient _api;
    private readonly IPluginHost _host;
    private readonly IPluginLogger _log;

    public PurchaseService(RoomState state, IKumoApiClient api, IPluginHost host, IPluginLogger log)
    {
        _state = state;
        _api = api;
        _host = host;
        _log = log;
    }

    public void OnWorldLoaded(int actor)
    {
        if (!_state.Actors.TryGetValue(actor, out var joiner)) return;
        var everyone = _state.Actors.Keys.ToList();
        Send(actor, joiner.UserId, everyone);
        foreach (var other in _state.Actors.Values.Where(a => a.ActorNumber != actor).ToList())
            Send(other.ActorNumber, other.UserId, new List<int> { actor });
    }

    private void Send(int actor, string userId, List<int> targets)
    {
        try
        {
            var owned = _api.GetPurchases(userId, _state.GameId);
            var payload = new object[] { actor, true, owned.ToArray() };
            _host.BroadcastEvent(targets, 0, EventCodes.Purchases, PhotonPayload.Wrap(0, payload), 0);
            _log.Info($"Kumo: event 51 for actor {actor} ({owned.Count} products) to [{string.Join(",", targets)}]");
        }
        catch (Exception ex)
        {
            _log.Warn($"Kumo: purchases for actor {actor} unavailable: {ex.Message}");
        }
    }
}
