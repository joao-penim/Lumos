# Contributing to Lumos

Thanks for helping! Bug reports, ideas and pull requests are all welcome.

- **Bugs and ideas**: open an [issue](https://github.com/joao-penim/Lumos/issues/new/choose). For bugs, attach `%APPDATA%\Lumos\agent.log` and list your displays (`LumosAgent.exe --hdr list`).
- **Pull requests**: keep them focused on one change. Make sure `dotnet build` has no warnings (warnings are errors) and `dotnet test` passes. CI checks both.
- **Security issues**: please don't open a public issue. See [SECURITY.md](SECURITY.md).

## Building from source

Requirements:

- Windows 10/11.
- The [.NET 10 SDK](https://dotnet.microsoft.com/download).
- Optional: Visual Studio or Build Tools with the **Desktop development with C++** workload, for the Native AOT agent build.

```powershell
dotnet build Lumos.slnx
dotnet test --project tests/Lumos.Core.Tests
dotnet run --project src/Lumos.App        # Debug builds put LumosAgent.exe next to Lumos.exe
```

Package everything like a release does (output in `publish/`):

```powershell
powershell -ExecutionPolicy Bypass -File tools/publish.ps1 -Zip
```

Without the C++ workload, `publish.ps1` publishes the agent as a regular self-contained app instead of Native AOT. It works the same and uses a bit more memory. CI always builds the Native AOT version.

### Handy agent commands

```
LumosAgent.exe                     start the agent (or open the UI if it's already running)
LumosAgent.exe --launch <appId>    HDR on, then start the app (what desktop shortcuts use)
LumosAgent.exe --exit              stop the running agent
LumosAgent.exe --hdr list          list displays and their HDR state
LumosAgent.exe --hdr on|off [id]   switch HDR (primary display by default)
```

## How it works

Lumos is two processes, so the part that's always running stays tiny:

| | `LumosAgent.exe` | `Lumos.exe` |
|---|---|---|
| Job | Detects apps, switches HDR, tray icon, IPC server | Settings UI (IPC client) |
| Tech | Plain .NET + Win32 P/Invoke, Native AOT, no UI framework | Avalonia + FluentAvalonia |
| Lifetime | From sign-in (optional) | Only while the window is open |

The agent is entirely event-driven and never polls:

- **App start**: `SetWinEventHook(EVENT_OBJECT_CREATE)` for new top-level windows. Games create their window before their swapchain, so HDR is usually on before the game checks for it.
- **App exit**: `RegisterWaitForSingleObject` on the process handle. All waits share one thread-pool wait thread. If a process can't be waited on (for example, anti-cheat denies `SYNCHRONIZE`), Lumos checks that one process every 2 s while it runs.
- **Tray and IPC**: one thread blocked in `GetMessage`, plus async named-pipe I/O (`\\.\pipe\Lumos.Agent`, current user only, newline-delimited JSON).

HDR is switched with the CCD API, the same one Windows Settings uses:

- **Windows 11 24H2 and newer**: `DISPLAYCONFIG_DEVICE_INFO_SET_HDR_STATE`.
- **Older builds**: `SET_ADVANCED_COLOR_STATE`.
- Displays are identified by their monitor device path, which stays the same across reboots.

All the decision rules live in `HdrSessionManager`:

- Per-display reference counting.
- Ownership: only HDR that Lumos turned on is ever turned off.
- The grace delay, holds for "Launch with HDR", and crash recovery via `state.json`.

`HdrSessionManager` has no OS calls and is covered by unit tests with fake displays and timers.

## Project layout

```
src/Lumos.Core/              Platform-neutral: models, settings, HdrSessionManager, IPC contracts
src/Lumos.Platform.Windows/  CCD HDR controller, WinEvent process watcher, autostart, shortcuts, shell helpers
src/Lumos.Agent/             Background agent: message loop, tray icon, named-pipe server
src/Lumos.App/               Avalonia + FluentAvalonia settings UI
tests/Lumos.Core.Tests/      Session-manager, IPC and settings tests
tools/publish.ps1            Release packaging (zip + SHA256SUMS.txt)
tools/IconGen/               Renders assets/*.svg into the .ico/.png files
assets/                      lumos.svg (app icon), tray-on/off/paused.svg (tray states)
```

### Changing the icons

Edit the SVGs in `assets/`, then regenerate the files the apps embed:

```powershell
dotnet run --project tools/IconGen                         # writes assets/*.ico and lumos-256.png
dotnet run --project tools/IconGen -- --preview sheet.png  # optional: every size on dark/light backgrounds
```

## Porting to Linux

`Lumos.Core` and the Avalonia UI are cross-platform. A `Lumos.Platform.Linux` project would need to provide:

- an `IHdrController`: KDE (`kscreen-doctor output.<name>.hdr.enable`) or GNOME/Mutter (D-Bus);
- process start/exit events: the netlink proc connector;
- a tray icon: StatusNotifierItem.

## Releasing

Maintainers: see [docs/RELEASING.md](docs/RELEASING.md).
