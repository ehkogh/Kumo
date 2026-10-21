namespace Kumo.Plugin.Configuration;

public sealed class KumoOptions
{
    public string ApiUrl { get; private set; } = string.Empty;

    public string ApiSecret { get; private set; } = string.Empty;

    /// <summary>
    /// Relay (and log) unmapped event codes inside the 2026 client's own range instead of
    /// dropping them. For mapping the 2026 event table; off in normal operation.
    /// </summary>
    public bool DiscoveryMode { get; private set; }

    public static bool TryCreate(
        Dictionary<string, string> config,
        out KumoOptions options,
        out string error)
    {
        options = new KumoOptions();

        if (!config.TryGetValue("ApiUrl", out var apiUrl) || string.IsNullOrWhiteSpace(apiUrl))
        {
            error = "ApiUrl is required.";
            return false;
        }

        if (!config.TryGetValue("PhotonApiSecret", out var secret) || string.IsNullOrWhiteSpace(secret))
        {
            error = "PhotonApiSecret is required.";
            return false;
        }

        options.ApiUrl = apiUrl.TrimEnd('/');
        options.ApiSecret = secret;
        options.DiscoveryMode = config.TryGetValue("DiscoveryMode", out var discovery) &&
                                bool.TryParse(discovery, out var enabled) && enabled;
        error = string.Empty;
        return true;
    }
}
