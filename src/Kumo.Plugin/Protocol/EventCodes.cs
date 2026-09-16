namespace Kumo.Plugin.Protocol;

public static class EventCodes
{
    public const byte Voice = 1;
    public const byte ExecutiveMessage = 2;
    public const byte PastEvents = 3;
    public const byte SyncEvents = 4;
    public const byte InitialSyncFinished = 5;
    public const byte ProcessEvent = 6;
    public const byte Serialization = 7;
    public const byte Interest = 8;
    public const byte Udon = 9;
    public const byte AdditionalSync = 15;
    public const byte ExecutiveAction = 33;
    public const byte RateLimits = 34;
    public const byte RateLimitTick = 35;
    public const byte UserRecordUpdate = 40;
    public const byte Properties = 42;
    public const byte InteractionPermissions = 60;
    public const byte Instantiate = 202;
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
