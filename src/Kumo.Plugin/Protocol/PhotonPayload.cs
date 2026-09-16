namespace Kumo.Plugin.Protocol;

public static class PhotonPayload
{
    public static Dictionary<byte, object> Wrap(int actorNumber, object? data) => new()
    {
        [245] = data!,
        [254] = actorNumber,
    };
}
