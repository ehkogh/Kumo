using Kumo.Plugin.Api;
using Kumo.Plugin.Configuration;
using Kumo.Plugin.Models;
using Photon.Hive.Plugin;

namespace Kumo.Plugin.Services;

public sealed class AuthorizationService
{
    private readonly IKumoApiClient _api;
    private readonly KumoOptions _options;
    private readonly IPluginLogger _log;

    public AuthorizationService(IKumoApiClient api, KumoOptions options, IPluginLogger log)
    {
        _api = api;
        _options = options;
        _log = log;
    }

    public JoinValidationResponse Authorize(string? token, string userId, string roomId, bool onCreate)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(token))
                return _api.ValidateJoin(roomId, token!, onCreate) ?? Invalid();

            if (!_options.AllowLegacyClients)
                return Invalid();

            _log.Info($"Kumo: {userId} presented no join token; using verified user identity.");
            return _api.GetUser(userId) ?? Invalid();
        }
        catch (Exception ex)
        {
            _log.Error($"Kumo: authorization backend failed for {userId}: {ex.Message}");
            return Invalid();
        }
    }

    private static JoinValidationResponse Invalid() => new() { Valid = false };
}
