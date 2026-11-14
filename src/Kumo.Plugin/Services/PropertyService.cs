using System.Collections;
using Kumo.Plugin.Models;
using Kumo.Plugin.Runtime;
using Photon.Hive.Plugin;

namespace Kumo.Plugin.Services;

public sealed class PropertyService
{
    /// <summary>
    /// Keys Kumo sets itself, from the API's record or the player's entitlements: whatever the
    /// client puts in them is ignored (or honoured for staff only, below).
    /// </summary>
    private static readonly HashSet<string> Managed = new(StringComparer.Ordinal)
    {
        "user", "avatarDict", "favatarDict", "avatarLookDict", "modTag", "isInvisible", "canModerateInstance", "steamUserId",
        "inVRMode", "showSocialRank", "avatarEyeHeight", "avatarHipHeight",
    };

    private readonly RoomState _state;
    private readonly IPluginHost _host;
    private readonly IPluginLogger? _log;
    /// <summary>Client keys already logged as passed through, so each is named once per room.</summary>
    private readonly HashSet<object> _passedThrough = new();

    public PropertyService(RoomState state, IPluginHost host, IPluginLogger? log = null)
    {
        _state = state;
        _host = host;
        _log = log;
    }

    /// <summary>
    /// Prepares a player's properties. <paramref name="partial"/> is for updates (SetProperties,
    /// event 42): Photon merges them into what the player already has, so only the keys the update
    /// carries are touched. Without it every update reset the defaults below, and the first height
    /// report after an avatar loaded set a VR player's <c>inVRMode</c> back to false for the rest
    /// of the session (full-body players lying down got a spazzing head, 2026-11-14).
    /// </summary>
    public bool TryPrepare(int actorNumber, Hashtable current, out Hashtable prepared, out string error, bool partial = false)
    {
        prepared = new Hashtable();
        error = string.Empty;

        if (!_state.Actors.TryGetValue(actorNumber, out var actor))
        {
            error = "Unknown actor.";
            return false;
        }

        var identity = actor.Identity;
        var isPrivileged = IsPrivileged(identity.User);
        var userProperties = ToUserProperties(identity.User);
        var result = partial ? new Hashtable() : new Hashtable
        {
            ["inVRMode"] = false,
            ["showSocialRank"] = true,
            ["steamUserId"] = "0",
            ["isInvisible"] = false,
            ["canModerateInstance"] = false,
        };

        // Everything else the client sets passes through, as plain Photon does. The equipped look
        // and its accessories travel in the player's own properties; rebuilt from a whitelist,
        // they were dropped, and an equipped accessory never showed on anyone (2026-11-14). The
        // keys Kumo vouches for are overwritten below, so this lets no claim through.
        foreach (DictionaryEntry entry in current)
        {
            if (entry.Key is string name && Managed.Contains(name)) continue;
            result[entry.Key] = entry.Value;
            if (_passedThrough.Add(entry.Key))
                _log?.Info($"Kumo: actor property '{entry.Key}' ({entry.Value?.GetType().Name ?? "null"}) passed through.");
        }

        if (isPrivileged && (!partial || current.ContainsKey("modTag")))
            result["modTag"] = string.Empty;

        // Claims the player is not entitled to are dropped, never refused. The properties handed on
        // are rebuilt here from the API's record, so leaving a claim out is already the whole
        // defence; failing the operation as well only costs the player their connection. And
        // clients send some of these fields routinely: the 2026 client includes modTag in the
        // property block it joins with, and refusing it refused every join.

        // A different, non-empty display name: honoured for staff only.
        if (isPrivileged &&
            current["user"] is Dictionary<string, object> presentedUser &&
            presentedUser.TryGetValue("displayName", out var displayNameValue) &&
            displayNameValue is string presentedName &&
            presentedName.Length > 0)
        {
            userProperties["displayName"] = presentedName;
        }

        // An empty modTag claims nothing; a non-empty one is honoured for staff only.
        if (isPrivileged && current["modTag"] is string modTag && modTag.Length > 0)
            result["modTag"] = modTag;

        if (isPrivileged && current["isInvisible"] is bool invisible && invisible)
            result["isInvisible"] = true;

        // Avatar measurements the client reports. Both are needed to place a remote player: with
        // only the eye height passed on, other players saw them floating above the ground.
        CopyIfPresent(current, result, "avatarEyeHeight");
        CopyIfPresent(current, result, "avatarHipHeight");
        CopyBooleanIfPresent(current, result, "showSocialRank");
        CopyBooleanIfPresent(current, result, "inVRMode");

        if (partial)
        {
            // Server-owned keys go out again only when the update touched them, rebuilt from the
            // API's record as on join. Re-sending avatarDict unasked makes every other client
            // restart the avatar download (see ProfileRefreshService).
            if (current.ContainsKey("user")) result["user"] = userProperties;
            if (current.ContainsKey("avatarDict")) result["avatarDict"] = ToAvatarProperties(identity.AvatarDict);
            if (current.ContainsKey("favatarDict")) result["favatarDict"] = ToAvatarProperties(identity.FavatarDict);
            if (current.ContainsKey("avatarLookDict")) result["avatarLookDict"] = ToLookProperties(identity.AvatarLookDict);
            if (current.ContainsKey("isInvisible") && !result.ContainsKey("isInvisible")) result["isInvisible"] = false;
            _log?.Info($"Kumo: actor {actorNumber} property update: {string.Join(", ", current.Keys.Cast<object>().Select(k => k is "user" or "avatarDict" or "favatarDict" or "avatarLookDict" ? k : $"{k}={current[k]}"))}");
            prepared = result;
            return true;
        }

        result["user"] = userProperties;
        result["avatarDict"] = ToAvatarProperties(identity.AvatarDict);
        result["favatarDict"] = ToAvatarProperties(identity.FavatarDict);
        // The look worn on the avatar: the client puts its accessories on from this (by id and
        // updated_at). With no look it is null, which the client skips and Photon removes. Never an
        // empty dictionary: the client then throws "The given key 'id' was not present" and drops
        // the whole update, avatar switch included (2026-11-14).
        result["avatarLookDict"] = ToLookProperties(identity.AvatarLookDict);

        prepared = result;
        return true;
    }

    private static Dictionary<string, object>? ToLookProperties(Newtonsoft.Json.Linq.JObject? look) =>
        look is not null && look["id"] is { Type: Newtonsoft.Json.Linq.JTokenType.String } id && ((string?)id)?.Length > 0
            ? (Dictionary<string, object>)ToPhoton(look)!
            : null;

    public void Broadcast(int actorNumber, Hashtable properties)
    {
        _host.BroadcastEvent(0, actorNumber, 0, 42, Protocol.PhotonPayload.Wrap(actorNumber, properties), 0);
    }

    /// <summary>A player's properties (event 42), to one client only.</summary>
    public void SendTo(int receiver, int actorNumber, Hashtable properties)
    {
        _host.BroadcastEvent(new List<int> { receiver }, actorNumber, 42, Protocol.PhotonPayload.Wrap(actorNumber, properties), 0);
    }

    private static bool IsPrivileged(UserRecord user) =>
        user.Tags.Contains("admin_moderator") || user.DeveloperType == "internal";

    private static void CopyIfPresent(Hashtable source, Hashtable target, string key)
    {
        if (source.ContainsKey(key))
            target[key] = source[key];
    }

    private static void CopyBooleanIfPresent(Hashtable source, Hashtable target, string key)
    {
        if (source[key] is bool value)
            target[key] = value;
    }

    private static Dictionary<string, object> ToUserProperties(UserRecord user)
    {
        var properties = KnownUserProperties(user);
        // Plain text and true/false fields only: Photon carries those as-is.
        foreach (var entry in user.Extra)
        {
            var key = entry.Key;
            var value = entry.Value;
            if (value is null || properties.ContainsKey(key)) continue;
            switch (value.Type)
            {
                case Newtonsoft.Json.Linq.JTokenType.String:
                    properties[key] = (string)value!;
                    break;
                case Newtonsoft.Json.Linq.JTokenType.Boolean:
                    properties[key] = (bool)value;
                    break;
            }
        }
        return properties;
    }

    private static Dictionary<string, object> KnownUserProperties(UserRecord user) => new()
    {
        ["id"] = user.Id,
        ["displayName"] = user.DisplayName,
        ["developerType"] = user.DeveloperType,
        ["currentAvatarImageUrl"] = user.CurrentAvatarImageUrl,
        ["currentAvatarThumbnailImageUrl"] = user.CurrentAvatarThumbnailImageUrl,
        ["userIcon"] = user.UserIcon,
        ["last_platform"] = user.LastPlatform,
        ["status"] = user.Status,
        ["statusDescription"] = user.StatusDescription,
        ["bio"] = user.Bio,
        ["tags"] = user.Tags,
        ["allowAvatarCopying"] = user.AllowAvatarCopying,
        // The game's in-room profile reads these from here: without them it offered "Get Age
        // Verified" to players the API reports as verified.
        ["ageVerified"] = user.AgeVerified,
        ["isAdult"] = user.IsAdult,
        ["ageVerificationStatus"] = user.AgeVerificationStatus,
    };

    /// <summary>
    /// A JSON value as Photon carries it: objects as string-keyed dictionaries, arrays as lists
    /// (as avatarDict sends them), and dates back as the ISO text they came as, since the client
    /// parses updated_at itself.
    /// </summary>
    private static object? ToPhoton(Newtonsoft.Json.Linq.JToken token) => token switch
    {
        Newtonsoft.Json.Linq.JObject o => o.Properties().Where(p => p.Value.Type != Newtonsoft.Json.Linq.JTokenType.Null)
            .ToDictionary(p => p.Name, p => ToPhoton(p.Value)!),
        Newtonsoft.Json.Linq.JArray a => a.Select(ToPhoton).Where(v => v is not null).Cast<object>().ToList(),
        Newtonsoft.Json.Linq.JValue { Type: Newtonsoft.Json.Linq.JTokenType.Date } d =>
            ((DateTime)d.Value!).ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fffK"),
        Newtonsoft.Json.Linq.JValue { Type: Newtonsoft.Json.Linq.JTokenType.Integer } i =>
            (long)i.Value! is var n && n >= int.MinValue && n <= int.MaxValue ? (int)n : n,
        Newtonsoft.Json.Linq.JValue { Type: Newtonsoft.Json.Linq.JTokenType.Float } f => Convert.ToDouble(f.Value),
        Newtonsoft.Json.Linq.JValue v => v.Value,
        _ => null,
    };

    private static Dictionary<string, object> ToAvatarProperties(AvatarRecord avatar) => new()
    {
        ["id"] = avatar.id,
        ["offlineAnalysisScanStatus"] = avatar.offlineAnalysisScanStatus,
        ["assetUrl"] = avatar.assetUrl,
        ["authorId"] = avatar.authorId,
        ["authorName"] = avatar.authorName,
        ["updated_at"] = avatar.updated_at,
        ["description"] = avatar.description,
        ["featured"] = avatar.featured,
        ["imageUrl"] = avatar.imageUrl,
        ["thumbnailImageUrl"] = avatar.thumbnailImageUrl,
        ["name"] = avatar.name,
        ["releaseStatus"] = avatar.releaseStatus,
        ["version"] = avatar.version,
        ["tags"] = avatar.tags,
        ["unityPackages"] = avatar.unityPackages.Select(ToUnityPackage).ToList(),
    };

    private static Dictionary<string, object> ToUnityPackage(UnityPackageRecord package) => new()
    {
        ["id"] = package.id,
        ["assetUrl"] = package.assetUrl,
        ["assetUrlObject"] = new Dictionary<string, object>(),
        ["created_at"] = package.created_at,
        ["platform"] = package.platform,
        ["pluginUrl"] = package.pluginUrl,
        ["pluginUrlObject"] = new Dictionary<string, object>(),
        ["unityVersion"] = package.unityVersion,
        ["unitySortNumber"] = package.unitySortNumber,
        ["variant"] = package.variant ?? "standard",
        ["assetVersion"] = package.assetVersion,
        ["performanceRating"] = package.performanceRating ?? "None",
        ["scanStatus"] = package.scanStatus ?? "passed",
    };
}
