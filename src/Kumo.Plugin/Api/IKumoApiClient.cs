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
}
