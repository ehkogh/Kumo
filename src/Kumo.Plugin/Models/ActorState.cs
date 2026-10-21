namespace Kumo.Plugin.Models;

public sealed class ActorState
{
    public int ActorNumber { get; set; }

    public string UserId { get; set; } = string.Empty;

    public string IpAddress { get; set; } = string.Empty;

    public JoinValidationResponse Identity { get; set; } = new();

    public bool Instantiated { get; set; }

    /// <summary>When the player's record was last re-read from the API, for throttling.</summary>
    public DateTime LastProfileRefresh { get; set; } = DateTime.MinValue;

    /// <summary>A throttled refresh is waiting to run.</summary>
    public bool ProfileRefreshPending { get; set; }

    public HashSet<int> InteractionActors { get; } = new();

    public Dictionary<int, UserModeration> Moderations { get; } = new();
}

public sealed class UserModeration
{
    public int ActorNumber { get; set; }

    public string UserId { get; set; } = string.Empty;

    public bool IsMuted { get; set; }

    public bool IsBlocked { get; set; }
}
