# GTA V Offline PvP

The GTA Online experience in **GTA V story mode**: a freemode session full of AI players, an AI crew that plays
alongside you, freemode events, races, and Online's PvP jobs against AI bots. The long-term goal is to recreate as much of Online as a single-player script can, including
CEO/organisation gameplay.

> **Status: alpha (v0.3).** Everything compiles against ScriptHookVDotNet 3.6.0 and SHVDN Enhanced, but **none
> of it has been play-tested in-game yet**. Bug reports with your `ScriptHookVDotNet.log` are very welcome.

Inspired by single-player "online PvP" mods for Red Dead Redemption 2.

## Features
### Online session (freemode), interaction menu (M)
- **AI players in your world**: 1–29 players with gamertags, ranks and crew tags who drive and walk around the map,
  show up as blips, and join and leave the session.
- **Griefers and bounties**: some players come after you (adjustable), shooting someone makes them fight back,
  and bounties go out on you and on them.
- **Online-style respawn** near where you died instead of the hospital, plus a **player list** (hold Z).
- **Interaction menu**: snacks, 5 tiers of body armor, passive mode, kill yourself, jobs, your profile.
- **RP and rank** that persist, with the RP bar and RANK UP shard. Jobs pay RP and cash.

Turn the session on from **M > Online session > Session**.

### Crew and AI partners (M > Crew & partners)
- **A crew that persists:** you start with 3 crew mates. Invite friendly players from your session, or recruit new
  ones. Up to 8 members.
- **Partners with you:** 1–4 crew mates follow you on foot and get into your car. When your car is full, they bring
  their own and escort you.
  - They fight griefers and anyone who attacks you.
  - When killed, they respawn near you.
  - When you fast-travel, they catch up.
- **Orders:** follow me, hold position, attack my target.
- **Team jobs:** your crew fill your side in team jobs, and they race against you.

### Freemode events and a living session
- **Freemode events** every 10/15/20 minutes, or on demand: **Checkpoints**, **Hot Property** and **King of the
  Castle**.
  - Every player in your session competes. Nearby players do it for real; far-away players score in the background.
  - A gold/silver/bronze panel tracks the standings, and the top 3 get paid.
- **AI players fight each other.** Crews stick together, and attacking one crew member brings the whole crew after you.
- **Job invites:** players invite you to jobs. Press **Y** or use the interaction menu to join.

### Jobs (F7, or M > Jobs)
- **Deathmatch**, **Team Deathmatch**, **Last Team Standing** (rounds, no respawns), **King of the Hill**
  (hold the hill alone to score) and **Gun Game** (a new gun every kill, finish with the knife) against AI bots.
  Players from your session join your job.
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

### Races (F7 > Races, or M > Races)
- **Race types:** lap or point-to-point races against up to 15 AI drivers, on routes generated from the road network.
  - Pick a length, one of 7 vehicle classes, and one of 10 start locations (or start where you are).
- **Online flow:**
  - Grid start and a 3-2-1-GO countdown.
  - Yellow checkpoints, with a faded preview of the next one and GPS routing.
  - Catch-up.
  - **Hold F to respawn** at your last checkpoint.
  - After the winner finishes, everyone else has 30 seconds before a DNF.
  - Results with best laps, then RP and cash by position.

## Requirements
The same `OfflinePvP.dll` works on both PC editions.

| | GTA V **Enhanced** (`GTA5_Enhanced.exe`) | GTA V **Legacy** (`GTA5.exe`) |
|---|---|---|
| Script Hook V | [Script Hook V](http://www.dev-c.com/gtav/scripthookv/) with Enhanced support (v3889.0 covers Enhanced 1.0.1158.13) | [Script Hook V](http://www.dev-c.com/gtav/scripthookv/) matching your build |
| .NET script hook | [ScriptHookVDotNet **Enhanced**](https://github.com/Chiheb-Bacha/ScriptHookVDotNetEnhanced/releases) v1.1.0.6+ (required: vanilla SHVDN does not run on Enhanced) | [ScriptHookVDotNet v3](https://github.com/scripthookvdotnet/scripthookvdotnet/releases) 3.6.0+, or SHVDN Enhanced |

Both need .NET Framework 4.8 and the Visual C++ 2019 x64 redistributable.
**Story mode only.** Never take Script Hook mods into GTA Online.

## Install (Enhanced)
1. Install Script Hook V: copy `ScriptHookV.dll` and `dinput8.dll` next to `GTA5_Enhanced.exe`.
2. Install SHVDN Enhanced: copy `ScriptHookVDotNet.asi`, `ScriptHookVDotNet.ini`, `ScriptHookVDotNet2.dll`,
   `ScriptHookVDotNet3.dll` and `MinHook.x64.dll` from the same release next to `GTA5_Enhanced.exe`.
   Don't mix files from different SHVDN versions.
3. Create a `scripts` folder there, and put `OfflinePvP.dll` and the `.ini` files from our
   [Releases](../../releases) zip in it.
4. **Turn off BattlEye for story mode.** Either untick BattlEye in the Rockstar Games Launcher settings, or add
   `-nobattleye` to the launch options (Steam: Properties > Launch Options; Epic: Manage > Launch Options).
   GTA Online won't be available until you turn it back on.
5. Load story mode. Press **M** for the interaction menu or **F7** for jobs.

If scripts don't load at all, check `ScriptHookVDotNet.log` next to the game exe. SHVDN Enhanced's page also
recommends DirectStorageFix for Enhanced. After a game update, wait for both Script Hook V and SHVDN Enhanced
to update before playing modded.

## Install (Legacy)
Same as above, using `GTA5.exe`'s folder and either SHVDN v3 or SHVDN Enhanced.

## Controls
| Key | Action |
|---|---|
| M | Interaction menu |
| F7 | Jobs menu (also works during a match) |
| Arrow keys / Numpad 8-2-4-6 | Navigate / change values |
| Enter | Start/end match, save settings, close results |
| Backspace | Close menu |
| Hold Z | Scoreboard in a job, standings in a race, player list in freemode |
| Y | Accept a job invite |
| Hold F (in a race) | Respawn at the last checkpoint |

You can rebind keys in `OfflinePvP.ini`.

## Building
On Windows you only need the .NET Framework 4.x C# compiler that ships with Windows. No Visual Studio required.
```powershell
.\build.ps1 -ShvdnDll "C:\path\to\ScriptHookVDotNet3.dll" -GtaDir "D:\Games\Grand Theft Auto V"
```
`-GtaDir` is optional. It copies the DLL into `scripts\` and never overwrites an existing INI.

On any OS with the .NET SDK (macOS and Linux included):
```bash
dotnet build OfflinePvP.csproj -c Release -p:ShvdnDll=/path/to/ScriptHookVDotNet3.dll
```
See [docs/DEVELOPMENT.md](docs/DEVELOPMENT.md) for the architecture and the design of each feature, and
[docs/RESEARCH.md](docs/RESEARCH.md) for the Online rules and natives behind them.

Release builds are compiled against SHVDN 3.6.0 so one DLL runs everywhere. `tools\check-compat.ps1` proves that
every type and member the DLL uses binds against a given `ScriptHookVDotNet3.dll` (CI runs it against both SHVDN
3.6.0 and SHVDN Enhanced):
```powershell
.\tools\check-compat.ps1 -ModDll bin\OfflinePvP.dll -ShvdnDll "C:\path\to\ScriptHookVDotNet3.dll"
```
Keep the code **C# 5 compatible**, because that is what the in-box compiler supports.

### Source layout
| File | Purpose |
|---|---|
| `src/DeathmatchScript.cs` | Jobs: match lifecycle, all PvP modes (incl. Gun Game), bot AI loop, death/respawn, HUD |
| `src/RaceScript.cs` | Races: route generation, grid, AI drivers, checkpoints, catch-up, respawn, results |
| `src/CrewScript.cs` | Crew roster and AI partners: follow, ride along, orders, respawn |
| `src/FreemodeScript.cs` | Online session: AI players, griefers, bounties, respawn, interaction menu |
| `src/FreemodeEvents.cs` | Freemode events, AI feuds and crews, job invites (part of `FreemodeScript`) |
| `src/Shared.cs` | Death-flow control, session state shared by scripts, RP/rank/inventory persistence |
| `src/Combatant.cs` | Participant model (player or bot, team, score), shared by future modes |
| `src/DmConfig.cs` | Settings, arenas, loadouts, difficulty presets, INI load/save |
| `src/UiKit.cs` | Dependency-free menu, text/rect drawing, big-message shards |
| `dist/*.ini` | Default settings shipped with releases (jobs, freemode, crew, races) |
| `OfflinePvP.csproj` | Cross-platform build with the .NET SDK |

## Contributing
Issues and PRs are welcome, especially for:
- in-game test reports
- better arena coordinates
- knowledge of Online's exact rules (scoring, spawn rules, HUD layout)

## License
MIT. See [LICENSE](LICENSE). This project is not affiliated with Rockstar Games or Take-Two Interactive.
