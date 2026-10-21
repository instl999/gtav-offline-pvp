# GTA V Offline PvP

GTA Online-style competitive modes against AI bots in **GTA V story mode**. The long-term goal is a faithful
recreation of Online's Deathmatch, then more Online PvP modes and CEO/organisation gameplay.

> **Status: early alpha (v0.1).** Free-for-all and Team Deathmatch are implemented and compile against
> ScriptHookVDotNet 3.6.0, but **they have not been play-tested in-game yet**. Bug reports with your
> `ScriptHookVDotNet.log` are very welcome.

Inspired by single-player "online PvP" mods for Red Dead Redemption 2.

## Features (v0.1)
- **Free-for-all** (everyone hostile) and **Team Deathmatch** (you + allied bots vs an enemy team).
- **Live tuning from the F7 menu, mid-match:** enemy/ally bot count (bots join and leave like real players),
  difficulty preset (combat skill, fire rate, health/armor, cover use), accuracy %, how often bots hunt *you*,
  weapon set, kill limit, time limit, respawn delay, radar blips, name tags, health bars, pickups and their respawn time,
  suicide penalty, time of day and weather, traffic and pedestrian clearing.
- **Online feel:** gamertag-style bot names, kill feed, joined/left messages, WASTED and WINNER screens,
  spawns placed away from enemies, 1.5 s flickering spawn protection (ends early when you shoot),
  weapon/health/armor pickups that respawn on a timer, -1 suicide penalty, ammo on kill, out-of-bounds timer,
  K/D scoreboard and results screen.
- **No hospital trips:** story-mode death handling is paused during a match and restored afterwards.
- **Arenas:** where you're standing (adjustable radius) or 9 presets around the map.

See [ROADMAP.md](ROADMAP.md) for the Online parity checklist and the planned modes.

## Requirements
- [Script Hook V](http://www.dev-c.com/gtav/scripthookv/) (matching your game build)
- [ScriptHookVDotNet v3](https://github.com/scripthookvdotnet/scripthookvdotnet/releases) (3.6.0 or newer)
- **Story mode only.** Never take Script Hook mods into GTA Online.

## Install
1. Download `OfflinePvP.dll` and `OfflinePvP.ini` from [Releases](../../releases).
2. Put both in `<GTA V folder>\scripts\`.
3. Load story mode and press **F7**.

## Controls
| Key | Action |
|---|---|
| F7 | Open/close menu (also works during a match) |
| Arrow keys / Numpad 8-2-4-6 | Navigate / change values |
| Enter | Start/end match, save settings, close results |
| Backspace | Close menu |
| Hold Z | Scoreboard |

You can rebind keys in `OfflinePvP.ini`.

## Building
You only need the .NET Framework 4.x C# compiler that ships with Windows. No Visual Studio required.
```powershell
.\build.ps1 -ShvdnDll "C:\path\to\ScriptHookVDotNet3.dll" -GtaDir "D:\Games\Grand Theft Auto V"
```
`-GtaDir` is optional. It copies the DLL into `scripts\` and never overwrites an existing INI.
Keep the code **C# 5 compatible**, because that is what the in-box compiler supports.

### Source layout
| File | Purpose |
|---|---|
| `src/DeathmatchScript.cs` | Match lifecycle, bot AI loop, death/respawn, HUD |
| `src/Combatant.cs` | Participant model (player or bot, team, score), shared by future modes |
| `src/DmConfig.cs` | Settings, arenas, loadouts, difficulty presets, INI load/save |
| `src/UiKit.cs` | Dependency-free menu, text/rect drawing, big-message shards |
| `dist/OfflinePvP.ini` | Default settings shipped with releases |

## Contributing
Issues and PRs are welcome, especially for:
- in-game test reports
- better arena coordinates
- knowledge of Online's exact rules (scoring, spawn rules, HUD layout)

## License
MIT. See [LICENSE](LICENSE). This project is not affiliated with Rockstar Games or Take-Two Interactive.
