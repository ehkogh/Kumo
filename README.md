# Kumo

Kumo is a compatibility plugin for a Photon-based VR social game. It keeps game-specific
room behavior outside the Photon server itself and is structured so support for different
client generations can evolve without turning the plugin into one giant callback file.

The current compatibility target is the 2022 client generation supported by the original
plugin. The implementation ports that behavior while splitting
backend access, actor state, property validation, moderation, event routing, instantiation,
rate-limit announcements, and interaction permissions into separate components.

## Build

Kumo currently references the local OpenPhoton `PhotonHivePlugin` contract for development:

```sh
dotnet build Kumo.slnx
```

The plugin assembly is `Kumo.Plugin.dll` and the factory type is
`Kumo.Plugin.KumoPluginFactory`.

## Configuration

Kumo accepts the legacy backend settings while the backend abstraction is being generalized:

```xml
<Plugin Name="Kumo"
        AssemblyName="Kumo.Plugin.dll"
        Type="Kumo.Plugin.KumoPluginFactory"
        ApiUrl="http://127.0.0.1:8080"
        PhotonApiSecret="change-me" />
```

## License

Kumo is derived from AGPL-3.0 code and remains licensed under AGPL-3.0.
