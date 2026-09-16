using Photon.Hive.Plugin;

namespace Kumo.Plugin;

public sealed class KumoPluginFactory : PluginFactoryBase
{
    public override IGamePlugin CreatePlugin(string pluginName) => new KumoPlugin();
}
