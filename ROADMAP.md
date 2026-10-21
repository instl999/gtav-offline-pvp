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
| Weapon, armor and health **pickups on the map** | ✅ | Placed on random spawn points (not creator-placed); weapons follow the loadout; respawn timer is configurable |
| Traffic on/off | ✅ | |
| Police off | ✅ | |
| Blips on/off | ✅ | |
| Opponent health bars | ✅ | Health and armor bars above name tags |
| Time of day / weather | ✅ | Restored when the match ends |
| Up to 16 players (incl. you) | ✅ | Up to 41 here |
| Team balancing | 🟡 | You set ally and enemy counts by hand |
| Arena bounds | ✅ | Circular; Online uses creator-defined areas |
| Creator-style hand-placed maps and spawn points | ⬜ | Arenas currently use auto-generated spawn points |

### In-match
| Online feature | Status | Notes |
|---|---|---|
| Respawn without hospital, on a timer | ✅ | |
| Spawns placed away from enemies | ✅ | |
| Spawn protection | ✅ | Invincible with a flickering see-through effect; ends early when you shoot |
| Kill feed | ✅ | |
| Killer cam / WASTED screen with killer name | 🟡 | WASTED shard with the killer's name; no killer cam or weapon icon |
| Top-right leaderboard HUD | 🟡 | Layout approximates Online's |
| Scoreboard with K/D | ✅ | |
| Interaction menu: snacks and armor | ✅ | M key; 5 snacks and 5 armor tiers, restockable |
| Out-of-bounds timer | ✅ | |
| Suicide penalty | ✅ | -1 kill for any death with no killer (falls, own explosives, out of bounds); can be turned off |
| Bot names above heads | ✅ | |
| Bots that play like Online players (strafing, peeking, rolling, rushing) | 🟡 | Uses the game's combat AI with tuned attributes |
| Bots using pickups and vehicles | ⬜ | |

### End of match
| Online feature | Status | Notes |
|---|---|---|
| Winner/loser shard | ✅ | |
| Results leaderboard | ✅ | |
| RP and cash rewards | ✅ | Persistent RP/rank and story-mode cash; amounts are this mod's, not Online's exact payouts |
| Rematch / next job vote | ⬜ | |

## Freemode (Online session)
| Online feature | Status | Notes |
|---|---|---|
| Other players in your session | ✅ | 1–29 AI players with gamertags, ranks and crew tags; virtual far away, real peds within ~180 m |
| Players driving, walking, roaming the map | ✅ | Drive Online-favourite cars between points of interest |
| Players joining and leaving | ✅ | "X joined." / "X left." churn |
| Player blips and name tags | ✅ | White blips with heading; red when hostile; flashing with a bounty |
| Player list | ✅ | Hold Z outside jobs |
| Griefers | ✅ | Off/Low/Normal/High; they chase you down in cars and on foot |
| Retaliation when you shoot someone | ✅ | |
| Bounties on you and on other players | ✅ | Survive yours for the cash, or claim theirs |
| Respawn near where you died | ✅ | Instead of the hospital; can be turned off |
| No wanted level for killing players | 🟡 | Restores the wanted level from before the fight |
| Passive mode | ✅ | No damage either way; your weapons are disabled |
| Kill yourself | ✅ | Interaction menu |
| RP, rank, RANK UP | ✅ | Persistent; ranks 100+ use Online's formula, below 100 an approximation |
| Players join your jobs | ✅ | Deathmatch bots take names from your session |
| AI players fighting each other | ⬜ | |
| Players in aircraft, bikes doing stunts, Oppressors | ⬜ | |
| Invites to jobs from AI players | ⬜ | |
| Mental state, bad sport, reporting | ⬜ | |

## PvP modes
| Mode | Status | Notes |
|---|---|---|
| Deathmatch | ✅ | |
| Team Deathmatch | ✅ | |
| Last Team Standing | ✅ | Rounds, no respawns; late joiners wait for the next round. No spectator camera yet |
| King of the Hill (free-for-all) | ✅ | One fixed hill; 1 point per second held alone; contested hills don't score |
| Team King of the Hill, moving or vehicle hills | ⬜ | |
| Vehicle Deathmatch | ⬜ | |
| Capture: GTA, Contend, Hold, Raid | ⬜ | |
| Adversary modes (Hunting Pack, Slasher, Every Bullet Counts, Come Out to Play, Running Back...) | ⬜ | |
| Gun Game / arms race | ⬜ | Community favourite |

## Online character and economy
Ideas from CruelMaster's "Online Content in Singleplayer" mod, which covers this side of Online:
- Freemode character (`mp_m/f_freemode_01`) with a character creator.
- Clothing store and saved outfits.
- Ammu-Nation with Online's weapon list and attachments.
- Personal vehicle with a starter car, insurance and Mors Mutual, and calling it in.
- AI friends and crew who appear in menus and join jobs as allies.
- Content gated by DLC packs.

## CEO / organisations
- Register as CEO and hire AI associates and bodyguards.
- Give associates orders: follow, guard, attack my target, hold position.
- Rival AI CEO organisations with a boss and associates, territory and escalating attacks.
- Online-style VIP work: Headhunter, Sightseer, crate steal and defend against rival orgs.
- Org funds, associate wages, and saved organisation state.

## Engineering
- Split the mode logic out of `DeathmatchScript` into a mode interface, so new modes can be plugged in.
- A map/spawn editor for hand-placed arenas, saved as XML/JSON.
- ~~CI build on GitHub Actions~~ (done: every push and PR is compiled against SHVDN 3.6.0 and SHVDN Enhanced,
  and the DLL is binary-checked against both).
- ~~GTA V Enhanced support~~ (done: same DLL, via SHVDN Enhanced).
