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
}

public sealed class AvatarRecord
{
    public string id = string.Empty;
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
    public int unitySortNumber;
}
