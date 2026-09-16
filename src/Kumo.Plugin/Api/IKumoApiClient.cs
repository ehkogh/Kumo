using Kumo.Plugin.Models;

namespace Kumo.Plugin.Api;

public interface IKumoApiClient
{
    RuntimeRemoteConfig? GetRuntimeConfig();

    JoinValidationResponse? ValidateJoin(string roomId, string token, bool onCreate);

    JoinValidationResponse? GetUser(string userId);

    void PlayerLeft(string roomId, string userId);

    void GameClosed(string roomId);
}
