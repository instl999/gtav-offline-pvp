# Development guide

How the mod is built, how the scripts fit together, and the design of the current iteration.
For the Online rules and natives behind these designs, see [RESEARCH.md](RESEARCH.md). For the parity checklist, see
[../ROADMAP.md](../ROADMAP.md).

## Workflow
Each iteration follows the same steps:
1. **Research** the Online feature: its rules, its feel, and the natives it needs. Add to `RESEARCH.md`.
2. **Design** it here: behaviour, settings, how it fits the existing scripts, what can go wrong in-game.
3. **Build** it, keeping to C# 5. Compile against SHVDN 3.6.0 **and** SHVDN Enhanced.
4. **Test.** CI builds and runs the binding checks. In-game testing follows the checklist at the bottom.

## Building
| Platform | Command |
|---|---|
| Windows, no SDK | `.\build.ps1 -ShvdnDll C:\path\to\ScriptHookVDotNet3.dll` (in-box csc, C# 5) |
| Any OS with the .NET SDK | `dotnet build OfflinePvP.csproj -c Release -p:ShvdnDll=/path/to/ScriptHookVDotNet3.dll` |

`OfflinePvP.csproj` targets net48 with `LangVersion 5`. Code that builds with it also builds with the in-box compiler.
Get the reference DLLs from the [SHVDN 3.6.0 release](https://github.com/scripthookvdotnet/scripthookvdotnet/releases/tag/v3.6.0)
and the [SHVDN Enhanced v1.1.0.6 release](https://github.com/Chiheb-Bacha/ScriptHookVDotNetEnhanced/releases).
**C# 5 means:** no `?.`, no `$"..."`, no `nameof`, no expression-bodied members, no `out var`, and no pattern matching.

## Architecture
SHVDN runs every `Script` subclass in the DLL on its own fiber. Scripts share static state through `Session`.

| Script | File | Owns |
|---|---|---|
| `FreemodeScript` | FreemodeScript.cs, FreemodeEvents.cs | AI session players, griefers, bounties, feuds, job invites, freemode events, the interaction menu (M), Online respawn |
| `DeathmatchScript` | DeathmatchScript.cs | Arena jobs: DM, TDM, LTS, KotH, Gun Game; the jobs menu (F7) |
| `RaceScript` | RaceScript.cs | Land races vs AI drivers; the races menu |
| `CrewScript` | CrewScript.cs | Persistent crew roster, AI partners in freemode, the crew menu |

Shared code (Shared.cs, UiKit.cs, Combatant.cs, DmConfig.cs):
- **`Session`** holds cross-script state.
  - `MatchActive` is set by whichever job owns the player. Freemode, events and partners stand down while it is set.
  - `Players` is the list of AI session players.
  - `CrewNames` is the crew roster.
  - `Open*Menu` and `Requested*` are flags one script sets and another picks up on its next tick.
  - **Never call another script's methods directly.** SHVDN scripts run on separate fibers, so cross-script calls are unsafe.
- **`DeathControl`** is a reference-counted suspension of story mode's `respawn_controller`, so several modes can own deaths.
- **`Progression`** handles RP, rank, stats and inventory in `OfflinePvP_Profile.ini`.
- **`WeaponStash`** saves and restores the player's story weapons around modes that take them away.
- **`SimpleMenu`** is a menu with no external dependencies. Only one menu has focus at a time across all scripts.
  - Menus from different scripts must not be linked with `Parent`: both scripts see the same key event.
  - Hand off with a `Session.Open*Menu` flag instead.

**Cross-script rules**
- **Job lifecycle:** a job sets `Session.MatchActive = true` **before** its fade-out and clears it in cleanup. A job
  refuses to start if `MatchActive` is already set.
- **Relationship groups** are created by name, so any script can refer to them:
  - `SPFM_PLAYERS`: neutral session players
  - `SPFM_HOSTILE`: griefers
  - `SPFM_FEUD_A` / `SPFM_FEUD_B`: AI-vs-AI fights
  - `SPFM_CREW`: your partners
  - `SPDM_*`: job teams

## Iteration v0.3 design: crew, races, Gun Game, freemode events

### 1. Crew and AI partners (`CrewScript`)
**Goal:** friends who are always around to play with you, the offline version of a party or crew in Online.

**Roster**
- Up to 8 members, saved in `OfflinePvP_Crew.ini` as `Name|Rank|Model|Weapon|Kills|Deaths`.
- On first run you get 3 crew mates.
- To add members:
  - Invite the nearest friendly session player within 40 m. About 65% accept; the rest say no.
  - Recruit a new member for $5,000.
- To kick a member, press Enter twice on them in the roster.

**Partners with you**
- 1–4 crew members from the top of the roster spawn out of sight behind you and run up to you.
- If you're in a vehicle with free seats, they spawn straight into it.
- They join your ped group: they follow, get into your car and fight whoever threatens you.
  - They're in `SPFM_CREW`, which is companion to you and hostile to `SPFM_HOSTILE` griefers.
  - Your bullets can't hurt them.
- If your car is full, they spawn their own car and escort you (`VehicleMissionType.Escort`). When you get out, they
  rejoin on foot.
- If they fall more than 250 m behind (fast travel, flying, character switch), they respawn next to you.
- When killed they respawn after N seconds (default 20); "Partners can die" can be turned off.
  - The death feed names the session player who killed them.

**Orders** (from the crew menu, or optional hotkeys)
- **Follow:** regroup on you.
- **Hold:** leave the group and guard the current position.
- **Attack my target:** whoever you're aiming at, for 30 s. Then they go back to following.

**Jobs and missions**
- In team jobs, your crew fill your side's slots first (`Session.TakeAllyName`).
- Partners despawn during jobs and story missions, unless "During story missions" is on.

### 2. Gun Game (`DeathmatchScript`, `MatchMode.GunGame = 4`)
**Rules**
- Free-for-all on a 16-step ladder, from Pistol .50 up through SMGs, shotguns, rifles, MG, marksman rifle, heavy
  sniper and grenade launcher, ending on the Knife.
- Each kill moves you one step up.
- A kill with the knife wins.
- Being knifed, or dying with no killer (when the suicide penalty is on), moves you one step down.

**How it works**
- Your story weapons are stashed at the start (`WeaponStash`) and restored when the match is cleaned up.
- Weapon pickups turn into armor pickups.
- The HUD shows your level and current weapon; the scoreboard shows LEVEL, KILLS, DEATHS.

### 3. Races (`RaceScript`)
**Settings**
- Lap or point-to-point; laps 1–5; length Short, Medium or Long.
- Vehicle class: Super, Sports, Muscle, Motorcycles, Off-road, Compacts or SUVs.
- 1–15 opponents; AI skill Easy, Normal or Hard; catch-up on or off.
- Traffic, time of day, weather.
- Location: where you stand, or one of the presets.

**Route generation**
- **Lap races:** points on a circle around the start (radius 250, 400 or 650 m), each snapped to the nearest vehicle node.
  - Points closer than 60 m to the previous one are dropped.
  - Points that snap more than 120 m off the circle are dropped.
- **Point-to-point:** step 180 m in a heading that wanders ±35°, snapping each step to a road.
  - A step is rejected if it doesn't move away from the start or is less than 90 m from the previous point.
- Path nodes only stream near the player, so the player is teleported to the start and the world streams for 3 s
  before the route is generated.
- The GPS draws the route through the next checkpoints using the multi-route natives.

**Starting grid**
- Two columns, 8 m apart in rows, behind the start, using the road heading from
  `GET_CLOSEST_VEHICLE_NODE_WITH_HEADING`.
- Everyone is frozen through 3-2-1-GO (with sounds and big text).
- GO unfreezes everyone and tasks the AI.

**Checkpoints and AI driving**
- Two checkpoints are shown: the current one (yellow, with an arrow pointing at the next) and a faded next one.
  The finish is chequered.
- Driving a racer within 14 m of the current checkpoint counts it (25 m for AI, which brake when they arrive).
- The AI is given the next checkpoint as soon as it passes one.
- AI driving:
  - `TASK_VEHICLE_DRIVE_TO_COORD_LONGRANGE`, racing style 787004, speed 70 m/s.
  - Skill sets `SET_DRIVER_ABILITY`, `SET_DRIVER_AGGRESSIVENESS` and `SET_DRIVER_RACING_MODIFIER`.

**Catch-up**
- If AI is behind you, its cheat power goes up (to ×1.6 when far behind). If it's far ahead, power goes down.
- This is applied every frame, as the native requires.

**Respawning**
- Hold F (vehicle exit, which is disabled during the race) for 1 s to respawn at your last checkpoint.
- AI that is stuck (under 2 m/s for 5 s), upside down, in water or wrecked respawns at its last checkpoint.
- If you die, you respawn at your last checkpoint after 3 s.

**Finish**
- When the first racer finishes, everyone else has 30 s; anyone still racing gets a DNF.
- Results show position, name, total time and best lap.
- Payout: RP and cash by position. Finishing pays, DNF pays less.

**Cleanup**
- Your original car is kept persistent through the race and you're returned to it afterwards ("Return after race").

### 4. Freemode events (`FreemodeEvents.cs`, partial `FreemodeScript`)
**Schedule**
- Every 10, 15 or 20 minutes (or off). The menu also has "Start event now".
- An event needs at least 2 AI players in the session.
- A help shard and notification announce the event. The event runs 6–10 minutes.
- The top-3 panel and your own score show bottom-right; event blips are purple.
- Passive mode keeps you out of events.

**Participants**
- Every session player takes part.
- Players far away score *virtually*, at rates tuned so a good human player wins about half the time.
- Players within streaming range take part *physically* through `EventThink`, which overrides their normal freemode
  `Think`.

**Events**
| Event | Rules | Physical AI behaviour |
|---|---|---|
| **Checkpoints** | 40 checkpoints on streets within 900 m of where the event starts. Collect them on foot or by car. Most collected wins; ends early if all are taken. | Materialized players drive to the nearest uncollected checkpoint. |
| **Hot Property** | A briefcase spawns 300–600 m away. Hold it to score 1 point per second. Dying drops it where you died. The carrier is shown with a red briefcase blip. | Players head for the case. A player who holds it is hunted: if you hold it, nearby players turn hostile and come after you. |
| **King of the Castle** | A 15 m area at a landmark. Alone inside, you're King and score 1 point per second. If others are inside it's contested. | Players drive to the castle, then hold it and fight anyone inside. |

**Rewards** (all participants get RP for taking part)
- 1st: $15,000 and 1,500 RP.
- 2nd: $8,000 and 1,000 RP.
- 3rd: $5,000 and 750 RP.

### 5. Livelier session
- **AI feuds:** every ~45 s there's a chance two materialized, non-hostile players near each other start a fight.
  - They go into `SPFM_FEUD_A` and `SPFM_FEUD_B`, which hate each other and are neutral to you.
  - The kill feed reports the result.
  - The loser may want revenge later.
- **Crew packs:** players who share a crew tag roam together and avenge each other.
  - When a crew member turns hostile because of you, another member in the same crew joins in.
- **Job invites:** every 3–6 minutes a session player invites you to a random job type.
  - The invite appears as a Multiplayer-icon notification.
  - Accept from the interaction menu ("Accept invite: Gun Game (0:25)") or with the `AcceptInvite` key (default Y).
  - Invites expire after 30 s.
  - Accepting sets `Session.RequestedMatchMode` or `RequestedRace`; the matching job script starts the job with its
    current settings.

## Risks we can't rule out without the game
- `GET_PLAYER_GROUP` membership survives character switches: the crew script re-adds partners on every think.
- Ped groups hold about 7 members. We cap partners at 4.
- AI racers can cut corners or take odd routes between checkpoints. Hand-placed routes would fix this; see
  "creator-style maps" on the roadmap.
- Race routes generated over water or in tunnels: snapped points with a Z far from the ground are rejected; the fallback
  shortens the race.
- Event AI is materialized only within about 180 m, so physical contests happen close to you. Far away, scoring is
  simulated.

## In-game test checklist (for testers)
- [ ] M > Crew & partners > Partners with you: partners arrive, follow, enter your car, fight a griefer, respawn after
  death, catch up after fast travel.
- [ ] A full car makes partners bring their own car and follow; getting out makes them rejoin on foot.
- [ ] Order Attack while aiming at a ped: partners attack, then come back to follow.
- [ ] TDM with crew members: ally names come from the crew.
- [ ] Gun Game: weapons step up per kill, the knife wins, story weapons come back after the match.
- [ ] Race, lap and point-to-point, at the current location and a preset:
  - [ ] grid placement and countdown
  - [ ] AI drive the route
  - [ ] checkpoints count
  - [ ] hold-F respawn works
  - [ ] the DNF timer, results and payout work
  - [ ] you get your car back
- [ ] Freemode events (M > Online session > Start event now), each of the 3 types: blips, scoring, HUD panel, payout.
- [ ] Job invite → accept → the job starts.
- [ ] AI feud: two players fight; the kill feed reports it.
