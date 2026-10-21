using Kumo.Plugin.Api;
using Kumo.Plugin.Models;
using Photon.Hive.Plugin;

namespace Kumo.Plugin.Services;

public sealed class AuthorizationService
{
    private readonly IKumoApiClient _api;
    private readonly IPluginLogger _log;

    public AuthorizationService(IKumoApiClient api, IPluginLogger log)
    {
        _api = api;
        _log = log;
    }

    public JoinValidationResponse Authorize(string? token, string userId, string roomId, bool onCreate)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(token))
                return Invalid();

            var result = _api.ValidateJoin(roomId, token!, onCreate) ?? Invalid();

            if (!result.Valid)
                return result;

            if (!string.Equals(result.User.Id, userId, StringComparison.Ordinal))
            {
                _log.Warn(
                    $"Kumo: backend identity '{result.User.Id}' did not match authenticated user '{userId}'.");

                if (!string.IsNullOrWhiteSpace(result.User.Id))
                {
                    try
                    {
                        _api.PlayerLeft(roomId, result.User.Id);
                    }
                    catch
                    {
                    }
                }

                return Invalid();
            }

            return result;
        }
        catch (Exception ex)
        {
            _log.Error($"Kumo: authorization backend failed for {userId}: {ex.Message}");
            return Invalid();
        }
    }

    /// <summary>
    /// Whether a room is one of the client's own local instances (<c>local:…</c>), such as its
    /// error world. The API never placed the player there, so nothing issued a join token.
    /// </summary>
    public static bool IsLocalRoom(string? gameId) =>
        gameId is not null && gameId.StartsWith("local:", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Admits a player to a local room by the user id Photon's name server already verified,
    /// asking the API for their record (which carries their avatar) instead of checking a token.
    /// </summary>
    /// <remarks>
    /// Local rooms still need the plugin: the 2026 client waits in "Waiting for network limits"
    /// until the rate-limit table arrives, and only the plugin sends it.
    /// </remarks>
    public JoinValidationResponse AuthorizeLocal(string userId)
    {
        try
        {
            var result = _api.GetUser(userId) ?? Invalid();
            if (result.Valid && !string.Equals(result.User.Id, userId, StringComparison.Ordinal))
            {
                _log.Warn($"Kumo: backend identity '{result.User.Id}' did not match authenticated user '{userId}'.");
                return Invalid();
            }

            return result;
        }
        catch (Exception ex)
        {
            _log.Error($"Kumo: user lookup failed for {userId}: {ex.Message}");
            return Invalid();
        }
    }

    private static JoinValidationResponse Invalid() => new() { Valid = false };
}
