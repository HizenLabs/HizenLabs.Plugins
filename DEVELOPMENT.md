# Local development

How to build, deploy, and iterate on plugins locally. This file is identical in the Free
and Premium repos; they share the same build system (the `shared` submodule).

## Layout

```
<repo>/
  src/...                  the plugin project (your .cs plugins live here)
  shared/                  submodule: build props, bundler, test-env (local Rust servers)
  Directory.Build.props    imports your local overrides, then shared
  Build.local.props        <- you create this (git-ignored)
  Deploy.local.props                <- you create this (git-ignored)
  *.example                templates for the two files above
```

Both `Build.local.props` and `Deploy.local.props` are git-ignored and
per-developer. They are listed in the solution's `build` folder, so Visual Studio shows
them in the tree even before they exist - a missing one appears greyed out, and you can
create it from the matching `.example` next to it.

## The test-env (where managed assemblies come from)

Plugins compile against the game + framework managed assemblies (`Rust`, `Carbon`/`Oxide`,
etc.). Those come from the local installs in `shared/test-env`: `install.ps1` exports each
server's managed set to `shared/test-env/servers/rust-<platform>-<config>/refs`.

Install (and start) the servers from `shared/test-env`:

```powershell
.\install.ps1               # all mods x branches; exports refs
.\start.ps1                 # launch, each in its own console window
.\start.ps1 -Mod Carbon -Branch Release
```

`RustManagedDir` points the build at that `refs` folder for the target you are building.

## 1. Choose the managed assemblies directory (RustManagedDir)

You usually do NOT need to set this - it defaults to this repo's own test-env. Override it
only to reuse ANOTHER checkout's test-env instead of installing your own.

Copy `Build.local.props.example` to `Build.local.props` and set
`RustManagedDir`. The main case is Premium reusing Free's servers:

```xml
<Project>
  <PropertyGroup>
    <RustManagedDir>$(MSBuildThisFileDirectory)../free/shared/test-env/servers/rust-$(Platform.ToLowerInvariant())-$(Configuration.ToLowerInvariant())/refs</RustManagedDir>
  </PropertyGroup>
</Project>
```

With that, you install/start from the Free checkout only; Premium compiles and deploys
against those same live servers. No second install, no clash.

## 2. Choose which plugins to deploy (Deploy.local.props)

Copy `Deploy.local.props.example` to `Deploy.local.props`. Set `DeployEnabled` and list
plugins by NAME - the `.cs` filename, no path and no extension:

```xml
<Project>
  <PropertyGroup>
    <DeployEnabled>true</DeployEnabled>
  </PropertyGroup>
  <ItemGroup>
    <DeployPlugin Include="ServerBroadcast" />
  </ItemGroup>
</Project>
```

Each name is resolved to its source anywhere under `src/`. A name with no matching `.cs`
fails the build with a clear message (so a typo does not silently no-op).

## 3. Build

Build the plugin project for a target (Configuration x Platform). On build, each listed
plugin is bundled to a single compile-checked `.cs` and copied into the matching server's
plugins folder, which hot-reloads it:

| Build              | Server              |
| ------------------ | ------------------- |
| Release \| Carbon  | rust-carbon-release |
| Release \| Oxide   | rust-oxide-release  |
| Staging \| Carbon  | rust-carbon-staging |
| Staging \| Oxide   | rust-oxide-staging  |

If the bundle fails to compile, the build fails and nothing is copied to the server; the
staged `.cs` is left under `obj/bundled/` for inspection.

## Keeping shared up to date

The build system lives in the `shared` submodule. If deploy-by-name or bundling behaves
unexpectedly, your submodule may be behind. Update it:

```bash
git submodule update --remote shared
```
