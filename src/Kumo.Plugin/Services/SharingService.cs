using System.Collections;
using Kumo.Plugin.Runtime;
using Photon.Hive.Plugin;

namespace Kumo.Plugin.Services;

/// <summary>
/// Shared objects (event 74): stickers, and likely props, portals and prints, which share the
/// same property layout (0 object type, 1 action, 2 object id, 4/5 placement, 128+ per type).
/// <para>
/// The sender leaves out what only the server can vouch for. A receiving 2026 client refuses a
/// sticker Create without keys 9, 11 and 128 ("Required Properties: 1,0,2,11,4,5,9,128,129,130"),
/// none of which the sender includes; VRChat's server adds them after reading the spawn token
/// (131). Kumo adds them here, as worked out from the receiving client's own complaints ("the
/// property type map requires type ...", "from a player that doesn't exist"): 9 a boolean (true),
/// 11 the placing actor number, 128 an int (0). With these, stickers show for everyone (2026-10-19).
/// The token is not checked yet.
/// </para>
/// </summary>
public sealed class SharingService
{
    private const byte KeyAction = 1;
    private const byte KeyObjectId = 2;
    /// <summary>A boolean (the client's type map says so); most likely "the server checked the token".</summary>
    private const byte KeyVerified = 9;
    /// <summary>An Int32: the placing player's actor number. With anything else the receiver says "Received a StickerEvent from a player that doesn't exist".</summary>
    private const byte KeyOwnerActor = 11;

    /// <summary>An Int32 (type map); meaning unknown, 0 works.</summary>
    private const byte KeyStickerInt = 128;

    private readonly IPluginHost _host;
    private readonly IPluginLogger _log;

    public SharingService(IPluginHost host, IPluginLogger log)
    {
        _host = host;
        _log = log;
    }

    public void Prepare(IRaiseEventCallInfo info)
    {
        if (info.Request.Data is not IDictionary data) return;
        if (!IsCreate(data[KeyAction])) return;

        if (data[KeyObjectId] is null or "") data[KeyObjectId] = Guid.NewGuid().ToString();
        if (!data.Contains(KeyVerified)) data[KeyVerified] = true;
        if (!data.Contains(KeyOwnerActor)) data[KeyOwnerActor] = info.ActorNr;
        if (!data.Contains(KeyStickerInt)) data[KeyStickerInt] = 0;

        _log.Info($"Kumo: shared object create from actor {info.ActorNr}, filled 2/9/11/128: " +
                  string.Join(", ", data.Keys.Cast<object>().Select(k => $"{k}={data[k]?.GetType().Name}")));
    }

    private static bool IsCreate(object? action) => action switch
    {
        byte b => b == 0,
        int i => i == 0,
        short s => s == 0,
        _ => false,
    };
}
