using Kumo.Plugin.Protocol;
using Kumo.Plugin.Runtime;
using Photon.Hive.Plugin;

namespace Kumo.Plugin.Services;

public sealed class RateLimitService
{
    private readonly RoomState _state;
    private readonly IPluginHost _host;
    private object? _timer;

    public RateLimitService(RoomState state, IPluginHost host)
    {
        _state = state;
        _host = host;
    }

    public void Start() => _timer ??= _host.CreateTimer(Tick, 1000, 1000);

    public void Stop()
    {
        if (_timer is null)
            return;

        _host.StopTimer(_timer);
        _timer = null;
    }

    public void SendConfiguration(int actorNumber)
    {
        var data = PhotonPayload.Wrap(0, new Dictionary<byte, object>
        {
            [0] = _state.RateLimits,
            [2] = _state.RateLimiterActive,
        });

        _host.BroadcastEvent(new List<int> { actorNumber }, 0, EventCodes.RateLimits, data, 0);
    }

    // Sent as the receiving actor, not actor 0. Actor 0 is not a player, and some clients discard
    // an event that claims to come from it ("no valid originating player"); this form is the one
    // proven against the 2026 client.
    private void Tick()
    {
        foreach (var actor in _state.Actors.Keys.ToArray())
        {
            _host.BroadcastEvent(
                new List<int> { actor },
                actor,
                EventCodes.RateLimitTick,
                PhotonPayload.Wrap(actor, null),
                0);
        }
    }
}
