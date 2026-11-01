# Research notes: GTA Online rules and the natives behind them

Working reference for recreating Online offline. Compiled 2026-10-31 from gta.wiki, Rockstar Support, press coverage
and alloc8or's native database (`gta5-nativedb-data/natives.json`). **[?]** marks things recalled rather than sourced.
Corrections welcome as issues.

## Freemode events (Online rules)
General: need ≥3 unaffiliated players; first event ~20 min after joining, then a ~20 min cooldown, alternating with
Business Battles. A help message warns first, a countdown sits bottom-right, then a gold/silver/bronze top-3 panel
with your own score shows bottom-right. Event objects/areas use **purple blips**. Passive players can't take part.
No Bad Sport for event kills.

| Event | Rules | Length | Reward (ballpark) |
|---|---|---|---|
| Hot Property | Grab the briefcase (purple blip), keep it longest in total. Carrier is blipped for everyone; dying drops it and pauses your timer | 10 min | Winner ~$12.5–21.5k, others $2.5–8.5k + RP |
| Checkpoints | 120 yellow checkpoints over ~¼ of the map; most collected wins. Aircraft can't collect (Air Checkpoints variant can) | 10 min | $750–1,500 + RP each; 1st ~$4.5k bonus, 2nd ~$4k |
| King of the Castle | FFA; hold the castle area for ~1 pt/s; kill the king to take over; +5 per kill [?] | 10 min | Winner ~$13.6k (gold 2× silver 2× bronze) |
| Kill List (co-op) | 1–4 players board an armed vehicle (Savage/Buzzard/Hydra/Valkyrie/Rhino); kill 9–44 Merryweather in waves | until cleared | ~$20–30k + RP |
| Kill List Competitive | Same, players compete for most kills in separate vehicles (removed 2018) | — | — |
| Hunt the Beast | A random opt-in player becomes the Beast (≈3× strength, off radar, super jump [?]), must reach 10 landmarks in 30 min; revealed at landmarks or after 3 min idle. Others hunt it | 30 min | ~$30k |
| Moving Target | Get in a marked vehicle, deliver it in 10 min; everyone sees you | 10 min | $10k deliverer / $20k destroyer |
| Hold the Wheel | Be in the humble purple vehicle (Caddy, BMX, Faggio…) when time runs out; sudden death otherwise | 10 min | $20k |
| Criminal Damage | Most $ damage to vehicles in 5 min (vehicle price ≈ value); no police | 3 + 5 min | ~$35k 1st, $25k 2nd |
| Penned In | Stay inside a shrinking/moving dome in a car, weapons off; 7 s outside = boom; last alive wins (removed 2018) | ~5 min | ~$16–25k |
| Freemode Challenges | Scoreboard stunts: longest wheelie, highest speed, most near misses, longest jump… | 3–5 min | small |

## Races
- Types: Standard (lap/point-to-point), GTA (weapons, rocket/boost/repair pickups), Non-Contact, Rally, Stunt, Air, Sea, Bike.
- Checkpoints: yellow cylinders with an arrow to the next one; chequered flag on the finish. Passing close counts.
  Checkpoints are respawn points; hold the exit button to respawn at the last checkpoint (briefly ghosted).
- Lobby options: laps (usually 2–3), catch-up (Off/Classic/Advanced/Boosted), slipstream, traffic, time/weather,
  wanted levels, ghosting, "Boost on GO".
- Flow: grid → 3-2-1-GO with cars frozen → after the winner finishes, the rest get ~30 s [?] → DNF → WINNER/position
  shard → RP/cash by position (a few $k to ~$20k [?]), bonus RP for best lap.

## Deathmatch family, Adversary and Capture
- **"Gun Game" doesn't exist in Online.** The closest is **Kill Quota** (teams move up a weapon list by reaching kill quotas).
  We ship a classic FFA Gun Game anyway; it's the most requested arena-shooter mode.
- DM: up to 16 players, 2–4 teams, kill target off/10–150, 5–60 min, weapons forced/owned + pickups.
- KotH (2019): 3 hills A/B/C, 3 s to capture, owned hills fill your bar; vehicle hills possible.
- Adversary modes worth recreating with bots: Juggernaut, Every Bullet Counts (2-bullet Marksman Pistol + melee,
  standing still 5 s reveals you), Slasher (shotgun hunter vs flashlights, hunted get guns after 3 min), Come Out to Play
  (runners on foot vs hunters on bikes), Hunting Pack (bomb car must keep speed), Drop Zone (hold a zone 2 min total),
  Sumo, Siege Mentality, Lost vs Damned, Trading Places.
- Capture: Contend (neutral packages to your base), GTA (vehicles to your base), Hold (stockpile, raid others), Raid
  (classic CTF). Dropped package on carrier death; underwater package is destroyed.

## Job flow
Invites arrive as a phone pop-up ("X has invited you to …"); Quick Job finds a lobby. Corona: job card, host settings,
player list with ready state. End: WINNER shard → RP/JP/$ → leaderboard with Like/Dislike → next-job vote
(5 jobs + Random + Refresh, Replay, Freemode).

## CEO / MC
Up to 3 associates (later 7 [?]). Abilities: Ghost Organization ($12k, 3 min off radar), Bribe Authorities ($15k, cops
off ~2 min), drop ammo/armor/BST, request Buzzard. VIP Work: Headhunter (4 targets, 2 in armored cars with 3 guards,
2★ per kill), Sightseer (find 3 packages in 15 min, CEO blipped after the first), Executive Search (CEO hides in an
area 10 min), Asset Recovery, Piracy Prevention, Executive Deathmatch.

## What makes AI "players" feel real (community observations)
Oppressor Mk II and Buzzard griefers; rooftop snipers (Maze Bank, FIB, Legion); spawn camping at Legion/LSIA; sticky
bombs on personal vehicles; tank griefers; crews of 2–4 with the same tag roaming together and taking revenge;
passive-mode taunting with emotes; horn/headlight spam; sending mugger/bounty after losing; Ghost Organization after
dying; showing off at LS Customs / car meet; job invites; rage-quitting after losing a 1v1.

## Natives (names as in alloc8or's nativedb; all checked present in SHVDN 3.6 and SHVDN Enhanced `Hash`)
**Racing AI**
- `TASK_VEHICLE_DRIVE_TO_COORD_LONGRANGE(ped, veh, x, y, z, speed m/s, drivingStyle, stopRange)` loads its own path
  nodes. AI brakes near the target: hand over the next checkpoint 15–25 m early.
- `SET_DRIVE_TASK_CRUISE_SPEED` / `SET_DRIVE_TASK_DRIVING_STYLE` change a running drive task without re-tasking.
- `SET_DRIVER_ABILITY` (0–1), `SET_DRIVER_AGGRESSIVENESS` (0–1), `SET_DRIVER_RACING_MODIFIER` (0–1; R* uses .2/.5/1).
- Catch-up: `SET_VEHICLE_CHEAT_POWER_INCREASE(veh, 0.2–1.8)` **every frame**.
- Driving style bits: 1 stop for cars, 2 stop for peds, 4 swerve around cars, 8 steer around stationary cars,
  16 steer around peds, 32 steer around objects, 128 stop at lights, 256 go off-road when avoiding, 512 drive into
  oncoming traffic, 262144 shortcut links, 524288 change lanes around obstructions, 1073741824 force join in road
  direction. Presets: Normal 786603, Avoid traffic 786468, Rushed 1074528293. Racing: 4|8|16|32|512|262144|524288 = 787004.

**Checkpoints / routes**
- `CREATE_CHECKPOINT(type, pos, nextPos, diameter, r, g, b, a, reserved)` is persistent (create once). Cylinder arrow
  types 0–4; chequered = 4/9/14…; rings 10–14 for air. SHVDN: `World.CreateCheckpoint(CheckpointIcon, …)`.
- `GET_CLOSEST_VEHICLE_NODE_WITH_HEADING(x, y, z, out pos, out heading, nodeType 1, 3.0, 0)`.
- `GET_NTH_CLOSEST_VEHICLE_NODE_FAVOUR_DIRECTION(…)` to step a route forward. `GENERATE_DIRECTIONS_TO_COORD` returns
  turn hints only, no points.
- Path nodes only stream near the player: `REQUEST_PATH_NODES_IN_AREA_THIS_FRAME` until `ARE_NODES_LOADED_FOR_AREA`.
- `START_GPS_MULTI_ROUTE` + `ADD_POINT_TO_GPS_MULTI_ROUTE` + `SET_GPS_MULTI_ROUTE_RENDER(true)` draws a road route
  through several points.

**Partners**
- `GET_PLAYER_GROUP(PLAYER_ID())`; `SET_PED_AS_GROUP_MEMBER`, `SET_PED_NEVER_LEAVES_GROUP`, `SET_GROUP_SEPARATION_RANGE`,
  `SET_GROUP_FORMATION`. Members auto-enter the leader's vehicle. Player group ≈7 members [?].
- Vehicle following: `TASK_VEHICLE_ESCORT` (SHVDN `StartVehicleMission(…, VehicleMissionType.Escort, …)`), `TASK_VEHICLE_FOLLOW`.
- Combat attributes: 0 cover, 1 vehicles, 2 drive-bys, 3 leave vehicle, 5 always fight, 13 aggressive, 21 chase on foot,
  42 flank, 46 fight armed when unarmed, 52 vehicle attack, 58 disable flee.

**Aircraft**
- `TASK_HELI_MISSION(pilot, heli, targetVeh, targetPed, x, y, z, mission, speed, radius, heading, maxHeight(int), minHeight(int), slowDownDist, flags)`;
  missions 4 go to, 6 attack, 8 flee, 9 circle. `SET_HELI_BLADES_FULL_SPEED` for an airborne start.
- Oppressor Mk II hover flight has no AI support [?]; skip or fake with forces.
