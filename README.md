# Kumo

Kumo is a Photon plugin for a VR social game.

## Build

I currently use my own OpenPhoton implementation for Kumo, however a standard Photon plugin dll should work.

```sh
dotnet build Kumo.slnx
```

The plugin assembly is `Kumo.Plugin.dll` and the factory type is
`Kumo.Plugin.KumoPluginFactory`.

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
