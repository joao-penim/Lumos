# Changelog

All notable changes to Lumos are documented here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow [Semantic Versioning](https://semver.org/).

The release workflow copies the section matching the tag (for example `## [0.1.0]`) into the GitHub Release notes.

## [0.2.0]

Add games straight from your launchers.

### Added
- **Choose from installed games**: lists games from Steam, the EA app, Epic Games and GOG, with icons. Lumos picks the exe that shows the game window (for example the Unreal `-Win64-Shipping` exe, or the game exe instead of an anti-cheat launcher) and fills in the launcher's launch command, such as `steam://rungameid/…`. Games you already added are greyed out.
- **Game folders** in Settings: add your own folders (for example `D:\Games`), and each subfolder shows up as a game.
- The Add/Edit dialog suggests other exes from the game's folder, and Browse opens in the program's folder.

## [0.1.0]

First public release.

### Added
- Turns HDR on automatically while chosen apps run, and back off when the last one closes (alt-tab safe).
- Only turns off HDR that Lumos turned on. Respects HDR that was already on, or that you change by hand.
- Grace delay (3 s by default) so launchers that restart the game don't make the screen flicker.
- "Launch with HDR" and desktop shortcuts that turn HDR on before the app starts.
- Per-app display choice: primary (default), all HDR displays, or a specific monitor.
- Crash recovery: restores SDR after the agent was killed, and when you sign out or shut down.
- Tray icon with status, pause and exit.
- Settings window in the Windows 11 style: Apps, Displays, Settings.
- Event-driven background agent (no polling) built with Native AOT.
