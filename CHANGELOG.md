# Changelog

## 2.2.0

### New

- **Linux version** (x64, also Steam Deck in Desktop Mode): its own build next to the Windows exe, with the same tabs and features. Built on Avalonia, released as `CanEmAllEditor-linux-x64.tar.gz`
- Linux save detection: the game's Proton prefix (`steamapps/compatdata/5133900` and `5364590`) in the native, Flatpak and Snap Steam folders and in every library from `libraryfolders.vdf` (second drive, SD card), plus a native Unity folder
- "Game is running" check on Linux looks for the game's Wine process

### Changed

- Save format, skill data and texts moved into a shared core project (`src/CanEmAllEditor.Core`), used by both editors; the Windows editor works as before
- `build.cmd` builds both editors; `build-linux.sh` builds the Linux one on Linux

## 2.1.0

Tested with a real prestige run (factory sold, permanent upgrades bought).

### Changed

- Starter pack gives bat, knife, pistol, SMG and the vacuum – in the full game it no longer runs up to the flamethrower
- Mails added by the editor get the latest received day already in the inbox (mail days keep counting after a factory sale, the run day starts over)
- `--snapshot` renders the tabs maximized and without the window frame

### Verified

- `unlockedPermanentUpgrades` stores the ids 232–252; a real prestige save is read and written back byte for byte

## 2.0.0

### New

- **Full game** support (Steam app 5133900) next to the demo; the edition is detected from the save folder
- 252 skills with the full game's prices and layout, new **Robots** and **Turrets** categories, chainsaw
- **Prestige** tab: upgrade points, money earned this run, the 21 permanent upgrades, lifetime statistics, factories sold, ending seen
- **Mails** tab: all 46 story mails with their text, received / read / accepted, received day
- New settings: field of view, invert Y, head bob, blood and wound color
- "Everything" quick action: all skills plus all permanent upgrades

## 1.0.0

First release for the demo: money, day, statistics, skill tree with the game's layout and icons, skill list, game settings, backups, raw JSON, English and German.
