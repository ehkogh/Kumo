namespace Kumo.Plugin.Protocol;

/// <summary>
/// Photon event codes, as the 2026 client (build 2026.3.3p2-1921) uses them. Kumo serves that
/// client only.
/// </summary>
/// <remarks>
/// <para>
/// Most of the 2022 table survived: the 2026 client still instantiates its player on 202, sets
/// properties on 42, answers moderation on 33 and reports interaction permissions on 60. What
/// changed is the per-frame traffic (movement 7 → 16, sync 9 → 12), the user-record update
/// (40 → 13), and a few new codes. Payloads of the new ones are FlatBuffers
/// (<c>VRC.Core.Networking.FlatBuffers</c>).
/// </para>
/// <para>
/// Each code says how it was confirmed. "2022, not yet seen on 2026" codes are kept because the
/// rest of that table carried over; drop any that two-player sessions show the client never uses.
/// Codes not listed are dropped, unless discovery mode relays them (up to <see cref="DiscoveryRange"/>).
/// </para>
/// </remarks>
public static class EventCodes
{
    // ---- Raised by clients: seen from the 2026 client ---------------------------------------

    /// <summary>Encoded uSpeak audio; observed after the server enables voice with event 8.</summary>
    public const byte Voice = 1;

    /// <summary>Client view-interest updates; the server replies with a Boolean on the same code.</summary>
    public const byte Interest = 8;

    /// <summary>Udon/object sync startup traffic observed in Elevator Anomaly; payload schema unconfirmed.</summary>
    public const byte UdonSyncControl = 10;

    /// <summary>Udon variable sync observed carrying SeasonHalloweenRecompense and TimeGamePlaying.</summary>
    public const byte UdonVariableSync = 11;

    /// <summary>Binary sync of players and networked objects, periodic; relayed. (2022: 9.)</summary>
    public const byte Sync = 12;

    /// <summary>
    /// Avatar parameter sync (expressions, gestures): FlatBuffers, sent whenever parameters
    /// change, so constantly in a busy room. Relayed. First mistaken for the user-record update
    /// because, alone in a room, it fires only around avatar changes; handled that way it was
    /// never relayed (expressions did not sync) and every one triggered a profile re-read.
    /// </summary>
    public const byte AvatarParameters = 13;

    /// <summary>Movement, about 15 a second; relayed. (2022: 7.)</summary>
    public const byte Movement = 16;

    /// <summary>New in 2026, payload <c>[[]]</c> on join; meaning unknown. Relayed.</summary>
    public const byte Unknown23 = 23;

    /// <summary>
    /// Ownership: <c>[viewId, newOwner]</c>. Clients raise it to take an object; the server
    /// records it and announces it to everyone as actor 0 (OwnershipService). The client handles
    /// it with the same code as 210.
    /// </summary>
    public const byte OwnershipTransfer = 22;

    /// <summary>
    /// New in 2026: the joiner's persistence metadata, raised once after 23:
    /// <c>[1, 0, byte[] gzip([int32 length][FlatBuffers32 WorldMetadata])]</c>. The server
    /// records it and restores the player (PersistenceService). Not relayed.
    /// </summary>
    public const byte WorldMetadata = 25;

    /// <summary>
    /// Moderation. The server sends actions (warn, kick, mic off) and requests each joiner's block
    /// and mute lists (type 20); clients answer that request on this code (type 21).
    /// </summary>
    public const byte ExecutiveAction = 33;

    /// <summary>
    /// The rate-limit table. The server sends it; the 2026 client also raises this code itself,
    /// with no payload, and waits in "Waiting for network limits" until it has the table.
    /// </summary>
    public const byte RateLimits = 34;

    /// <summary>
    /// "My record changed, read it again": the 2026 client raises this (payload <c>1</c>) after
    /// every avatar switch, once its <c>PUT /avatars/{id}/select</c> has gone through. The server
    /// re-reads the player from the API and sends the new properties (ProfileRefreshService);
    /// without that the new avatar never reached anyone, the player included. Not relayed.
    /// </summary>
    public const byte UserUpdate = 40;

    /// <summary>Player properties (eye and hip height from clients; the full set from the server).</summary>
    public const byte Properties = 42;

    /// <summary>Who may interact with whom (PhysBones, contacts).</summary>
    public const byte InteractionPermissions = 60;

    /// <summary>
    /// New in 2026: a content token request for the file a player is about to download,
    /// <c>{0: fileId, 1: version, 2: variant, 3: ?}</c>. The server answers on the same code
    /// (ContentTokenService); unanswered, the client waits 30 s before every in-room avatar
    /// download. Not relayed: other clients reject it. The server also re-reads the player from
    /// the API, since a new avatar is the usual reason for the request.
    /// </summary>
    public const byte AvatarFile = 73;

    /// <summary>
    /// New in 2026: a shared object (seen: stickers; the same layout serves props, portals and
    /// prints). <c>0</c> object type (8 = sticker), <c>1</c> action (0 Create, 1 Destroy, 2 Update),
    /// <c>2</c> object id, <c>4</c>/<c>5</c> packed position/rotation, <c>129</c> item id,
    /// <c>130</c> scale, <c>131</c> the spawn token from the API's <c>/inventory/spawn</c>.
    /// A Create needs keys the server adds (receivers refuse it otherwise): <c>9</c> bool,
    /// <c>11</c> the placing actor, <c>128</c> int. See SharingService.
    /// </summary>
    public const byte ItemSpawn = 74;

    /// <summary>
    /// New in 2026: an emoji, <c>{0: item id, 1: spawn token}</c>, after <c>/inventory/spawn</c>.
    /// Relayed as is; receivers need nothing added.
    /// </summary>
    public const byte Emoji = 76;

    /// <summary>New in 2026: chatbox text, payload the message as a string. Relayed.</summary>
    public const byte Chatbox = 43;

    /// <summary>New in 2026: chatbox typing indicator, payload a small int (state). Relayed.</summary>
    public const byte ChatboxTyping = 44;

    /// <summary>Instantiation of the player object (<c>VRCPlayer</c>, views actor×100000+1..3).</summary>
    public const byte Instantiate = 202;

    // ---- Raised by clients: 2022 codes, not yet seen on 2026 --------------------------------

    public const byte PastEvents = 3;
    public const byte SyncEvents = 4;
    public const byte InitialSyncFinished = 5;
    public const byte ProcessEvent = 6;
    public const byte Udon = 9;
    public const byte AdditionalSync = 15;

    /// <summary>
    /// The top of the codes discovery mode relays when unknown: everything below the Photon-
    /// reserved range. The client's own enum has 24 values (0..23), but it raises codes beyond it
    /// too (73, 74), so the range is not the enum.
    /// </summary>
    public const byte DiscoveryRange = 199;

    // ---- Sent only by the server ------------------------------------------------------------

    /// <summary>The message shown to a player being removed (kick). 2022 code; unconfirmed on 2026.</summary>
    public const byte ExecutiveMessage = 2;

    /// <summary>Per-second rate-limit tick.</summary>
    public const byte RateLimitTick = 35;

    // The two below were read from the client's network event dispatcher (GameAssembly RVA
    // 0x1e469b0, build 2026.3.3p2-1921), which handles 8, 12, 18, 20-22, 24, 26-29, 33, 61, 74,
    // 197, 198, 209 and 210 from the server.

    /// <summary>
    /// Serialization recovery for late joiners. Parameter 245 is <c>object[4]</c>:
    /// <c>[int, int, int, byte[]]</c> (the ints look like id, part, part count; parts are
    /// reassembled by <c>SerializationRecoveryHandler</c>); the bytes are gzip of a FlatBuffers32
    /// <c>WorldMetadata</c>, and each <c>ObjectMetadataPair</c> in it is re-dispatched locally
    /// as event 198 from sender 0 (the client then finds the view and applies the bunches).
    /// Sent as actor 0 by PersistenceService with players' persistence objects.
    /// </summary>
    public const byte SerializationRecovery = 24;

    /// <summary>
    /// A player's persistence has been restored. Parameter 245: that player's actor number
    /// (Int32). The client raises Udon's <c>_onPlayerRestored</c> for that player and marks it
    /// restored; worlds wait for it. Found by the "_onPlayerRestored" literal being loaded in
    /// this handler. See PersistenceService.
    /// </summary>
    public const byte PlayerRestored = 28;

    /// <summary>
    /// Sets a per-player Boolean on the sender's VRCPlayer (field 0x4a); the client ignores
    /// <c>true</c> for the local player. Meaning unknown. It is NOT persistence-restored: a
    /// session sending it after 25 changed nothing in Udon. Never sent.
    /// </summary>
    public const byte PlayerFlag26 = 26;
}

public static class ExecutiveActionFields
{
    public const byte Type = 0;
    public const byte TargetUser = 1;
    public const byte Message = 2;
    public const byte MainProperty = 3;
    public const byte Heading = 5;
    public const byte BlockedUsers = 10;
    public const byte MutedUsers = 11;
}

public static class ExecutiveActionTypes
{
    public const byte Warn = 3;
    public const byte Kick = 4;
    public const byte MicOff = 8;
    public const byte RequestPlayerModerations = 20;
    public const byte ReplyPlayerModerations = 21;
    public const byte BlockUser = 22;
    public const byte MuteUser = 23;
}
