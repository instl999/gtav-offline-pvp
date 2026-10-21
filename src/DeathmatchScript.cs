using System;
using System.Collections.Generic;
using System.Linq;
using GTA;
using GTA.Math;
using GTA.Native;
using Color = System.Drawing.Color;
using KeyEventArgs = System.Windows.Forms.KeyEventArgs;
using Keys = System.Windows.Forms.Keys;

namespace OfflinePvP
{
    /// <summary>
    /// Single-player "GTA Online style" deathmatch: Free-for-all or Team Deathmatch against AI bots,
    /// with live-tunable bot count, difficulty and accuracy (F7 menu, works mid-match).
    /// </summary>
    public sealed class DeathmatchScript : Script
    {
        enum State { Idle, Running, Ended }

        static readonly string[] FfaModels = {
            "a_m_y_hipster_01", "a_m_y_hipster_02", "a_m_y_business_01", "a_m_y_beach_01", "a_m_y_skater_01",
            "a_m_y_stbla_01", "a_m_y_stwhi_01", "a_m_y_downtown_01", "a_m_y_vinewood_01", "a_m_y_ktown_01",
            "a_f_y_hipster_01", "a_f_y_tourist_01", "a_f_y_bevhills_01", "g_m_y_ballaeast_01", "g_m_y_famca_01",
            "g_m_y_mexgoon_01", "g_m_y_lost_01", "g_m_y_korean_01", "g_m_y_salvagoon_01", "g_m_m_armgoon_01",
            "s_m_y_blackops_01", "s_m_m_highsec_01" };
        static readonly string[] AllyModels = { "s_m_y_blackops_01", "s_m_y_blackops_02", "s_m_y_blackops_03" };
        static readonly string[] EnemyModels = { "g_m_y_lost_01", "g_m_y_lost_02", "g_m_y_lost_03", "g_m_y_mexgoon_01", "g_m_y_mexgoon_02" };

        static readonly Color Blue = Color.FromArgb(255, 93, 182, 229);
        static readonly Color Red = Color.FromArgb(255, 224, 50, 50);
        static readonly Color Gold = Color.FromArgb(255, 240, 200, 80);

        const int FfaGroupCount = 8;
        const int SpawnProtectMs = 1500;
        const int OutOfBoundsMs = 10000;
        const int ResultsMs = 12000;

        readonly Random rng = new Random();
        readonly DmConfig cfg = new DmConfig();
        readonly SimpleMenu menu = new SimpleMenu("Offline PvP");
        readonly BigMessage shard = new BigMessage();
        readonly List<Combatant> combatants = new List<Combatant>();
        readonly List<KeyValuePair<Ped, int>> corpses = new List<KeyValuePair<Ped, int>>();
        readonly List<Vector3> spawns = new List<Vector3>();
        readonly List<FeedEntry> feed = new List<FeedEntry>();

        State state = State.Idle;
        bool startRequested, endRequested;
        int configVersion;
        Combatant player;
        Vector3 center, origin;
        float radius;
        Blip areaBlip;
        int matchStart, endedAt, outOfBoundsSince, nextRosterCheck, nextTeamId;
        string resultLine;
        RelationshipGroup allyGroup, enemyGroup;

        // Last Team Standing
        readonly int[] roundWins = new int[2];
        int roundNo, nextRoundAt;
        bool roundLive;

        // King of the Hill
        const float HillRadius = 12f;
        Vector3 hill;
        Blip hillBlip;
        Combatant king;
        bool hillContested;
        int nextHillTick;

        bool Teams { get { return cfg.Mode == MatchMode.TeamDeathmatch || cfg.Mode == MatchMode.LastTeamStanding; } }
        bool Lts { get { return cfg.Mode == MatchMode.LastTeamStanding; } }
        bool Koth { get { return cfg.Mode == MatchMode.KingOfTheHill; } }
        RelationshipGroup[] ffaGroups;
        readonly List<PickupSpot> pickups = new List<PickupSpot>();
        int nextPickupCheck;
        bool envDirty, timeOverridden, weatherOverridden, savedClockPaused;
        TimeSpan savedTime;
        Weather savedWeather;

        sealed class FeedEntry
        {
            public string Text;
            public int Time;
        }

        enum PickupKind { Weapon, Health, Armor }

        /// <summary>A fixed map location that holds one pickup and refills it on a timer.</summary>
        sealed class PickupSpot
        {
            public Vector3 Position;
            public PickupKind Kind;
            public int Handle;      // native pickup handle, 0 = currently collected / not spawned
            public Blip Blip;
            public int RespawnAt;
        }

        public DeathmatchScript()
        {
            cfg.Load(Settings);
            Progression.Init(System.IO.Path.Combine(BaseDirectory, "OfflinePvP_Profile.ini"));
            BuildMenu();
            Tick += OnTick;
            KeyDown += OnKeyDown;
            Aborted += OnAborted;
            Interval = 0;
        }

        // ------------------------------------------------------------------ menu

        void BuildMenu()
        {
            Func<bool> idle = delegate { return state == State.Idle; };
            Func<bool> always = null;

            menu.AddAction("Start / end match", "Start a match, or end the current one.",
                delegate { if (state == State.Idle) startRequested = true; else endRequested = true; menu.Visible = false; }, always)
                .Value = delegate { return state == State.Idle ? "Start" : (state == State.Running ? "End" : "Finishing"); };

            menu.Add("Mode", "Deathmatch, Team Deathmatch, Last Team Standing (rounds, no respawns) or King of the Hill.",
                delegate { return DmConfig.ModeNames[(int)cfg.Mode]; },
                delegate (int d) { cfg.Mode = (MatchMode)Wrap((int)cfg.Mode + d, DmConfig.ModeNames.Length); }, idle);
            menu.Add("Rounds to win", "LIVE. Last Team Standing: rounds a team needs. No respawns within a round.",
                delegate { return cfg.RoundsToWin.ToString(); },
                delegate (int d) { cfg.RoundsToWin = DmConfig.Clamp(cfg.RoundsToWin + d, 1, 10); }, delegate { return Lts; });
            menu.Add("Hill target score", "LIVE. King of the Hill: 1 point per second you hold the hill alone.",
                delegate { return cfg.HillTarget.ToString(); },
                delegate (int d) { cfg.HillTarget = DmConfig.Clamp(cfg.HillTarget + d * 10, 30, 600); }, delegate { return Koth; });
            menu.Add("Arena", "Where the match is played. 'Current location' uses where you stand.",
                delegate { return DmConfig.Arenas[cfg.Arena].Name; },
                delegate (int d) { cfg.Arena = Wrap(cfg.Arena + d, DmConfig.Arenas.Length); }, idle);
            menu.Add("Arena radius", "Size of the play area. Leaving it for 10s kills you.",
                delegate { return cfg.ArenaRadius + " m"; },
                delegate (int d) { cfg.ArenaRadius = DmConfig.Clamp(cfg.ArenaRadius + d * 10, 40, 250); }, idle);

            menu.Add("Enemy bots", "LIVE. Number of hostile AI players.",
                delegate { return cfg.EnemyCount.ToString(); },
                delegate (int d) { cfg.EnemyCount = DmConfig.Clamp(cfg.EnemyCount + d, 1, 40); Changed(); }, always);
            menu.Add("Allied bots", "LIVE. AI teammates (team modes only).",
                delegate { return cfg.AllyCount.ToString(); },
                delegate (int d) { cfg.AllyCount = DmConfig.Clamp(cfg.AllyCount + d, 0, 20); Changed(); }, always);
            menu.Add("Difficulty", "LIVE. Combat skill, fire rate, health/armor, tactics. Resets accuracy to the preset.",
                delegate { return cfg.DifficultyProfile.Name; },
                delegate (int d)
                {
                    cfg.Difficulty = DmConfig.Clamp(cfg.Difficulty + d, 0, DmConfig.Difficulties.Length - 1);
                    cfg.Accuracy = cfg.DifficultyProfile.DefaultAccuracy;
                    Changed();
                }, always);
            menu.Add("Accuracy", "LIVE. Bot hit chance, 0-100%.",
                delegate { return cfg.Accuracy + "%"; },
                delegate (int d) { cfg.Accuracy = DmConfig.Clamp(cfg.Accuracy + d * 5, 0, 100); Changed(); }, always);
            menu.Add("Aggression toward you", "LIVE. Chance an enemy bot hunts you instead of the nearest target.",
                delegate { return cfg.PlayerBias + "%"; },
                delegate (int d) { cfg.PlayerBias = DmConfig.Clamp(cfg.PlayerBias + d * 5, 0, 100); Changed(); }, always);
            menu.Add("Weapons", "LIVE. Bot loadout; applies from each bot's next respawn.",
                delegate { return DmConfig.Loadouts[cfg.Loadout].Name; },
                delegate (int d) { cfg.Loadout = Wrap(cfg.Loadout + d, DmConfig.Loadouts.Length); Changed(); }, always);
            menu.Add("Kill limit", "LIVE. First player (FFA) or team (TDM) to reach it wins. 0 = off.",
                delegate { return cfg.KillLimit == 0 ? "Off" : cfg.KillLimit.ToString(); },
                delegate (int d) { cfg.KillLimit = DmConfig.Clamp(cfg.KillLimit + d * 5, 0, 500); Changed(); }, always);
            menu.Add("Time limit", "LIVE. Match length in minutes. 0 = off.",
                delegate { return cfg.TimeLimit == 0 ? "Off" : cfg.TimeLimit + " min"; },
                delegate (int d) { cfg.TimeLimit = DmConfig.Clamp(cfg.TimeLimit + d, 0, 60); Changed(); }, always);
            menu.Add("Respawn delay", "LIVE. Seconds before you and bots respawn.",
                delegate { return cfg.RespawnDelay + " s"; },
                delegate (int d) { cfg.RespawnDelay = DmConfig.Clamp(cfg.RespawnDelay + d, 1, 15); Changed(); }, always);
            menu.Add("Radar blips", "LIVE. Show every player on the radar, like Online.",
                delegate { return OnOff(cfg.Blips); },
                delegate (int d) { cfg.Blips = !cfg.Blips; Changed(); }, always);
            menu.Add("Name tags", "LIVE. Gamertags above visible players.",
                delegate { return OnOff(cfg.NameTags); },
                delegate (int d) { cfg.NameTags = !cfg.NameTags; Changed(); }, always);
            menu.Add("Health bars", "LIVE. Health and armor bars above visible players.",
                delegate { return OnOff(cfg.HealthBars); },
                delegate (int d) { cfg.HealthBars = !cfg.HealthBars; Changed(); }, always);
            menu.Add("Pickups", "LIVE. Weapon, health and armor pickups around the arena.",
                delegate { return OnOff(cfg.Pickups); },
                delegate (int d) { cfg.Pickups = !cfg.Pickups; Changed(); }, always);
            menu.Add("Pickup respawn", "LIVE. Seconds before a collected pickup comes back.",
                delegate { return cfg.PickupRespawn + " s"; },
                delegate (int d) { cfg.PickupRespawn = DmConfig.Clamp(cfg.PickupRespawn + d * 5, 5, 120); Changed(); }, always);
            menu.Add("Suicide penalty", "LIVE. Dying without a killer (falls, explosions, out of bounds) costs 1 kill.",
                delegate { return OnOff(cfg.SuicidePenalty); },
                delegate (int d) { cfg.SuicidePenalty = !cfg.SuicidePenalty; Changed(); }, always);
            menu.Add("Time of day", "LIVE. Locks the clock for the match; restored afterwards.",
                delegate { return DmConfig.TimeNames[cfg.TimeOfDay]; },
                delegate (int d) { cfg.TimeOfDay = Wrap(cfg.TimeOfDay + d, DmConfig.TimeNames.Length); envDirty = true; }, always);
            menu.Add("Weather", "LIVE. Weather for the match; restored afterwards.",
                delegate { return DmConfig.WeatherNames[cfg.Weather]; },
                delegate (int d) { cfg.Weather = Wrap(cfg.Weather + d, DmConfig.WeatherNames.Length); envDirty = true; }, always);
            menu.Add("Clear traffic and pedestrians", "LIVE. Removes ambient peds, cars and police from the arena.",
                delegate { return OnOff(cfg.ClearAmbient); },
                delegate (int d) { cfg.ClearAmbient = !cfg.ClearAmbient; Changed(); }, always);
            menu.Add("Give me the loadout", "Receive the selected weapon set whenever you spawn.",
                delegate { return OnOff(cfg.PlayerLoadout); },
                delegate (int d) { cfg.PlayerLoadout = !cfg.PlayerLoadout; }, always);
            menu.Add("Return after match", "Teleport back to where you started when the match ends.",
                delegate { return OnOff(cfg.ReturnAfterMatch); },
                delegate (int d) { cfg.ReturnAfterMatch = !cfg.ReturnAfterMatch; }, always);
            menu.AddAction("Save settings", "Write these settings to OfflinePvP.ini.",
                delegate { cfg.Save(Settings); GTA.UI.Notification.Show("~g~Deathmatch settings saved."); }, always);
        }

        void Changed()
        {
            configVersion++;
        }

        static string OnOff(bool b)
        {
            return b ? "On" : "Off";
        }

        static int Wrap(int v, int n)
        {
            return ((v % n) + n) % n;
        }

        void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == cfg.MenuKey)
            {
                menu.Visible = !menu.Visible;
                return;
            }
            if (menu.HandleKey(e.KeyCode)) return;
            if (state == State.Ended && e.KeyCode == Keys.Enter) endedAt = 0; // skip results screen
        }

        // ------------------------------------------------------------------ main loop

        void OnTick(object sender, EventArgs e)
        {
            if (Session.OpenJobsMenu)
            {
                Session.OpenJobsMenu = false;
                menu.Visible = true;
            }
            menu.Subtitle = state == State.Idle ? "JOBS" : (state == State.Running ? "MATCH IN PROGRESS" : "MATCH OVER");
            menu.Tick();
            shard.Tick();

            if (startRequested)
            {
                startRequested = false;
                StartMatch();
            }
            if (endRequested)
            {
                endRequested = false;
                if (state == State.Running) EndMatch(true);
            }

            if (state == State.Idle) return;

            if (envDirty)
            {
                envDirty = false;
                ApplyEnvironment();
            }
            KeepDeathSystemSuppressed();
            SuppressWorld();
            player.Ped = Game.Player.Character;

            int now = Game.GameTime;
            if (state == State.Running)
            {
                UpdatePlayer(now);
                UpdateBots(now);
                UpdateCorpses(now);
                if (now >= nextPickupCheck)
                {
                    nextPickupCheck = now + 250;
                    UpdatePickups(now);
                }
                if (now >= nextRosterCheck)
                {
                    nextRosterCheck = now + 500;
                    SyncRoster();
                }
                if (Lts && !roundLive && nextRoundAt != 0 && now >= nextRoundAt) StartRound();
                if (Koth && now >= nextHillTick)
                {
                    nextHillTick = now + 1000;
                    UpdateHill();
                }
                if (state == State.Running && cfg.TimeLimit > 0 && now - matchStart >= cfg.TimeLimit * 60000) EndMatch(false);
            }
            if (Koth && state != State.Idle) DrawHill();
            else if (state == State.Ended && now - endedAt > ResultsMs)
            {
                Cleanup(false);
                return;
            }

            DrawOverheads();
            DrawHud(now);
            DrawFeed(now);
            if (state == State.Ended || Game.IsKeyPressed(cfg.ScoreboardKey)) DrawScoreboard();
        }

        // ------------------------------------------------------------------ match lifecycle

        void StartMatch()
        {
            if (state != State.Idle) return;
            Ped me = Game.Player.Character;
            if (me.IsDead)
            {
                GTA.UI.Notification.Show("~r~You can't start a deathmatch while dead.");
                return;
            }

            GTA.UI.Screen.FadeOut(500);
            Wait(600);

            origin = me.Position;
            radius = cfg.ArenaRadius;
            center = cfg.Arena == 0 ? me.Position : DmConfig.Arenas[cfg.Arena].Center;

            if (me.IsInVehicle()) me.Task.ClearAllImmediately();
            me.IsPositionFrozen = true; // don't fall through unstreamed ground
            me.Position = center;

            // Give the world time to stream collision/navmesh around the arena.
            int deadline = Game.GameTime + 4000;
            float ground = 0f;
            while (Game.GameTime < deadline)
            {
                Wait(250);
                ground = World.GetGroundHeight(new Vector2(center.X, center.Y));
                if (ground != 0f && Game.GameTime > deadline - 2500) break;
            }
            if (ground != 0f) center = new Vector3(center.X, center.Y, ground);
            me.IsPositionFrozen = false;

            GenerateSpawns();
            SetupRelationships(me);
            SaveEnvironment();
            ApplyEnvironment();

            combatants.Clear();
            feed.Clear();
            nextTeamId = 1;
            player = new Combatant { Name = "You", IsPlayer = true, Team = 0, Ped = me, Alive = true };
            combatants.Add(player);

            if (cfg.ClearAmbient)
            {
                Function.Call(Hash.CLEAR_AREA_OF_PEDS, center.X, center.Y, center.Z, radius + 50f, 1);
                Function.Call(Hash.CLEAR_AREA_OF_COPS, center.X, center.Y, center.Z, radius + 50f, 0);
            }

            Session.MatchActive = true; // freemode session stands down
            DeathControl.Acquire(DeathOwner);
            Function.Call(Hash.SET_MAX_WANTED_LEVEL, 0);
            Game.Player.WantedLevel = 0;
            Game.Player.IgnoredByPolice = true;

            areaBlip = World.CreateBlip(center, radius);
            areaBlip.Color = BlipColor.Purple;
            areaBlip.Alpha = 70;

            state = State.Running;
            int now = Game.GameTime;
            matchStart = now;
            nextRosterCheck = now;
            nextPickupCheck = now;
            outOfBoundsSince = 0;
            roundWins[0] = roundWins[1] = 0;
            roundNo = 1;
            nextRoundAt = 0;
            roundLive = false;
            king = null;
            hillContested = false;
            if (Koth) SetupHill();
            // Everyone is in from the first second, like an Online job (SyncRoster adds at most 3 per side per pass).
            for (int i = 0; i < 20 && combatants.Count(c => !c.IsPlayer) < cfg.EnemyCount + (Teams ? cfg.AllyCount : 0); i++) SyncRoster();
            roundLive = Lts;

            PlacePlayer();
            GTA.UI.Screen.FadeIn(800);
            string title = DmConfig.ModeNames[(int)cfg.Mode].ToUpperInvariant();
            string sub;
            if (Lts) sub = string.Format("No respawns. First team to {0} rounds", cfg.RoundsToWin);
            else if (Koth) sub = string.Format("Hold the hill alone. First to {0} points", cfg.HillTarget);
            else sub = cfg.KillLimit > 0 ? string.Format("First to {0} kills", cfg.KillLimit) : "Most kills wins";
            shard.Show("SHOW_SHARD_CENTERED_MP_MESSAGE", title, sub, 4000);
            AddFeed(string.Format("~p~{0}~s~ at {1}", title, DmConfig.Arenas[cfg.Arena].Name));
        }

        void EndMatch(bool aborted)
        {
            if (state != State.Running) return;
            state = State.Ended;
            endedAt = Game.GameTime;

            foreach (Combatant c in combatants)
            {
                if (c.Ped != null && c.Ped.Exists()) c.Ped.ResetOpacity();
                c.SpawnProtectUntil = 0;
                if (c.IsPlayer || c.Ped == null || !c.Ped.Exists() || c.Ped.IsDead) continue;
                c.Ped.Task.ClearAll();
                c.Ped.IsInvincible = true;
                c.Ped.BlockPermanentEvents = true;
            }
            if (player.Alive) Game.Player.Character.IsInvincible = true;

            string title, sub;
            if (Teams)
            {
                int us = TeamScore(0), them = TeamScore(1);
                title = us > them ? "~b~WINNER" : (us < them ? "~r~LOSER" : "DRAW");
                sub = string.Format("Your team {0} - {1} Enemy team", us, them);
            }
            else
            {
                List<Combatant> ranked = Ranked();
                Combatant top = ranked[0];
                int pos = ranked.IndexOf(player) + 1;
                title = top == player ? "~y~WINNER" : string.Format("{0}{1} PLACE", pos, Ordinal(pos));
                sub = top == player ? string.Format("You won with {0} {1}", ScoreOf(player), ScoreUnit)
                                    : string.Format("{0} won with {1} {2}", top.Name, ScoreOf(top), ScoreUnit);
            }
            if (aborted)
            {
                sub = "Match ended early. " + sub; // like quitting a job in Online: no payout
            }
            else
            {
                bool won = Teams ? TeamScore(0) > TeamScore(1) : Ranked()[0] == player;
                int rp = 500 + 100 * Math.Max(0, player.Kills) + 5 * player.Points + (won ? 1000 : 0);
                int cash = 1000 + 250 * Math.Max(0, player.Kills) + (won ? 4000 : 0);
                Progression.AddRP(rp, DmConfig.ModeNames[(int)cfg.Mode]);
                Progression.AddCash(cash, DmConfig.ModeNames[(int)cfg.Mode] + " payout");
                Progression.RecordMatch(won);
                sub += string.Format("   +{0:N0} RP  +${1:N0}", rp, cash);
            }
            resultLine = sub;
            shard.Show("SHOW_SHARD_CENTERED_MP_MESSAGE", title, sub, 6000);
            Audio.PlaySoundFrontend("Mission_Pass_Notify", "DLC_HEISTS_GENERAL_FRONTEND_SOUNDS");
        }

        void Cleanup(bool scriptAborting)
        {
            foreach (Combatant c in combatants)
            {
                if (c.IsPlayer) continue;
                DeleteBlip(c);
                if (c.Ped != null && c.Ped.Exists()) c.Ped.Delete();
            }
            foreach (KeyValuePair<Ped, int> kv in corpses)
            {
                if (kv.Key != null && kv.Key.Exists()) kv.Key.Delete();
            }
            corpses.Clear();
            combatants.Clear();
            if (areaBlip != null && areaBlip.Exists()) areaBlip.Delete();
            areaBlip = null;
            ClearPickups();
            RestoreEnvironment();
            if (hillBlip != null && hillBlip.Exists()) hillBlip.Delete();
            hillBlip = null;
            shard.Hide();

            Ped me = Game.Player.Character;
            if (me.IsDead) ResurrectPlayer(me.Position, me.Heading);
            me.IsInvincible = false;
            me.ResetOpacity();

            Game.Player.IgnoredByPolice = false;
            Function.Call(Hash.SET_MAX_WANTED_LEVEL, 5);
            RestoreDeathSystem(!scriptAborting);

            if (!scriptAborting && cfg.ReturnAfterMatch && origin != Vector3.Zero)
            {
                GTA.UI.Screen.FadeOut(400);
                Wait(450);
                if (me.IsInVehicle()) me.Task.ClearAllImmediately();
                me.Position = origin;
                Wait(500);
                GTA.UI.Screen.FadeIn(600);
            }
            state = State.Idle;
            Session.MatchActive = false;
        }

        void OnAborted(object sender, EventArgs e)
        {
            if (state != State.Idle) Cleanup(true);
        }

        // ------------------------------------------------------------------ roster (live bot count)

        void SyncRoster()
        {
            if (state != State.Running) return;
            bool tdm = Teams;
            SyncSide(false, cfg.EnemyCount);
            SyncSide(true, tdm ? cfg.AllyCount : 0);
        }

        void SyncSide(bool allies, int wanted)
        {
            List<Combatant> side = combatants.Where(c => !c.IsPlayer && c.IsAlly == allies && !c.PendingRemoval).ToList();
            // Count raised again: bots that were about to leave stay instead.
            foreach (Combatant c in combatants.Where(c => !c.IsPlayer && c.IsAlly == allies && c.PendingRemoval))
            {
                if (side.Count >= wanted) break;
                c.PendingRemoval = false;
                side.Add(c);
            }
            int spawnedThisPass = 0;
            for (int have = side.Count; have < wanted && spawnedThisPass < 3; have++, spawnedThisPass++)
            {
                Combatant c = new Combatant
                {
                    Name = Session.TakeJobName(TakenNames(), rng) ?? BotNames.Make(rng, TakenNames()),
                    IsAlly = allies,
                    Team = Teams ? (allies ? 0 : 1) : nextTeamId++,
                };
                combatants.Add(c);
                if (Lts && roundLive)
                {
                    c.RespawnAt = int.MaxValue; // Last Team Standing: joins at the next round
                    AddFeed(string.Format("{0} joined (next round).", Colored(c)));
                    continue;
                }
                SpawnBot(c);
                AddFeed(string.Format("{0} joined.", Colored(c)));
            }
            // Too many: dead ones leave now, live ones after they next die (no popping mid-fight).
            for (int i = side.Count - 1; i >= 0 && side.Count(c => !c.PendingRemoval) > wanted; i--)
            {
                Combatant c = side[i];
                c.PendingRemoval = true;
                if (!c.Alive) RemoveBot(c);
            }
        }

        void RemoveBot(Combatant c)
        {
            DeleteBlip(c);
            if (c.Ped != null && c.Ped.Exists())
            {
                if (c.Alive) c.Ped.Delete();
                else c.Ped.MarkAsNoLongerNeeded();
            }
            combatants.Remove(c);
            if (c.Target != null) c.Target = null;
            foreach (Combatant o in combatants) if (o.Target == c) o.Target = null;
            AddFeed(string.Format("{0} left.", Colored(c)));
        }

        // ------------------------------------------------------------------ spawning

        void GenerateSpawns()
        {
            spawns.Clear();
            for (int attempt = 0; attempt < 300 && spawns.Count < 40; attempt++)
            {
                double ang = rng.NextDouble() * Math.PI * 2;
                float dist = (float)(Math.Sqrt(rng.NextDouble()) * radius * 0.9);
                float x = center.X + (float)Math.Cos(ang) * dist;
                float y = center.Y + (float)Math.Sin(ang) * dist;
                float z = World.GetGroundHeight(new Vector2(x, y));
                if (z == 0f) z = center.Z;
                Vector3 safe = World.GetSafeCoordForPed(new Vector3(x, y, z + 1f), false, 2 | 4 | 8); // not isolated, not interior, not water
                if (safe == Vector3.Zero) continue;
                if (Flat(safe, center) > radius) continue;
                if (Math.Abs(safe.Z - center.Z) > 25f) continue; // rooftops, tunnels, sea floor
                if (spawns.Any(s => s.DistanceTo(safe) < 6f)) continue;
                spawns.Add(safe);
            }
            if (spawns.Count < 4)
            {
                // Navmesh not loaded or arena over water: fall back to a ring near the center.
                for (int i = 0; i < 12; i++)
                {
                    double ang = Math.PI * 2 * i / 12.0;
                    spawns.Add(center + new Vector3((float)Math.Cos(ang) * 15f, (float)Math.Sin(ang) * 15f, 0.5f));
                }
            }
        }

        /// <summary>Online-style spawn: among a few random points pick the one farthest from enemies.</summary>
        Vector3 PickSpawn(Combatant who)
        {
            Vector3 best = spawns[rng.Next(spawns.Count)];
            float bestScore = -1f;
            for (int i = 0; i < 8; i++)
            {
                Vector3 cand = spawns[rng.Next(spawns.Count)];
                float nearest = 9999f;
                foreach (Combatant c in combatants)
                {
                    if (c == who || !c.Alive || !who.IsHostileTo(c) || c.Ped == null || !c.Ped.Exists()) continue;
                    float d = c.Ped.Position.DistanceTo(cand);
                    if (d < nearest) nearest = d;
                }
                if (nearest > bestScore)
                {
                    bestScore = nearest;
                    best = cand;
                }
            }
            return best;
        }

        void SpawnBot(Combatant c)
        {
            if (c.Ped != null && c.Ped.Exists())
            {
                if (c.Ped.IsDead) TrackCorpse(c.Ped);
                else c.Ped.Delete();
            }
            c.Ped = null;

            string[] pool = Teams ? (c.IsAlly ? AllyModels : EnemyModels) : FfaModels;
            Model model = LoadModel(pool);
            if (model == null)
            {
                c.RespawnAt = Game.GameTime + 2000; // retry later
                return;
            }

            Vector3 pos = PickSpawn(c);
            Ped ped = World.CreatePed(model, pos, (float)rng.Next(360));
            model.MarkAsNoLongerNeeded();
            if (ped == null)
            {
                c.RespawnAt = Game.GameTime + 2000;
                return;
            }

            c.Ped = ped;
            c.Alive = true;
            c.Target = null;
            c.NextThink = Game.GameTime + 300;
            c.SpawnProtectUntil = Game.GameTime + SpawnProtectMs;

            Function.Call(Hash.SET_PED_RANDOM_COMPONENT_VARIATION, ped, 0);
            ped.RelationshipGroup = GroupFor(c);
            ped.DropsEquippedWeaponOnDeath = false;
            ped.IsInvincible = true;

            DifficultyProfile p = cfg.DifficultyProfile;
            ped.MaxHealth = 100 + p.ExtraHealth;
            ped.Health = ped.MaxHealth;
            ped.Armor = p.Armor;

            WeaponHash[] set = DmConfig.Loadouts[cfg.Loadout].Weapons;
            WeaponHash w = set[rng.Next(set.Length)];
            ped.Weapons.Give(w, 9999, true, true);

            ApplyStats(c);
            UpdateBlip(c);
        }

        void ApplyStats(Combatant c)
        {
            Ped ped = c.Ped;
            if (ped == null || !ped.Exists()) return;
            DifficultyProfile p = cfg.DifficultyProfile;

            ped.Accuracy = cfg.Accuracy;
            ped.ShootRate = p.ShootRate;
            Function.Call(Hash.SET_PED_COMBAT_ABILITY, ped, p.CombatAbility);
            Function.Call(Hash.SET_PED_COMBAT_MOVEMENT, ped, p.CombatMovement);
            Function.Call(Hash.SET_PED_COMBAT_RANGE, ped, 1);
            Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, ped, 0, p.UseCover);  // can use cover
            Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, ped, 1, false);       // can use vehicles
            Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, ped, 2, false);       // can do drivebys
            Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, ped, 5, true);        // fight armed peds when unarmed
            Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, ped, 46, true);       // always fight
            Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, ped, 58, true);       // never flee from combat
            Function.Call(Hash.SET_PED_FLEE_ATTRIBUTES, ped, 0, false);
            Function.Call(Hash.SET_PED_SEEING_RANGE, ped, radius * 2f);
            Function.Call(Hash.SET_PED_HEARING_RANGE, ped, radius * 2f);
            Function.Call(Hash.SET_PED_DIES_WHEN_INJURED, ped, true);           // no writhing on the ground
            Function.Call(Hash.SET_PED_SUFFERS_CRITICAL_HITS, ped, true);       // headshots count
            Function.Call(Hash.SET_PED_KEEP_TASK, ped, true);
            ped.MaxHealth = 100 + p.ExtraHealth;
            if (ped.Health > ped.MaxHealth) ped.Health = ped.MaxHealth;

            c.AppliedConfigVersion = configVersion;
            UpdateBlip(c);
        }

        Model LoadModel(string[] pool)
        {
            for (int attempt = 0; attempt < 4; attempt++)
            {
                Model m = new Model(pool[rng.Next(pool.Length)]);
                if (!m.IsInCdImage || !m.IsPed) continue;
                if (m.Request(1000)) return m;
            }
            return null;
        }

        void PlacePlayer()
        {
            Vector3 pos = PickSpawn(player);
            Ped me = Game.Player.Character;
            me.Position = pos;
            me.Heading = (float)rng.Next(360);
            me.Health = me.MaxHealth;
            me.Armor = 50;
            me.IsInvincible = true;
            player.Alive = true;
            player.SpawnProtectUntil = Game.GameTime + SpawnProtectMs;
            if (cfg.PlayerLoadout)
            {
                foreach (WeaponHash w in DmConfig.Loadouts[cfg.Loadout].Weapons) me.Weapons.Give(w, 9999, false, true);
                me.Weapons.Select(DmConfig.Loadouts[cfg.Loadout].Weapons[0], true);
            }
        }

        // ------------------------------------------------------------------ relationships

        void SetupRelationships(Ped me)
        {
            RelationshipGroup playerGroup = me.RelationshipGroup;
            allyGroup = World.AddRelationshipGroup("SPDM_ALLY");
            enemyGroup = World.AddRelationshipGroup("SPDM_ENEMY");
            allyGroup.SetRelationshipBetweenGroups(playerGroup, Relationship.Companion, true);
            allyGroup.SetRelationshipBetweenGroups(allyGroup, Relationship.Companion, true);
            enemyGroup.SetRelationshipBetweenGroups(enemyGroup, Relationship.Companion, true);
            enemyGroup.SetRelationshipBetweenGroups(playerGroup, Relationship.Hate, true);
            enemyGroup.SetRelationshipBetweenGroups(allyGroup, Relationship.Hate, true);

            // FFA: several mutually hostile groups (each also hates itself) so every bot fights every bot.
            ffaGroups = new RelationshipGroup[FfaGroupCount];
            for (int i = 0; i < FfaGroupCount; i++) ffaGroups[i] = World.AddRelationshipGroup("SPDM_FFA_" + i);
            for (int i = 0; i < FfaGroupCount; i++)
            {
                ffaGroups[i].SetRelationshipBetweenGroups(playerGroup, Relationship.Hate, true);
                for (int j = 0; j < FfaGroupCount; j++) ffaGroups[i].SetRelationshipBetweenGroups(ffaGroups[j], Relationship.Hate, true);
            }
        }

        RelationshipGroup GroupFor(Combatant c)
        {
            if (Teams) return c.IsAlly ? allyGroup : enemyGroup;
            return ffaGroups[c.Team % FfaGroupCount];
        }

        // ------------------------------------------------------------------ per-frame updates

        void UpdatePlayer(int now)
        {
            Ped me = player.Ped;
            if (player.Alive) UpdateSpawnProtection(player, now);

            Game.DisableControlThisFrame(Control.CharacterWheel);

            if (player.Alive && me.IsDead)
            {
                OnDeath(player);
                return;
            }
            if (!player.Alive)
            {
                if (now >= player.RespawnAt)
                {
                    Vector3 pos = PickSpawn(player);
                    ResurrectPlayer(pos, (float)rng.Next(360));
                    PlacePlayer();
                }
                return;
            }

            // Out of bounds countdown, like Online.
            if (Flat(me.Position, center) > radius + 5f)
            {
                if (outOfBoundsSince == 0) outOfBoundsSince = now;
                int left = (OutOfBoundsMs - (now - outOfBoundsSince)) / 1000 + 1;
                GTA.UI.Screen.ShowSubtitle(string.Format("~r~Return to the deathmatch area: {0}s", left), 100);
                if (now - outOfBoundsSince > OutOfBoundsMs)
                {
                    me.Kill();
                    outOfBoundsSince = 0;
                }
            }
            else
            {
                outOfBoundsSince = 0;
            }
        }

        void UpdateBots(int now)
        {
            // Copy: callbacks may add/remove combatants.
            foreach (Combatant c in combatants.ToArray())
            {
                if (c.IsPlayer) continue;

                if (c.Alive && (c.Ped == null || !c.Ped.Exists() || c.Ped.IsDead))
                {
                    OnDeath(c);
                    continue;
                }
                if (!c.Alive)
                {
                    if (now >= c.RespawnAt)
                    {
                        if (c.PendingRemoval) RemoveBot(c);
                        else SpawnBot(c);
                    }
                    continue;
                }

                UpdateSpawnProtection(c, now);
                if (now >= c.NextThink)
                {
                    c.NextThink = now + 900 + rng.Next(600);
                    Think(c, now);
                }
            }
        }

        /// <summary>Online-style spawn protection: invincible and flickering until it runs out or you open fire.</summary>
        static void UpdateSpawnProtection(Combatant c, int now)
        {
            if (c.SpawnProtectUntil == 0) return;
            Ped ped = c.Ped;
            if (ped == null || !ped.Exists()) return;
            if (now > c.SpawnProtectUntil || ped.IsShooting)
            {
                EndSpawnProtection(c);
                return;
            }
            ped.Opacity = (now / 120) % 2 == 0 ? 255 : 100;
        }

        static void EndSpawnProtection(Combatant c)
        {
            c.SpawnProtectUntil = 0;
            if (c.Ped == null || !c.Ped.Exists()) return;
            c.Ped.IsInvincible = false;
            c.Ped.ResetOpacity();
        }

        void Think(Combatant bot, int now)
        {
            if (bot.AppliedConfigVersion != configVersion) ApplyStats(bot);

            Ped ped = bot.Ped;
            if (Koth)
            {
                ThinkHill(bot, now);
                return;
            }
            bool targetValid = bot.Target != null && bot.Target.Alive && bot.Target.Ped != null
                && bot.Target.Ped.Exists() && bot.IsHostileTo(bot.Target);
            Combatant next = bot.Target;
            if (!targetValid || now - bot.TargetPickedAt > 10000)
            {
                next = PickTarget(bot);
                bot.TargetPickedAt = now;
            }

            bool inCombat = Function.Call<bool>(Hash.IS_PED_IN_COMBAT, ped, 0);
            if (next != null)
            {
                if (next != bot.Target || !inCombat) ped.Task.FightAgainst(next.Ped);
            }
            else if (Flat(ped.Position, center) > radius)
            {
                ped.Task.RunTo(spawns[rng.Next(spawns.Count)]);
            }
            else if (!inCombat)
            {
                ped.Task.FightAgainstHatedTargets(radius * 2f);
            }
            bot.Target = next;

            bot.VisibleToPlayer = Function.Call<bool>(Hash.HAS_ENTITY_CLEAR_LOS_TO_ENTITY, Game.Player.Character, ped, 17);
        }

        Combatant PickTarget(Combatant bot)
        {
            if (!bot.IsAlly && player.Alive && bot.IsHostileTo(player) && rng.Next(100) < cfg.PlayerBias) return player;

            Combatant best = null;
            float bestScore = float.MaxValue;
            Vector3 from = bot.Ped.Position;
            foreach (Combatant c in combatants)
            {
                if (!c.Alive || !bot.IsHostileTo(c) || c.Ped == null || !c.Ped.Exists()) continue;
                float score = c.Ped.Position.DistanceTo(from) + (float)rng.NextDouble() * 20f; // jitter avoids dog-piling
                if (score < bestScore)
                {
                    bestScore = score;
                    best = c;
                }
            }
            return best;
        }

        void OnDeath(Combatant victim)
        {
            int now = Game.GameTime;
            victim.Alive = false;
            victim.Deaths++;
            victim.Streak = 0;
            victim.RespawnAt = Lts ? int.MaxValue : now + cfg.RespawnDelay * 1000; // LTS: out until the next round
            DeleteBlip(victim);
            if (victim.Ped != null && victim.Ped.Exists()) victim.Ped.ResetOpacity();
            victim.SpawnProtectUntil = 0;

            Combatant killer = ResolveKiller(victim.Ped);
            bool penalty = false;
            if (killer != null && killer != victim)
            {
                bool teamKill = !killer.IsHostileTo(victim);
                if (teamKill)
                {
                    killer.Kills--;
                    AddFeed(string.Format("{0} ~o~team-killed~s~ {1}", Colored(killer), Colored(victim)));
                }
                else
                {
                    killer.Kills++;
                    killer.Streak++;
                    AddFeed(string.Format("{0} killed {1}", Colored(killer), Colored(victim)));
                }
                if (killer.IsPlayer && !teamKill)
                {
                    OnPlayerKill(victim);
                    Progression.RecordKill();
                }
            }
            else
            {
                // No other combatant got the kill: fell, blew themselves up or left the arena. Online docks a point for it.
                penalty = cfg.SuicidePenalty;
                if (penalty) victim.Kills--;
                string how = killer != victim ? "died" : (victim.IsPlayer ? "killed yourself" : "killed themselves");
                AddFeed(string.Format("{0} {1}.{2}", Colored(victim), how, penalty ? " ~r~-1~s~" : ""));
            }

            if (victim.IsPlayer)
            {
                Progression.RecordDeath();
                string by = killer != null && killer != victim ? "Killed by " + killer.Name : (penalty ? "Suicide: -1 kill" : "");
                shard.Show("SHOW_SHARD_WASTED_MP_MESSAGE", "~r~WASTED", by, Lts ? 3000 : cfg.RespawnDelay * 1000);
            }
            else
            {
                TrackCorpse(victim.Ped);
                victim.Ped = null;
            }

            CheckKillLimit();
            CheckRoundEnd();
        }

        void OnPlayerKill(Combatant victim)
        {
            Audio.PlaySoundFrontend("HACKING_SUCCESS", "");
            Weapon cur = Game.Player.Character.Weapons.Current;
            if (cur != null && cur.Hash != WeaponHash.Unarmed) cur.Ammo += cur.MaxAmmoInClip * 2; // Online-style ammo pickup
            string msg = null;
            switch (player.Streak)
            {
                case 3: msg = "~y~Killing spree!"; break;
                case 5: msg = "~o~Rampage!"; break;
                case 8: msg = "~r~Unstoppable!"; break;
                case 12: msg = "~p~Godlike!"; break;
            }
            if (msg != null) GTA.UI.Screen.ShowSubtitle(msg, 2500);
        }

        Combatant ResolveKiller(Ped victim)
        {
            if (victim == null || !victim.Exists()) return null;
            Entity e = victim.Killer;
            Ped kp = e as Ped;
            if (kp == null)
            {
                Vehicle v = e as Vehicle;
                if (v != null) kp = v.Driver;
            }
            if (kp == null) return null;
            foreach (Combatant c in combatants)
            {
                if (c.Ped != null && c.Ped.Handle == kp.Handle) return c;
            }
            return null;
        }

        void CheckKillLimit()
        {
            if (state != State.Running || Lts) return;
            if (Koth)
            {
                if (combatants.Any(c => c.Points >= cfg.HillTarget)) EndMatch(false);
                return;
            }
            if (cfg.KillLimit <= 0) return;
            if (Teams)
            {
                if (TeamScore(0) >= cfg.KillLimit || TeamScore(1) >= cfg.KillLimit) EndMatch(false);
            }
            else if (combatants.Any(c => c.Kills >= cfg.KillLimit))
            {
                EndMatch(false);
            }
        }

        void TrackCorpse(Ped ped)
        {
            if (ped == null || !ped.Exists()) return;
            corpses.Add(new KeyValuePair<Ped, int>(ped, Game.GameTime));
        }

        void UpdateCorpses(int now)
        {
            for (int i = corpses.Count - 1; i >= 0; i--)
            {
                bool tooMany = corpses.Count > 15 && i == 0;
                if (now - corpses[i].Value > 20000 || tooMany)
                {
                    Ped p = corpses[i].Key;
                    if (p != null && p.Exists()) p.Delete();
                    corpses.RemoveAt(i);
                }
            }
        }

        // ------------------------------------------------------------------ pickups

        void CreatePickupSpots()
        {
            ClearPickups();
            // Half weapons, a quarter each health and armor, spread over random spawn points.
            List<Vector3> pool = spawns.OrderBy(x => rng.Next()).ToList();
            int count = Math.Min(pool.Count, DmConfig.Clamp(spawns.Count / 3, 4, 14));
            for (int i = 0; i < count; i++)
            {
                PickupKind kind = i % 4 == 1 ? PickupKind.Health : (i % 4 == 3 ? PickupKind.Armor : PickupKind.Weapon);
                pickups.Add(new PickupSpot { Position = pool[i], Kind = kind });
            }
        }

        void UpdatePickups(int now)
        {
            if (!cfg.Pickups)
            {
                if (pickups.Count > 0) ClearPickups();
                return;
            }
            if (pickups.Count == 0) CreatePickupSpots();
            foreach (PickupSpot spot in pickups)
            {
                if (spot.Handle != 0)
                {
                    bool gone = !Function.Call<bool>(Hash.DOES_PICKUP_EXIST, spot.Handle)
                        || Function.Call<bool>(Hash.HAS_PICKUP_BEEN_COLLECTED, spot.Handle);
                    if (gone)
                    {
                        RemovePickup(spot);
                        spot.RespawnAt = now + cfg.PickupRespawn * 1000;
                    }
                }
                else if (now >= spot.RespawnAt)
                {
                    SpawnPickup(spot);
                }
                UpdatePickupBlip(spot);
            }
        }

        void SpawnPickup(PickupSpot spot)
        {
            PickupType type;
            int value = 0;
            if (spot.Kind == PickupKind.Health) type = PickupType.Health;
            else if (spot.Kind == PickupKind.Armor) type = PickupType.Armour;
            else
            {
                type = WeaponPickupFor(DmConfig.Loadouts[cfg.Loadout].Weapons); // follows live loadout changes
                value = 120; // ammo
            }
            const int flags = (1 << 3) | (1 << 8) | (1 << 9); // placement: snap to ground, upright, rotate
            spot.Handle = Function.Call<int>(Hash.CREATE_PICKUP, type, spot.Position.X, spot.Position.Y, spot.Position.Z,
                flags, value, true, 0);
            if (spot.Handle == 0) spot.RespawnAt = Game.GameTime + 5000; // retry later
        }

        /// <summary>A random pickup from the loadout; weapons with no pickup type are skipped, falling back to a carbine.</summary>
        PickupType WeaponPickupFor(WeaponHash[] set)
        {
            List<PickupType> types = new List<PickupType>();
            foreach (WeaponHash w in set)
            {
                PickupType t;
                if (Enum.TryParse("Weapon" + w, true, out t)) types.Add(t);
            }
            return types.Count > 0 ? types[rng.Next(types.Count)] : PickupType.WeaponCarbineRifle;
        }

        void UpdatePickupBlip(PickupSpot spot)
        {
            if (!cfg.Blips || spot.Handle == 0)
            {
                if (spot.Blip != null && spot.Blip.Exists()) spot.Blip.Delete();
                spot.Blip = null;
                return;
            }
            if (spot.Blip != null && spot.Blip.Exists()) return;
            spot.Blip = World.CreateBlip(spot.Position);
            spot.Blip.Sprite = spot.Kind == PickupKind.Health ? BlipSprite.Health
                : (spot.Kind == PickupKind.Armor ? BlipSprite.Armor : BlipSprite.Pistol);
            spot.Blip.Color = spot.Kind == PickupKind.Health ? BlipColor.Green
                : (spot.Kind == PickupKind.Armor ? BlipColor.Blue : BlipColor.White);
            spot.Blip.Scale = 0.6f;
            spot.Blip.IsShortRange = true;
        }

        static void RemovePickup(PickupSpot spot)
        {
            if (spot.Handle != 0 && Function.Call<bool>(Hash.DOES_PICKUP_EXIST, spot.Handle))
                Function.Call(Hash.REMOVE_PICKUP, spot.Handle);
            spot.Handle = 0;
            if (spot.Blip != null && spot.Blip.Exists()) spot.Blip.Delete();
            spot.Blip = null;
        }

        void ClearPickups()
        {
            foreach (PickupSpot spot in pickups) RemovePickup(spot);
            pickups.Clear();
        }

        // ------------------------------------------------------------------ time of day / weather

        void SaveEnvironment()
        {
            savedTime = World.CurrentTimeOfDay;
            savedWeather = World.Weather;
            savedClockPaused = World.IsClockPaused;
            timeOverridden = weatherOverridden = false;
        }

        void ApplyEnvironment()
        {
            int hour = DmConfig.TimeHours[cfg.TimeOfDay];
            if (hour >= 0)
            {
                World.CurrentTimeOfDay = new TimeSpan(hour, 0, 0);
                World.IsClockPaused = true;
                timeOverridden = true;
            }
            else if (timeOverridden)
            {
                RestoreTime();
            }

            string weather = DmConfig.WeatherIds[cfg.Weather];
            if (weather != null)
            {
                Function.Call(Hash.SET_OVERRIDE_WEATHER, weather);
                Function.Call(Hash.SET_WEATHER_TYPE_NOW_PERSIST, weather);
                weatherOverridden = true;
            }
            else if (weatherOverridden)
            {
                RestoreWeather();
            }
        }

        void RestoreEnvironment()
        {
            if (timeOverridden) RestoreTime();
            if (weatherOverridden) RestoreWeather();
        }

        void RestoreTime()
        {
            World.CurrentTimeOfDay = savedTime;
            World.IsClockPaused = savedClockPaused;
            timeOverridden = false;
        }

        void RestoreWeather()
        {
            Function.Call(Hash.CLEAR_OVERRIDE_WEATHER);
            Function.Call(Hash.CLEAR_WEATHER_TYPE_PERSIST);
            World.Weather = savedWeather;
            weatherOverridden = false;
        }

        // ------------------------------------------------------------------ death system / world

        const string DeathOwner = "deathmatch";

        static void KeepDeathSystemSuppressed()
        {
            DeathControl.KeepSuppressed();
        }

        static void RestoreDeathSystem(bool canWait)
        {
            DeathControl.Release(DeathOwner, canWait);
        }

        static void ResurrectPlayer(Vector3 pos, float heading)
        {
            DeathControl.Resurrect(pos, heading);
        }

        void SuppressWorld()
        {
            Game.Player.WantedLevel = 0;
            if (!cfg.ClearAmbient) return;
            Function.Call(Hash.SET_PED_DENSITY_MULTIPLIER_THIS_FRAME, 0f);
            Function.Call(Hash.SET_SCENARIO_PED_DENSITY_MULTIPLIER_THIS_FRAME, 0f, 0f);
            Function.Call(Hash.SET_VEHICLE_DENSITY_MULTIPLIER_THIS_FRAME, 0f);
            Function.Call(Hash.SET_RANDOM_VEHICLE_DENSITY_MULTIPLIER_THIS_FRAME, 0f);
            Function.Call(Hash.SET_PARKED_VEHICLE_DENSITY_MULTIPLIER_THIS_FRAME, 0f);
        }

        // ------------------------------------------------------------------ HUD

        void UpdateBlip(Combatant c)
        {
            bool want = cfg.Blips && c.Alive && c.Ped != null && c.Ped.Exists();
            if (!want)
            {
                DeleteBlip(c);
                return;
            }
            if (c.Blip == null || !c.Blip.Exists())
            {
                c.Blip = c.Ped.AddBlip();
                c.Blip.Scale = 0.75f;
                c.Blip.Name = c.Name;
            }
            c.Blip.Color = c.IsAlly ? BlipColor.Blue : BlipColor.Red;
        }

        static void DeleteBlip(Combatant c)
        {
            if (c.Blip != null && c.Blip.Exists()) c.Blip.Delete();
            c.Blip = null;
        }

        /// <summary>Name tags and Online-style health/armor bars above visible players.</summary>
        void DrawOverheads()
        {
            if (!cfg.NameTags && !cfg.HealthBars) return;
            Vector3 me = Game.Player.Character.Position;
            foreach (Combatant c in combatants)
            {
                if (c.IsPlayer || !c.Alive || c.Ped == null || !c.Ped.Exists()) continue;
                float d = c.Ped.Position.DistanceTo(me);
                if (d > (c.IsAlly ? 120f : 60f)) continue;
                if (!c.IsAlly && !c.VisibleToPlayer) continue;
                System.Drawing.PointF p = GTA.UI.Screen.WorldToScreen(c.Ped.Position + new Vector3(0f, 0f, 1.15f));
                if (p.IsEmpty) continue;
                float scale = Math.Max(0.22f, 0.38f - d * 0.003f);
                Color color = c.IsAlly ? Blue : Red;
                if (cfg.NameTags) Draw.Text(c.Name, p.X, p.Y, scale, color, GTA.UI.Alignment.Center);
                if (cfg.HealthBars) DrawHealthBar(c.Ped, p.X, p.Y - 9f, 70f * scale / 0.38f, color);
            }
        }

        static void DrawHealthBar(Ped ped, float cx, float y, float w, Color color)
        {
            // Bots die at the injured threshold (100), so the bar covers only the health above it.
            int max = Math.Max(1, ped.MaxHealth - 100);
            float hp = Math.Max(0f, Math.Min(1f, (ped.Health - 100) / (float)max));
            float armor = Math.Max(0f, Math.Min(1f, ped.Armor / 100f));
            float x = cx - w / 2f;
            Draw.Rect(x - 1f, y - 1f, w + 2f, 7f, Color.FromArgb(170, 0, 0, 0));
            Draw.Rect(x, y, w * hp, 3f, color);
            if (armor > 0f) Draw.Rect(x, y + 3.5f, w * armor, 1.5f, Color.FromArgb(255, 220, 220, 220));
        }

        void DrawHud(int now)
        {
            const float right = 1270f, w = 250f;
            float y = 200f;
            Draw.Rect(right - w, y, w, 96f, Color.FromArgb(160, 0, 0, 0));

            string time = "--:--";
            if (cfg.TimeLimit > 0)
            {
                int left = Math.Max(0, cfg.TimeLimit * 60000 - ((state == State.Ended ? endedAt : now) - matchStart)) / 1000;
                time = string.Format("{0:00}:{1:00}", left / 60, left % 60);
            }

            if (Koth)
            {
                string kingText = hillContested ? "~o~CONTESTED" : (king == null ? "~c~Hill empty" : (king == player ? "~b~You are King" : "~r~King: " + king.Name));
                Draw.Text(kingText, 640f, 60f, 0.5f, Color.White, GTA.UI.Alignment.Center);
            }
            if (Teams)
            {
                Draw.Text(string.Format("~b~YOUR TEAM {0}", TeamScore(0)), right - w + 8f, y + 6f, 0.42f, Color.White, GTA.UI.Alignment.Left);
                Draw.Text(string.Format("~r~ENEMY {0}", TeamScore(1)), right - 8f, y + 6f, 0.42f, Color.White, GTA.UI.Alignment.Right);
            }
            else
            {
                List<Combatant> ranked = Ranked();
                int pos = ranked.IndexOf(player) + 1;
                Draw.Text(string.Format("{0}{1} / {2}", pos, Ordinal(pos), ranked.Count), right - w + 8f, y + 6f, 0.42f, Gold, GTA.UI.Alignment.Left);
                Combatant leader = ranked[0];
                Draw.Text(string.Format("Leader: {0} ({1})", leader == player ? "You" : leader.Name, ScoreOf(leader)),
                    right - 8f, y + 10f, 0.3f, Color.White, GTA.UI.Alignment.Right);
            }
            string line2;
            if (Koth) line2 = string.Format("Points {0} / {1}   Kills {2}", player.Points, cfg.HillTarget, player.Kills);
            else if (Lts) line2 = string.Format("Round {0}   First to {1}   K {2} D {3}", roundNo, cfg.RoundsToWin, player.Kills, player.Deaths);
            else line2 = string.Format("Kills {0}{1}   Deaths {2}", player.Kills, cfg.KillLimit > 0 ? " / " + cfg.KillLimit : "", player.Deaths);
            Draw.Text(line2, right - w + 8f, y + 38f, 0.36f, Color.White, GTA.UI.Alignment.Left);
            Draw.Text("Time  " + time, right - w + 8f, y + 64f, 0.36f, Color.White, GTA.UI.Alignment.Left);
            Draw.Text(string.Format("Hold {0}: scores", cfg.ScoreboardKey), right - 8f, y + 66f, 0.28f,
                Color.FromArgb(255, 180, 180, 180), GTA.UI.Alignment.Right);

            if (state == State.Running && !player.Alive)
            {
                string msg;
                if (Lts)
                {
                    int alive = combatants.Count(c => c.Team == 0 && c.Alive);
                    msg = nextRoundAt != 0 ? "Next round starting..." : string.Format("Eliminated. {0} teammate{1} still alive", alive, alive == 1 ? "" : "s");
                }
                else
                {
                    msg = string.Format("Respawning in {0}", Math.Max(0, (player.RespawnAt - now) / 1000 + 1));
                }
                Draw.Text(msg, 640f, 600f, 0.6f, Color.White, GTA.UI.Alignment.Center);
            }
        }

        void DrawFeed(int now)
        {
            feed.RemoveAll(f => now - f.Time > 7000);
            float y = 305f;
            for (int i = Math.Max(0, feed.Count - 6); i < feed.Count; i++)
            {
                Draw.Text(feed[i].Text, 1262f, y, 0.32f, Color.White, GTA.UI.Alignment.Right);
                y += 20f;
            }
        }

        void DrawScoreboard()
        {
            const float w = 560f, x = 640f - w / 2f, rowH = 22f;
            float y = 90f;
            Draw.Rect(x, y, w, 34f, Color.FromArgb(230, 180, 20, 30));
            string head = state == State.Ended ? "RESULTS" : DmConfig.ModeNames[(int)cfg.Mode].ToUpperInvariant();
            Draw.Text(head, 640f, y + 3f, 0.6f, Color.White, GTA.UI.Alignment.Center, GTA.UI.Font.Pricedown);
            y += 34f;
            if (state == State.Ended && resultLine != null)
            {
                Draw.Rect(x, y, w, rowH, Color.FromArgb(220, 0, 0, 0));
                Draw.Text(resultLine + "   (Enter to close)", 640f, y + 2f, 0.32f, Gold, GTA.UI.Alignment.Center);
                y += rowH;
            }

            if (Teams)
            {
                y = DrawTeamRows(y, 0, string.Format("YOUR TEAM  {0}", TeamScore(0)), Blue, x, w, rowH);
                y = DrawTeamRows(y + 4f, 1, string.Format("ENEMY TEAM  {0}", TeamScore(1)), Red, x, w, rowH);
            }
            else
            {
                DrawRowHeader(x, y, w, rowH);
                y += rowH;
                int pos = 1;
                foreach (Combatant c in Ranked()) y = DrawRow(c, pos++, x, y, w, rowH);
            }
        }

        float DrawTeamRows(float y, int team, string title, Color color, float x, float w, float rowH)
        {
            Draw.Rect(x, y, w, rowH, Color.FromArgb(230, color.R / 3, color.G / 3, color.B / 3));
            Draw.Text(title, x + 8f, y + 2f, 0.36f, color, GTA.UI.Alignment.Left);
            y += rowH;
            DrawRowHeader(x, y, w, rowH);
            y += rowH;
            int pos = 1;
            foreach (Combatant c in Ranked().Where(c => c.Team == team)) y = DrawRow(c, pos++, x, y, w, rowH);
            return y;
        }

        void DrawRowHeader(float x, float y, float w, float rowH)
        {
            Color grey = Color.FromArgb(255, 170, 170, 170);
            Draw.Rect(x, y, w, rowH, Color.FromArgb(220, 0, 0, 0));
            Draw.Text("#", x + 10f, y + 3f, 0.28f, grey, GTA.UI.Alignment.Left);
            Draw.Text("PLAYER", x + 40f, y + 3f, 0.28f, grey, GTA.UI.Alignment.Left);
            Draw.Text(Koth ? "POINTS" : "KILLS", x + w - 170f, y + 3f, 0.28f, grey, GTA.UI.Alignment.Right);
            Draw.Text(Koth ? "KILLS" : "DEATHS", x + w - 95f, y + 3f, 0.28f, grey, GTA.UI.Alignment.Right);
            Draw.Text(Koth ? "DEATHS" : "K/D", x + w - 15f, y + 3f, 0.28f, grey, GTA.UI.Alignment.Right);
        }

        float DrawRow(Combatant c, int pos, float x, float y, float w, float rowH)
        {
            bool me = c.IsPlayer;
            Draw.Rect(x, y, w, rowH, me ? Color.FromArgb(220, 60, 60, 20) : Color.FromArgb(180, 0, 0, 0));
            Color fg = me ? Gold : (c.Alive ? Color.White : Color.FromArgb(255, 150, 150, 150));
            string kd = (c.Deaths == 0 ? (float)c.Kills : (float)c.Kills / c.Deaths).ToString("0.00");
            Draw.Text(pos.ToString(), x + 10f, y + 2f, 0.32f, fg, GTA.UI.Alignment.Left);
            Draw.Text(me ? Game.Player.Name : c.Name, x + 40f, y + 2f, 0.32f, fg, GTA.UI.Alignment.Left);
            Draw.Text((Koth ? c.Points : c.Kills).ToString(), x + w - 170f, y + 2f, 0.32f, fg, GTA.UI.Alignment.Right);
            Draw.Text((Koth ? c.Kills : c.Deaths).ToString(), x + w - 95f, y + 2f, 0.32f, fg, GTA.UI.Alignment.Right);
            Draw.Text(Koth ? c.Deaths.ToString() : kd, x + w - 15f, y + 2f, 0.32f, fg, GTA.UI.Alignment.Right);
            return y + rowH;
        }

        // ------------------------------------------------------------------ Last Team Standing

        void CheckRoundEnd()
        {
            if (!Lts || !roundLive || state != State.Running) return;
            bool usAlive = combatants.Any(c => c.Team == 0 && c.Alive);
            bool themAlive = combatants.Any(c => c.Team == 1 && c.Alive);
            if (usAlive && themAlive) return;

            roundLive = false;
            int winner = usAlive ? 0 : (themAlive ? 1 : -1);
            if (winner >= 0) roundWins[winner]++;
            string title = winner == 0 ? "~b~ROUND WON" : (winner == 1 ? "~r~ROUND LOST" : "ROUND DRAW");
            shard.Show("SHOW_SHARD_CENTERED_MP_MESSAGE", title,
                string.Format("Your team {0} - {1} Enemy team", roundWins[0], roundWins[1]), 4000);
            if (winner >= 0 && roundWins[winner] >= cfg.RoundsToWin)
            {
                EndMatch(false);
                return;
            }
            nextRoundAt = Game.GameTime + 5000;
        }

        void StartRound()
        {
            nextRoundAt = 0;
            roundNo++;
            foreach (Combatant c in combatants.ToArray())
            {
                if (c.IsPlayer) continue;
                if (c.PendingRemoval) RemoveBot(c);
                else SpawnBot(c); // replaces a living ped too, so everyone starts fresh
            }
            if (Game.Player.Character.IsDead) ResurrectPlayer(PickSpawn(player), (float)rng.Next(360));
            PlacePlayer();
            roundLive = true;
            shard.Show("SHOW_SHARD_CENTERED_MP_MESSAGE", "ROUND " + roundNo,
                string.Format("Your team {0} - {1} Enemy team", roundWins[0], roundWins[1]), 2500);
        }

        // ------------------------------------------------------------------ King of the Hill

        void SetupHill()
        {
            hill = spawns.OrderBy(p => Flat(p, center)).First();
            hillBlip = World.CreateBlip(hill, HillRadius);
            hillBlip.Color = BlipColor.Yellow;
            hillBlip.Alpha = 140;
            nextHillTick = Game.GameTime + 1000;
        }

        bool InHill(Vector3 pos)
        {
            return Flat(pos, hill) <= HillRadius && Math.Abs(pos.Z - hill.Z) < 6f;
        }

        /// <summary>Once a second: a sole occupant is King and scores; two or more contest it.</summary>
        void UpdateHill()
        {
            List<Combatant> inside = combatants.Where(c => c.Alive && c.Ped != null && c.Ped.Exists() && !c.Ped.IsDead && InHill(c.Ped.Position)).ToList();
            Combatant newKing = inside.Count == 1 ? inside[0] : null;
            hillContested = inside.Count > 1;
            if (newKing != null && newKing != king)
            {
                AddFeed(string.Format("{0} {1} the hill.", Colored(newKing), newKing.IsPlayer ? "take" : "takes"));
                if (newKing.IsPlayer) Audio.PlaySoundFrontend("CHECKPOINT_PERFECT", "HUD_MINI_GAME_SOUNDSET");
            }
            king = newKing;
            if (king != null) king.Points++;
            CheckKillLimit();
        }

        void DrawHill()
        {
            Color c = hillContested ? Color.FromArgb(110, 255, 140, 0)
                : (king == null ? Color.FromArgb(80, 255, 255, 255) : (king == player ? Color.FromArgb(110, 93, 182, 229) : Color.FromArgb(110, 224, 50, 50)));
            World.DrawMarker(MarkerType.VerticalCylinder, hill - new Vector3(0f, 0f, 1f), Vector3.Zero, Vector3.Zero,
                new Vector3(HillRadius * 2f, HillRadius * 2f, 2f), c, false, false, false, null, null, false);
            if (hillBlip != null && hillBlip.Exists())
                hillBlip.Color = hillContested ? BlipColor.Orange : (king == null ? BlipColor.Yellow : (king == player ? BlipColor.Blue : BlipColor.Red));
        }

        /// <summary>Bots fight anyone close, otherwise push onto the hill and hold it.</summary>
        void ThinkHill(Combatant bot, int now)
        {
            Ped ped = bot.Ped;
            Combatant near = null;
            float best = 30f;
            foreach (Combatant c in combatants)
            {
                if (!c.Alive || !bot.IsHostileTo(c) || c.Ped == null || !c.Ped.Exists()) continue;
                float d = c.Ped.Position.DistanceTo(ped.Position);
                if (InHill(c.Ped.Position)) d -= 15f; // whoever holds the hill is the priority
                if (d < best)
                {
                    best = d;
                    near = c;
                }
            }
            if (near != null)
            {
                if (near != bot.Target || !Function.Call<bool>(Hash.IS_PED_IN_COMBAT, ped, 0)) ped.Task.FightAgainst(near.Ped);
                bot.Target = near;
                bot.HeadingToHill = false;
            }
            else if (!InHill(ped.Position))
            {
                if (!bot.HeadingToHill)
                {
                    ped.Task.RunTo(hill + new Vector3(rng.Next(-6, 6), rng.Next(-6, 6), 0f));
                    bot.HeadingToHill = true;
                }
                bot.Target = null;
            }
            else
            {
                if (bot.HeadingToHill || bot.Target != null) ped.Task.FightAgainstHatedTargets(60f);
                bot.HeadingToHill = false;
                bot.Target = null;
            }
            bot.VisibleToPlayer = Function.Call<bool>(Hash.HAS_ENTITY_CLEAR_LOS_TO_ENTITY, Game.Player.Character, ped, 17);
        }

        // ------------------------------------------------------------------ helpers

        List<Combatant> Ranked()
        {
            return combatants.OrderByDescending(ScoreOf).ThenBy(c => c.Deaths).ThenBy(c => c.IsPlayer ? 0 : 1).ToList();
        }

        int ScoreOf(Combatant c)
        {
            return Koth ? c.Points : c.Kills;
        }

        string ScoreUnit { get { return Koth ? "points" : "kills"; } }

        List<string> TakenNames()
        {
            return combatants.Select(x => x.Name).ToList();
        }

        int TeamScore(int team)
        {
            if (Lts) return roundWins[team];
            return combatants.Where(c => c.Team == team).Sum(c => c.Kills);
        }

        string Colored(Combatant c)
        {
            if (c.IsPlayer) return "~y~You~s~";
            if (Teams) return (c.IsAlly ? "~b~" : "~r~") + c.Name + "~s~";
            return "~r~" + c.Name + "~s~";
        }

        void AddFeed(string text)
        {
            feed.Add(new FeedEntry { Text = text, Time = Game.GameTime });
        }

        static float Flat(Vector3 a, Vector3 b)
        {
            float dx = a.X - b.X, dy = a.Y - b.Y;
            return (float)Math.Sqrt(dx * dx + dy * dy);
        }

        static string Ordinal(int n)
        {
            if (n % 100 >= 11 && n % 100 <= 13) return "th";
            switch (n % 10)
            {
                case 1: return "st";
                case 2: return "nd";
                case 3: return "rd";
                default: return "th";
            }
        }
    }
}
