namespace Kumo.Plugin.Configuration;

public sealed class KumoOptions
{
    public string ApiUrl { get; private set; } = string.Empty;

    public string ApiSecret { get; private set; } = string.Empty;

    public bool AllowLegacyClients { get; private set; }

    public bool StrictLegacyViewLayout { get; private set; }

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
        options.AllowLegacyClients = GetBoolean(config, "AllowLegacyClients");
        options.StrictLegacyViewLayout = GetBoolean(config, "StrictLegacyViewLayout");
        error = string.Empty;
        return true;
    }

    private static bool GetBoolean(Dictionary<string, string> config, string key) =>
        config.TryGetValue(key, out var value) &&
        value.Equals("true", StringComparison.OrdinalIgnoreCase);
}
