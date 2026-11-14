# Changelog

All notable changes to Lumos are documented here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow [Semantic Versioning](https://semver.org/).

The release workflow copies the section matching the tag (for example `## [0.1.0]`) into the GitHub Release notes.

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
