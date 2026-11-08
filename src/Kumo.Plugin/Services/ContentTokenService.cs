using System.Collections;
using Kumo.Plugin.Protocol;
using Photon.Hive.Plugin;

namespace Kumo.Plugin.Services;

/// <summary>
/// Answers the client's content token requests (event 73).
/// </summary>
/// <remarks>
/// <para>
/// Before downloading an avatar in a room, the 2026 client asks the room server for a file
/// token: event 73 <c>{0: fileId, 1: fileVersion, 2: variant, 3: ?}</c>. Its content token
/// manager (the client's "[CTM]") waits up to 30 seconds for the answer and, when none comes,
/// downloads without one. That wait was the 30 seconds every in-room avatar took to appear; the
/// menu preview, which is not in a room, never asked.
/// </para>
/// <para>
/// The answer is event 73 back to the requester, as actor 0, with a Hashtable:
/// <c>fileId</c> (string), <c>fileVersion</c> (int), <c>variant</c> (string), <c>avatarToken</c>
/// (string) and <c>avatarTokenExp</c> (long, Unix seconds). The client sends the token with the
/// download as <c>x-vrc-filetoken</c>; our file host does not check it, so any value works.
/// </para>
/// <para>
/// The request is for the server only. Relayed to other players, their token manager rejects it
/// ("[CTM] Invalid event data"), which is what every two-player join used to log.
/// </para>
/// </remarks>
public sealed class ContentTokenService
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromHours(1);

    private readonly IPluginHost _host;
    private readonly IPluginLogger _log;
    private int _answered;

    public ContentTokenService(IPluginHost host, IPluginLogger log)
    {
        _host = host;
        _log = log;
    }

    public void Answer(int actor, object? request)
    {
        if (request is not IDictionary fields || Field(fields, 0) is not string fileId)
        {
            _log.Warn($"Kumo: unreadable content token request from actor {actor}");
            return;
        }

        var reply = new Hashtable
        {
            ["fileId"] = fileId,
            ["fileVersion"] = Convert.ToInt32(Field(fields, 1) ?? 0),
            ["avatarToken"] = Guid.NewGuid().ToString("N"),
            ["avatarTokenExp"] = DateTimeOffset.UtcNow.Add(Lifetime).ToUnixTimeSeconds(),
        };
        if (Field(fields, 2) is string variant) reply["variant"] = variant;

        _host.BroadcastEvent(new List<int> { actor }, 0, EventCodes.AvatarFile, PhotonPayload.Wrap(0, reply), 0);
        if (Interlocked.Increment(ref _answered) <= 20)
            _log.Info($"Kumo: content token for actor {actor}: {fileId} v{reply["fileVersion"]} {reply["variant"] ?? "-"}");
    }

    // The request's keys arrive as bytes; look them up whichever integer type they came as.
    private static object? Field(IDictionary fields, int key)
    {
        foreach (DictionaryEntry e in fields)
            if (e.Key is IConvertible k && Convert.ToInt32(k) == key)
                return e.Value;
        return null;
    }
}
