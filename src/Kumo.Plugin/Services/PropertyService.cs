using System.Collections;
using Kumo.Plugin.Models;
using Kumo.Plugin.Runtime;
using Photon.Hive.Plugin;

namespace Kumo.Plugin.Services;

public sealed class PropertyService
{
    private readonly RoomState _state;
    private readonly IPluginHost _host;

    public PropertyService(RoomState state, IPluginHost host)
    {
        _state = state;
        _host = host;
    }

    public bool TryPrepare(int actorNumber, Hashtable current, out Hashtable prepared, out string error)
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
        var result = new Hashtable
        {
            ["inVRMode"] = false,
            ["showSocialRank"] = true,
            ["steamUserId"] = "0",
            ["isInvisible"] = false,
            ["canModerateInstance"] = false,
        };

        if (isPrivileged)
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

        result["user"] = userProperties;
        result["avatarDict"] = ToAvatarProperties(identity.AvatarDict);
        result["favatarDict"] = ToAvatarProperties(identity.FavatarDict);

        prepared = result;
        return true;
    }

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
