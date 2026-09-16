using Kumo.Plugin.Protocol;
using Photon.Hive.Plugin;

namespace Kumo.Plugin.Services;

public sealed class InterestService
{
    private readonly IPluginHost _host;
    private readonly IPluginLogger _log;

    public InterestService(IPluginHost host, IPluginLogger log)
    {
        _host = host;
        _log = log;
    }

    public void Handle(IRaiseEventCallInfo info)
    {
        if (info.Request.Data is byte[] data)
            LogInterests(info.ActorNr, data);

        var playerViews = _host.GameActors.Select(actor => actor.ActorNr).ToArray();

        var payload = new Dictionary<byte, object>
        {
            [245] = playerViews,
            [254] = 0,
        };

        _host.BroadcastEvent(new List<int> { info.ActorNr }, 0, EventCodes.Interest, payload, 0);
        info.Cancel();
    }

    private void LogInterests(int actorNumber, byte[] data)
    {
        for (var offset = 0; offset + 6 <= data.Length; offset += 6)
        {
            var viewId = BitConverter.ToInt32(data, offset);
            var interest = BitConverter.ToInt16(data, offset + 4);
            _log.Debug($"Kumo: actor {actorNumber} set interest {interest} for view {viewId}.");
        }
    }
}
