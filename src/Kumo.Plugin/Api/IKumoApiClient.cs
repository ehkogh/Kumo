using Kumo.Plugin.Models;

namespace Kumo.Plugin.Api;

public interface IKumoApiClient
{
    RuntimeRemoteConfig? GetRuntimeConfig();

    JoinValidationResponse? ValidateJoin(string roomId, string token, bool onCreate);

    JoinValidationResponse? GetUser(string userId);

    void PlayerLeft(string roomId, string userId);

    void GameClosed(string roomId);

    /// <summary>The player's owned products of this world, one record per product (VRCat /photon/purchases).</summary>
    List<Dictionary<string, object>> GetPurchases(string userId, string worldId);

    /// <summary>
    /// Who bought or lost one of this world's products since <paramref name="since"/> (the
    /// <c>Now</c> of the previous answer; null for the first call, which lists nobody).
    /// </summary>
    PurchaseChanges? GetPurchaseChanges(string worldId, long? since);

    /// <summary>
    /// The records a placed prop's event 74 Create carries (VRCat /photon/propSpawn): the prop and
    /// the placer's inventory item, as Photon dictionaries. Either is null if VRCat has none.
    /// </summary>
    (Dictionary<string, object>? Prop, Dictionary<string, object>? Item) GetPropSpawn(string userId, string propId, string inventoryId);
}

public sealed class PurchaseChanges
{
    public long Now { get; set; }

    public List<string> UserIds { get; set; } = new();
}
