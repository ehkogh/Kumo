using System.Collections;
using Kumo.Plugin.Api;
using Kumo.Plugin.Configuration;
using Kumo.Plugin.Models;
using Kumo.Plugin.Runtime;
using Kumo.Plugin.Services;
using Photon.Hive.Plugin;

namespace Kumo.Plugin;

public sealed class KumoPlugin : PluginBase
{
    private RoomState? _state;
    private IKumoApiClient? _api;
    private IPluginLogger? _log;
    private AuthorizationService? _authorization;
    private PropertyService? _properties;
    private ModerationService? _moderation;
    private RateLimitService? _rateLimits;
    private InteractionPermissionService? _interactions;
    private EventRouter? _events;
    private PersistenceService? _persistence;

    public override string Name => "Kumo";

    public override string Version =>
        typeof(KumoPlugin).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    public override bool SetupInstance(IPluginHost host, Dictionary<string, string> config, out string errorMsg)
    {
        if (!KumoOptions.TryCreate(config, out var options, out errorMsg))
            return false;

        if (!base.SetupInstance(host, config, out errorMsg))
            return false;

        var log = host.CreateLogger(Name);
        var state = new RoomState();
        var api = new LegacyBackendApiClient(options);

        try
        {
            var remote = api.GetRuntimeConfig();
            if (remote is not null)
            {
                foreach (var pair in remote.RateLimitList)
                {
                    if (pair.Key >= byte.MinValue && pair.Key <= byte.MaxValue)
                        state.RateLimits[(byte)pair.Key] = pair.Value;
                }

                state.RateLimiterActive = remote.RatelimiterActive;
                state.MaxAccountsPerIp = remote.MaxAccsPerIp > 0 ? remote.MaxAccsPerIp : 5;
            }
        }
        catch (Exception ex)
        {
            log.Warn($"Kumo: runtime configuration unavailable; using defaults: {ex.Message}");
        }

        var properties = new PropertyService(state, host);
        var moderation = new ModerationService(state, host, log);
        var rateLimits = new RateLimitService(state, host);
        var interactions = new InteractionPermissionService(state, host);
        var profiles = new ProfileRefreshService(state, api, host, properties);
        var interest = new InterestService(host, log);
        var instantiation = new InstantiationService(state, host, log);
        var persistence = new PersistenceService(state, host, log, new FilePersistenceStore(options.PersistencePath));
        _persistence = persistence;

        _state = state;
        _api = api;
        _log = log;
        _authorization = new AuthorizationService(api, log);
        _properties = properties;
        _moderation = moderation;
        _rateLimits = rateLimits;
        _interactions = interactions;
        _events = new EventRouter(
            host,
            log,
            properties,
            profiles,
            moderation,
            interest,
            interactions,
            instantiation,
            rateLimits,
            new SharingService(host, log),
            persistence,
            options.DiscoveryMode);
        if (options.DiscoveryMode)
            log.Info("Kumo: discovery mode on; unmapped 2026 event codes are relayed and logged.");

        rateLimits.Start();
        persistence.Start();
        log.Info($"Kumo: player persistence stored in {options.PersistencePath}");
        errorMsg = string.Empty;
        return true;
    }

    public override void OnCreateGame(ICreateGameCallInfo info)
    {
        if (!Ready(info))
            return;

        if (info.Request.SuppressRoomEvents ||
            (info.Request.RoomFlags & RoomOptionFlags.SuppressPlayerInfo) != 0)
        {
            info.Fail("Suppressed rooms are not allowed.");
            return;
        }

        info.Request.RoomFlags = 35;
        _state!.GameId = info.Request.GameId;

        var identity = InLocalRoom
            ? _authorization!.AuthorizeLocal(info.UserId)
            : _authorization!.Authorize(
                ExtractJoinToken(info.Request.Parameters),
                info.UserId,
                _state.GameId,
                onCreate: true);

        if (!identity.Valid)
        {
            info.Fail("Invalid join token.");
            return;
        }

        // A local room has no world record behind it, so no capacity; it holds its one player.
        _state.Capacity = Math.Max(1, identity.WorldCapacity);
        _state.WorldAuthor = identity.WorldAuthor ?? string.Empty;
        _state.InstanceCreator = identity.InstanceCreator ?? string.Empty;

        var actor = CreateActor(1, identity);
        _state.Actors[1] = actor;

        if (!_properties!.TryPrepare(1, info.Request.ActorProperties, out var prepared, out var error))
        {
            _state.Actors.TryRemove(1, out _);
            RejectAuthorizedJoin(info, identity.User.Id, error);
            return;
        }

        info.Request.ActorProperties = prepared;
        info.Continue();
        _moderation!.RequestExistingForActor(1);
        _rateLimits!.SendConfiguration(1);
    }

    public override void OnJoin(IJoinGameCallInfo info)
    {
        if (!Ready(info))
            return;

        var identity = InLocalRoom
            ? _authorization!.AuthorizeLocal(info.UserId)
            : _authorization!.Authorize(
                ExtractJoinToken(info.Request.Parameters),
                info.UserId,
                _state!.GameId,
                onCreate: false);

        if (!identity.Valid)
        {
            info.Fail("Invalid join token.");
            return;
        }

        var isStaff = identity.User.Tags.Contains("admin_moderator");
        var isCapacityExempt = isStaff ||
                               identity.User.Id == _state!.WorldAuthor ||
                               identity.User.Id == _state.InstanceCreator;

        if (_state!.Capacity > 0 && PluginHost.GameActors.Count >= _state.Capacity * 2 && !isCapacityExempt)
        {
            RejectAuthorizedJoin(info, identity.User.Id, "Game is full.");
            return;
        }

        if (!isStaff && _state.Actors.Values.Any(actor => actor.UserId == identity.User.Id))
        {
            RejectAuthorizedJoin(info, identity.User.Id, "User is already in this room.");
            return;
        }

        if (!isStaff)
        {
            var ipCount = _state.Actors.Values.Count(actor => actor.IpAddress == identity.Ip);
            if (ipCount >= _state.MaxAccountsPerIp)
            {
                RejectAuthorizedJoin(info, identity.User.Id, "Maximum account limit per instance reached.");
                return;
            }
        }

        var actor = CreateActor(info.ActorNr, identity);
        _state.Actors[info.ActorNr] = actor;

        if (!_properties!.TryPrepare(info.ActorNr, info.Request.ActorProperties, out var prepared, out var error))
        {
            _state.Actors.TryRemove(info.ActorNr, out _);
            RejectAuthorizedJoin(info, identity.User.Id, error);
            return;
        }

        info.Request.ActorProperties = prepared;
        info.Continue();

        _moderation!.RequestExistingForActor(info.ActorNr);
        _moderation.RequestNewActorFromOthers(info.ActorNr);
        _rateLimits!.SendConfiguration(info.ActorNr);
        _interactions!.Announce();
    }

    public override void OnLeave(ILeaveGameCallInfo info)
    {
        _persistence?.OnLeave(info.ActorNr);
        if (_state is not null && _api is not null && _state.Actors.TryRemove(info.ActorNr, out var actor) && !InLocalRoom)
        {
            try
            {
                _api.PlayerLeft(_state.GameId, actor.UserId);
            }
            catch (Exception ex)
            {
                _log?.Warn($"Kumo: failed to report player leave: {ex.Message}");
            }
        }

        info.Continue();
    }

    public override void OnCloseGame(ICloseGameCallInfo info)
    {
        _rateLimits?.Stop();
        _persistence?.OnClose();

        if (_state is not null && _api is not null && !InLocalRoom)
        {
            try
            {
                _api.GameClosed(_state.GameId);
            }
            catch (Exception ex)
            {
                _log?.Warn($"Kumo: failed to report game close: {ex.Message}");
            }
        }

        info.Continue();
    }

    public override void BeforeSetProperties(IBeforeSetPropertiesCallInfo info)
    {
        if (_properties is null)
        {
            info.Fail("Kumo is not initialized.");
            return;
        }

        if (info.Request.ActorNumber == 0 || info.Request.ActorNumber != info.ActorNr)
        {
            PluginHost.RemoveActor(info.ActorNr, "Invalid actor property target.");
            info.Fail("Invalid actor property target.");
            return;
        }

        if (!_properties.TryPrepare(info.ActorNr, info.Request.Properties, out var prepared, out var error))
        {
            info.Fail(error);
            return;
        }

        info.Request.Broadcast = true;
        info.Request.Properties = prepared;
        info.Request.Parameters[251] = prepared;
        info.Continue();
    }

    public override void OnSetProperties(ISetPropertiesCallInfo info) => info.Continue();

    public override void OnRaiseEvent(IRaiseEventCallInfo info)
    {
        if (_events is null)
        {
            info.Fail("Kumo is not initialized.");
            return;
        }

        _events.Handle(info);
    }

    /// <summary>
    /// A local room's presence is not the API's business: it never placed the player there, and
    /// reporting a leave or close for a room it does not know fails.
    /// </summary>
    private bool InLocalRoom => AuthorizationService.IsLocalRoom(_state?.GameId);

    private bool Ready(ICallInfo info)
    {
        if (_state is not null && _authorization is not null && _properties is not null)
            return true;

        info.Fail("Kumo is not initialized.");
        return false;
    }

    private static ActorState CreateActor(int actorNumber, JoinValidationResponse identity) => new()
    {
        ActorNumber = actorNumber,
        UserId = identity.User.Id,
        IpAddress = identity.Ip,
        Identity = identity,
    };

    private void RejectAuthorizedJoin(ICallInfo info, string userId, string reason)
    {
        if (_api is not null && _state is not null && !InLocalRoom)
        {
            try
            {
                _api.PlayerLeft(_state.GameId, userId);
            }
            catch (Exception ex)
            {
                _log?.Warn($"Kumo: failed to roll back rejected join presence: {ex.Message}");
            }
        }

        info.Fail(reason);
    }

    private static string? ExtractJoinToken(Dictionary<byte, object> parameters)
    {
        if (!parameters.TryGetValue(248, out var raw) || raw is not Hashtable auth)
            return null;

        return auth[(byte)2] as string;
    }
}
