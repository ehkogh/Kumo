using System.IO.Compression;
using Kumo.Plugin.Protocol;
using Photon.Hive.Plugin;

namespace Kumo.Plugin.Services;

/// <summary>
/// Interest (event 8): which of the others' views a player wants, and at what level. The 2026
/// client expects a server-originated Boolean response. It stores that value as its room voice
/// state, which the speaker checks before raising a voice event.
/// </summary>
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

        var payload = new Dictionary<byte, object>
        {
            [245] = true,
            [254] = 0,
        };

        _host.BroadcastEvent(new List<int> { info.ActorNr }, 0, EventCodes.Interest, payload, 0);
        info.Cancel();
    }

    /// <summary>
    /// 2026 layout: entries of a level byte then an Int32 view id; a leading 0xFF means the rest is
    /// gzip of the same. (2022 was view id then Int16 level.)
    /// </summary>
    private void LogInterests(int actorNumber, byte[] data)
    {
        try
        {
            if (data.Length > 0 && data[0] == 0xFF)
            {
                using var gzip = new GZipStream(new MemoryStream(data, 1, data.Length - 1), CompressionMode.Decompress);
                using var plain = new MemoryStream();
                gzip.CopyTo(plain);
                data = plain.ToArray();
            }

            for (var offset = 0; offset + 5 <= data.Length; offset += 5)
            {
                var level = data[offset];
                var viewId = BitConverter.ToInt32(data, offset + 1);
                _log.Debug($"Kumo: actor {actorNumber} set interest {level} for view {viewId}.");
            }
        }
        catch (InvalidDataException)
        {
            _log.Debug($"Kumo: actor {actorNumber} sent an interest list that did not decompress.");
        }
    }
}
