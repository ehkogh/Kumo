using Newtonsoft.Json;

namespace Kumo.Plugin.Models;

public sealed class JoinValidationResponse
{
    public string? Time;
    public bool Valid;
    public string Ip = string.Empty;
    public UserRecord User = new();
    public AvatarRecord AvatarDict = new();
    public AvatarRecord FavatarDict = new();
    /// <summary>The look worn on the current avatar, as its inventory item (empty when none).</summary>
    public Newtonsoft.Json.Linq.JObject? AvatarLookDict;
    public int WorldCapacity;
    public string WorldAuthor = string.Empty;
    public string InstanceCreator = string.Empty;
}

public sealed class RuntimeRemoteConfig
{
    public Dictionary<int, int> RateLimitList = new();
    public bool RatelimiterActive;
    public int MaxAccsPerIp = 5;
}

public sealed class UserRecord
{
    public string Id = string.Empty;
    public string DisplayName = string.Empty;
    public string DeveloperType = string.Empty;
    public string CurrentAvatarImageUrl = string.Empty;
    public string CurrentAvatarThumbnailImageUrl = string.Empty;
    public string UserIcon = string.Empty;

    [JsonProperty("last_platform")]
    public string LastPlatform = string.Empty;

    public string Status = string.Empty;
    public string StatusDescription = string.Empty;
    public string Bio = string.Empty;
    public IList<string> Tags = new List<string>();
    public bool AllowAvatarCopying;

    // Age verification, shown on the player's in-game profile (built from the room properties).
    public bool AgeVerified;
    public bool IsAdult;
    public string AgeVerificationStatus = "hidden";

    // Everything else the API puts on the record (iconUrl, iconType, profilePicOverride, the
    // decorations, banner fields…). Passed on to the room's user properties, so a field the 2026
    // client reads there is never dropped here just because this list did not name it.
    [JsonExtensionData]
    public IDictionary<string, Newtonsoft.Json.Linq.JToken> Extra = new Dictionary<string, Newtonsoft.Json.Linq.JToken>();
}

public sealed class AvatarRecord
{
    public string id = string.Empty;
    public string offlineAnalysisScanStatus = "unknown";
    public string assetUrl = string.Empty;
    public string authorId = string.Empty;
    public string authorName = string.Empty;
    public string updated_at = string.Empty;
    public string description = string.Empty;
    public bool featured;
    public string imageUrl = string.Empty;
    public string thumbnailImageUrl = string.Empty;
    public string name = string.Empty;
    public string releaseStatus = string.Empty;
    public int version;
    public IList<string> tags = new List<string>();
    public IList<UnityPackageRecord> unityPackages = new List<UnityPackageRecord>();
}

public sealed class UnityPackageRecord
{
    public string id = string.Empty;
    public string assetUrl = string.Empty;
    public Dictionary<string, object> assetUrlObject = new();
    public string created_at = string.Empty;
    public string platform = string.Empty;
    public string pluginUrl = string.Empty;
    public Dictionary<string, object> pluginUrlObject = new();
    public string unityVersion = string.Empty;

    // VRChat's own format (20220322000) does not fit an int.
    public long unitySortNumber;

    // The 2026 client picks the package for its variant (standard, security, impostor) and fails
    // with "Variant specific assetUrl was null" when none says which it is.
    public string variant = "standard";
    public int assetVersion;
    public string performanceRating = "None";
    public string scanStatus = "passed";
}
