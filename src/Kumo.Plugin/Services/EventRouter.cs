using System.Collections;
using Kumo.Plugin.Protocol;
using Photon.Hive.Plugin;
using Newtonsoft.Json;

namespace Kumo.Plugin.Services;

public sealed class EventRouter
{
    private readonly IPluginHost _host;
    private readonly IPluginLogger _log;
    private readonly PropertyService _properties;
    private readonly ProfileRefreshService _profiles;
    private readonly ModerationService _moderation;
    private readonly InterestService _interest;
    private readonly InteractionPermissionService _interactions;
    private readonly InstantiationService _instantiation;

    public EventRouter(
        IPluginHost host,
        IPluginLogger log,
        PropertyService properties,
        ProfileRefreshService profiles,
        ModerationService moderation,
        InterestService interest,
        InteractionPermissionService interactions,
        InstantiationService instantiation)
    {
        _host = host;
        _log = log;
        _properties = properties;
        _profiles = profiles;
        _moderation = moderation;
        _interest = interest;
        _interactions = interactions;
        _instantiation = instantiation;
    }

    public void Handle(IRaiseEventCallInfo info)
    {
        info.Request.Cache = CacheOperations.DoNotCache;

        switch (info.Request.EvCode)
        {
            case 0:
            case EventCodes.ExecutiveMessage:
                info.Fail("Unauthorized.");
                return;

            case EventCodes.Voice:
            case EventCodes.PastEvents:
            case EventCodes.SyncEvents:
            case EventCodes.InitialSyncFinished:
            case EventCodes.ProcessEvent:
            case EventCodes.Serialization:
            case EventCodes.Udon:
            case EventCodes.AdditionalSync:
                info.Continue();
                return;

            case EventCodes.Interest:
                _interest.Handle(info);
                return;

            case EventCodes.ExecutiveAction:
                _moderation.Handle(info);
                return;

            case EventCodes.UserRecordUpdate:
                _profiles.Refresh(info.ActorNr);
                info.Cancel();
                return;

            case EventCodes.Properties:
                HandleProperties(info);
                return;

            case EventCodes.InteractionPermissions:
                if (info.Request.Parameters.TryGetValue(245, out var permissionData))
                    _interactions.Update(info.ActorNr, permissionData);
                info.Cancel();
                return;

            case EventCodes.Instantiate:
                _instantiation.Handle(info);
                return;
        }

        if (info.Request.EvCode >= 200)
        {
            info.Continue();
            return;
        }

        _log.Warn($"Kumo: unknown event code {info.Request.EvCode} from actor {info.ActorNr}: " +
                  JsonConvert.SerializeObject(info.Request.Data));
        info.Cancel();
    }

    private void HandleProperties(IRaiseEventCallInfo info)
    {
        if (info.Request.Data is not Hashtable properties)
        {
            info.Fail("Invalid properties.");
            return;
        }

        if (!_properties.TryPrepare(info.ActorNr, properties, out var prepared, out var error))
        {
            info.Fail(error);
            return;
        }

        _host.SetProperties(info.ActorNr, prepared, null, true);
        _properties.Broadcast(info.ActorNr, prepared);
        info.Cancel();
    }
}
