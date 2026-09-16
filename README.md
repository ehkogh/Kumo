# Kumo

Kumo is a Photon plugin for a VR social game.

## Build

I currently use my own OpenPhoton implementation for Kumo, however a standard Photon plugin dll should work.

```sh
dotnet build Kumo.slnx
```

The plugin assembly is `Kumo.Plugin.dll` and the factory type is
`Kumo.Plugin.KumoPluginFactory`.

## Releases

Release builds are tag-driven. Pushing a tag such as `v0.1.0` builds Kumo against
the latest published OpenPhoton release, stamps the plugin assembly with `0.1.0`,
packages the plugin and its private dependencies, and creates the matching GitHub
release automatically.

The release archive intentionally does not include `PhotonHivePlugin.dll`. That
assembly is the host/plugin contract and must be supplied by the Photon server.

The release workflow can also be run manually with a version number to rehearse a
build without publishing a GitHub release.

## Configuration

```xml
<Plugin Name="Kumo"
        AssemblyName="Kumo.Plugin.dll"
        Type="Kumo.Plugin.KumoPluginFactory"
        ApiUrl="http://127.0.0.1:8080"
        PhotonApiSecret="change-me" />
```

## License

Kumo is derived from AGPL-3.0 code and remains licensed under AGPL-3.0.
