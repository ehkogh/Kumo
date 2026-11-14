using Newtonsoft.Json.Linq;

namespace Kumo.Plugin.Protocol;

/// <summary>JSON from the API as Photon carries it to the client's model parsers.</summary>
public static class PhotonJson
{
    /// <summary>
    /// Objects as string-keyed dictionaries, arrays as lists (as avatarDict sends them), nulls left
    /// out, dates as the ISO text they came as (the client parses updated_at itself), integers as
    /// Int32 where they fit.
    /// </summary>
    public static object? ToPhoton(JToken? token) => token switch
    {
        JObject o => o.Properties().Where(p => p.Value.Type != JTokenType.Null)
            .ToDictionary(p => p.Name, p => ToPhoton(p.Value)!),
        JArray a => a.Select(ToPhoton).Where(v => v is not null).Cast<object>().ToList(),
        JValue { Type: JTokenType.Date } d => ((DateTime)d.Value!).ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fffK"),
        JValue { Type: JTokenType.Integer } i => (long)i.Value! is var n && n >= int.MinValue && n <= int.MaxValue ? (int)n : n,
        JValue { Type: JTokenType.Float } f => Convert.ToDouble(f.Value),
        JValue v => v.Value,
        _ => null,
    };
}
