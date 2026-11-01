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
    internal sealed class RaceClass
    {
        public readonly string Name;
        public readonly string[] Models;
        public readonly bool Bikes, OffRoad;

        public RaceClass(string name, bool bikes, bool offRoad, params string[] models)
        {
            Name = name;
            Bikes = bikes;
            OffRoad = offRoad;
            Models = models;
        }
    }

    internal sealed class RaceConfig
    {
        public static readonly RaceClass[] Classes =
        {
            new RaceClass("Super", false, false, "adder", "zentorno", "t20", "osiris", "turismor", "entityxf", "cheetah", "infernus", "vacca", "nero"),
            new RaceClass("Sports", false, false, "elegy2", "sultan", "jester", "massacro", "carbonizzare", "banshee", "comet2", "feltzer2", "kuruma", "ninef"),
            new RaceClass("Muscle", false, false, "dominator", "gauntlet", "sabregt", "vigero", "buccaneer", "phoenix", "dukes", "blade"),
            new RaceClass("Motorcycles", true, false, "bati", "akuma", "hakuchou", "double", "carbonrs", "ruffian", "vader", "pcj"),
            new RaceClass("Off-road", false, true, "sandking", "bifta", "dubsta3", "brawler", "trophytruck", "mesa3", "rebel2", "bfinjection"),
            new RaceClass("Compacts", false, false, "blista", "issi2", "panto", "prairie", "rhapsody", "brioso"),
            new RaceClass("SUVs", false, false, "baller", "granger", "cavalcade2", "huntley", "dubsta", "xls", "rocoto"),
        };

        public static readonly ArenaPreset[] Locations =
        {
            new ArenaPreset("Current location", 0f, 0f, 0f),
            new ArenaPreset("Downtown Los Santos", 195f, -935f, 30f),
            new ArenaPreset("Vinewood", 300f, 180f, 104f),
            new ArenaPreset("Del Perro", -1350f, -600f, 28f),
            new ArenaPreset("La Mesa", 800f, -1000f, 26f),
            new ArenaPreset("LS Port", 1000f, -3000f, 6f),
            new ArenaPreset("Sandy Shores", 1850f, 3700f, 33f),
            new ArenaPreset("Grapeseed", 1700f, 4800f, 42f),
            new ArenaPreset("Paleto Bay", -200f, 6300f, 31f),
            new ArenaPreset("Great Ocean Highway", -3000f, 400f, 14f),
        };

        public static readonly string[] TypeNames = { "Laps", "Point to point" };
        public static readonly string[] LengthNames = { "Short", "Medium", "Long" };
        public static readonly string[] SkillNames = { "Easy", "Normal", "Hard" };

        public int Type = 0;
        public int Laps = 3;
        public int Length = 1;
        public int Class = 1;
        public int Opponents = 7;
        public int Skill = 1;
        public bool CatchUp = true;
        public bool Traffic = false;
        public int Location = 0;
        public int TimeOfDay = 0;
        public int Weather = 0;
        public bool ReturnAfter = true;

        public bool PointToPoint { get { return Type == 1; } }

        public void Load(ScriptSettings s)
        {
            Type = DmConfig.Clamp(s.GetValue("Race", "Type", Type), 0, TypeNames.Length - 1);
            Laps = DmConfig.Clamp(s.GetValue("Race", "Laps", Laps), 1, 5);
            Length = DmConfig.Clamp(s.GetValue("Race", "Length", Length), 0, LengthNames.Length - 1);
            Class = DmConfig.Clamp(s.GetValue("Race", "VehicleClass", Class), 0, Classes.Length - 1);
            Opponents = DmConfig.Clamp(s.GetValue("Race", "Opponents", Opponents), 1, 15);
            Skill = DmConfig.Clamp(s.GetValue("Race", "AISkill", Skill), 0, SkillNames.Length - 1);
            CatchUp = s.GetValue("Race", "CatchUp", CatchUp);
            Traffic = s.GetValue("Race", "Traffic", Traffic);
            Location = DmConfig.Clamp(s.GetValue("Race", "Location", Location), 0, Locations.Length - 1);
            TimeOfDay = DmConfig.Clamp(s.GetValue("Race", "TimeOfDay", TimeOfDay), 0, DmConfig.TimeNames.Length - 1);
            Weather = DmConfig.Clamp(s.GetValue("Race", "Weather", Weather), 0, DmConfig.WeatherNames.Length - 1);
            ReturnAfter = s.GetValue("Race", "ReturnAfterRace", ReturnAfter);
        }

        public void Save(ScriptSettings s)
        {
            s.SetValue("Race", "Type", Type);
            s.SetValue("Race", "Laps", Laps);
            s.SetValue("Race", "Length", Length);
            s.SetValue("Race", "VehicleClass", Class);
            s.SetValue("Race", "Opponents", Opponents);
            s.SetValue("Race", "AISkill", Skill);
            s.SetValue("Race", "CatchUp", CatchUp);
            s.SetValue("Race", "Traffic", Traffic);
            s.SetValue("Race", "Location", Location);
            s.SetValue("Race", "TimeOfDay", TimeOfDay);
            s.SetValue("Race", "Weather", Weather);
            s.SetValue("Race", "ReturnAfterRace", ReturnAfter);
            s.Save();
        }
    }

    /// <summary>
    /// Online-style land races against AI drivers on generated routes: grid start, 3-2-1-GO, checkpoints, laps,
    /// catch-up, respawn at the last checkpoint, 30 s to finish after the winner, results and payout.
    /// </summary>
    public sealed class RaceScript : Script
    {
        enum State { Idle, Countdown, Racing, Results }

        sealed class Racer
        {
            public string Name;
            public bool IsPlayer;
            public Ped Ped;
            public Vehicle Car;
            public Blip Blip;
            public int Next;           // index of the next checkpoint in the whole race (all laps)
            public Vector3 LastCp;     // respawn point
            public float LastHeading;
            public bool Finished, Dnf;
            public int FinishTime, Place;
            public int LapStart, BestLap;
            public int SlowSince, RespawnAt;
            public float Dist;         // to the next checkpoint, for standings
            public int ShownPlace;     // number on the blip
        }

        const int CountdownMs = 3000, FinishWindowMs = 30000, ResultsMs = 15000;
        const float PlayerCpRadius = 14f, AiCpRadius = 25f;
        const int RacingStyle = 4 | 8 | 16 | 32 | 512 | 262144 | 524288; // swerve, ignore lights, oncoming, shortcuts
        static readonly Color Yellow = Color.FromArgb(255, 240, 200, 80);
        static readonly Color Gold = Color.FromArgb(255, 240, 200, 80);
        static readonly Color Blue = Color.FromArgb(255, 93, 182, 229);

        readonly Random rng = new Random();
        readonly RaceConfig cfg = new RaceConfig();
        readonly ScriptSettings settingsFile;
        readonly SimpleMenu menu = new SimpleMenu("Races");
        readonly BigMessage shard = new BigMessage();
        readonly List<Racer> racers = new List<Racer>();
        readonly List<Vector3> route = new List<Vector3>();

        State state = State.Idle;
        bool startRequested, endRequested;
        Racer player;
        int goAt, raceStart, finishDeadline, endedAt, finishCount, respawnHeldSince, playerDeadAt;
        Checkpoint cpNow, cpNext;
        Blip blipNow, blipNext;
        int shownCp = -1;
        Vector3 origin;
        Vehicle originalCar;
        TimeSpan savedTime;
        Weather savedWeather;
        bool savedClockPaused, envChanged;

        public RaceScript()
        {
            // Own file: every script in the DLL shares Settings (OfflinePvP.ini), and two writers would clobber each other.
            settingsFile = ScriptSettings.Load(System.IO.Path.Combine(BaseDirectory, "OfflinePvP_Race.ini"));
            cfg.Load(settingsFile);
            Progression.Init(System.IO.Path.Combine(BaseDirectory, "OfflinePvP_Profile.ini"));
            BuildMenu();
            Tick += OnTick;
            KeyDown += OnKeyDown;
            Aborted += delegate { if (state != State.Idle) Cleanup(true); };
            Interval = 0;
        }

        int Total { get { return cfg.PointToPoint ? route.Count - 1 : route.Count * cfg.Laps; } }

        Vector3 CpPos(int k)
        {
            return cfg.PointToPoint ? route[k + 1] : route[(k + 1) % route.Count];
        }

        bool IsLapEnd(int k)
        {
            return !cfg.PointToPoint && (k + 1) % route.Count == 0;
        }

        // ------------------------------------------------------------------ menu

        void BuildMenu()
        {
            Func<bool> idle = delegate { return state == State.Idle; };
            Func<bool> always = null;
            menu.Subtitle = "RACES";

            menu.AddAction("Start / end race", "Start a race with these settings, or end the current one.",
                delegate { if (state == State.Idle) startRequested = true; else endRequested = true; menu.Visible = false; }, always)
                .Value = delegate { return state == State.Idle ? "Start" : "End"; };
            menu.Add("Type", "Laps around a circuit, or point to point.",
                delegate { return RaceConfig.TypeNames[cfg.Type]; },
                delegate (int d) { cfg.Type = Wrap(cfg.Type + d, RaceConfig.TypeNames.Length); }, idle);
            menu.Add("Laps", "Lap races only.",
                delegate { return cfg.Laps.ToString(); },
                delegate (int d) { cfg.Laps = DmConfig.Clamp(cfg.Laps + d, 1, 5); }, delegate { return idle() && !cfg.PointToPoint; });
            menu.Add("Length", "How big the circuit is, or how far the sprint goes.",
                delegate { return RaceConfig.LengthNames[cfg.Length]; },
                delegate (int d) { cfg.Length = Wrap(cfg.Length + d, RaceConfig.LengthNames.Length); }, idle);
            menu.Add("Vehicle class", "Everyone races a random car from this class.",
                delegate { return RaceConfig.Classes[cfg.Class].Name; },
                delegate (int d) { cfg.Class = Wrap(cfg.Class + d, RaceConfig.Classes.Length); }, idle);
            menu.Add("Location", "Where the race starts. Pick somewhere with plenty of roads.",
                delegate { return RaceConfig.Locations[cfg.Location].Name; },
                delegate (int d) { cfg.Location = Wrap(cfg.Location + d, RaceConfig.Locations.Length); }, idle);
            menu.Add("Opponents", "AI racers. Players from your crew and session fill the grid.",
                delegate { return cfg.Opponents.ToString(); },
                delegate (int d) { cfg.Opponents = DmConfig.Clamp(cfg.Opponents + d, 1, 15); }, idle);
            menu.Add("AI skill", "Driving ability, aggression and how hard they push.",
                delegate { return RaceConfig.SkillNames[cfg.Skill]; },
                delegate (int d) { cfg.Skill = DmConfig.Clamp(cfg.Skill + d, 0, RaceConfig.SkillNames.Length - 1); }, idle);
            menu.Add("Catch-up", "LIVE. Racers behind get more power, the leaders a little less.",
                delegate { return cfg.CatchUp ? "On" : "Off"; },
                delegate (int d) { cfg.CatchUp = !cfg.CatchUp; }, always);
            menu.Add("Traffic", "LIVE. Ambient traffic on the roads.",
                delegate { return cfg.Traffic ? "On" : "Off"; },
                delegate (int d) { cfg.Traffic = !cfg.Traffic; }, always);
            menu.Add("Time of day", "Restored after the race.",
                delegate { return DmConfig.TimeNames[cfg.TimeOfDay]; },
                delegate (int d) { cfg.TimeOfDay = Wrap(cfg.TimeOfDay + d, DmConfig.TimeNames.Length); }, idle);
            menu.Add("Weather", "Restored after the race.",
                delegate { return DmConfig.WeatherNames[cfg.Weather]; },
                delegate (int d) { cfg.Weather = Wrap(cfg.Weather + d, DmConfig.WeatherNames.Length); }, idle);
            menu.Add("Return after race", "Put you back where you started, in your own car.",
                delegate { return cfg.ReturnAfter ? "On" : "Off"; },
                delegate (int d) { cfg.ReturnAfter = !cfg.ReturnAfter; }, always);
            menu.AddAction("Save settings", "Write these settings to OfflinePvP_Race.ini.",
                delegate { cfg.Save(settingsFile); GTA.UI.Notification.Show("~g~Race settings saved."); }, always);
        }

        static int Wrap(int v, int n)
        {
            return ((v % n) + n) % n;
        }

        void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (menu.HandleKey(e.KeyCode)) return;
            if (e.KeyCode == Keys.Enter)
            {
                if (state == State.Results) endedAt = 0;                       // close results
                else if (state == State.Racing && player != null && player.Finished) finishDeadline = 0; // skip the wait
            }
        }

        // ------------------------------------------------------------------ main loop

        void OnTick(object sender, EventArgs e)
        {
            if (Session.OpenRaceMenu)
            {
                Session.OpenRaceMenu = false;
                menu.Visible = true;
            }
            if (Session.RequestedRace)
            {
                Session.RequestedRace = false;
                if (state == State.Idle && !Session.MatchActive) startRequested = true;
            }
            menu.Subtitle = state == State.Idle ? "RACES" : "RACE IN PROGRESS";
            menu.Tick();
            shard.Tick();

            if (startRequested)
            {
                startRequested = false;
                StartRace();
            }
            if (endRequested)
            {
                endRequested = false;
                if (state == State.Countdown || state == State.Racing) EndRace(true);
            }
            if (state == State.Idle) return;

            int now = Game.GameTime;
            DeathControl.KeepSuppressed();
            Game.Player.WantedLevel = 0;
            Game.DisableControlThisFrame(Control.CharacterWheel);
            if (!cfg.Traffic)
            {
                Function.Call(Hash.SET_VEHICLE_DENSITY_MULTIPLIER_THIS_FRAME, 0f);
                Function.Call(Hash.SET_RANDOM_VEHICLE_DENSITY_MULTIPLIER_THIS_FRAME, 0f);
                Function.Call(Hash.SET_PARKED_VEHICLE_DENSITY_MULTIPLIER_THIS_FRAME, 0f);
            }

            if (state == State.Countdown)
            {
                UpdateStandings();
                DrawCountdown(now);
                if (now >= goAt) Go(now);
            }
            else if (state == State.Racing)
            {
                UpdatePlayer(now);
                UpdateAi(now);
                UpdateStandings();
                UpdateCheckpointVisuals();
                if (finishCount > 0 && now >= finishDeadline) EndRace(false);
                else if (racers.All(r => r.Finished)) EndRace(false);
            }
            else if (state == State.Results && now - endedAt > ResultsMs)
            {
                Cleanup(false);
                return;
            }

            if (state == State.Racing || state == State.Countdown) DrawNames();
            DrawHud(now);
            if (state == State.Results || (state == State.Racing && Game.IsKeyPressed(Keys.Z))) DrawResults();
        }

        // ------------------------------------------------------------------ start

        void StartRace()
        {
            Ped me = Game.Player.Character;
            if (me.IsDead) return;
            if (Session.MatchActive)
            {
                GTA.UI.Notification.Show("~r~Finish your current job first.");
                return;
            }
            Session.MatchActive = true;
            GTA.UI.Screen.FadeOut(500);
            Wait(600);

            origin = me.Position;
            originalCar = me.CurrentVehicle;
            if (originalCar != null && originalCar.Exists()) originalCar.IsPersistent = true;
            Vector3 start = cfg.Location == 0 ? me.Position : RaceConfig.Locations[cfg.Location].Center;
            if (me.IsInVehicle()) me.Task.ClearAllImmediately();
            me.IsPositionFrozen = true;
            me.Position = start + new Vector3(0f, 0f, 2f);
            StreamArea(start, 3000);

            bool ok = BuildRoute(start);
            if (!ok)
            {
                me.IsPositionFrozen = false;
                GTA.UI.Screen.FadeIn(500);
                Session.MatchActive = false;
                GTA.UI.Notification.Show("~r~Couldn't build a race route here.~s~ Try a spot with more roads, or another length.");
                return;
            }

            SaveEnvironment();
            ApplyEnvironment();
            DeathControl.Acquire(DeathOwner);
            Function.Call(Hash.SET_MAX_WANTED_LEVEL, 0);
            Game.Player.IgnoredByPolice = true;
            Function.Call(Hash.CLEAR_AREA_OF_VEHICLES, route[0].X, route[0].Y, route[0].Z, 80f, false, false, false, false, false, false, 0);

            racers.Clear();
            finishCount = 0;
            shownCp = -1;
            int slots = cfg.Opponents + 1;
            int playerSlot = rng.Next(slots);
            List<string> taken = new List<string>();
            for (int i = 0; i < slots; i++)
            {
                Racer r;
                if (i == playerSlot)
                {
                    r = new Racer { Name = Game.Player.Name, IsPlayer = true };
                    player = r;
                }
                else
                {
                    string name = Session.TakeAllyName(taken, rng) ?? BotNames.Make(rng, taken);
                    taken.Add(name);
                    r = new Racer { Name = name };
                }
                racers.Add(r);
                PlaceOnGrid(r, i);
            }
            me.IsPositionFrozen = false;
            if (player.Car == null)
            {
                Cleanup(true);
                GTA.UI.Screen.FadeIn(500);
                GTA.UI.Notification.Show("~r~Couldn't spawn your race car.");
                return;
            }

            state = State.Countdown;
            goAt = Game.GameTime + CountdownMs + 1500;
            GTA.UI.Screen.FadeIn(800);
            string title = cfg.PointToPoint ? "POINT TO POINT" : string.Format("{0} LAP{1}", cfg.Laps, cfg.Laps == 1 ? "" : "S");
            shard.Show("SHOW_SHARD_CENTERED_MP_MESSAGE", "RACE", string.Format("{0}  -  {1}  -  {2} racers", title,
                RaceConfig.Classes[cfg.Class].Name, racers.Count), 2500);
        }

        /// <summary>Waits while the world streams in around a point (collision, path nodes).</summary>
        void StreamArea(Vector3 p, int ms)
        {
            int deadline = Game.GameTime + ms;
            while (Game.GameTime < deadline)
            {
                Function.Call(Hash.REQUEST_PATH_NODES_IN_AREA_THIS_FRAME, p.X - 800f, p.Y - 800f, p.X + 800f, p.Y + 800f);
                Wait(0);
            }
        }

        // ------------------------------------------------------------------ route generation

        Vector3 Node(Vector3 near, out float heading)
        {
            OutputArgument pos = new OutputArgument(), hdg = new OutputArgument();
            int type = RaceConfig.Classes[cfg.Class].OffRoad ? 1 : 0;
            bool ok = Function.Call<bool>(Hash.GET_CLOSEST_VEHICLE_NODE_WITH_HEADING, near.X, near.Y, near.Z, pos, hdg, type, 3f, 0f);
            heading = hdg.GetResult<float>();
            return ok ? pos.GetResult<Vector3>() : Vector3.Zero;
        }

        void EnsureNodes(Vector3 a, Vector3 b)
        {
            float x1 = Math.Min(a.X, b.X) - 200f, y1 = Math.Min(a.Y, b.Y) - 200f;
            float x2 = Math.Max(a.X, b.X) + 200f, y2 = Math.Max(a.Y, b.Y) + 200f;
            int deadline = Game.GameTime + 1500;
            while (Game.GameTime < deadline)
            {
                Function.Call(Hash.REQUEST_PATH_NODES_IN_AREA_THIS_FRAME, x1, y1, x2, y2);
                if (Function.Call<bool>(Hash.ARE_NODES_LOADED_FOR_AREA, x1, y1, x2, y2)) return;
                Wait(0);
            }
        }

        bool BuildRoute(Vector3 near)
        {
            float h;
            route.Clear();
            Vector3 start = Node(near, out h);
            if (start == Vector3.Zero) return false;
            for (int attempt = 0; attempt < 3; attempt++)
            {
                route.Clear();
                route.Add(start);
                float scale = 1f - attempt * 0.3f;
                if (cfg.PointToPoint) BuildSprint(start, h, scale);
                else BuildCircuit(start, scale);
                if (route.Count >= 4) return true;
            }
            return false;
        }

        void BuildCircuit(Vector3 start, float scale)
        {
            float[] radius = { 250f, 400f, 650f };
            int[] points = { 8, 10, 12 };
            float r = radius[cfg.Length] * scale;
            int n = points[cfg.Length];
            double a0 = rng.NextDouble() * Math.PI * 2;
            int dir = rng.Next(2) == 0 ? 1 : -1;
            // The start sits on the circle, so the circuit begins and ends where you are.
            Vector3 c = start - new Vector3((float)Math.Cos(a0) * r, (float)Math.Sin(a0) * r, 0f);
            EnsureNodes(c - new Vector3(r, r, 0f), c + new Vector3(r, r, 0f));
            for (int i = 1; i < n; i++)
            {
                double a = a0 + dir * i * Math.PI * 2 / n;
                Vector3 want = c + new Vector3((float)Math.Cos(a) * r, (float)Math.Sin(a) * r, start.Z);
                float h;
                Vector3 p = Node(want, out h);
                if (p == Vector3.Zero || Flat(p, want) > 120f) continue;
                if (Flat(p, route[route.Count - 1]) < 60f) continue;
                if (i == n - 1 && Flat(p, start) < 60f) continue;
                route.Add(p);
            }
        }

        void BuildSprint(Vector3 start, float roadHeading, float scale)
        {
            int[] steps = { 6, 10, 15 };
            const float step = 180f;
            double h = (roadHeading + 90f) * Math.PI / 180.0; // GTA heading 0 = north (+Y)
            if (rng.Next(2) == 0) h += Math.PI;
            int wanted = (int)(steps[cfg.Length] * scale) + 1;
            for (int i = 1; i < wanted; i++)
            {
                Vector3 prev = route[route.Count - 1];
                bool added = false;
                for (int attempt = 0; attempt < 8 && !added; attempt++)
                {
                    double a = h + (rng.NextDouble() - 0.5) * (Math.PI * 70 / 180) * (1 + attempt * 0.4);
                    Vector3 want = prev + new Vector3((float)Math.Cos(a) * step, (float)Math.Sin(a) * step, 0f);
                    if (want.X < -3600f || want.X > 4200f || want.Y < -3700f || want.Y > 7800f) continue;
                    EnsureNodes(prev, want);
                    float rh;
                    Vector3 p = Node(want, out rh);
                    if (p == Vector3.Zero || Flat(p, prev) < 90f) continue;
                    if (Flat(p, start) < Flat(prev, start) + 40f) continue; // keep moving away from the start
                    route.Add(p);
                    h = Math.Atan2(p.Y - prev.Y, p.X - prev.X);
                    added = true;
                }
                if (!added) break;
            }
        }

        // ------------------------------------------------------------------ grid & vehicles

        void PlaceOnGrid(Racer r, int slot)
        {
            Vector3 start = route[0];
            Vector3 fwd = CpPos(0) - start;
            fwd.Z = 0f;
            fwd.Normalize();
            Vector3 right = new Vector3(fwd.Y, -fwd.X, 0f);
            int row = slot / 2, col = slot % 2;
            Vector3 pos = start - fwd * (8f + row * 8f) + right * (col == 0 ? -2.6f : 2.6f);
            float heading = (float)(Math.Atan2(fwd.Y, fwd.X) * 180.0 / Math.PI) - 90f;
            r.LastCp = pos;
            r.LastHeading = heading;
            SpawnVehicle(r, pos, heading);
        }

        void SpawnVehicle(Racer r, Vector3 pos, float heading)
        {
            RaceClass cls = RaceConfig.Classes[cfg.Class];
            Model model = Session.LoadModel(rng, cls.Models, m => m.IsVehicle);
            if (model == null) return;
            float ground = World.GetGroundHeight(new Vector2(pos.X, pos.Y));
            if (ground != 0f && Math.Abs(ground - pos.Z) < 8f) pos.Z = ground;
            Vehicle car = World.CreateVehicle(model, pos + new Vector3(0f, 0f, 0.5f), heading);
            model.MarkAsNoLongerNeeded();
            if (car == null) return;
            car.PlaceOnGround();
            car.IsPersistent = true;
            Function.Call(Hash.SET_VEHICLE_MOD_KIT, car, 0);
            Function.Call(Hash.SET_VEHICLE_COLOURS, car, rng.Next(160), rng.Next(160));
            Function.Call(Hash.SET_VEHICLE_IS_RACING, car, true);
            car.IsPositionFrozen = state != State.Racing;
            r.Car = car;

            Ped me = Game.Player.Character;
            if (r.IsPlayer)
            {
                me.SetIntoVehicle(car, VehicleSeat.Driver);
                r.Ped = me;
                Function.Call(Hash.SET_VEHICLE_ENGINE_ON, car, true, true, false);
            }
            else
            {
                if (r.Ped == null || !r.Ped.Exists() || r.Ped.IsDead) r.Ped = CreateDriver(car);
                else r.Ped.SetIntoVehicle(car, VehicleSeat.Driver);
                if (r.Ped == null) return;
                if (cls.Bikes) r.Ped.GiveHelmet(false, Helmet.RegularMotorcycleHelmet, 0);
            }
            UpdateRacerBlip(r);
        }

        Ped CreateDriver(Vehicle car)
        {
            Model model = Session.LoadModel(rng, Session.PlayerModels, m => m.IsPed);
            if (model == null) return null;
            Ped ped = car.CreatePedOnSeat(VehicleSeat.Driver, model);
            model.MarkAsNoLongerNeeded();
            if (ped == null) return null;
            ped.IsPersistent = true;
            ped.BlockPermanentEvents = true;
            ped.CanBeKnockedOffBike = false;
            Function.Call(Hash.SET_PED_RANDOM_COMPONENT_VARIATION, ped, 0);
            Function.Call(Hash.SET_PED_CONFIG_FLAG, ped, 32, false);            // don't fly through the windscreen
            Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, ped, 3, false);       // never leave the vehicle
            Function.Call(Hash.SET_PED_CAN_BE_DRAGGED_OUT, ped, false);
            float[] ability = { 0.6f, 0.85f, 1f };
            float[] aggro = { 0.3f, 0.6f, 0.9f };
            float[] racing = { 0.2f, 0.5f, 1f };
            Function.Call(Hash.SET_DRIVER_ABILITY, ped, ability[cfg.Skill]);
            Function.Call(Hash.SET_DRIVER_AGGRESSIVENESS, ped, aggro[cfg.Skill]);
            Function.Call(Hash.SET_DRIVER_RACING_MODIFIER, ped, racing[cfg.Skill]);
            return ped;
        }

        void Go(int now)
        {
            state = State.Racing;
            raceStart = now;
            Audio.PlaySoundFrontend("GO", "HUD_MINI_GAME_SOUNDSET");
            foreach (Racer r in racers)
            {
                r.LapStart = now;
                if (r.Car != null && r.Car.Exists()) r.Car.IsPositionFrozen = false;
                if (!r.IsPlayer) DriveToNext(r);
            }
        }

        void DriveToNext(Racer r)
        {
            if (r.Ped == null || !r.Ped.Exists() || r.Car == null || !r.Car.Exists() || r.Finished) return;
            Vector3 p = CpPos(Math.Min(r.Next, Total - 1));
            float[] speed = { 45f, 60f, 75f };
            Function.Call(Hash.TASK_VEHICLE_DRIVE_TO_COORD_LONGRANGE, r.Ped, r.Car, p.X, p.Y, p.Z, speed[cfg.Skill], RacingStyle, 5f);
        }

        // ------------------------------------------------------------------ racing

        void UpdatePlayer(int now)
        {
            Ped me = Game.Player.Character;
            player.Ped = me;
            if (player.Finished) return;

            if (me.IsDead)
            {
                if (playerDeadAt == 0) playerDeadAt = now;
                if (now - playerDeadAt > 3000)
                {
                    DeathControl.Resurrect(player.LastCp, player.LastHeading);
                    playerDeadAt = 0;
                    RespawnRacer(player);
                }
                return;
            }

            // Hold F (exit vehicle) to respawn at the last checkpoint, like Online.
            Game.DisableControlThisFrame(Control.VehicleExit);
            Game.DisableControlThisFrame(Control.Enter);
            bool held = Function.Call<bool>(Hash.IS_DISABLED_CONTROL_PRESSED, 0, (int)Control.VehicleExit)
                     || Function.Call<bool>(Hash.IS_DISABLED_CONTROL_PRESSED, 0, (int)Control.Enter);
            if (held)
            {
                if (respawnHeldSince == 0) respawnHeldSince = now;
                float t = Math.Min(1f, (now - respawnHeldSince) / 1000f);
                Draw.Rect(565f, 640f, 150f, 8f, Color.FromArgb(160, 0, 0, 0));
                Draw.Rect(565f, 640f, 150f * t, 8f, Color.White);
                if (t >= 1f)
                {
                    respawnHeldSince = 0;
                    RespawnRacer(player);
                    return;
                }
            }
            else
            {
                respawnHeldSince = 0;
            }
            if (!me.IsInVehicle()) GTA.UI.Screen.ShowSubtitle("Hold ~INPUT_VEH_EXIT~ to respawn in your car.", 100);

            Vector3 pos = me.Position;
            Vector3 cp = CpPos(player.Next);
            player.Dist = Flat(pos, cp);
            if (player.Dist < PlayerCpRadius && Math.Abs(pos.Z - cp.Z) < 12f) PassCheckpoint(player, now);
        }

        void UpdateAi(int now)
        {
            float playerProgress = Progress(player);
            foreach (Racer r in racers)
            {
                if (r.IsPlayer || r.Finished) continue;
                if (r.RespawnAt != 0)
                {
                    if (now >= r.RespawnAt) { r.RespawnAt = 0; RespawnRacer(r); }
                    continue;
                }
                bool broken = r.Car == null || !r.Car.Exists() || r.Car.IsDead || r.Ped == null || !r.Ped.Exists() || r.Ped.IsDead
                    || !r.Ped.IsInVehicle(r.Car) || r.Car.IsInWater;
                if (broken)
                {
                    r.RespawnAt = now + 2000;
                    continue;
                }
                Vector3 pos = r.Car.Position;
                Vector3 cp = CpPos(r.Next);
                r.Dist = Flat(pos, cp);
                if (r.Dist < AiCpRadius && Math.Abs(pos.Z - cp.Z) < 15f)
                {
                    PassCheckpoint(r, now);
                    if (r.Finished) continue;
                }

                // Stuck or flipped: respawn at the last checkpoint after 5 s.
                bool slow = r.Car.Speed < 2f || r.Car.IsUpsideDown;
                if (!slow) r.SlowSince = 0;
                else if (r.SlowSince == 0) r.SlowSince = now;
                else if (now - r.SlowSince > 5000) { r.SlowSince = 0; RespawnRacer(r); continue; }

                // Catch-up: power is applied every frame (the native resets each frame).
                float power = 1f;
                if (cfg.CatchUp)
                {
                    float gap = (Progress(r) - playerProgress) / 1000f; // in checkpoints, + = ahead of you
                    if (gap < -2f) power = 1.6f;
                    else if (gap < -1f) power = 1.35f;
                    else if (gap < 0f) power = 1.15f;
                    else if (gap > 2f) power = 0.85f;
                }
                power += (cfg.Skill - 1) * 0.1f;
                Function.Call(Hash.SET_VEHICLE_CHEAT_POWER_INCREASE, r.Car, Math.Max(0.2f, power));
            }
        }

        /// <summary>Monotonic race progress: 1000 per checkpoint, minus metres to the next one.</summary>
        float Progress(Racer r)
        {
            if (r.Finished) return 1000f * Total + 100000f - r.Place;
            return 1000f * r.Next - Math.Min(r.Dist, 999f);
        }

        void PassCheckpoint(Racer r, int now)
        {
            r.LastCp = CpPos(r.Next);
            Vector3 ahead = r.Next + 1 < Total ? CpPos(r.Next + 1) : r.LastCp + (r.Car != null && r.Car.Exists() ? r.Car.ForwardVector : Vector3.WorldNorth);
            r.LastHeading = HeadingTo(r.LastCp, ahead);
            if (IsLapEnd(r.Next))
            {
                int lap = now - r.LapStart;
                if (r.BestLap == 0 || lap < r.BestLap) r.BestLap = lap;
                r.LapStart = now;
                if (r.IsPlayer && r.Next + 1 < Total)
                {
                    int lapNo = (r.Next + 1) / route.Count + 1;
                    GTA.UI.Screen.ShowSubtitle(lapNo == cfg.Laps ? "~y~FINAL LAP" : string.Format("Lap {0} / {1}   {2}", lapNo, cfg.Laps, FormatTime(lap)), 2500);
                }
            }
            r.Next++;
            if (r.IsPlayer) Audio.PlaySoundFrontend("CHECKPOINT_NORMAL", "HUD_MINI_GAME_SOUNDSET");
            if (r.Next >= Total)
            {
                Finish(r, now);
                return;
            }
            if (!r.IsPlayer) DriveToNext(r);
        }

        void Finish(Racer r, int now)
        {
            r.Finished = true;
            r.FinishTime = now - raceStart;
            r.Place = ++finishCount;
            if (finishCount == 1) finishDeadline = now + FinishWindowMs;
            if (r.IsPlayer)
            {
                string title = r.Place == 1 ? "~y~WINNER" : string.Format("{0}{1}", r.Place, Ordinal(r.Place));
                shard.Show("SHOW_SHARD_CENTERED_MP_MESSAGE", title, FormatTime(r.FinishTime), 4000);
                Audio.PlaySoundFrontend(r.Place == 1 ? "RACE_PLACED" : "CHECKPOINT_PERFECT", "HUD_AWARDS");
                ClearCheckpointVisuals();
            }
            else
            {
                if (r.Ped != null && r.Ped.Exists()) r.Ped.Task.CruiseWithVehicle(r.Car, 12f, DrivingStyle.Normal);
                if (r.Place == 1) GTA.UI.Notification.Show(string.Format("~b~{0}~s~ won the race in {1}.", r.Name, FormatTime(r.FinishTime)));
            }
        }

        void RespawnRacer(Racer r)
        {
            r.SlowSince = 0;
            Vector3 pos = r.LastCp;
            float heading = r.LastHeading;
            if (!r.IsPlayer)
            {
                // Don't land on someone already sitting on that checkpoint.
                foreach (Racer o in racers)
                {
                    if (o == r || o.Car == null || !o.Car.Exists()) continue;
                    if (o.Car.Position.DistanceTo(pos) < 4f) pos += new Vector3((float)rng.NextDouble() * 6f - 3f, (float)rng.NextDouble() * 6f - 3f, 0f);
                }
            }
            bool carOk = r.Car != null && r.Car.Exists() && !r.Car.IsDead && !r.Car.IsInWater;
            if (!carOk)
            {
                if (r.Car != null && r.Car.Exists())
                {
                    r.Car.IsPersistent = false;
                    r.Car.MarkAsNoLongerNeeded();
                }
                r.Car = null;
                SpawnVehicle(r, pos, heading);
            }
            else
            {
                r.Car.Position = pos + new Vector3(0f, 0f, 0.5f);
                r.Car.Heading = heading;
                r.Car.Velocity = Vector3.Zero;
                r.Car.PlaceOnGround();
                r.Car.Repair();
                Ped driver = r.IsPlayer ? Game.Player.Character : r.Ped;
                if (driver != null && driver.Exists() && !driver.IsDead && !driver.IsInVehicle(r.Car)) driver.SetIntoVehicle(r.Car, VehicleSeat.Driver);
                if (!r.IsPlayer && (driver == null || !driver.Exists() || driver.IsDead)) r.Ped = CreateDriver(r.Car);
            }
            if (r.Car != null && r.Car.Exists())
            {
                Function.Call(Hash.SET_VEHICLE_ENGINE_ON, r.Car, true, true, false);
                r.Car.ForwardSpeed = 8f;
            }
            if (!r.IsPlayer) DriveToNext(r);
        }

        void UpdateStandings()
        {
            int place = finishCount;
            foreach (Racer r in racers.Where(x => !x.Finished).OrderByDescending(Progress))
            {
                place++;
                r.Place = place;
            }
            foreach (Racer r in racers)
            {
                if (r.IsPlayer || r.Blip == null || !r.Blip.Exists() || r.ShownPlace == r.Place) continue;
                r.Blip.NumberLabel = r.Place;
                r.ShownPlace = r.Place;
            }
        }

        // ------------------------------------------------------------------ end

        void EndRace(bool aborted)
        {
            if (state != State.Racing && state != State.Countdown) return;
            int now = Game.GameTime;
            foreach (Racer r in racers.Where(x => !x.Finished))
            {
                r.Dnf = true;
                r.Finished = true;
            }
            int n = racers.Count;
            state = State.Results;
            endedAt = now;
            ClearCheckpointVisuals();
            foreach (Racer r in racers)
            {
                if (r.IsPlayer || r.Ped == null || !r.Ped.Exists() || r.Car == null || !r.Car.Exists()) continue;
                r.Ped.Task.CruiseWithVehicle(r.Car, 10f, DrivingStyle.Normal);
            }
            if (aborted)
            {
                shard.Show("SHOW_SHARD_CENTERED_MP_MESSAGE", "RACE OVER", "Race ended early. No payout.", 4000);
                return;
            }
            int place = player.Place;
            int rp, cash;
            if (player.Dnf)
            {
                rp = 150;
                cash = 0;
                shard.Show("SHOW_SHARD_CENTERED_MP_MESSAGE", "~r~DNF", "You didn't finish in time.", 4000);
            }
            else
            {
                rp = 300 + (n - place) * 120 + (place == 1 ? 1000 : 0) + Total * 15;
                cash = 500 + (n - place) * 400 + (place == 1 ? 4000 : 0);
            }
            if (player.BestLap > 0 && racers.Where(r => r.BestLap > 0).Min(r => r.BestLap) == player.BestLap) rp += 250;
            Progression.AddRP(rp, "Race");
            if (cash > 0) Progression.AddCash(cash, "Race payout");
            Progression.RecordMatch(!player.Dnf && place == 1);
        }

        void Cleanup(bool aborting)
        {
            ClearCheckpointVisuals();
            Ped me = Game.Player.Character;
            if (me.IsDead) DeathControl.Resurrect(me.Position, me.Heading);
            foreach (Racer r in racers)
            {
                if (r.Blip != null && r.Blip.Exists()) r.Blip.Delete();
                if (!r.IsPlayer && r.Ped != null && r.Ped.Exists()) r.Ped.Delete();
            }
            bool returning = !aborting && cfg.ReturnAfter && origin != Vector3.Zero;
            if (returning)
            {
                GTA.UI.Screen.FadeOut(400);
                Wait(450);
            }
            foreach (Racer r in racers)
            {
                if (r.Car == null || !r.Car.Exists()) continue;
                if (r.IsPlayer && !returning)
                {
                    r.Car.IsPersistent = false; // keep driving the race car if you stay put
                    r.Car.MarkAsNoLongerNeeded();
                    continue;
                }
                if (r.IsPlayer && me.IsInVehicle(r.Car)) me.Task.WarpOutOfVehicle(r.Car);
                r.Car.Delete();
            }
            racers.Clear();
            if (returning)
            {
                if (originalCar != null && originalCar.Exists() && !originalCar.IsDead && originalCar.IsSeatFree(VehicleSeat.Driver))
                {
                    me.SetIntoVehicle(originalCar, VehicleSeat.Driver);
                }
                else
                {
                    me.Position = origin;
                }
                Wait(500);
                GTA.UI.Screen.FadeIn(600);
            }
            if (originalCar != null && originalCar.Exists()) originalCar.IsPersistent = false;
            originalCar = null;
            RestoreEnvironment();
            Game.Player.IgnoredByPolice = false;
            Function.Call(Hash.SET_MAX_WANTED_LEVEL, 5);
            DeathControl.Release(DeathOwner, !aborting);
            shard.Hide();
            state = State.Idle;
            Session.MatchActive = false;
        }

        const string DeathOwner = "race";

        // ------------------------------------------------------------------ checkpoints, blips, GPS

        void UpdateCheckpointVisuals()
        {
            if (player.Finished || player.Next == shownCp) return;
            shownCp = player.Next;
            ClearCheckpointVisuals();
            shownCp = player.Next;
            int k = player.Next;
            Vector3 p = CpPos(k);
            bool last = k == Total - 1;
            Vector3 aim = last ? p : CpPos(k + 1);
            cpNow = World.CreateCheckpoint(last ? CheckpointIcon.CylinderCheckerboard : CheckpointIcon.CylinderSingleArrow,
                p, aim, 10f, Color.FromArgb(150, 240, 200, 80));
            if (cpNow != null) Function.Call(Hash.SET_CHECKPOINT_CYLINDER_HEIGHT, cpNow.Handle, 6f, 6f, 10f);
            blipNow = World.CreateBlip(p);
            blipNow.Sprite = last ? BlipSprite.RaceFinish : BlipSprite.Standard;
            blipNow.Color = BlipColor.Yellow;
            blipNow.Scale = 1f;
            if (!last)
            {
                Vector3 after = k + 2 < Total ? CpPos(k + 2) : aim;
                bool nextLast = k + 1 == Total - 1;
                cpNext = World.CreateCheckpoint(nextLast ? CheckpointIcon.CylinderCheckerboard : CheckpointIcon.CylinderSingleArrow,
                    aim, after, 10f, Color.FromArgb(60, 240, 200, 80));
                if (cpNext != null) Function.Call(Hash.SET_CHECKPOINT_CYLINDER_HEIGHT, cpNext.Handle, 6f, 6f, 10f);
                blipNext = World.CreateBlip(aim);
                blipNext.Sprite = nextLast ? BlipSprite.RaceFinish : BlipSprite.Standard;
                blipNext.Color = BlipColor.Yellow;
                blipNext.Scale = 0.65f;
                blipNext.Alpha = 140;
            }
            // GPS through the next few checkpoints, following the roads.
            Function.Call(Hash.CLEAR_GPS_MULTI_ROUTE);
            Function.Call(Hash.START_GPS_MULTI_ROUTE, 12, true, true); // HUD_COLOUR_YELLOW
            for (int i = k; i < Math.Min(Total, k + 3); i++)
            {
                Vector3 q = CpPos(i);
                Function.Call(Hash.ADD_POINT_TO_GPS_MULTI_ROUTE, q.X, q.Y, q.Z);
            }
            Function.Call(Hash.SET_GPS_MULTI_ROUTE_RENDER, true);
        }

        void ClearCheckpointVisuals()
        {
            if (cpNow != null && cpNow.Exists()) cpNow.Delete();
            if (cpNext != null && cpNext.Exists()) cpNext.Delete();
            if (blipNow != null && blipNow.Exists()) blipNow.Delete();
            if (blipNext != null && blipNext.Exists()) blipNext.Delete();
            cpNow = cpNext = null;
            blipNow = blipNext = null;
            Function.Call(Hash.SET_GPS_MULTI_ROUTE_RENDER, false);
            Function.Call(Hash.CLEAR_GPS_MULTI_ROUTE);
            shownCp = -1;
        }

        void UpdateRacerBlip(Racer r)
        {
            if (r.IsPlayer || r.Car == null || !r.Car.Exists()) return;
            if (r.Blip != null && r.Blip.Exists()) r.Blip.Delete();
            r.ShownPlace = 0;
            r.Blip = r.Car.AddBlip();
            r.Blip.Color = Session.CrewNames.Contains(r.Name) ? BlipColor.Blue : BlipColor.White;
            r.Blip.Scale = 0.7f;
            r.Blip.Name = r.Name;
        }

        // ------------------------------------------------------------------ HUD

        void DrawCountdown(int now)
        {
            int left = goAt - now;
            if (left > CountdownMs) return;
            int n = left / 1000 + 1;
            if (n != lastBeep)
            {
                lastBeep = n;
                Audio.PlaySoundFrontend("3_2_1", "HUD_MINI_GAME_SOUNDSET");
            }
            Draw.Text(n.ToString(), 640f, 250f, 2.2f, n == 1 ? Color.FromArgb(255, 240, 80, 60) : Color.White, GTA.UI.Alignment.Center, GTA.UI.Font.Pricedown);
        }
        int lastBeep;

        void DrawHud(int now)
        {
            if (state == State.Countdown || player == null) return;
            const float right = 1270f, w = 230f;
            float y = 560f;
            Draw.Rect(right - w, y, w, 112f, Color.FromArgb(160, 0, 0, 0));
            Draw.Text("POSITION", right - w + 8f, y + 6f, 0.32f, Color.White, GTA.UI.Alignment.Left);
            Draw.Text(string.Format("{0}{1} / {2}", player.Place, Ordinal(player.Place), racers.Count), right - 8f, y + 2f, 0.5f, Gold, GTA.UI.Alignment.Right);
            y += 30f;
            if (!cfg.PointToPoint)
            {
                int lap = Math.Min(cfg.Laps, player.Next / route.Count + 1);
                Draw.Text("LAP", right - w + 8f, y + 6f, 0.32f, Color.White, GTA.UI.Alignment.Left);
                Draw.Text(string.Format("{0} / {1}", lap, cfg.Laps), right - 8f, y + 4f, 0.42f, Color.White, GTA.UI.Alignment.Right);
            }
            else
            {
                Draw.Text("CHECKPOINT", right - w + 8f, y + 6f, 0.32f, Color.White, GTA.UI.Alignment.Left);
                Draw.Text(string.Format("{0} / {1}", Math.Min(player.Next, Total), Total), right - 8f, y + 4f, 0.42f, Color.White, GTA.UI.Alignment.Right);
            }
            y += 28f;
            int t = player.Finished && !player.Dnf ? player.FinishTime : (state == State.Results ? endedAt - raceStart : now - raceStart);
            Draw.Text("TIME", right - w + 8f, y + 6f, 0.32f, Color.White, GTA.UI.Alignment.Left);
            Draw.Text(FormatTime(t), right - 8f, y + 4f, 0.42f, Color.White, GTA.UI.Alignment.Right);
            y += 28f;
            Draw.Text("Hold Z: standings", right - 8f, y + 4f, 0.26f, Color.FromArgb(255, 180, 180, 180), GTA.UI.Alignment.Right);

            if (state == State.Racing && finishCount > 0)
            {
                int left = Math.Max(0, finishDeadline - now) / 1000;
                string msg = player.Finished ? "Waiting for the others  {0}s   (Enter: results)" : "~r~Finish within {0}s";
                Draw.Text(string.Format(msg, left), 640f, 60f, 0.5f, Color.White, GTA.UI.Alignment.Center);
            }
        }

        void DrawNames()
        {
            Vector3 me = Game.Player.Character.Position;
            foreach (Racer r in racers)
            {
                if (r.IsPlayer || r.Car == null || !r.Car.Exists()) continue;
                float d = r.Car.Position.DistanceTo(me);
                if (d > 90f) continue;
                System.Drawing.PointF s = GTA.UI.Screen.WorldToScreen(r.Car.Position + new Vector3(0f, 0f, 1.6f));
                if (s.IsEmpty) continue;
                float scale = Math.Max(0.22f, 0.36f - d * 0.0015f);
                Color c = Session.CrewNames.Contains(r.Name) ? Blue : Color.White;
                Draw.Text(string.Format("{0}{1}  {2}", r.Place, Ordinal(r.Place), r.Name), s.X, s.Y, scale, c, GTA.UI.Alignment.Center);
            }
        }

        void DrawResults()
        {
            const float w = 560f, x = 640f - w / 2f, rowH = 22f;
            float y = 90f;
            Draw.Rect(x, y, w, 34f, Color.FromArgb(230, 180, 20, 30));
            Draw.Text(state == State.Results ? "RESULTS" : "STANDINGS", 640f, y + 3f, 0.6f, Color.White, GTA.UI.Alignment.Center, GTA.UI.Font.Pricedown);
            y += 34f;
            Color grey = Color.FromArgb(255, 170, 170, 170);
            Draw.Rect(x, y, w, rowH, Color.FromArgb(220, 0, 0, 0));
            Draw.Text("#", x + 10f, y + 3f, 0.28f, grey, GTA.UI.Alignment.Left);
            Draw.Text("RACER", x + 40f, y + 3f, 0.28f, grey, GTA.UI.Alignment.Left);
            Draw.Text("BEST LAP", x + w - 130f, y + 3f, 0.28f, grey, GTA.UI.Alignment.Right);
            Draw.Text("TIME", x + w - 15f, y + 3f, 0.28f, grey, GTA.UI.Alignment.Right);
            y += rowH;
            foreach (Racer r in racers.OrderBy(r => r.Dnf ? 1 : 0).ThenBy(r => r.Place))
            {
                Draw.Rect(x, y, w, rowH, r.IsPlayer ? Color.FromArgb(220, 60, 60, 20) : Color.FromArgb(180, 0, 0, 0));
                Color fg = r.IsPlayer ? Gold : Color.White;
                Draw.Text(r.Dnf ? "-" : r.Place.ToString(), x + 10f, y + 2f, 0.32f, fg, GTA.UI.Alignment.Left);
                Draw.Text(r.Name, x + 40f, y + 2f, 0.32f, fg, GTA.UI.Alignment.Left);
                Draw.Text(r.BestLap > 0 ? FormatTime(r.BestLap) : "--", x + w - 130f, y + 2f, 0.32f, fg, GTA.UI.Alignment.Right);
                string time = r.Dnf ? "DNF" : (r.Finished ? FormatTime(r.FinishTime) : "racing");
                Draw.Text(time, x + w - 15f, y + 2f, 0.32f, fg, GTA.UI.Alignment.Right);
                y += rowH;
            }
            if (state == State.Results)
            {
                Draw.Rect(x, y, w, rowH, Color.FromArgb(220, 0, 0, 0));
                Draw.Text("Enter to close", 640f, y + 2f, 0.3f, Gold, GTA.UI.Alignment.Center);
            }
        }

        // ------------------------------------------------------------------ environment

        void SaveEnvironment()
        {
            savedTime = World.CurrentTimeOfDay;
            savedWeather = World.Weather;
            savedClockPaused = World.IsClockPaused;
            envChanged = false;
        }

        void ApplyEnvironment()
        {
            int hour = DmConfig.TimeHours[cfg.TimeOfDay];
            if (hour >= 0)
            {
                World.CurrentTimeOfDay = new TimeSpan(hour, 0, 0);
                World.IsClockPaused = true;
                envChanged = true;
            }
            string weather = DmConfig.WeatherIds[cfg.Weather];
            if (weather != null)
            {
                Function.Call(Hash.SET_OVERRIDE_WEATHER, weather);
                envChanged = true;
            }
        }

        void RestoreEnvironment()
        {
            if (!envChanged) return;
            if (DmConfig.TimeHours[cfg.TimeOfDay] >= 0)
            {
                World.CurrentTimeOfDay = savedTime;
                World.IsClockPaused = savedClockPaused;
            }
            Function.Call(Hash.CLEAR_OVERRIDE_WEATHER);
            World.Weather = savedWeather;
            envChanged = false;
        }

        // ------------------------------------------------------------------ helpers

        static float Flat(Vector3 a, Vector3 b)
        {
            float dx = a.X - b.X, dy = a.Y - b.Y;
            return (float)Math.Sqrt(dx * dx + dy * dy);
        }

        static float HeadingTo(Vector3 from, Vector3 to)
        {
            return (float)(Math.Atan2(to.Y - from.Y, to.X - from.X) * 180.0 / Math.PI) - 90f;
        }

        static string FormatTime(int ms)
        {
            if (ms < 0) ms = 0;
            return string.Format("{0:00}:{1:00}.{2:0}", ms / 60000, ms / 1000 % 60, ms / 100 % 10);
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
