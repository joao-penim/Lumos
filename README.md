<p align="center"><img src="assets/lumos-256.png" width="96" alt="Lumos" /></p>

# Lumos

[![CI](https://github.com/joao-penim/Lumos/actions/workflows/ci.yml/badge.svg)](https://github.com/joao-penim/Lumos/actions/workflows/ci.yml)

**Automatic HDR, per app, for Windows 11.**

Keep your desktop in SDR. When a game or app on your list starts, Lumos turns HDR on. It stays on through alt-tab, and when the app closes Lumos turns it back off. You no longer need to remember Win + Alt + B before launching a game, then restart it when you forget.

Lumos turns real HDR on and off. It is not Windows Auto HDR, which converts SDR content to HDR.

## Features

- **Per-app HDR**: add any `.exe` by browsing for it or picking it from the running apps.
- **Alt-tab safe**: HDR follows the app's process lifetime, not window focus.
- **Restores your setup**: Lumos only turns HDR off if Lumos turned it on. If HDR was already on, or you switch it off yourself mid-session, Lumos respects that.
- **Several apps at once**: HDR stays on until the last watched app closes.
- **Grace delay** (3 s by default): avoids flicker when a launcher or anti-cheat restarts the game exe.
- **Launch with HDR / desktop shortcuts**: turns HDR on *before* starting the app, for games that only check HDR at startup. Steam URIs such as `steam://rungameid/…` work as the launch command.
- **Per-app display choice**: primary display (default), all HDR displays, or a specific monitor.
- **Crash-safe**: if the agent is killed while HDR is on, it restores SDR on the next start. It also restores SDR when you sign out or shut down.
- **Windows 11 look**: Fluent design, follows the system light/dark theme and accent color.

## Efficiency

Lumos is split into two processes so the part that's always running stays tiny:

| | `LumosAgent.exe` (always on) | `Lumos.exe` (settings window) |
|---|---|---|
| Job | Detects apps, switches HDR, tray icon | Manage apps, displays, settings |
| Runs | From sign-in (optional) | Only while the window is open; **exits when closed** |
| Measured idle | **0.000 s CPU over 90 s**, ~7.7 MB private memory, 7 threads | n/a |

The agent never polls:

- **App start**: a WinEvent hook fires when a top-level window is created. Games create their window before their swapchain, so HDR is usually on before the game checks for it.
- **App exit**: a kernel wait on the process handle, shared on one thread-pool wait thread.
- **Tray and IPC**: one thread blocked in `GetMessage`, plus async named-pipe I/O.

The numbers above are from a regular self-contained build. With Native AOT (see *Build*) the agent is smaller still.

## Install

1. Download `Lumos-<version>-win-x64.zip` from [Releases](https://github.com/joao-penim/Lumos/releases).
2. (Recommended) [verify the download](#verify-your-download).
3. Extract it anywhere, for example `%LOCALAPPDATA%\Programs\Lumos`, and run `Lumos.exe`.

Lumos isn't code-signed yet, so on first run Windows SmartScreen may show "Windows protected your PC". Choose **More info → Run anyway**. Verifying the download first is how you know the file is genuine.

### Verify your download

Every release is built by GitHub Actions from the tagged commit. Nothing is built on a personal machine.

**1. Checksum**: the file wasn't corrupted or swapped:

```powershell
Get-FileHash .\Lumos-0.1.0-win-x64.zip -Algorithm SHA256
```

The hash must match the line in `SHA256SUMS.txt` on the release page.

**2. Build provenance**: the file was built by this repository's release workflow, from a specific commit. This needs the [GitHub CLI](https://cli.github.com/):

```powershell
gh attestation verify .\Lumos-0.1.0-win-x64.zip --repo joao-penim/Lumos
```

The same works for `Lumos.exe` and `LumosAgent.exe` after extracting.

## Usage

1. Run `Lumos.exe`. It starts the background agent automatically.
2. Open **Apps → Add app**, then choose an `.exe` or a running app.
3. Optional: in **Settings**, turn on **Start with Windows** so the agent runs at sign-in.

Tray icon:

- Left-click opens Lumos.
- Right-click shows the menu: active apps, Pause, Exit.
- The sun turns yellow while Lumos has HDR on.

Command line (agent):

```
LumosAgent.exe                     start the agent (or open the UI if it's already running)
LumosAgent.exe --launch <appId>    HDR on, then start the app (what desktop shortcuts use)
LumosAgent.exe --exit              stop the running agent
LumosAgent.exe --hdr list          diagnostics: list displays and HDR state
LumosAgent.exe --hdr on|off [id]   diagnostics: switch HDR (primary display by default)
```

Settings and logs live in `%APPDATA%\Lumos` (`settings.json`, `state.json`, `agent.log`).

## Build

Requirements: Windows 10/11 and the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```powershell
dotnet build Lumos.slnx
dotnet test --project tests/Lumos.Core.Tests
dotnet run --project src/Lumos.App                    # UI (Debug builds put LumosAgent.exe next to it)
powershell -ExecutionPolicy Bypass -File tools/publish.ps1 -Zip   # -> publish/win-x64 + zip + SHA256SUMS.txt
```

### Releasing

1. Update `CHANGELOG.md` with a `## [x.y.z]` section.
2. Commit, then tag and push:

   ```powershell
   git tag v0.1.0
   git push origin v0.1.0
   ```

3. The **Release** workflow then runs the tests and builds the Native AOT package. It writes the checksums and the provenance attestation, and creates a **draft** release.
4. Review the draft on GitHub and publish it.

Tags with a suffix (for example `v0.2.0-beta.1`) are marked as pre-releases.

The agent's **Native AOT** build needs the *Desktop development with C++* workload (Visual Studio or Build Tools). Without it, `publish.ps1` falls back to a regular self-contained agent.

## Project layout

```
src/Lumos.Core/              Platform-neutral: models, settings, HdrSessionManager (all HDR rules), IPC contracts
src/Lumos.Platform.Windows/  CCD HDR controller, WinEvent process watcher, autostart, shortcuts, shell helpers
src/Lumos.Agent/             Background agent: message loop, tray icon, named-pipe server
src/Lumos.App/               Avalonia + FluentAvalonia settings UI
tests/Lumos.Core.Tests/      Session-manager and IPC tests (fakes for displays and timers)
tools/                       publish.ps1, make-icons.ps1
```

HDR is switched through the same CCD API that Windows Settings uses:

- **Windows 11 24H2 and newer**: `DISPLAYCONFIG_DEVICE_INFO_SET_HDR_STATE`.
- **Older builds**: `SET_ADVANCED_COLOR_STATE`.

## Linux (later)

All the HDR rules live in `Lumos.Core` and have no OS calls, and the UI is Avalonia, which also runs on Linux. A future `Lumos.Platform.Linux` would provide:

- HDR control: KDE (`kscreen-doctor output.<name>.hdr.enable`) or GNOME/Mutter (D-Bus).
- Process events: the netlink proc connector.
- A tray icon via StatusNotifierItem.

## Known limitations

- Detection keys off a top-level window appearing. Apps that never create a window aren't detected. Use "Launch with HDR" for those.
- If a game checks HDR before creating its window, use its desktop shortcut so HDR is on first.
- Not yet verified with games running elevated (as admin). They're detected by window and path, and if their exit can't be waited on, Lumos checks that one process every 2 s instead.
