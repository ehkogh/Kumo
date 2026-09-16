using System.Collections.Concurrent;
using Kumo.Plugin.Models;

namespace Kumo.Plugin.Runtime;

public sealed class RoomState
{
    public ConcurrentDictionary<int, ActorState> Actors { get; } = new();

    public Dictionary<byte, int> RateLimits { get; } = new();

    public bool RateLimiterActive { get; set; }

    public int MaxAccountsPerIp { get; set; } = 5;

    public int Capacity { get; set; }

    public string GameId { get; set; } = string.Empty;

    public string WorldAuthor { get; set; } = string.Empty;

    public string InstanceCreator { get; set; } = string.Empty;
}
