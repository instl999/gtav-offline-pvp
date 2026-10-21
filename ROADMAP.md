# Roadmap

The target is **as close to GTA Online's PvP as a single-player script can get**. Some things can't be
replicated: real human opponents, matchmaking/lobbies, netcode, and Rockstar's RP and cash economy. Everything
the player can *see and do* inside a match is in scope.

Legend: ✅ done · 🟡 partial · ⬜ planned

This checklist is written from knowledge of Online's Deathmatch rules. If you know Online's exact behavior
for an item, please open an issue with corrections.

## Deathmatch parity

### Match setup (Online's lobby options)
| Online feature | Status | Notes |
|---|---|---|
| Deathmatch (FFA) | ✅ | |
| Team Deathmatch, 2 teams | ✅ | |
| Team Deathmatch, 3–4 teams | ⬜ | `Combatant.Team` already supports any number of teams |
| Vehicle Deathmatch | ⬜ | |
| Target score / kill limit | ✅ | |
| Time limit | ✅ | |
| Weapons: forced loadout | ✅ | 8 weapon sets |
| Weapons: owned weapons / weapon lock | ⬜ | |
| Weapon, armor and health **pickups on the map** | ⬜ | High priority for the Online feel |
| Traffic on/off | ✅ | |
| Police off | ✅ | |
| Blips on/off | ✅ | |
| Opponent health bars | ⬜ | |
| Time of day / weather | ⬜ | |
| Up to 16 players (incl. you) | ✅ | Up to 41 here |
| Team balancing | 🟡 | You set ally and enemy counts by hand |
| Arena bounds | ✅ | Circular; Online uses creator-defined areas |
| Creator-style hand-placed maps and spawn points | ⬜ | Arenas currently use auto-generated spawn points |

### In-match
| Online feature | Status | Notes |
|---|---|---|
| Respawn without hospital, on a timer | ✅ | |
| Spawns placed away from enemies | ✅ | |
| Spawn protection | 🟡 | Invincible only; no flashing see-through effect yet |
| Kill feed | ✅ | |
| Killer cam / WASTED screen with killer name | 🟡 | WASTED shard with the killer's name; no killer cam or weapon icon |
| Top-right leaderboard HUD | 🟡 | Layout approximates Online's |
| Scoreboard with K/D | ✅ | |
| Interaction menu: snacks and armor | ⬜ | |
| Out-of-bounds timer | ✅ | |
| Suicide penalty | ⬜ | |
| Bot names above heads | ✅ | |
| Bots that play like Online players (strafing, peeking, rolling, rushing) | 🟡 | Uses the game's combat AI with tuned attributes |
| Bots using pickups and vehicles | ⬜ | |

### End of match
| Online feature | Status | Notes |
|---|---|---|
| Winner/loser shard | ✅ | |
| Results leaderboard | ✅ | |
| RP and cash rewards | ⬜ | Could pay story-mode cash |
| Rematch / next job vote | ⬜ | |

## More PvP modes (planned)
These are listed roughly in priority order. All of them share the `Combatant`/team core.
1. **Last Team Standing**: rounds with no respawns.
2. **King of the Hill**: hold a zone to score.
3. **Vehicle Deathmatch**
4. **Capture**: GTA, Contend, Hold and Raid.
5. **Adversary modes**, e.g. Hunting Pack, Slasher, Every Bullet Counts, Come Out to Play, Running Back.
6. **Gun Game / arms race** (community favourite)

## CEO / organisations
- Register as CEO and hire AI associates and bodyguards.
- Give associates orders: follow, guard, attack my target, hold position.
- Rival AI CEO organisations with a boss and associates, territory and escalating attacks.
- Online-style VIP work: Headhunter, Sightseer, crate steal and defend against rival orgs.
- Org funds, associate wages, and saved organisation state.

## Engineering
- Split the mode logic out of `DeathmatchScript` into a mode interface, so new modes can be plugged in.
- A map/spawn editor for hand-placed arenas, saved as XML/JSON.
- CI build on GitHub Actions.
