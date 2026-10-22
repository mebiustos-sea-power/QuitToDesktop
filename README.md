# Quit to Desktop

Adds **Quit Sea Power** to the bottom of the shared Esc pause menu. The item is available wherever Sea Power uses that menu, including missions, campaigns, tutorials and the encyclopedia.

Selecting it opens a separate application-exit confirmation. An amber warning band and icon, an explicit title, and a distinct exit button distinguish it from the game's mission-exit confirmation. **Cancel** receives initial keyboard focus. Esc cancels this dialog and keeps the pause menu open. Tab switches between the two buttons; Enter or Space activates the focused button.

The mod requests normal Unity application exit only after explicit approval. It does not automatically save progress or finalize mission/battle results. Save using the game's existing controls before quitting if needed. The existing mission-exit action and its confirmation remain available.

UI patches are installed on a later Unity update after the outermost successful `NoesisUnity.Init` call has returned. The early startup observer does not patch or initialize UI types. Reentrant initialization calls cannot trigger installation midway through the outer call. If initialization or patch installation fails, the mod records an error and leaves its UI extension unavailable; partial product patches are rolled back.

Mod-owned Anchor Chain, initialization, Unity update, game-hook, button, language and dialog callbacks catch ordinary `Exception` values and log their context. Exceptions originating in the game's own Noesis initialization retain the game's existing handling. Native process faults are outside managed exception handling.

## Language

The menu entry and every confirmation label follow the game's language setting. English and Japanese are included; every other language uses English. Language changes update the existing menu item and an open confirmation.

## Requirements and installation

- Sea Power 0.8.7 (the version used for initial verification).
- BepInEx and Anchor Chain, already required by Anchor Chain code mods.

When ready to install, close Sea Power, copy the packaged `QuitToDesktop` folder to `Sea Power_Data/StreamingAssets`, enable **Quit to Desktop** and **Anchor Chain** in the mod manager, and restart the game. To disable it, uncheck it and restart. The build script only creates a local package; it never installs it.

The plugin ID is `mebiustos.quittodesktop`. Dynamic Campaign Mod is optional. The shared menu-assignment hook also handles menus rebuilt by other mods; the installed Dynamic Campaign Mod's battle-menu filter is included in compatibility checks.

## Build and verification

Use a Windows .NET SDK with the .NET 10 and .NET Standard 2.1 targeting packs. Copy `Directory.Build.local.props.example` to `Directory.Build.local.props` and set the game and Anchor Chain paths. No external NuGet packages are required. Game, Unity, Noesis and mod-loader assemblies must come from your own local installations; this repository does not include those DLLs. Keep machine-specific paths in the ignored `Directory.Build.local.props` file; the example file contains placeholders only.

```powershell
& .\build.ps1 -Test
```

The package is written to `dist/QuitToDesktop` and `dist/QuitToDesktop-v0.1.1.zip`. The three package files are the plugin DLL, this README, and `_info.ini`; the bilingual UI and XAML are embedded in the DLL.

Core tests cover mandatory confirmation, cancellation, stale/duplicate approval, menu replacement and ordering, mod-manager activation, language selection, deferred startup, and managed-error logging. Separate tests use the game's actual Mono runtime and native Noesis library without starting or attaching to Sea Power. Before native Noesis initialization, they verify that only the startup observer is installed. They then verify that an initialization failure is logged without installing UI hooks, and that nested initialization waits for the outer return and a later update before installing the real hooks. They also exercise the packaged dialog, native keyboard and mouse routing, language changes, lifecycle cleanup, actual Harmony hooks and the installed campaign's menu filter. Application exit is replaced by a counter in the dialog fixture. A local ignored copy of the game assembly bypasses global game-data loading and the pause-view Unity scene guard; all six product hook methods retain their original IL. The installed game assembly is read-only and is never changed.

These isolated checks do not prove live game input order, rendering under every UI scale, or the game's shutdown behavior with a loaded mission. Those require in-game verification after installation. Live rendering and shutdown with a loaded mission remain unverified.

## Source structure

- `Core/`: confirmation tokens, pause-input suppression, and menu composition.
- `UI/`: the independent modal dialog and its lifecycle.
- `Localization.cs`: English/Japanese strings and selection policy.
- `MenuEntry.cs`: the native button and language-resource integration.
- `Startup/`: initialization completion and deferred installation.
- `GamePatches.cs`: game-specific integration points.
- `FailureBoundary.cs`: shared ordinary-exception handling for mod callbacks.

No game assets, assemblies, save files or settings are rewritten. Local dependency paths, build outputs and `docs/agent-memory.md` are excluded from version control; only the three package files are distributed.
