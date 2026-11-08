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

    /// <summary>
    /// Where player persistence (PlayerData, PlayerObjects) is kept: one JSON file per world and
    /// user. Defaults to a "persistence" folder beside the plugin.
    /// </summary>
    public string PersistencePath { get; private set; } = string.Empty;

    /// <summary>
    /// Diagnostics: view ids whose every sync bunch (10/11) and ownership change (22) is logged in
    /// full. Comma-separated; empty in normal operation.
    /// </summary>
    public int[] TraceViews { get; private set; } = Array.Empty<int>();

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
        options.TraceViews = config.TryGetValue("TraceViews", out var traceViews)
            ? traceViews.Split(',').Select(v => int.TryParse(v.Trim(), out var id) ? id : -1).Where(id => id >= 0).ToArray()
            : Array.Empty<int>();
        options.PersistencePath = config.TryGetValue("PersistencePath", out var persistence) && !string.IsNullOrWhiteSpace(persistence)
            ? persistence
            : Path.Combine(Path.GetDirectoryName(typeof(KumoOptions).Assembly.Location) ?? ".", "persistence");
        error = string.Empty;
        return true;
    }
}
