# Plugins

This directory holds AutoNate plugins — runtime-loaded extensions that subscribe to host hooks (actions and filters) defined in `AutoNate.Plugin.Abstractions`.

## Layout

```
plugins/
├── Directory.Build.props      shared csproj settings
├── Directory.Build.targets    post-build "zip the bin output" step
├── HelloPlugin/               sample plugin
│   ├── HelloPlugin.csproj     deliberately empty — Directory.Build.props does the work
│   ├── plugin.json            manifest
│   ├── HelloPlugin.cs         IAutoNatePlugin implementation
│   └── dist/                  produced at build time; HelloPlugin.zip is the upload artifact
```

## Creating a new plugin

1. Copy `HelloPlugin/` to `plugins/MyPlugin/`.
2. Update `plugin.json` (`name`, `entryAssembly`, `entryType`).
3. Replace `HelloPlugin.cs` with your implementation of `IAutoNatePlugin`.
4. Add the project to the solution: `dotnet sln add plugins/MyPlugin/MyPlugin.csproj`.
5. Build: `dotnet build plugins/MyPlugin/MyPlugin.csproj` (or just build the whole solution in Rider).
6. Upload the produced `plugins/MyPlugin/dist/MyPlugin.zip` via the SPA at `/admin/plugins`.

## Build → upload → enable loop

1. **Build** — produces `dist/<PluginName>.zip` after every successful build.
2. **Upload** in `/admin/plugins`. The new row lands in `Disabled` status.
3. **Enable** — the host loads the assembly into a fresh `AssemblyLoadContext` and calls `Configure(registrar, hostServices)`.
4. **Iterate** — to pick up changes after editing your plugin: rebuild, then in the admin UI delete the previous version, re-upload, and enable. Hot reload of an already-loaded plugin is not supported in v1.

## Why the abstractions reference is `Private=false`

`Directory.Build.props` references `AutoNate.Plugin.Abstractions` with `<Private>false</Private>`, so the abstractions DLL is **not** copied into the plugin's `bin/` output (and therefore not into the zip). This is load-bearing:

- The host's `PluginAssemblyLoadContext` deliberately resolves abstractions types from the host's default load context.
- If a plugin shipped its own copy of `AutoNate.Plugin.Abstractions.dll`, the plugin ALC would load that copy, producing a different `Type` for `IAutoNatePlugin` than the host knows. The `(IAutoNatePlugin)pluginInstance` cast would fail silently.

The post-build zip target also explicitly excludes `AutoNate.Plugin.Abstractions.dll` as belt-and-suspenders.
