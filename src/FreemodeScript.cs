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
    internal enum AiTask { None, Cruise, Wander, Hunt, Fight }

    /// <summary>
    /// An AI "player" in the freemode session. Far away it is virtual (a moving blip); within streaming range it
    /// materializes as a real ped, usually in a car, and dematerializes again when you drive off.
    /// </summary>
    internal sealed class SessionPlayer
    {
        public string Name, Crew;
        public int Rank;
        public Vector3 Pos, Dest;
        public bool Driving;
        public Ped Ped;
        public Vehicle Car;
        public Blip Blip;
        public bool Hostile;
        public int HostileUntil;
        public bool Dead;
        public int RespawnAt;
        public int Kills, Deaths, Bounty, BountyUntil;
        public int NextThink;
        public AiTask Task;
        public bool VisibleToPlayer;

        public bool Materialized { get { return Ped != null; } }
    }

    internal sealed class FmConfig
    {
        public static readonly string[] GriefNames = { "Off", "Low", "Normal", "High" };

        public bool Enabled = false;
        public int Players = 15;
        public int Griefing = 1;
        public int Accuracy = 25;
        public bool Bounties = true;
        public bool OnlineRespawn = true;
        public bool Blips = true;
        public bool NameTags = true;
        public bool Churn = true;
        public Keys MenuKey = Keys.M;
        public Keys ListKey = Keys.Z;

        public void Load(ScriptSettings s)
        {
            Enabled = s.GetValue("Session", "Enabled", Enabled);
            Players = DmConfig.Clamp(s.GetValue("Session", "Players", Players), 1, 29);
            Griefing = DmConfig.Clamp(s.GetValue("Session", "Griefing", Griefing), 0, 3);
            Accuracy = DmConfig.Clamp(s.GetValue("Session", "Accuracy", Accuracy), 0, 100);
            Bounties = s.GetValue("Session", "Bounties", Bounties);
            OnlineRespawn = s.GetValue("Session", "OnlineRespawn", OnlineRespawn);
            Blips = s.GetValue("Session", "Blips", Blips);
            NameTags = s.GetValue("Session", "NameTags", NameTags);
            Churn = s.GetValue("Session", "PlayersJoinAndLeave", Churn);
            Keys k;
            if (Enum.TryParse(s.GetValue("Keys", "InteractionMenu", MenuKey.ToString()), true, out k)) MenuKey = k;
            if (Enum.TryParse(s.GetValue("Keys", "PlayerList", ListKey.ToString()), true, out k)) ListKey = k;
        }

        public void Save(ScriptSettings s)
        {
            s.SetValue("Session", "Enabled", Enabled);
            s.SetValue("Session", "Players", Players);
            s.SetValue("Session", "Griefing", Griefing);
            s.SetValue("Session", "Accuracy", Accuracy);
            s.SetValue("Session", "Bounties", Bounties);
            s.SetValue("Session", "OnlineRespawn", OnlineRespawn);
            s.SetValue("Session", "Blips", Blips);
            s.SetValue("Session", "NameTags", NameTags);
            s.SetValue("Session", "PlayersJoinAndLeave", Churn);
            s.SetValue("Keys", "InteractionMenu", MenuKey.ToString());
            s.SetValue("Keys", "PlayerList", ListKey.ToString());
            s.Save();
        }
    }

    /// <summary>
    /// GTA Online freemode in story mode: a session of AI players who roam, drive, grief, carry bounties and
    /// fight back, plus the interaction menu (snacks, armor, passive mode) and Online-style respawning.
    /// </summary>
    public sealed class FreemodeScript : Script
    {
        const string DeathOwner = "freemode";
        const float SpawnDist = 180f, DespawnDist = 280f;
        const int MaxMaterialized = 8;

        static readonly Vector2[] PointsOfInterest = {
            new Vector2(195f, -935f), new Vector2(300f, 180f), new Vector2(-75f, -820f), new Vector2(-1640f, -1080f),
            new Vector2(-1350f, -1500f), new Vector2(-1035f, -2730f), new Vector2(1100f, -640f), new Vector2(-800f, -180f),
            new Vector2(-110f, -1600f), new Vector2(800f, -1000f), new Vector2(1000f, -3000f), new Vector2(925f, 45f),
            new Vector2(-1450f, 100f), new Vector2(300f, -600f), new Vector2(-700f, -900f), new Vector2(100f, -1300f),
            new Vector2(-200f, 6300f), new Vector2(1850f, 3700f), new Vector2(1700f, 4800f), new Vector2(550f, 2700f),
            new Vector2(-3150f, 1100f), new Vector2(100f, -1750f), new Vector2(-1200f, -400f), new Vector2(1200f, -1400f),
            new Vector2(-500f, 250f), new Vector2(2550f, 400f), new Vector2(-2300f, 300f), new Vector2(1400f, 3600f) };

        static readonly string[] CarModels = { "kuruma2", "zentorno", "t20", "adder", "sultan", "elegy2", "banshee", "bati",
            "akuma", "comet2", "dubsta", "baller", "entityxf", "feltzer2", "carbonizzare", "massacro", "jester", "osiris",
            "turismor", "nightshade" };

        static readonly WeaponHash[] Primaries = { WeaponHash.CarbineRifle, WeaponHash.AssaultRifle, WeaponHash.SpecialCarbine,
            WeaponHash.SMG, WeaponHash.AssaultSMG, WeaponHash.PumpShotgun, WeaponHash.CombatMG, WeaponHash.MicroSMG };

        static readonly string[] CrewWords = { "LSPD", "GTAO", "KILL", "TRYH", "RICH", "NOOB", "OG", "BLVD", "SAND", "PALM",
            "WEST", "HOOD", "4L", "ZERO", "JUGG", "AFK" };

        static readonly Color White = Color.FromArgb(255, 240, 240, 240);
        static readonly Color Red = Color.FromArgb(255, 224, 50, 50);
        static readonly Color Gold = Color.FromArgb(255, 240, 200, 80);
        static readonly Color Blue = Color.FromArgb(255, 93, 182, 229);

        readonly Random rng = new Random();
        readonly FmConfig cfg = new FmConfig();
        readonly ScriptSettings settingsFile;
        readonly SimpleMenu root = new SimpleMenu("Interaction");
        readonly SimpleMenu sessionMenu = new SimpleMenu("Online");
        readonly SimpleMenu inventoryMenu = new SimpleMenu("Inventory");
        readonly SimpleMenu profileMenu = new SimpleMenu("Profile");
        readonly BigMessage shard = new BigMessage();

        List<SessionPlayer> Players { get { return Session.Players; } }

        bool running, suspended, passive, playerDead;
        int nextVirtual, nextStreaming, nextHostility, nextGrief, nextBounty, nextChurn, nextRoster, playerRespawnAt;
        int playerBounty, playerBountyUntil, wantedCalm;
        Vector3 deathPos;
        RelationshipGroup neutralGroup, hostileGroup;

        public FreemodeScript()
        {
            settingsFile = ScriptSettings.Load(System.IO.Path.Combine(BaseDirectory, "OfflinePvP_Freemode.ini"));
            cfg.Load(settingsFile);
            Progression.Init(System.IO.Path.Combine(BaseDirectory, "OfflinePvP_Profile.ini"));
            BuildMenus();
            Tick += OnTick;
            KeyDown += OnKeyDown;
            Aborted += delegate { StopSession(false); };
            Interval = 0;
        }

        // ------------------------------------------------------------------ menus

        void BuildMenus()
        {
            Func<bool> always = null;
            Func<bool> inSession = delegate { return running; };
            Func<bool> freeRoam = delegate { return !Session.MatchActive; };
            foreach (SimpleMenu m in new[] { root, sessionMenu, inventoryMenu, profileMenu })
            {
                m.Subtitle = "INTERACTION MENU";
                m.DisabledHelp = "Not available right now.";
            }

            root.AddSubmenu("Online session", "AI players in your world: count, griefers, bounties, respawn.", sessionMenu);
            root.AddSubmenu("Inventory", "Snacks and body armor.", inventoryMenu);
            root.Add("Passive mode", "AI players can't hurt you and you can't hurt them. Griefers back off.",
                delegate { return passive ? "On" : "Off"; },
                delegate (int d) { SetPassive(!passive); }, always);
            root.AddAction("Jobs", "Open the deathmatch / job menu.",
                delegate { root.Visible = false; Session.OpenJobsMenu = true; }, freeRoam);
            root.AddSubmenu("Profile", "Rank, RP and stats.", profileMenu);
            root.AddAction("Kill yourself", "Like Online's interaction-menu option.",
                delegate { root.Visible = false; Game.Player.Character.Kill(); }, freeRoam);

            sessionMenu.AddAction("Session", "Start or leave the Online session.",
                delegate { cfg.Enabled = !cfg.Enabled; if (!cfg.Enabled) StopSession(true); }, always)
                .Value = delegate { return cfg.Enabled ? "On" : "Off"; };
            sessionMenu.AddAction("Find new session", "Swap everyone out for a fresh lobby.",
                delegate { StopSession(false); cfg.Enabled = true; }, inSession);
            sessionMenu.Add("Players", "LIVE. How many AI players share your session (Online max is 30 incl. you).",
                delegate { return cfg.Players.ToString(); },
                delegate (int d) { cfg.Players = DmConfig.Clamp(cfg.Players + d, 1, 29); }, always);
            sessionMenu.Add("Griefers", "LIVE. How often players come after you.",
                delegate { return FmConfig.GriefNames[cfg.Griefing]; },
                delegate (int d) { cfg.Griefing = DmConfig.Clamp(cfg.Griefing + d, 0, 3); }, always);
            sessionMenu.Add("AI player accuracy", "LIVE. Hit chance of AI players.",
                delegate { return cfg.Accuracy + "%"; },
                delegate (int d) { cfg.Accuracy = DmConfig.Clamp(cfg.Accuracy + d * 5, 0, 100); ApplyAll(); }, always);
            sessionMenu.Add("Bounties", "LIVE. Bounties placed on you and on other players.",
                delegate { return cfg.Bounties ? "On" : "Off"; },
                delegate (int d) { cfg.Bounties = !cfg.Bounties; }, always);
            sessionMenu.Add("Online-style respawn", "LIVE. Respawn near where you died instead of at the hospital.",
                delegate { return cfg.OnlineRespawn ? "On" : "Off"; },
                delegate (int d) { cfg.OnlineRespawn = !cfg.OnlineRespawn; }, always);
            sessionMenu.Add("Players join and leave", "LIVE. Lobby churn, like a real public session.",
                delegate { return cfg.Churn ? "On" : "Off"; },
                delegate (int d) { cfg.Churn = !cfg.Churn; }, always);
            sessionMenu.Add("Player blips", "LIVE. Show every player on the map.",
                delegate { return cfg.Blips ? "On" : "Off"; },
                delegate (int d) { cfg.Blips = !cfg.Blips; }, always);
            sessionMenu.Add("Name tags", "LIVE. Names and ranks over nearby players.",
                delegate { return cfg.NameTags ? "On" : "Off"; },
                delegate (int d) { cfg.NameTags = !cfg.NameTags; }, always);
            sessionMenu.AddAction("Save settings", "Write these settings to OfflinePvP_Freemode.ini.",
                delegate { cfg.Save(settingsFile); GTA.UI.Notification.Show("~g~Session settings saved."); }, always);

            for (int i = 0; i < Progression.SnackNames.Length; i++)
            {
                int idx = i;
                inventoryMenu.AddAction(Progression.SnackNames[i], string.Format("Restores {0}% health.", Progression.SnackHeal[i]),
                    delegate { EatSnack(idx); }, always)
                    .Value = delegate { return "x" + Progression.Snacks[idx]; };
            }
            for (int i = 0; i < Progression.ArmorNames.Length; i++)
            {
                int idx = i;
                inventoryMenu.AddAction(Progression.ArmorNames[i], string.Format("Sets armor to {0}%.", Progression.ArmorValue[i]),
                    delegate { UseArmor(idx); }, always)
                    .Value = delegate { return "x" + Progression.Armor[idx]; };
            }
            inventoryMenu.AddAction("Restock everything", "Fill snacks and armor up for $2,500.", Restock, always)
                .Value = delegate { return "$2,500"; };

            profileMenu.Add("Rank", null, delegate { return Progression.Rank.ToString(); }, null, always);
            profileMenu.Add("RP", null, delegate { return Progression.RP.ToString("N0"); }, null, always);
            profileMenu.Add("RP to next rank", null,
                delegate { return (Progression.RpForRank(Progression.Rank + 1) - Progression.RP).ToString("N0"); }, null, always);
            profileMenu.Add("Kills / deaths", null,
                delegate { return string.Format("{0} / {1}", Progression.Kills, Progression.Deaths); }, null, always);
            profileMenu.Add("K/D ratio", null,
                delegate { return (Progression.Deaths == 0 ? Progression.Kills : Progression.Kills / (float)Progression.Deaths).ToString("0.00"); }, null, always);
            profileMenu.Add("Jobs won / played", null,
                delegate { return string.Format("{0} / {1}", Progression.MatchesWon, Progression.MatchesPlayed); }, null, always);
        }

        bool AnyMenuOpen { get { return root.Visible || sessionMenu.Visible || inventoryMenu.Visible || profileMenu.Visible; } }

        void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == cfg.MenuKey)
            {
                if (AnyMenuOpen)
                {
                    foreach (SimpleMenu m in new[] { root, sessionMenu, inventoryMenu, profileMenu }) m.Visible = false;
                }
                else
                {
                    root.Visible = true;
                }
                return;
            }
            foreach (SimpleMenu m in new[] { root, sessionMenu, inventoryMenu, profileMenu })
            {
                if (m.HandleKey(e.KeyCode)) return;
            }
        }

        // ------------------------------------------------------------------ inventory / passive

        void EatSnack(int i)
        {
            Ped me = Game.Player.Character;
            if (Progression.Snacks[i] <= 0) { GTA.UI.Notification.Show("You don't have any " + Progression.SnackNames[i] + "."); return; }
            if (me.Health >= me.MaxHealth) { GTA.UI.Notification.Show("Your health is already full."); return; }
            int usable = Math.Max(1, me.MaxHealth - 100);
            me.Health = Math.Min(me.MaxHealth, me.Health + usable * Progression.SnackHeal[i] / 100);
            Progression.Snacks[i]--;
            Progression.Save();
            Audio.PlaySoundFrontend("PICK_UP", "HUD_FRONTEND_DEFAULT_SOUNDSET");
        }

        void UseArmor(int i)
        {
            Ped me = Game.Player.Character;
            if (Progression.Armor[i] <= 0) { GTA.UI.Notification.Show("You don't have any " + Progression.ArmorNames[i] + "."); return; }
            if (me.Armor >= Progression.ArmorValue[i]) { GTA.UI.Notification.Show("Your armor is already at least that strong."); return; }
            me.Armor = Progression.ArmorValue[i];
            Progression.Armor[i]--;
            Progression.Save();
            Audio.PlaySoundFrontend("PICK_UP", "HUD_FRONTEND_DEFAULT_SOUNDSET");
        }

        void Restock()
        {
            const int cost = 2500;
            if (Game.Player.Money < cost) { GTA.UI.Notification.Show("~r~You can't afford that."); return; }
            for (int i = 0; i < 5; i++)
            {
                Progression.Snacks[i] = Progression.SnackMax[i];
                Progression.Armor[i] = Progression.ArmorMax;
            }
            Progression.AddCash(-cost, "Snacks and armor");
            Progression.Save();
        }

        void SetPassive(bool on)
        {
            passive = on;
            if (on)
            {
                foreach (SessionPlayer p in Players) SetHostile(p, false, 0);
                GTA.UI.Notification.Show("Passive mode ~g~enabled~s~.");
            }
            else
            {
                GTA.UI.Notification.Show("Passive mode ~r~disabled~s~.");
            }
            ApplyAll();
        }

        // ------------------------------------------------------------------ main loop

        void OnTick(object sender, EventArgs e)
        {
            Progression.Tick();
            shard.Tick();
            foreach (SimpleMenu m in new[] { root, sessionMenu, inventoryMenu, profileMenu }) m.Tick();
            if (passive && !Session.MatchActive)
            {
                Draw.Text("PASSIVE", 115f, 548f, 0.45f, Blue, GTA.UI.Alignment.Center);
                Game.DisableControlThisFrame(Control.Attack);
                Game.DisableControlThisFrame(Control.Attack2);
                Game.DisableControlThisFrame(Control.MeleeAttackLight);
                Game.DisableControlThisFrame(Control.MeleeAttackHeavy);
            }

            if (!cfg.Enabled)
            {
                if (running) StopSession(true);
                return;
            }
            if (!running) StartSession();

            bool shouldSuspend = Session.MatchActive || Game.IsMissionActive;
            if (shouldSuspend != suspended)
            {
                suspended = shouldSuspend;
                if (suspended) Suspend();
            }
            if (suspended) return;

            int now = Game.GameTime;
            UpdateDeathOwnership();
            UpdatePlayerDeath(now);

            if (now >= nextRoster) { nextRoster = now + 2000; SyncRoster(false); }
            if (now >= nextVirtual) { UpdateVirtual(now, (now - nextVirtual + 1000) / 1000f); nextVirtual = now + 1000; }
            if (now >= nextStreaming) { nextStreaming = now + 500; UpdateStreaming(now); }
            if (now >= nextHostility) { nextHostility = now + 1000; UpdateHostility(now); }
            if (cfg.Bounties && now >= nextBounty) { nextBounty = now + 60000; RollBounties(now); }
            if (cfg.Churn && now >= nextChurn) { nextChurn = now + 60000 + rng.Next(60000); Churn(); }
            UpdateBounties(now);
            UpdateMaterialized(now);
            UpdateBlips();
            DrawOverheads();
            DrawBountyHud(now);
            if (Game.IsKeyPressed(cfg.ListKey)) DrawPlayerList();
        }

        void StartSession()
        {
            running = true;
            suspended = false;
            neutralGroup = World.AddRelationshipGroup("SPFM_PLAYERS");
            hostileGroup = World.AddRelationshipGroup("SPFM_HOSTILE");
            RelationshipGroup mine = Game.Player.Character.RelationshipGroup;
            neutralGroup.SetRelationshipBetweenGroups(mine, Relationship.Neutral, true);
            neutralGroup.SetRelationshipBetweenGroups(neutralGroup, Relationship.Neutral, true);
            hostileGroup.SetRelationshipBetweenGroups(mine, Relationship.Hate, true);
            hostileGroup.SetRelationshipBetweenGroups(neutralGroup, Relationship.Neutral, true);

            int now = Game.GameTime;
            nextGrief = now + 45000;
            nextBounty = now + 90000;
            nextChurn = now + 90000;
            SyncRoster(true);
            ApplyAll();
            GTA.UI.Notification.Show(string.Format("Joined an ~b~Online~s~ session with {0} players.", Players.Count));
        }

        void StopSession(bool announce)
        {
            if (!running) return;
            foreach (SessionPlayer p in Players.ToArray()) Remove(p);
            Players.Clear();
            ApplyPlayerImmunity(false);
            running = false;
            suspended = false;
            playerBounty = 0;
            if (DeathControl.IsHeldBy(DeathOwner)) DeathControl.Release(DeathOwner, false);
            if (announce) GTA.UI.Notification.Show("You left the Online session.");
        }

        /// <summary>A job or story mission owns the player: get every AI player out of the world until it's over.</summary>
        void Suspend()
        {
            foreach (SessionPlayer p in Players)
            {
                Dematerialize(p);
                DeleteBlip(p);
            }
            if (DeathControl.IsHeldBy(DeathOwner)) DeathControl.Release(DeathOwner, true);
            playerDead = false;
        }

        // ------------------------------------------------------------------ roster

        void SyncRoster(bool silent)
        {
            if (Players.Count < cfg.Players) Join(silent || Players.Count < cfg.Players - 3);
            while (silent && Players.Count < cfg.Players) Join(true);
            if (Players.Count > cfg.Players)
            {
                SessionPlayer leaver = Players.FirstOrDefault(p => !p.Materialized) ?? Players[Players.Count - 1];
                Leave(leaver);
            }
        }

        void Join(bool silent)
        {
            SessionPlayer p = new SessionPlayer
            {
                Name = BotNames.Make(rng, Players.Select(x => x.Name).ToList()),
                Rank = 1 + (int)(Math.Pow(rng.NextDouble(), 2.5) * 650),
                Crew = rng.Next(100) < 30 ? CrewWords[rng.Next(CrewWords.Length)] : null,
                Driving = rng.Next(100) < 70,
            };
            p.Pos = FarPoint(Game.Player.Character.Position, 300f);
            p.Dest = RandomPoi();
            Players.Add(p);
            if (!silent) GTA.UI.Notification.Show(string.Format("~b~{0}~s~ joined.", p.Name));
        }

        void Leave(SessionPlayer p)
        {
            Remove(p);
            GTA.UI.Notification.Show(string.Format("~b~{0}~s~ left.", p.Name));
        }

        void Remove(SessionPlayer p)
        {
            Dematerialize(p);
            DeleteBlip(p);
            Players.Remove(p);
        }

        void Churn()
        {
            if (Players.Count < 2 || rng.Next(100) >= 50) return;
            List<SessionPlayer> away = Players.Where(p => !p.Materialized && !p.Hostile).ToList();
            if (away.Count == 0) return;
            Leave(away[rng.Next(away.Count)]);
            Join(false);
        }

        // ------------------------------------------------------------------ virtual movement & streaming

        Vector3 RandomPoi()
        {
            Vector2 v = PointsOfInterest[rng.Next(PointsOfInterest.Length)];
            return new Vector3(v.X + rng.Next(-80, 80), v.Y + rng.Next(-80, 80), 0f);
        }

        /// <summary>A point of interest at least <paramref name="minDist"/> from <paramref name="from"/>.</summary>
        Vector3 FarPoint(Vector3 from, float minDist)
        {
            for (int i = 0; i < 10; i++)
            {
                Vector3 c = RandomPoi();
                if (Flat(c, from) >= minDist) return c;
            }
            return RandomPoi();
        }

        void UpdateVirtual(int now, float dt)
        {
            dt = Math.Min(dt, 3f);
            Vector3 me = Game.Player.Character.Position;
            foreach (SessionPlayer p in Players)
            {
                if (p.Materialized) continue;
                if (p.Dead)
                {
                    if (now >= p.RespawnAt)
                    {
                        p.Dead = false;
                        p.Pos = p.Hostile ? Around(me, 400f) : FarPoint(me, 400f);
                        p.Dest = RandomPoi();
                    }
                    continue;
                }
                if (p.Hostile && !passive) p.Dest = me; // a griefer heading your way
                float speed = p.Hostile ? 35f : (p.Driving ? 20f : 2f);
                Vector3 to = p.Dest - p.Pos;
                to.Z = 0f;
                float len = to.Length();
                if (len < 30f)
                {
                    p.Dest = RandomPoi();
                    if (rng.Next(100) < 30) p.Driving = !p.Driving;
                    continue;
                }
                p.Pos += to / len * Math.Min(len, speed * dt);
            }
        }

        Vector3 Around(Vector3 c, float dist)
        {
            double a = rng.NextDouble() * Math.PI * 2;
            return new Vector3(c.X + (float)Math.Cos(a) * dist, c.Y + (float)Math.Sin(a) * dist, c.Z);
        }

        void UpdateStreaming(int now)
        {
            Ped mePed = Game.Player.Character;
            Vector3 me = mePed.Position;
            int materialized = Players.Count(p => p.Materialized);
            foreach (SessionPlayer p in Players.OrderBy(x => Flat(x.Pos, me)).ToList())
            {
                if (p.Materialized)
                {
                    if (!p.Ped.Exists())
                    {
                        p.Ped = null;
                        materialized--;
                        continue;
                    }
                    if (!p.Ped.IsDead) p.Pos = p.Ped.Position;
                    if (Flat(p.Pos, me) > DespawnDist && !p.Ped.IsDead)
                    {
                        Dematerialize(p);
                        materialized--;
                    }
                }
                else if (!p.Dead && materialized < MaxMaterialized && Flat(p.Pos, me) < SpawnDist)
                {
                    if (Materialize(p, me)) materialized++;
                }
            }
        }

        bool Materialize(SessionPlayer p, Vector3 me)
        {
            // Appear on the edge of view distance, never right in front of you.
            Vector3 dir = p.Pos - me;
            dir.Z = 0f;
            float len = dir.Length();
            if (len < 1f) dir = Around(Vector3.Zero, 1f);
            else dir /= len;
            Vector3 around = me + dir * Math.Max(len, 130f);

            Model pedModel = Session.LoadModel(rng, Session.PlayerModels, m => m.IsPed);
            if (pedModel == null) return false;

            Ped ped = null;
            Vehicle car = null;
            if (p.Driving)
            {
                Vector3 street = World.GetNextPositionOnStreet(around, true);
                Model carModel = street == Vector3.Zero ? null : Session.LoadModel(rng, CarModels, m => m.IsVehicle);
                if (carModel != null)
                {
                    car = World.CreateVehicle(carModel, street, (float)rng.Next(360));
                    carModel.MarkAsNoLongerNeeded();
                    if (car != null)
                    {
                        car.PlaceOnGround();
                        ped = car.CreatePedOnSeat(VehicleSeat.Driver, pedModel);
                        if (ped == null) { car.Delete(); car = null; }
                    }
                }
            }
            if (ped == null)
            {
                Vector3 walk = World.GetNextPositionOnSidewalk(around);
                if (walk == Vector3.Zero) walk = World.GetSafeCoordForPed(around, true, 0);
                if (walk == Vector3.Zero)
                {
                    pedModel.MarkAsNoLongerNeeded();
                    return false;
                }
                ped = World.CreatePed(pedModel, walk, (float)rng.Next(360));
                p.Driving = false;
            }
            pedModel.MarkAsNoLongerNeeded();
            if (ped == null)
            {
                if (car != null) car.Delete();
                return false;
            }

            p.Ped = ped;
            p.Car = car;
            p.Task = AiTask.None;
            p.NextThink = Game.GameTime + 200;
            Function.Call(Hash.SET_PED_RANDOM_COMPONENT_VARIATION, ped, 0);
            ped.MaxHealth = 300; // Online players soak more than pedestrians
            ped.Health = 300;
            ped.Armor = rng.Next(100) < 50 ? 50 : 0;
            ped.DropsEquippedWeaponOnDeath = false;
            ped.Weapons.Give(WeaponHash.Pistol50, 500, false, true);
            ped.Weapons.Give(Primaries[rng.Next(Primaries.Length)], 999, true, true);
            Function.Call(Hash.SET_PED_COMBAT_ABILITY, ped, 2);
            Function.Call(Hash.SET_PED_COMBAT_MOVEMENT, ped, 2);
            Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, ped, 0, true);   // use cover
            Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, ped, 2, true);   // drive-bys
            Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, ped, 5, true);
            Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, ped, 46, true);  // always fight
            Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, ped, 58, true);  // never flee
            Function.Call(Hash.SET_PED_FLEE_ATTRIBUTES, ped, 0, false);
            Function.Call(Hash.SET_PED_DIES_WHEN_INJURED, ped, true);
            Function.Call(Hash.SET_PED_SUFFERS_CRITICAL_HITS, ped, true);
            Function.Call(Hash.SET_PED_KEEP_TASK, ped, true);
            Apply(p);
            return true;
        }

        void Dematerialize(SessionPlayer p)
        {
            if (p.Ped != null && p.Ped.Exists())
            {
                if (!p.Ped.IsDead) p.Pos = p.Ped.Position;
                p.Ped.Delete();
            }
            p.Ped = null;
            if (p.Car != null && p.Car.Exists())
            {
                Vehicle mine = Game.Player.Character.CurrentVehicle;
                if (mine != null && mine.Handle == p.Car.Handle) p.Car.MarkAsNoLongerNeeded(); // you took their car: keep it
                else p.Car.Delete();
            }
            p.Car = null;
        }

        /// <summary>Re-applies relationship, accuracy and passive-mode damage rules to one materialized player.</summary>
        void Apply(SessionPlayer p)
        {
            if (p.Ped == null || !p.Ped.Exists()) return;
            p.Ped.RelationshipGroup = p.Hostile && !passive ? hostileGroup : neutralGroup;
            p.Ped.Accuracy = cfg.Accuracy;
            RelationshipGroup mine = Game.Player.Character.RelationshipGroup;
            Function.Call(Hash.SET_ENTITY_CAN_BE_DAMAGED_BY_RELATIONSHIP_GROUP, p.Ped, !passive, mine.Hash);
        }

        void ApplyAll()
        {
            if (!running) return;
            foreach (SessionPlayer p in Players) Apply(p);
            ApplyPlayerImmunity(passive);
        }

        /// <summary>Passive mode: AI players can't damage you. Re-applied regularly because character switches swap the ped.</summary>
        void ApplyPlayerImmunity(bool immune)
        {
            Ped me = Game.Player.Character;
            Function.Call(Hash.SET_ENTITY_CAN_BE_DAMAGED_BY_RELATIONSHIP_GROUP, me, !immune, neutralGroup.Hash);
            Function.Call(Hash.SET_ENTITY_CAN_BE_DAMAGED_BY_RELATIONSHIP_GROUP, me, !immune, hostileGroup.Hash);
        }

        // ------------------------------------------------------------------ AI behaviour

        void UpdateMaterialized(int now)
        {
            Ped me = Game.Player.Character;
            bool hostileNearby = false;
            foreach (SessionPlayer p in Players.ToArray())
            {
                if (!p.Materialized || !p.Ped.Exists()) continue;
                if (p.Ped.IsDead)
                {
                    OnAiDeath(p, now);
                    continue;
                }
                if (p.Hostile && p.Ped.Position.DistanceTo(me.Position) < 150f) hostileNearby = true;
                if (now >= p.NextThink)
                {
                    p.NextThink = now + 1000 + rng.Next(500);
                    Think(p, me);
                }
            }
            if (!hostileNearby) wantedCalm = Game.Player.WantedLevel;
        }

        void Think(SessionPlayer p, Ped me)
        {
            Ped ped = p.Ped;
            // Shoot an Online player and they shoot back.
            if (!p.Hostile && !passive && Function.Call<bool>(Hash.HAS_ENTITY_BEEN_DAMAGED_BY_ENTITY, ped, me, true))
            {
                Function.Call(Hash.CLEAR_ENTITY_LAST_DAMAGE_ENTITY, ped);
                SetHostile(p, true, 90000);
            }

            bool hunt = p.Hostile && !passive && !playerDead && !me.IsDead;
            if (hunt)
            {
                float d = ped.Position.DistanceTo(me.Position);
                if (ped.IsInVehicle() && d > 50f)
                {
                    if (p.Task != AiTask.Hunt) { ped.Task.VehicleChase(me); p.Task = AiTask.Hunt; }
                }
                else if (p.Task != AiTask.Fight || !Function.Call<bool>(Hash.IS_PED_IN_COMBAT, ped, me))
                {
                    ped.Task.FightAgainst(me);
                    p.Task = AiTask.Fight;
                }
            }
            else if (p.Task != AiTask.Cruise && p.Task != AiTask.Wander)
            {
                ped.Task.ClearAll();
                if (ped.IsInVehicle())
                {
                    ped.Task.CruiseWithVehicle(ped.CurrentVehicle, 20f, DrivingStyle.Normal);
                    p.Task = AiTask.Cruise;
                }
                else
                {
                    ped.Task.WanderAround();
                    p.Task = AiTask.Wander;
                }
            }
            p.VisibleToPlayer = Function.Call<bool>(Hash.HAS_ENTITY_CLEAR_LOS_TO_ENTITY, me, ped, 17);
        }

        void SetHostile(SessionPlayer p, bool hostile, int durationMs)
        {
            p.Hostile = hostile;
            p.HostileUntil = Game.GameTime + durationMs;
            p.Task = AiTask.None;
            Apply(p);
        }

        void UpdateHostility(int now)
        {
            ApplyPlayerImmunity(passive);
            foreach (SessionPlayer p in Players)
            {
                if (p.Hostile && now > p.HostileUntil) SetHostile(p, false, 0);
            }
            if (passive || cfg.Griefing == 0 || now < nextGrief) return;
            nextGrief = now + 30000;
            int hostiles = Players.Count(p => p.Hostile);
            if (hostiles >= cfg.Griefing || rng.Next(100) >= cfg.Griefing * 25) return;
            Vector3 me = Game.Player.Character.Position;
            List<SessionPlayer> candidates = Players.Where(p => !p.Hostile && !p.Dead && Flat(p.Pos, me) < 2500f).ToList();
            if (candidates.Count == 0) return;
            SetHostile(candidates[rng.Next(candidates.Count)], true, 120000 + rng.Next(60000));
        }

        // ------------------------------------------------------------------ deaths

        void OnAiDeath(SessionPlayer p, int now)
        {
            Ped ped = p.Ped;
            Ped killer = KillerPed(ped);
            Ped me = Game.Player.Character;
            p.Deaths++;
            p.Dead = true;
            p.RespawnAt = now + 8000 + rng.Next(7000);
            if (killer != null && killer.Handle == me.Handle)
            {
                Progression.RecordKill();
                Progression.AddRP(50, "Player kill");
                GTA.UI.Notification.Show(string.Format("You killed ~r~{0}~s~.", p.Name));
                if (p.Bounty > 0)
                {
                    Progression.AddCash(p.Bounty, "Bounty claimed on " + p.Name);
                    p.Bounty = 0;
                }
                // Online doesn't give you a wanted level for killing players.
                if (Game.Player.WantedLevel > wantedCalm) Game.Player.WantedLevel = wantedCalm;
                if (rng.Next(100) < 50) SetHostile(p, true, 90000); // revenge
                else SetHostile(p, false, 0);
            }
            else
            {
                SessionPlayer other = killer == null ? null : Players.FirstOrDefault(o => o.Ped != null && o.Ped.Handle == killer.Handle);
                if (other != null)
                {
                    other.Kills++;
                    GTA.UI.Notification.Show(string.Format("~b~{0}~s~ killed ~b~{1}~s~.", other.Name, p.Name));
                }
                else
                {
                    GTA.UI.Notification.Show(string.Format("~b~{0}~s~ died.", p.Name));
                }
            }
            ped.MarkAsNoLongerNeeded(); // the body stays for a bit, like Online
            p.Ped = null;
            if (p.Car != null && p.Car.Exists()) p.Car.MarkAsNoLongerNeeded();
            p.Car = null;
            p.Task = AiTask.None;
            DeleteBlip(p);
        }

        static Ped KillerPed(Ped victim)
        {
            if (victim == null || !victim.Exists()) return null;
            Entity e = victim.Killer;
            Ped kp = e as Ped;
            if (kp == null)
            {
                Vehicle v = e as Vehicle;
                if (v != null) kp = v.Driver;
            }
            return kp;
        }

        void UpdateDeathOwnership()
        {
            bool want = cfg.OnlineRespawn;
            bool held = DeathControl.IsHeldBy(DeathOwner);
            if (want && !held) DeathControl.Acquire(DeathOwner);
            else if (!want && held && !playerDead) DeathControl.Release(DeathOwner, true);
            if (DeathControl.IsHeldBy(DeathOwner)) DeathControl.KeepSuppressed();
        }

        void UpdatePlayerDeath(int now)
        {
            Ped me = Game.Player.Character;
            if (!playerDead && me.IsDead)
            {
                playerDead = true;
                deathPos = me.Position;
                playerRespawnAt = now + 4000;
                Progression.RecordDeath();
                Ped killer = KillerPed(me);
                SessionPlayer by = killer == null ? null : Players.FirstOrDefault(p => p.Ped != null && p.Ped.Handle == killer.Handle);
                string sub = "";
                if (by != null)
                {
                    by.Kills++;
                    sub = "Killed by " + by.Name;
                    GTA.UI.Notification.Show(string.Format("~r~{0}~s~ killed you.", by.Name));
                    if (playerBounty > 0)
                    {
                        GTA.UI.Notification.Show(string.Format("~r~{0}~s~ claimed the ${1:N0} bounty on you.", by.Name, playerBounty));
                        playerBounty = 0;
                    }
                }
                if (DeathControl.IsHeldBy(DeathOwner)) shard.Show("SHOW_SHARD_WASTED_MP_MESSAGE", "~r~WASTED", sub, 4000);
                return;
            }
            if (!playerDead) return;
            if (!me.IsDead)
            {
                playerDead = false; // story mode handled it (Online respawn off)
                return;
            }
            if (DeathControl.IsHeldBy(DeathOwner) && now >= playerRespawnAt)
            {
                // Online drops you back in nearby, not on top of whoever killed you.
                Vector3 near = Around(deathPos, 80f + rng.Next(70));
                Vector3 pos = World.GetNextPositionOnSidewalk(near);
                if (pos == Vector3.Zero) pos = World.GetNextPositionOnStreet(near, true);
                if (pos == Vector3.Zero) pos = deathPos;
                DeathControl.Resurrect(pos, (float)rng.Next(360));
                playerDead = false;
            }
        }

        // ------------------------------------------------------------------ bounties

        void RollBounties(int now)
        {
            List<SessionPlayer> alive = Players.Where(p => !p.Dead).ToList();
            if (alive.Count == 0) return;
            if (playerBounty == 0 && !passive && rng.Next(100) < 12)
            {
                SessionPlayer from = alive[rng.Next(alive.Count)];
                playerBounty = (2 + rng.Next(9)) * 1000;
                playerBountyUntil = now + 180000;
                GTA.UI.Notification.Show(string.Format("~r~{0}~s~ placed a ~g~${1:N0}~s~ bounty on you.", from.Name, playerBounty));
                foreach (SessionPlayer hunter in alive.Where(p => p != from).OrderBy(p => rng.Next()).Take(2))
                    SetHostile(hunter, true, 180000);
            }
            if (!Players.Any(p => p.Bounty > 0) && rng.Next(100) < 20)
            {
                SessionPlayer target = alive[rng.Next(alive.Count)];
                target.Bounty = (2 + rng.Next(9)) * 1000;
                target.BountyUntil = now + 300000;
                GTA.UI.Notification.Show(string.Format("A ~g~${0:N0}~s~ bounty has been placed on ~r~{1}~s~.", target.Bounty, target.Name));
            }
        }

        void UpdateBounties(int now)
        {
            if (playerBounty > 0 && now > playerBountyUntil && !playerDead)
            {
                Progression.AddCash(playerBounty, "Bounty survived");
                Progression.AddRP(500, "Bounty survived");
                playerBounty = 0;
            }
            foreach (SessionPlayer p in Players)
            {
                if (p.Bounty > 0 && now > p.BountyUntil) p.Bounty = 0;
            }
        }

        // ------------------------------------------------------------------ HUD

        void UpdateBlips()
        {
            foreach (SessionPlayer p in Players)
            {
                if (!cfg.Blips || p.Dead)
                {
                    DeleteBlip(p);
                    continue;
                }
                Vector3 pos = p.Materialized ? p.Ped.Position : p.Pos;
                if (p.Blip == null || !p.Blip.Exists())
                {
                    p.Blip = World.CreateBlip(pos);
                    p.Blip.Sprite = BlipSprite.Standard;
                    p.Blip.Scale = 0.8f;
                    p.Blip.Name = p.Name;
                    p.Blip.ShowsHeadingIndicator = true;
                }
                p.Blip.Position = pos;
                bool red = (p.Hostile && !passive) || p.Bounty > 0;
                p.Blip.Color = red ? BlipColor.Red : BlipColor.White;
                p.Blip.IsFlashing = p.Bounty > 0;
            }
        }

        static void DeleteBlip(SessionPlayer p)
        {
            if (p.Blip != null && p.Blip.Exists()) p.Blip.Delete();
            p.Blip = null;
        }

        void DrawOverheads()
        {
            if (!cfg.NameTags) return;
            Vector3 me = Game.Player.Character.Position;
            foreach (SessionPlayer p in Players)
            {
                if (!p.Materialized || p.Dead || !p.Ped.Exists() || !p.VisibleToPlayer) continue;
                float d = p.Ped.Position.DistanceTo(me);
                if (d > 80f) continue;
                System.Drawing.PointF s = GTA.UI.Screen.WorldToScreen(p.Ped.Position + new Vector3(0f, 0f, 1.15f));
                if (s.IsEmpty) continue;
                float scale = Math.Max(0.22f, 0.36f - d * 0.002f);
                string label = string.Format("{0}  ~c~{1}{2}", p.Name, p.Rank, p.Bounty > 0 ? "  ~g~$" : "");
                Draw.Text(label, s.X, s.Y, scale, p.Hostile && !passive ? Red : White, GTA.UI.Alignment.Center);
            }
        }

        void DrawBountyHud(int now)
        {
            if (playerBounty <= 0) return;
            int left = Math.Max(0, (playerBountyUntil - now) / 1000);
            Draw.Rect(1020f, 160f, 250f, 30f, Color.FromArgb(160, 0, 0, 0));
            Draw.Text(string.Format("~r~BOUNTY~s~ ${0:N0}   {1}:{2:00}", playerBounty, left / 60, left % 60),
                1262f, 164f, 0.4f, White, GTA.UI.Alignment.Right);
        }

        void DrawPlayerList()
        {
            const float w = 380f, x = 1270f - w, rowH = 20f;
            float y = 60f;
            Draw.Rect(x, y, w, 30f, Color.FromArgb(230, 20, 60, 120));
            Draw.Text(string.Format("OFFLINE SESSION  {0}/30", Players.Count + 1), x + 8f, y + 4f, 0.42f, White, GTA.UI.Alignment.Left);
            y += 30f;
            Row(x, y, w, rowH, Progression.Rank.ToString(), Game.Player.Name, "", Gold, true);
            y += rowH;
            foreach (SessionPlayer p in Players.OrderBy(p => p.Name))
            {
                string status = p.Dead ? "~c~dead" : (p.Bounty > 0 ? string.Format("~g~${0:N0}", p.Bounty) : (p.Hostile && !passive ? "~r~hostile" : ""));
                Row(x, y, w, rowH, p.Rank.ToString(), p.Crew == null ? p.Name : p.Name + "  ~c~[" + p.Crew + "]", status, White, false);
                y += rowH;
            }
        }

        static void Row(float x, float y, float w, float h, string rank, string name, string status, Color color, bool me)
        {
            Draw.Rect(x, y, w, h, me ? Color.FromArgb(220, 60, 60, 20) : Color.FromArgb(170, 0, 0, 0));
            Draw.Rect(x + 4f, y + 3f, 34f, h - 6f, Color.FromArgb(255, 30, 90, 160));
            Draw.Text(rank, x + 21f, y + 1f, 0.3f, Color.White, GTA.UI.Alignment.Center);
            Draw.Text(name, x + 46f, y + 1f, 0.32f, color, GTA.UI.Alignment.Left);
            Draw.Text(status, x + w - 8f, y + 1f, 0.3f, Color.White, GTA.UI.Alignment.Right);
        }

        static float Flat(Vector3 a, Vector3 b)
        {
            float dx = a.X - b.X, dy = a.Y - b.Y;
            return (float)Math.Sqrt(dx * dx + dy * dy);
        }
    }
}
