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

    private void Tick()
    {
        foreach (var actor in _state.Actors.Keys.ToArray())
        {
            _host.BroadcastEvent(
                new List<int> { actor },
                0,
                EventCodes.RateLimitTick,
                PhotonPayload.Wrap(0, null),
                0);
        }
    }
}
