using System.Net.Http;
using Kumo.Plugin.Configuration;
using Kumo.Plugin.Models;
using Newtonsoft.Json;

namespace Kumo.Plugin.Api;

public sealed class LegacyBackendApiClient : IKumoApiClient
{
    private static readonly HttpClient Http = new();
    private readonly KumoOptions _options;

    public LegacyBackendApiClient(KumoOptions options) => _options = options;

    public RuntimeRemoteConfig? GetRuntimeConfig() =>
        Get<RuntimeRemoteConfig>("/api/1/photon/getConfig", ("secret", _options.ApiSecret));

    public JoinValidationResponse? ValidateJoin(string roomId, string token, bool onCreate)
    {
        var query = new List<(string Key, string Value)>
        {
            ("secret", _options.ApiSecret),
            ("roomId", roomId),
            ("jwt", token),
        };

        if (onCreate)
            query.Add(("onCreate", "true"));

        return Get<JoinValidationResponse>("/api/1/photon/validateJoin", query.ToArray());
    }

    public JoinValidationResponse? GetUser(string userId) =>
        Get<JoinValidationResponse>(
            "/api/1/photon/user",
            ("secret", _options.ApiSecret),
            ("userId", userId));

    public void PlayerLeft(string roomId, string userId) =>
        GetString(
            "/api/1/photon/playerLeft",
            ("secret", _options.ApiSecret),
            ("roomId", roomId),
            ("userId", userId));

    public void GameClosed(string roomId) =>
        GetString(
            "/api/1/photon/gameClosed",
            ("secret", _options.ApiSecret),
            ("roomId", roomId));

    public List<Dictionary<string, object>> GetPurchases(string userId, string worldId)
    {
        var body = GetString("/api/1/photon/purchases", ("secret", _options.ApiSecret), ("userId", userId), ("worldId", worldId));
        // Dates stay strings: OpenPhoton's serializer has no DateTime (the first event 51 was
        // dropped for "No GpBinary V18 mapping for System.DateTime").
        var records = JsonConvert.DeserializeObject<List<Dictionary<string, object>>>(body,
            new JsonSerializerSettings { DateParseHandling = DateParseHandling.None }) ?? new();
        // Photon carries plain values only: longs become ints, anything else a string.
        return records.Select(record => record.ToDictionary(
            pair => pair.Key,
            pair => pair.Value switch
            {
                long n and >= int.MinValue and <= int.MaxValue => (object)(int)n,
                string or bool or int or double => pair.Value,
                null => "",
                var v => v.ToString() ?? "",
            })).ToList();
    }

    public PurchaseChanges? GetPurchaseChanges(string worldId, long? since) =>
        Get<PurchaseChanges>("/api/1/photon/purchases/changed", ("secret", _options.ApiSecret), ("worldId", worldId),
            ("since", since?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? ""));

    private T? Get<T>(string path, params (string Key, string Value)[] query)
    {
        var body = GetString(path, query);
        return JsonConvert.DeserializeObject<T>(body);
    }

    private string GetString(string path, params (string Key, string Value)[] query)
    {
        var queryString = string.Join(
            "&",
            query.Select(pair =>
                $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value ?? string.Empty)}"));

        var uri = $"{_options.ApiUrl}{path}?{queryString}";
        using var response = Http.GetAsync(uri).GetAwaiter().GetResult();
        response.EnsureSuccessStatusCode();
        return response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
    }
}
