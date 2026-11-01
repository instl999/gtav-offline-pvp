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
    internal enum CrewOrder { Follow, Hold, Attack }

    /// <summary>
    /// A member of your crew. The roster persists; while they are "with you" they are a real ped in your group
    /// that follows you, rides with you, fights for you and respawns near you when killed.
    /// </summary>
    internal sealed class CrewMember
    {
        public string Name;
        public int Rank;
        public string Model;
        public WeaponHash Weapon;
        public int Kills, Deaths;

        // Runtime only
        public Ped Ped;
        public Blip Blip;
        public Vehicle OwnCar;       // their own car when yours has no free seat
        public bool Escorting;       // driving OwnCar behind you (task issued once, not every think)
        public bool Dead;
        public int RespawnAt;
        public int NextThink;
        public CrewOrder Order;
        public Vector3 HoldAt;
        public Ped AttackTarget;
        public int OrderUntil;
        public bool VisibleToPlayer;

        public bool Spawned { get { return Ped != null && Ped.Exists(); } }

        public string Serialize()
        {
            return string.Join("|", new[] { Name, Rank.ToString(), Model, ((uint)Weapon).ToString(), Kills.ToString(), Deaths.ToString() });
        }

        public static CrewMember Parse(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            string[] f = s.Split('|');
            if (f.Length < 6 || f[0].Length == 0) return null;
            int rank, kills, deaths;
            uint weapon;
            if (!int.TryParse(f[1], out rank) || !uint.TryParse(f[3], out weapon)
                || !int.TryParse(f[4], out kills) || !int.TryParse(f[5], out deaths)) return null;
            return new CrewMember { Name = f[0], Rank = rank, Model = f[2], Weapon = (WeaponHash)weapon, Kills = kills, Deaths = deaths };
        }
    }

    internal sealed class CrewConfig
    {
        public const int MaxRoster = 8, MaxOut = 4;
        public static readonly string[] OrderNames = { "Follow me", "Hold position", "Attack my target" };

        public int PartnersOut = 2;     // how many crew members come with you
        public bool Active = false;     // partners called in
        public int Accuracy = 45;
        public bool CanDie = true;
        public int RespawnSeconds = 20;
        public bool Blips = true;
        public bool NameTags = true;
        public bool DuringMissions = false;
        public Keys MenuKey = Keys.None;
        public Keys FollowKey = Keys.None;
        public Keys AttackKey = Keys.None;

        public void Load(ScriptSettings s)
        {
            PartnersOut = DmConfig.Clamp(s.GetValue("Partners", "Count", PartnersOut), 1, MaxOut);
            Active = s.GetValue("Partners", "CallOnStart", Active);
            Accuracy = DmConfig.Clamp(s.GetValue("Partners", "Accuracy", Accuracy), 0, 100);
            CanDie = s.GetValue("Partners", "CanDie", CanDie);
            RespawnSeconds = DmConfig.Clamp(s.GetValue("Partners", "RespawnSeconds", RespawnSeconds), 5, 120);
            Blips = s.GetValue("Partners", "Blips", Blips);
            NameTags = s.GetValue("Partners", "NameTags", NameTags);
            DuringMissions = s.GetValue("Partners", "DuringStoryMissions", DuringMissions);
            MenuKey = Key(s, "CrewMenu", MenuKey);
            FollowKey = Key(s, "OrderFollow", FollowKey);
            AttackKey = Key(s, "OrderAttack", AttackKey);
        }

        public void Save(ScriptSettings s)
        {
            s.SetValue("Partners", "Count", PartnersOut);
            s.SetValue("Partners", "CallOnStart", Active);
            s.SetValue("Partners", "Accuracy", Accuracy);
            s.SetValue("Partners", "CanDie", CanDie);
            s.SetValue("Partners", "RespawnSeconds", RespawnSeconds);
            s.SetValue("Partners", "Blips", Blips);
            s.SetValue("Partners", "NameTags", NameTags);
            s.SetValue("Partners", "DuringStoryMissions", DuringMissions);
            s.SetValue("Keys", "CrewMenu", MenuKey.ToString());
            s.SetValue("Keys", "OrderFollow", FollowKey.ToString());
            s.SetValue("Keys", "OrderAttack", AttackKey.ToString());
        }

        static Keys Key(ScriptSettings s, string name, Keys fallback)
        {
            Keys k;
            return Enum.TryParse(s.GetValue("Keys", name, fallback.ToString()), true, out k) ? k : fallback;
        }
    }

    /// <summary>
    /// Your crew: AI partners who play alongside you in freemode like friends in an Online session, and fill your
    /// side in team jobs. Open with M > Crew &amp; partners.
    /// </summary>
    public sealed class CrewScript : Script
    {
        const float CatchUpDist = 250f;
        const int RecruitCost = 5000;

        static readonly WeaponHash[] Weapons = { WeaponHash.CarbineRifle, WeaponHash.SpecialCarbine, WeaponHash.AssaultRifle,
            WeaponHash.AssaultSMG, WeaponHash.CombatPDW, WeaponHash.PumpShotgun, WeaponHash.CombatMG, WeaponHash.HeavyShotgun };
        static readonly string[] CarModels = { "kuruma", "sultan", "baller", "dubsta", "schafter2", "buffalo2", "granger" };
        static readonly Color Blue = Color.FromArgb(255, 93, 182, 229);

        readonly Random rng = new Random();
        readonly CrewConfig cfg = new CrewConfig();
        readonly ScriptSettings file;
        readonly List<CrewMember> roster = new List<CrewMember>();
        readonly SimpleMenu menu = new SimpleMenu("Crew");
        readonly SimpleMenu rosterMenu = new SimpleMenu("Roster");
        readonly SimpleMenu settingsMenu = new SimpleMenu("Partners");
        RelationshipGroup crewGroup;
        bool suspended;
        int kickArmed = -1, kickArmedAt;

        public CrewScript()
        {
            file = ScriptSettings.Load(System.IO.Path.Combine(BaseDirectory, "OfflinePvP_Crew.ini"));
            cfg.Load(file);
            LoadRoster();
            BuildMenus();
            Tick += OnTick;
            KeyDown += OnKeyDown;
            Aborted += delegate { DismissAll(false); };
            Interval = 0;
        }

        IEnumerable<CrewMember> Out { get { return roster.Take(cfg.PartnersOut); } }

        // ------------------------------------------------------------------ roster persistence

        void LoadRoster()
        {
            int count = DmConfig.Clamp(file.GetValue("Crew", "Count", -1), -1, CrewConfig.MaxRoster);
            if (count < 0)
            {
                // First run: you start with a few friends, like a crew you already play with.
                for (int i = 0; i < 3; i++) roster.Add(NewMember(null, 0));
                SaveRoster();
            }
            for (int i = 0; i < count; i++)
            {
                CrewMember m = CrewMember.Parse(file.GetValue("Crew", "Member" + i, ""));
                if (m != null && roster.All(r => r.Name != m.Name)) roster.Add(m);
            }
            SyncNames();
        }

        void SaveRoster()
        {
            file.SetValue("Crew", "Count", roster.Count);
            for (int i = 0; i < CrewConfig.MaxRoster; i++)
                file.SetValue("Crew", "Member" + i, i < roster.Count ? roster[i].Serialize() : "");
            file.Save();
            SyncNames();
        }

        void SyncNames()
        {
            Session.CrewNames.Clear();
            Session.CrewNames.AddRange(roster.Select(m => m.Name));
        }

        CrewMember NewMember(string name, int rank)
        {
            List<string> taken = roster.Select(r => r.Name).Concat(Session.Players.Select(p => p.Name)).ToList();
            return new CrewMember
            {
                Name = name ?? BotNames.Make(rng, taken),
                Rank = rank > 0 ? rank : 20 + rng.Next(180),
                Model = Session.PlayerModels[rng.Next(Session.PlayerModels.Length)],
                Weapon = Weapons[rng.Next(Weapons.Length)],
            };
        }

        // ------------------------------------------------------------------ menus

        void BuildMenus()
        {
            Func<bool> always = null;
            Func<bool> freeRoam = delegate { return !Session.MatchActive; };
            Func<bool> anyOut = delegate { return cfg.Active && !suspended; };
            foreach (SimpleMenu m in new[] { menu, rosterMenu, settingsMenu })
            {
                m.Subtitle = "CREW & PARTNERS";
                m.DisabledHelp = "Not available right now.";
            }

            menu.AddAction("Partners with you", "Call your crew in, or send them home. They follow you, ride with you and fight for you.",
                delegate { SetActive(!cfg.Active); }, freeRoam)
                .Value = delegate { return cfg.Active ? "On" : "Off"; };
            menu.Add("How many", "LIVE. Crew members who come with you (top of your roster first).",
                delegate { return cfg.PartnersOut.ToString(); },
                delegate (int d) { cfg.PartnersOut = DmConfig.Clamp(cfg.PartnersOut + d, 1, CrewConfig.MaxOut); }, always);
            menu.AddAction("Order: follow me", "Partners regroup on you and follow.",
                delegate { GiveOrder(CrewOrder.Follow); }, anyOut);
            menu.AddAction("Order: hold position", "Partners stay and guard where they are standing.",
                delegate { GiveOrder(CrewOrder.Hold); }, anyOut);
            menu.AddAction("Order: attack my target", "Aim at someone first. Partners go after them for 30 seconds.",
                delegate { GiveOrder(CrewOrder.Attack); }, anyOut);
            menu.AddAction("Invite nearby player to crew", "Ask the closest friendly player in your session to join your crew.",
                InviteNearest, freeRoam);
            menu.AddAction("Recruit a new member", "Someone from your friends list joins your crew.", RecruitNew, always)
                .Value = delegate { return "$" + RecruitCost.ToString("N0"); };
            menu.AddSubmenu("Roster", "Your crew. Enter twice on a member to kick them.", rosterMenu);
            menu.AddSubmenu("Settings", "Accuracy, respawn, blips and name tags.", settingsMenu);

            settingsMenu.Add("Accuracy", "LIVE. Partner hit chance.",
                delegate { return cfg.Accuracy + "%"; },
                delegate (int d) { cfg.Accuracy = DmConfig.Clamp(cfg.Accuracy + d * 5, 0, 100); ApplyAll(); }, always);
            settingsMenu.Add("Partners can die", "LIVE. Off: they can't be killed.",
                delegate { return cfg.CanDie ? "On" : "Off"; },
                delegate (int d) { cfg.CanDie = !cfg.CanDie; ApplyAll(); }, always);
            settingsMenu.Add("Respawn time", "LIVE. Seconds before a killed partner comes back.",
                delegate { return cfg.RespawnSeconds + " s"; },
                delegate (int d) { cfg.RespawnSeconds = DmConfig.Clamp(cfg.RespawnSeconds + d * 5, 5, 120); }, always);
            settingsMenu.Add("Blips", "LIVE. Blue blips for your partners.",
                delegate { return cfg.Blips ? "On" : "Off"; },
                delegate (int d) { cfg.Blips = !cfg.Blips; }, always);
            settingsMenu.Add("Name tags", "LIVE. Names over your partners.",
                delegate { return cfg.NameTags ? "On" : "Off"; },
                delegate (int d) { cfg.NameTags = !cfg.NameTags; }, always);
            settingsMenu.Add("During story missions", "Keep partners while a story mission runs (may get in the way).",
                delegate { return cfg.DuringMissions ? "On" : "Off"; },
                delegate (int d) { cfg.DuringMissions = !cfg.DuringMissions; }, always);
            settingsMenu.AddAction("Save settings", "Write these settings to OfflinePvP_Crew.ini.",
                delegate { cfg.Save(file); file.Save(); GTA.UI.Notification.Show("~g~Crew settings saved."); }, always);

            RebuildRosterMenu();
        }

        void RebuildRosterMenu()
        {
            rosterMenu.Items.Clear();
            for (int i = 0; i < roster.Count; i++)
            {
                int idx = i;
                rosterMenu.AddAction(roster[i].Name, string.Format("Rank {0}. {1} kills, {2} deaths with you.", roster[i].Rank, roster[i].Kills, roster[i].Deaths),
                    delegate { Kick(idx); }, delegate { return !Session.MatchActive; })
                    .Value = delegate
                    {
                        if (kickArmed == idx && Game.GameTime - kickArmedAt < 4000) return "~r~Enter again to kick";
                        return (idx < cfg.PartnersOut ? "~b~with you~s~  " : "") + "Rank " + roster[idx].Rank;
                    };
            }
            if (roster.Count == 0) rosterMenu.AddAction("(empty)", "Invite players or recruit someone.", null, null);
        }

        SimpleMenu[] Menus { get { return new[] { menu, rosterMenu, settingsMenu }; } }

        void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (cfg.MenuKey != Keys.None && e.KeyCode == cfg.MenuKey)
            {
                bool open = Menus.Any(m => m.Visible);
                foreach (SimpleMenu m in Menus) m.Visible = false;
                if (!open) menu.Visible = true;
                return;
            }
            if (cfg.FollowKey != Keys.None && e.KeyCode == cfg.FollowKey) { GiveOrder(CrewOrder.Follow); return; }
            if (cfg.AttackKey != Keys.None && e.KeyCode == cfg.AttackKey) { GiveOrder(CrewOrder.Attack); return; }
            foreach (SimpleMenu m in Menus)
            {
                if (m.HandleKey(e.KeyCode)) return;
            }
        }

        // ------------------------------------------------------------------ crew management

        void InviteNearest()
        {
            if (roster.Count >= CrewConfig.MaxRoster) { GTA.UI.Notification.Show("~r~Your crew is full."); return; }
            Vector3 me = Game.Player.Character.Position;
            SessionPlayer best = Session.Players
                .Where(p => p.Materialized && !p.Dead && !p.Hostile && p.Ped.Exists() && p.Ped.Position.DistanceTo(me) < 40f
                    && roster.All(r => r.Name != p.Name))
                .OrderBy(p => p.Ped.Position.DistanceTo(me)).FirstOrDefault();
            if (best == null)
            {
                GTA.UI.Notification.Show("Nobody friendly from your session is close enough. Get within 40 m of a player.");
                return;
            }
            if (rng.Next(100) < 35)
            {
                GTA.UI.Notification.Show(GTA.UI.NotificationIcon.Multiplayer, best.Name, "Crew invite", "nah im good thx", false, false);
                return;
            }
            CrewMember m = NewMember(best.Name, best.Rank);
            roster.Add(m);
            SaveRoster();
            RebuildRosterMenu();
            GTA.UI.Notification.Show(GTA.UI.NotificationIcon.Multiplayer, best.Name, "Crew invite", "~g~Accepted.~s~ " + best.Name + " joined your crew.", false, false);
        }

        void RecruitNew()
        {
            if (roster.Count >= CrewConfig.MaxRoster) { GTA.UI.Notification.Show("~r~Your crew is full."); return; }
            if (Game.Player.Money < RecruitCost) { GTA.UI.Notification.Show("~r~You can't afford that."); return; }
            Progression.AddCash(-RecruitCost, "Crew recruitment");
            CrewMember m = NewMember(null, 0);
            roster.Add(m);
            SaveRoster();
            RebuildRosterMenu();
            GTA.UI.Notification.Show(string.Format("~b~{0}~s~ joined your crew.", m.Name));
        }

        void Kick(int idx)
        {
            if (idx >= roster.Count) return;
            if (kickArmed != idx || Game.GameTime - kickArmedAt > 4000)
            {
                kickArmed = idx;
                kickArmedAt = Game.GameTime;
                return;
            }
            kickArmed = -1;
            CrewMember m = roster[idx];
            Despawn(m, true);
            roster.RemoveAt(idx);
            SaveRoster();
            RebuildRosterMenu();
            GTA.UI.Notification.Show(string.Format("You kicked ~b~{0}~s~ from your crew.", m.Name));
        }

        void SetActive(bool on)
        {
            if (on && roster.Count == 0)
            {
                GTA.UI.Notification.Show("Your crew is empty. Invite or recruit someone first.");
                return;
            }
            cfg.Active = on;
            if (!on)
            {
                DismissAll(true);
                GTA.UI.Notification.Show("Your partners went home.");
            }
            else
            {
                GTA.UI.Notification.Show("Your crew is on the way.");
            }
        }

        void GiveOrder(CrewOrder order)
        {
            if (!cfg.Active || suspended) return;
            Ped target = null;
            if (order == CrewOrder.Attack)
            {
                Entity e = Game.Player.TargetedEntity;
                target = e as Ped;
                Vehicle v = e as Vehicle;
                if (target == null && v != null) target = v.Driver;
                if (target == null || !target.Exists() || target.IsDead)
                {
                    GTA.UI.Notification.Show("Aim at someone first, then give the order.");
                    return;
                }
            }
            int now = Game.GameTime;
            foreach (CrewMember m in Out)
            {
                m.Order = order;
                m.AttackTarget = target;
                m.OrderUntil = now + 30000;
                m.NextThink = now;
                if (m.Spawned) m.HoldAt = m.Ped.Position;
            }
            string line = order == CrewOrder.Follow ? "On you, boss." : (order == CrewOrder.Hold ? "Holding here." : "On it.");
            GTA.UI.Screen.ShowSubtitle("~b~Crew:~s~ " + line, 2000);
        }

        // ------------------------------------------------------------------ main loop

        void OnTick(object sender, EventArgs e)
        {
            if (Session.OpenCrewMenu)
            {
                Session.OpenCrewMenu = false;
                menu.Visible = true;
            }
            foreach (SimpleMenu m in Menus) m.Tick();

            bool shouldSuspend = Session.MatchActive || (!cfg.DuringMissions && Game.IsMissionActive);
            if (shouldSuspend != suspended)
            {
                suspended = shouldSuspend;
                if (suspended) DismissAll(false);
            }
            if (!cfg.Active || suspended)
            {
                if (!cfg.Active && roster.Any(m => m.Spawned)) DismissAll(false);
                return;
            }

            int now = Game.GameTime;
            Ped me = Game.Player.Character;
            if (crewGroup == null) SetupGroup();

            // Members past the "how many" count go home.
            foreach (CrewMember m in roster.Skip(cfg.PartnersOut)) if (m.Spawned || m.Blip != null) Despawn(m, true);

            if (me.IsDead) { DrawOverheads(); UpdateBlips(); return; }

            foreach (CrewMember m in Out.ToList())
            {
                if (m.Dead)
                {
                    if (now >= m.RespawnAt) { m.Dead = false; Spawn(m, me, true); }
                    continue;
                }
                if (!m.Spawned)
                {
                    if (now >= m.NextThink && !Spawn(m, me, false)) m.NextThink = now + 3000;
                    continue;
                }
                if (m.Ped.IsDead)
                {
                    OnPartnerDeath(m, now);
                    continue;
                }
                if (now >= m.NextThink)
                {
                    m.NextThink = now + 800 + rng.Next(400);
                    Think(m, me, now);
                }
            }
            UpdateBlips();
            DrawOverheads();
        }

        void SetupGroup()
        {
            crewGroup = World.AddRelationshipGroup("SPFM_CREW");
            RelationshipGroup mine = Game.Player.Character.RelationshipGroup;
            crewGroup.SetRelationshipBetweenGroups(mine, Relationship.Companion, true);
            crewGroup.SetRelationshipBetweenGroups(crewGroup, Relationship.Companion, true);
            // Griefers from the Online session hate you; your crew hates them right back.
            RelationshipGroup hostile = World.AddRelationshipGroup("SPFM_HOSTILE");
            crewGroup.SetRelationshipBetweenGroups(hostile, Relationship.Hate, true);
            RelationshipGroup sessionPlayers = World.AddRelationshipGroup("SPFM_PLAYERS");
            crewGroup.SetRelationshipBetweenGroups(sessionPlayers, Relationship.Neutral, true);
        }

        PedGroup PlayerGroup
        {
            get { return new PedGroup(Function.Call<int>(Hash.GET_PLAYER_GROUP, Game.Player)); }
        }

        // ------------------------------------------------------------------ spawning

        bool Spawn(CrewMember m, Ped me, bool respawn)
        {
            Model model = new Model(m.Model);
            if (!model.IsInCdImage || !model.IsPed) model = new Model(Session.PlayerModels[rng.Next(Session.PlayerModels.Length)]);
            if (!model.Request(1000)) return false;

            Ped ped = null;
            Vehicle mine = me.CurrentVehicle;
            if (mine != null && mine.Exists() && FreeSeat(mine) != VehicleSeat.None)
            {
                ped = mine.CreatePedOnSeat(FreeSeat(mine), model); // straight into your ride
            }
            else
            {
                // Out of sight behind you, so they run up like a friend who just arrived.
                Vector3 behind = me.Position - me.ForwardVector * (25f + rng.Next(15)) + me.RightVector * (rng.Next(-10, 10));
                Vector3 pos = World.GetNextPositionOnSidewalk(behind);
                if (pos == Vector3.Zero || pos.DistanceTo(me.Position) > 80f) pos = World.GetSafeCoordForPed(behind, true, 0);
                if (pos == Vector3.Zero || pos.DistanceTo(me.Position) > 80f) pos = me.Position + me.RightVector * 3f;
                ped = World.CreatePed(model, pos, me.Heading);
            }
            model.MarkAsNoLongerNeeded();
            if (ped == null) return false;

            m.Ped = ped;
            m.Order = CrewOrder.Follow;
            m.NextThink = Game.GameTime + 300;
            Function.Call(Hash.SET_PED_RANDOM_COMPONENT_VARIATION, ped, 0);
            ped.IsPersistent = true;
            ped.MaxHealth = 300;
            ped.Health = 300;
            ped.Armor = 100;
            ped.DropsEquippedWeaponOnDeath = false;
            ped.Weapons.Give(WeaponHash.Pistol50, 500, false, true);
            ped.Weapons.Give(m.Weapon, 999, true, true);
            Function.Call(Hash.SET_PED_COMBAT_ABILITY, ped, 2);
            Function.Call(Hash.SET_PED_COMBAT_MOVEMENT, ped, 2);
            Function.Call(Hash.SET_PED_COMBAT_RANGE, ped, 1);
            Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, ped, 0, true);   // use cover
            Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, ped, 2, true);   // drive-bys
            Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, ped, 46, true);  // always fight
            Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, ped, 58, true);  // never flee
            Function.Call(Hash.SET_PED_FLEE_ATTRIBUTES, ped, 0, false);
            Function.Call(Hash.SET_PED_DIES_WHEN_INJURED, ped, true);
            Function.Call(Hash.SET_PED_CAN_BE_TARGETTED_BY_PLAYER, ped, Game.Player, false);
            Function.Call(Hash.SET_PED_CONFIG_FLAG, ped, 32, false);       // can fly through windscreen: no
            ped.CanBeKnockedOffBike = false;
            Apply(m);
            JoinGroup(m);
            if (respawn) GTA.UI.Notification.Show(string.Format("~b~{0}~s~ is back.", m.Name));
            return true;
        }

        void Apply(CrewMember m)
        {
            if (!m.Spawned) return;
            m.Ped.RelationshipGroup = crewGroup;
            m.Ped.Accuracy = cfg.Accuracy;
            m.Ped.IsInvincible = !cfg.CanDie;
            // Your stray bullets don't hurt your crew.
            Function.Call(Hash.SET_ENTITY_CAN_BE_DAMAGED_BY_RELATIONSHIP_GROUP, m.Ped, false, Game.Player.Character.RelationshipGroup.Hash);
        }

        void ApplyAll()
        {
            if (crewGroup == null) return;
            foreach (CrewMember m in roster) Apply(m);
        }

        void JoinGroup(CrewMember m)
        {
            if (!m.Spawned) return;
            PedGroup g = PlayerGroup;
            if (!g.Contains(m.Ped))
            {
                g.Add(m.Ped, false);
                g.SeparationRange = 9999f;
                g.Formation = Formation.Loose;
            }
            m.Ped.NeverLeavesGroup = true;
        }

        void LeaveGroup(CrewMember m)
        {
            if (!m.Spawned) return;
            m.Ped.NeverLeavesGroup = false;
            if (m.Ped.IsInGroup) m.Ped.LeaveGroup();
        }

        static VehicleSeat FreeSeat(Vehicle v)
        {
            int passengers = v.PassengerCapacity;
            for (int s = 0; s < passengers; s++)
            {
                if (v.IsSeatFree((VehicleSeat)s)) return (VehicleSeat)s;
            }
            return VehicleSeat.None;
        }

        void Despawn(CrewMember m, bool resetDead)
        {
            if (m.Blip != null && m.Blip.Exists()) m.Blip.Delete();
            m.Blip = null;
            if (m.Spawned)
            {
                LeaveGroup(m);
                m.Ped.Delete();
            }
            m.Ped = null;
            ReleaseCar(m);
            if (resetDead) m.Dead = false;
        }

        void ReleaseCar(CrewMember m)
        {
            if (m.OwnCar != null && m.OwnCar.Exists())
            {
                m.OwnCar.IsPersistent = false;
                m.OwnCar.MarkAsNoLongerNeeded();
            }
            m.OwnCar = null;
            m.Escorting = false;
        }

        void DismissAll(bool resetDead)
        {
            foreach (CrewMember m in roster) Despawn(m, resetDead);
        }

        // ------------------------------------------------------------------ behaviour

        void Think(CrewMember m, Ped me, int now)
        {
            Ped ped = m.Ped;
            float dist = ped.Position.DistanceTo(me.Position);
            m.VisibleToPlayer = dist < 60f && Function.Call<bool>(Hash.HAS_ENTITY_CLEAR_LOS_TO_ENTITY, me, ped, 17);

            // Left far behind (fast travel, flying, character switch): catch up like a friend teleporting to you.
            if (dist > CatchUpDist && m.Order != CrewOrder.Hold)
            {
                Despawn(m, false);
                Spawn(m, me, false);
                return;
            }

            if (m.Order == CrewOrder.Attack)
            {
                bool done = m.AttackTarget == null || !m.AttackTarget.Exists() || m.AttackTarget.IsDead || now > m.OrderUntil;
                if (done)
                {
                    m.Order = CrewOrder.Follow;
                }
                else
                {
                    LeaveGroup(m);
                    if (!ped.IsInCombatAgainst(m.AttackTarget)) ped.Task.FightAgainst(m.AttackTarget);
                    return;
                }
            }
            if (m.Order == CrewOrder.Hold)
            {
                LeaveGroup(m);
                if (!ped.IsInCombat && ped.Position.DistanceTo(m.HoldAt) > 4f) ped.Task.RunTo(m.HoldAt, false, -1);
                else if (!ped.IsInCombat && ped.IsStopped) ped.Task.GuardCurrentPosition();
                return;
            }

            // Follow. Your car has no seat left: they bring their own and tail you.
            Vehicle mine = me.CurrentVehicle;
            bool youDrive = mine != null && mine.Exists();
            if (youDrive && !ped.IsInVehicle() && FreeSeat(mine) == VehicleSeat.None && dist > 15f)
            {
                if (m.OwnCar == null || !m.OwnCar.Exists() || m.OwnCar.IsDead)
                {
                    ReleaseCar(m);
                    m.OwnCar = SpawnCarFor(ped);
                    if (m.OwnCar != null)
                    {
                        LeaveGroup(m);
                        ped.SetIntoVehicle(m.OwnCar, VehicleSeat.Driver);
                    }
                }
            }
            if (ped.IsInVehicle() && m.OwnCar != null && ped.CurrentVehicle == m.OwnCar)
            {
                if (youDrive)
                {
                    if (!m.Escorting)
                    {
                        ped.Task.StartVehicleMission(m.OwnCar, me, VehicleMissionType.Escort, 60f,
                            VehicleDrivingFlags.DrivingModeAvoidVehicles, 10f, 20f, true);
                        m.Escorting = true;
                    }
                    return;
                }
                // You got out: park up and join you on foot.
                ped.Task.LeaveVehicle(m.OwnCar, false);
                ReleaseCar(m);
            }
            JoinGroup(m);
        }

        Vehicle SpawnCarFor(Ped ped)
        {
            Vector3 street = World.GetNextPositionOnStreet(ped.Position, true);
            if (street == Vector3.Zero || street.DistanceTo(ped.Position) > 60f) return null;
            Model model = Session.LoadModel(rng, CarModels, x => x.IsVehicle);
            if (model == null) return null;
            Vehicle car = World.CreateVehicle(model, street, ped.Heading);
            model.MarkAsNoLongerNeeded();
            if (car == null) return null;
            car.PlaceOnGround();
            car.IsPersistent = true;
            return car;
        }

        void OnPartnerDeath(CrewMember m, int now)
        {
            m.Deaths++;
            m.Dead = true;
            m.RespawnAt = now + cfg.RespawnSeconds * 1000;
            Entity k = m.Ped.Killer;
            Ped killer = k as Ped;
            if (killer == null && k is Vehicle) killer = ((Vehicle)k).Driver;
            SessionPlayer by = killer == null ? null : Session.Players.FirstOrDefault(p => p.Ped != null && p.Ped.Handle == killer.Handle);
            GTA.UI.Notification.Show(by != null
                ? string.Format("~r~{0}~s~ killed your crew mate ~b~{1}~s~.", by.Name, m.Name)
                : string.Format("Your crew mate ~b~{0}~s~ died.", m.Name));
            LeaveGroup(m);
            m.Ped.IsPersistent = false;
            m.Ped.MarkAsNoLongerNeeded();
            m.Ped = null;
            ReleaseCar(m);
            if (m.Blip != null && m.Blip.Exists()) m.Blip.Delete();
            m.Blip = null;
            SaveRoster();
        }

        // ------------------------------------------------------------------ HUD

        void UpdateBlips()
        {
            foreach (CrewMember m in roster)
            {
                bool want = cfg.Blips && m.Spawned && !m.Ped.IsDead;
                if (!want)
                {
                    if (m.Blip != null && m.Blip.Exists()) m.Blip.Delete();
                    m.Blip = null;
                    continue;
                }
                if (m.Blip == null || !m.Blip.Exists())
                {
                    m.Blip = m.Ped.AddBlip();
                    m.Blip.Color = BlipColor.Blue;
                    m.Blip.Scale = 0.75f;
                    m.Blip.Name = m.Name;
                    m.Blip.IsFriendly = true;
                }
            }
        }

        void DrawOverheads()
        {
            if (!cfg.NameTags) return;
            Vector3 me = Game.Player.Character.Position;
            foreach (CrewMember m in roster)
            {
                if (!m.Spawned || m.Ped.IsDead || !m.VisibleToPlayer) continue;
                float d = m.Ped.Position.DistanceTo(me);
                if (d > 60f) continue;
                System.Drawing.PointF s = GTA.UI.Screen.WorldToScreen(m.Ped.Position + new Vector3(0f, 0f, 1.15f));
                if (s.IsEmpty) continue;
                float scale = Math.Max(0.22f, 0.36f - d * 0.002f);
                Draw.Text(string.Format("{0}  ~c~{1}", m.Name, m.Rank), s.X, s.Y, scale, Blue, GTA.UI.Alignment.Center);
            }
        }
    }
}
