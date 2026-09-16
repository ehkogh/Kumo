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

    private static JoinValidationResponse Invalid() => new() { Valid = false };
}
