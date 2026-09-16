using System.Collections;
using Kumo.Plugin.Models;
using Kumo.Plugin.Runtime;
using Photon.Hive.Plugin;

namespace Kumo.Plugin.Services;

public sealed class PropertyService
{
    private readonly RoomState _state;
    private readonly IPluginHost _host;
    private readonly IPluginLogger _log;
    private readonly HashSet<string> _reportedClaims = new();

    public PropertyService(RoomState state, IPluginHost host, IPluginLogger log)
    {
        _state = state;
        _host = host;
        _log = log;
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

        if (current["user"] is Dictionary<string, object> presentedUser &&
            presentedUser.TryGetValue("displayName", out var displayNameValue) &&
            displayNameValue is string presentedName &&
            !string.IsNullOrEmpty(presentedName) &&
            !string.Equals(presentedName, identity.User.DisplayName, StringComparison.Ordinal))
        {
            if (isPrivileged)
                userProperties["displayName"] = presentedName;
            else
                LogClaimOnce(actorNumber, "displayName", $"ignoring displayName claim '{presentedName}'");
        }

        if (current["modTag"] is string modTag && !string.IsNullOrEmpty(modTag))
        {
            if (isPrivileged)
                result["modTag"] = modTag;
            else
                LogClaimOnce(actorNumber, "modTag", $"ignoring modTag claim '{modTag}'");
        }

        if (current["isInvisible"] is bool invisible && invisible)
        {
            if (isPrivileged)
                result["isInvisible"] = true;
            else
                LogClaimOnce(actorNumber, "isInvisible", "ignoring isInvisible claim");
        }

        CopyIfPresent(current, result, "avatarEyeHeight");
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

    private void LogClaimOnce(int actorNumber, string claim, string message)
    {
        lock (_reportedClaims)
        {
            if (!_reportedClaims.Add($"{actorNumber}:{claim}"))
                return;
        }

        _log.Warn($"Kumo: actor {actorNumber} {message}.");
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

    private static Dictionary<string, object> ToUserProperties(UserRecord user) => new()
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
    };

    private static Dictionary<string, object> ToAvatarProperties(AvatarRecord avatar) => new()
    {
        ["id"] = avatar.id,
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
    };
}
