using System;
using System.Collections.Generic;
using System.Linq;
using GTA;
using GTA.Math;
using GTA.Native;
using Color = System.Drawing.Color;

namespace OfflinePvP
{
    internal enum FmEventKind { Checkpoints, HotProperty, KingOfTheCastle }

    /// <summary>
    /// Freemode events (Checkpoints, Hot Property, King of the Castle), AI-vs-AI feuds, crews sticking together,
    /// and job invites: the things that make an Online session feel alive between jobs.
    /// </summary>
    public sealed partial class FreemodeScript
    {
        sealed class EventEntry
        {
            public SessionPlayer Player; // null = you
            public string Name;
            public float Score;
        }

        sealed class EventPoint
        {
            public Vector3 Pos;
            public bool Taken;
            public Blip Blip;
        }

        static readonly string[] EventNames = { "Checkpoints", "Hot Property", "King of the Castle" };
        static readonly string[] EventHelp = {
            "Collect the most checkpoints.",
            "Hold the briefcase for the longest time.",
            "Hold the castle alone for the longest time." };
        static readonly int[] EventMinutes = { 8, 10, 8 };
        static readonly int[] PrizeCash = { 15000, 8000, 5000 };
        static readonly int[] PrizeRp = { 1500, 1000, 750 };
        static readonly Color Purple = Color.FromArgb(255, 160, 90, 220);
        const float CastleRadius = 15f;
        const int CheckpointCash = 750;

        bool eventOn;
        FmEventKind eventKind;
        int eventEnds, nextEvent, nextEventTick, eventLastTick;
        readonly List<EventEntry> entries = new List<EventEntry>();
        readonly List<EventPoint> points = new List<EventPoint>();
        int pendingCheckpointCash;

        // Hot Property
        Vector3 casePos;
        SessionPlayer caseHolder;   // null with caseOnGround false = you hold it
        bool caseOnGround;
        Prop caseProp;
        Blip caseBlip;

        // King of the Castle
        Vector3 castle;
        Blip castleBlip, castleArea;
        object king;                // SessionPlayer, PlayerKing, or null
        static readonly object PlayerKing = new object();
        bool castleContested;

        // Feuds / invites
        int nextFeud, nextInvite, inviteUntil, inviteJob;
        string inviteFrom;

        bool EventRunning { get { return eventOn; } }

        // ================================================================== schedule

        void ScheduleNextEvent(int now)
        {
            int minutes = FmConfig.EventEveryMinutes[cfg.EventEvery];
            nextEvent = minutes == 0 ? int.MaxValue : now + minutes * 60000;
        }

        void UpdateEvents(int now)
        {
            if (!eventOn)
            {
                if (now >= nextEvent)
                {
                    ScheduleNextEvent(now);
                    StartRandomEvent(now, false);
                }
                return;
            }
            DrawEventWorld();
            if (now < nextEventTick) return;
            float dt = Math.Min(3f, (now - eventLastTick) / 1000f);
            eventLastTick = now;
            nextEventTick = now + 1000;

            switch (eventKind)
            {
                case FmEventKind.Checkpoints: TickCheckpoints(dt); break;
                case FmEventKind.HotProperty: TickHotProperty(dt); break;
                case FmEventKind.KingOfTheCastle: TickCastle(now, dt); break;
            }
            bool allTaken = eventKind == FmEventKind.Checkpoints && points.All(p => p.Taken);
            if (now >= eventEnds || allTaken) FinishEvent();
        }

        void StartRandomEvent(int now, bool forced)
        {
            if (eventOn || !running || suspended || Session.MatchActive) return;
            if (Players.Count(p => !p.Dead) < 2)
            {
                if (forced) GTA.UI.Notification.Show("Not enough players in the session for an event.");
                return;
            }
            FmEventKind kind = (FmEventKind)rng.Next(EventNames.Length);
            entries.Clear();
            points.Clear();
            pendingCheckpointCash = 0;
            entries.Add(new EventEntry { Name = "You" });
            foreach (SessionPlayer p in Players)
            {
                // A few try-hards, many casuals: skill drives virtual scoring.
                p.EventWeight = 0.3f + (float)Math.Pow(rng.NextDouble(), 2.5) * 2.2f;
                p.EventGoal = -1;
                entries.Add(new EventEntry { Player = p, Name = p.Name });
            }

            bool ok;
            switch (kind)
            {
                case FmEventKind.Checkpoints: ok = SetupCheckpoints(); break;
                case FmEventKind.HotProperty: ok = SetupHotProperty(); break;
                default: ok = SetupCastle(); break;
            }
            if (!ok)
            {
                ClearEventWorld();
                if (forced) GTA.UI.Notification.Show("Couldn't set up an event here. Try somewhere with more roads.");
                return;
            }
            eventOn = true;
            eventKind = kind;
            eventEnds = now + EventMinutes[(int)kind] * 60000;
            eventLastTick = now;
            nextEventTick = now + 1000;
            shard.Show("SHOW_SHARD_CENTERED_MP_MESSAGE", "~p~" + EventNames[(int)kind].ToUpperInvariant(), EventHelp[(int)kind], 4000);
            Audio.PlaySoundFrontend("FLIGHT_SCHOOL_LESSON_PASSED", "HUD_AWARDS");
            GTA.UI.Notification.Show(string.Format("~p~Freemode event:~s~ {0}. {1}{2}", EventNames[(int)kind], EventHelp[(int)kind],
                passive ? " ~r~You're in passive mode and can't take part." : ""));
        }

        EventEntry Entry(SessionPlayer p)
        {
            EventEntry e = entries.FirstOrDefault(x => x.Player == p);
            if (e == null && p != null)
            {
                e = new EventEntry { Player = p, Name = p.Name };
                entries.Add(e);
            }
            return e;
        }

        void Score(SessionPlayer p, float amount)
        {
            if (p == null && passive) return;
            Entry(p).Score += amount;
        }

        void FinishEvent()
        {
            List<EventEntry> ranked = entries.Where(e => e.Score > 0).OrderByDescending(e => e.Score).ToList();
            EventEntry mine = Entry(null);
            int place = ranked.IndexOf(mine);
            string top = ranked.Count == 0 ? "Nobody scored." : string.Format("1st: {0} ({1})", ranked[0].Player == null ? "You" : ranked[0].Name, FormatScore(ranked[0].Score));
            shard.Show("SHOW_SHARD_CENTERED_MP_MESSAGE", "~p~" + EventNames[(int)eventKind].ToUpperInvariant() + " OVER",
                place >= 0 && place < 3 ? string.Format("You came {0}{1}. {2}", place + 1, Ordinal(place + 1), top) : top, 5000);
            if (pendingCheckpointCash > 0) Progression.AddCash(pendingCheckpointCash, "Checkpoints collected");
            if (place >= 0 && place < 3)
            {
                Progression.AddCash(PrizeCash[place], EventNames[(int)eventKind] + " " + (place + 1) + Ordinal(place + 1) + " place");
                Progression.AddRP(PrizeRp[place], EventNames[(int)eventKind]);
            }
            else if (mine.Score > 0)
            {
                Progression.AddRP(250, "Event participation");
            }
            EndEvent(false);
        }

        void EndEvent(bool cancelled)
        {
            if (!eventOn) return;
            eventOn = false;
            if (cancelled) pendingCheckpointCash = 0;
            foreach (SessionPlayer p in Players)
            {
                if (p.EventGoal != -1) p.Task = AiTask.None; // back to normal freemode behaviour
                p.EventGoal = -1;
            }
            // Players who were only hostile because of the event calm down.
            foreach (SessionPlayer p in Players.Where(x => x.Hostile && x.HostileUntil >= eventEnds - 1000)) SetHostile(p, false, 0);
            ClearEventWorld();
            lastGoalKey.Clear();
        }

        void ClearEventWorld()
        {
            foreach (EventPoint p in points) if (p.Blip != null && p.Blip.Exists()) p.Blip.Delete();
            points.Clear();
            if (caseProp != null && caseProp.Exists()) caseProp.Delete();
            caseProp = null;
            if (caseBlip != null && caseBlip.Exists()) caseBlip.Delete();
            caseBlip = null;
            caseHolder = null;
            caseOnGround = false;
            foreach (Blip b in new[] { castleBlip, castleArea }) if (b != null && b.Exists()) b.Delete();
            castleBlip = castleArea = null;
            king = null;
        }

        /// <summary>Where a far-away (virtual) player should head for the event, or Zero.</summary>
        Vector3 EventDest(SessionPlayer p)
        {
            if (!eventOn || p.EventGoal < 0) return Vector3.Zero;
            if (eventKind == FmEventKind.HotProperty && caseOnGround) return casePos;
            if (eventKind == FmEventKind.KingOfTheCastle) return castle;
            if (eventKind == FmEventKind.Checkpoints && p.EventGoal < points.Count && !points[p.EventGoal].Taken) return points[p.EventGoal].Pos;
            return Vector3.Zero;
        }

        /// <summary>The player holding the briefcase or the castle: purple flashing blip.</summary>
        bool IsEventKey(SessionPlayer p)
        {
            if (!eventOn) return false;
            if (eventKind == FmEventKind.HotProperty) return !caseOnGround && caseHolder == p && p != null;
            if (eventKind == FmEventKind.KingOfTheCastle) return king == (object)p && p != null;
            return false;
        }

        /// <summary>Someone died or left (p == null: you): drop the briefcase / lose the castle.</summary>
        void OnEventPlayerGone(SessionPlayer p, Vector3 where)
        {
            if (!eventOn) return;
            if (eventKind == FmEventKind.HotProperty && !caseOnGround && caseHolder == p)
            {
                string who = p == null ? "You" : "~b~" + p.Name + "~s~";
                DropCase(where);
                GTA.UI.Notification.Show(string.Format("{0} dropped the ~p~briefcase~s~.", who));
            }
            if (eventKind == FmEventKind.KingOfTheCastle && king == (p == null ? PlayerKing : (object)p)) king = null;
        }

        // ================================================================== Checkpoints

        bool SetupCheckpoints()
        {
            Vector3 me = Game.Player.Character.Position;
            for (int attempt = 0; attempt < 300 && points.Count < 40; attempt++)
            {
                double a = rng.NextDouble() * Math.PI * 2;
                float d = (float)(Math.Sqrt(rng.NextDouble()) * 900.0);
                Vector3 want = new Vector3(me.X + (float)Math.Cos(a) * d, me.Y + (float)Math.Sin(a) * d, me.Z);
                Vector3 pos = World.GetNextPositionOnStreet(want, true);
                if (pos == Vector3.Zero || Flat(pos, want) > 100f) continue;
                if (points.Any(p => Flat(p.Pos, pos) < 60f)) continue;
                points.Add(new EventPoint { Pos = pos });
            }
            if (points.Count < 10) return false;
            foreach (EventPoint p in points)
            {
                p.Blip = World.CreateBlip(p.Pos);
                p.Blip.Color = BlipColor.Yellow;
                p.Blip.Scale = 0.6f;
                p.Blip.Name = "Checkpoint";
            }
            foreach (SessionPlayer p in Players) p.EventGoal = rng.Next(points.Count);
            return true;
        }

        void TickCheckpoints(float dt)
        {
            Vector3 me = Game.Player.Character.Position;
            // Far-away players collect virtually, preferring checkpoints you can't see.
            // About one checkpoint every 20 s across a full lobby, split by skill.
            float totalRate = 0.05f * Math.Min(1f, Players.Count / 10f);
            List<SessionPlayer> away = Players.Where(x => !x.Materialized && !x.Dead).ToList();
            float weightSum = Math.Max(0.01f, away.Sum(p => p.EventWeight));
            foreach (SessionPlayer p in away)
            {
                if (rng.NextDouble() >= totalRate * dt * p.EventWeight / weightSum) continue;
                List<EventPoint> far = points.Where(x => !x.Taken && Flat(x.Pos, me) > 300f).ToList();
                if (far.Count == 0) continue;
                Take(far[rng.Next(far.Count)], p);
            }
            foreach (SessionPlayer p in Players)
            {
                if (p.EventGoal < 0 || p.EventGoal >= points.Count || points[p.EventGoal].Taken) p.EventGoal = NearestFree(p.Materialized ? p.Ped.Position : p.Pos);
            }
        }

        int NearestFree(Vector3 from)
        {
            int best = -1;
            float bestD = float.MaxValue;
            for (int i = 0; i < points.Count; i++)
            {
                if (points[i].Taken) continue;
                float d = Flat(points[i].Pos, from);
                if (d < bestD) { bestD = d; best = i; }
            }
            return best;
        }

        void Take(EventPoint cp, SessionPlayer by)
        {
            cp.Taken = true;
            if (cp.Blip != null && cp.Blip.Exists()) cp.Blip.Delete();
            cp.Blip = null;
            Score(by, 1f);
            if (by == null)
            {
                pendingCheckpointCash += CheckpointCash;
                Progression.AddRP(50, "Checkpoint");
                Audio.PlaySoundFrontend("CHECKPOINT_NORMAL", "HUD_MINI_GAME_SOUNDSET");
            }
        }

        // ================================================================== Hot Property

        bool SetupHotProperty()
        {
            Vector3 me = Game.Player.Character.Position;
            for (int i = 0; i < 20; i++)
            {
                Vector3 want = Around(me, 300f + rng.Next(300));
                Vector3 pos = World.GetNextPositionOnSidewalk(want);
                if (pos == Vector3.Zero) pos = World.GetNextPositionOnStreet(want, true);
                if (pos == Vector3.Zero || Flat(pos, want) > 120f) continue;
                DropCase(pos);
                foreach (SessionPlayer p in Players) p.EventGoal = rng.Next(100) < 45 ? 0 : -1; // not everyone goes for it
                return true;
            }
            return false;
        }

        void DropCase(Vector3 pos)
        {
            caseOnGround = true;
            caseHolder = null;
            casePos = pos;
            if (caseProp != null && caseProp.Exists()) caseProp.Delete();
            caseProp = null;
            // The prop only exists near you; far away the case is just a blip.
            if (Flat(pos, Game.Player.Character.Position) < 200f)
            {
                Model m = new Model("prop_ld_case_01");
                if (m.IsInCdImage && m.Request(500))
                {
                    caseProp = World.CreateProp(m, pos, false, true);
                    m.MarkAsNoLongerNeeded();
                }
            }
            foreach (SessionPlayer p in Players.Where(x => x.Hostile && x.HostileUntil >= eventEnds - 1000)) SetHostile(p, false, 0);
            UpdateCaseBlip();
        }

        void PickUpCase(SessionPlayer by)
        {
            caseOnGround = false;
            caseHolder = by;
            if (caseProp != null && caseProp.Exists()) caseProp.Delete();
            caseProp = null;
            if (by == null)
            {
                GTA.UI.Notification.Show("You have the ~p~briefcase~s~. Hold on to it!");
                Audio.PlaySoundFrontend("PICK_UP", "HUD_FRONTEND_DEFAULT_SOUNDSET");
                // The nearest players come for it.
                Vector3 me = Game.Player.Character.Position;
                foreach (SessionPlayer p in Players.Where(x => !x.Dead && !x.Hostile).OrderBy(x => Flat(x.Pos, me)).Take(4))
                    SetHostile(p, true, eventEnds - Game.GameTime);
            }
            else
            {
                GTA.UI.Notification.Show(string.Format("~b~{0}~s~ has the ~p~briefcase~s~.", by.Name));
            }
            UpdateCaseBlip();
        }

        void UpdateCaseBlip()
        {
            if (caseBlip != null && caseBlip.Exists()) caseBlip.Delete();
            caseBlip = null;
            if (!caseOnGround) return; // a carrier is shown by their own (purple, flashing) blip
            caseBlip = World.CreateBlip(casePos);
            caseBlip.Sprite = BlipSprite.Briefcase2;
            caseBlip.Color = BlipColor.Purple;
            caseBlip.Name = "Briefcase";
            caseBlip.IsFlashing = true;
        }

        void TickHotProperty(float dt)
        {
            Ped me = Game.Player.Character;
            if (caseOnGround)
            {
                float reach = me.IsInVehicle() ? 4f : 2.5f;
                if (!passive && !playerDead && me.Position.DistanceTo(casePos) < reach) { PickUpCase(null); return; }
                // Far away, the closest interested player gets there first.
                foreach (SessionPlayer p in Players.Where(x => !x.Dead && x.EventGoal == 0 && !x.Materialized))
                {
                    if (Flat(p.Pos, casePos) < 30f && Flat(casePos, me.Position) > 200f) { PickUpCase(p); return; }
                }
                return;
            }
            Score(caseHolder, dt);
            if (caseHolder == null)
            {
                GTA.UI.Screen.ShowSubtitle("You have the ~p~briefcase~s~.", 1100);
                return;
            }
            // A virtual carrier can be robbed by other virtual players.
            if (!caseHolder.Materialized && rng.NextDouble() < 0.012 * dt)
            {
                SessionPlayer thief = Players.FirstOrDefault(x => !x.Dead && !x.Materialized && x != caseHolder && x.EventGoal == 0);
                if (thief != null)
                {
                    GTA.UI.Notification.Show(string.Format("~b~{0}~s~ killed ~b~{1}~s~ and took the ~p~briefcase~s~.", thief.Name, caseHolder.Name));
                    caseHolder.Dead = true;
                    caseHolder.Deaths++;
                    caseHolder.RespawnAt = Game.GameTime + 10000;
                    thief.Kills++;
                    caseHolder = thief;
                }
            }
        }

        // ================================================================== King of the Castle

        bool SetupCastle()
        {
            Vector3 me = Game.Player.Character.Position;
            for (int i = 0; i < 20; i++)
            {
                Vector3 want = Around(me, 250f + rng.Next(450));
                Vector3 pos = World.GetNextPositionOnSidewalk(want);
                if (pos == Vector3.Zero || Flat(pos, want) > 150f) continue;
                castle = pos;
                castleArea = World.CreateBlip(castle, CastleRadius);
                castleArea.Color = BlipColor.Purple;
                castleArea.Alpha = 120;
                castleBlip = World.CreateBlip(castle);
                castleBlip.Sprite = BlipSprite.Castle;
                castleBlip.Color = BlipColor.Purple;
                castleBlip.Name = "Castle";
                foreach (SessionPlayer p in Players) p.EventGoal = rng.Next(100) < 40 ? 0 : -1;
                return true;
            }
            return false;
        }

        bool InCastle(Vector3 p)
        {
            return Flat(p, castle) <= CastleRadius && Math.Abs(p.Z - castle.Z) < 8f;
        }

        void TickCastle(int now, float dt)
        {
            Ped me = Game.Player.Character;
            List<object> inside = new List<object>();
            if (!passive && !playerDead && InCastle(me.Position)) inside.Add(PlayerKing);
            foreach (SessionPlayer p in Players)
            {
                if (p.Dead || p.EventGoal != 0) continue;
                if (p.Materialized ? (p.Ped.Exists() && !p.Ped.IsDead && InCastle(p.Ped.Position)) : Flat(p.Pos, castle) < 30f) inside.Add(p);
            }
            castleContested = inside.Count > 1;
            object newKing = inside.Count == 1 ? inside[0] : (inside.Contains(king) ? king : null);
            if (newKing != king && newKing != null)
            {
                SessionPlayer sp = newKing as SessionPlayer;
                GTA.UI.Notification.Show(sp == null ? "You are the ~p~King of the Castle~s~." : string.Format("~b~{0}~s~ is the ~p~King of the Castle~s~.", sp.Name));
            }
            king = newKing;
            if (king != null && !castleContested) Score(king == PlayerKing ? null : (SessionPlayer)king, dt);

            // Far-away contests sort themselves out: someone gets knocked off.
            List<SessionPlayer> virtualIn = inside.OfType<SessionPlayer>().Where(p => !p.Materialized).ToList();
            if (virtualIn.Count > 1 && rng.NextDouble() < 0.1 * dt)
            {
                SessionPlayer loser = virtualIn[rng.Next(virtualIn.Count)];
                SessionPlayer winner = virtualIn.First(p => p != loser);
                loser.Dead = true;
                loser.Deaths++;
                loser.RespawnAt = now + 10000;
                winner.Kills++;
                if (king == (object)loser) king = null;
            }

            // You're on the hill: contenders near you come for you.
            if (inside.Contains(PlayerKing))
            {
                foreach (SessionPlayer p in Players.Where(x => x.Materialized && !x.Hostile && x.EventGoal == 0 && x.Ped.Exists()
                    && Flat(x.Ped.Position, castle) < 120f))
                    SetHostile(p, true, 20000);
            }
        }

        // ================================================================== physical AI for events

        /// <summary>Drives a materialized player's event behaviour. Returns true when it handled this think.</summary>
        bool EventThink(SessionPlayer p, Ped me)
        {
            if (!eventOn || p.EventGoal < 0) return false;
            Ped ped = p.Ped;
            Vector3 goal;
            float reach;
            switch (eventKind)
            {
                case FmEventKind.Checkpoints:
                    if (p.EventGoal >= points.Count) return false;
                    EventPoint cp = points[p.EventGoal];
                    if (cp.Taken) return false;
                    goal = cp.Pos;
                    reach = 8f;
                    if (ped.Position.DistanceTo(goal) < reach)
                    {
                        Take(cp, p);
                        p.EventGoal = NearestFree(ped.Position);
                        p.Task = AiTask.None;
                        return true;
                    }
                    break;
                case FmEventKind.HotProperty:
                    if (!caseOnGround)
                    {
                        // Carrying it: drive off and keep moving. Otherwise behave normally.
                        if (caseHolder != p) return false;
                        if (p.Task != AiTask.Cruise && ped.IsInVehicle())
                        {
                            ped.Task.CruiseWithVehicle(ped.CurrentVehicle, 30f, DrivingStyle.AvoidTrafficExtremely);
                            p.Task = AiTask.Cruise;
                        }
                        else if (p.Task != AiTask.Wander && !ped.IsInVehicle())
                        {
                            ped.Task.WanderAround(); // keep moving on foot
                            p.Task = AiTask.Wander;
                        }
                        return true;
                    }
                    goal = casePos;
                    // A car can't park on top of it: from a vehicle, close enough counts (you get 4 m).
                    if (ped.Position.DistanceTo(goal) < (ped.IsInVehicle() ? 8f : 3f)) { PickUpCase(p); return true; }
                    break;
                default:
                    goal = castle;
                    if (InCastle(ped.Position))
                    {
                        if (ped.IsInVehicle())
                        {
                            ped.Task.LeaveVehicle();
                            return true;
                        }
                        // Session players are neutral to each other, so pick a rival on the hill explicitly.
                        SessionPlayer rival = Players.FirstOrDefault(x => x != p && x.Materialized && x.Ped.Exists() && !x.Ped.IsDead
                            && InCastle(x.Ped.Position));
                        if (rival != null)
                        {
                            if (!ped.IsInCombatAgainst(rival.Ped)) ped.Task.FightAgainst(rival.Ped);
                        }
                        else if (p.Task != AiTask.Fight)
                        {
                            ped.Task.GuardCurrentPosition();
                        }
                        p.Task = AiTask.Fight;
                        return true;
                    }
                    break;
            }
            // Head for the goal once (re-task only when the goal changes).
            int goalKey = (int)(goal.X * 7 + goal.Y * 13);
            if (p.Task == AiTask.Hunt && lastGoalKey.ContainsKey(p) && lastGoalKey[p] == goalKey) return true;
            lastGoalKey[p] = goalKey;
            if (ped.IsInVehicle()) ped.Task.DriveTo(ped.CurrentVehicle, goal, 6f, 35f, DrivingStyle.Rushed);
            else ped.Task.RunTo(goal, false, -1);
            p.Task = AiTask.Hunt;
            return true;
        }

        readonly Dictionary<SessionPlayer, int> lastGoalKey = new Dictionary<SessionPlayer, int>();

        void DrawEventWorld()
        {
            Vector3 me = Game.Player.Character.Position;
            Color yellow = Color.FromArgb(110, 240, 200, 80);
            if (eventKind == FmEventKind.Checkpoints)
            {
                foreach (EventPoint p in points)
                {
                    if (p.Taken || Flat(p.Pos, me) > 150f) continue;
                    World.DrawMarker(MarkerType.VerticalCylinder, p.Pos - new Vector3(0f, 0f, 1f), Vector3.Zero, Vector3.Zero,
                        new Vector3(8f, 8f, 4f), yellow, false, false, false, null, null, false);
                    if (!passive && !playerDead && Flat(p.Pos, me) < 6f && Math.Abs(p.Pos.Z - me.Z) < 6f) Take(p, null);
                }
            }
            else if (eventKind == FmEventKind.KingOfTheCastle && Flat(castle, me) < 200f)
            {
                Color c = castleContested ? Color.FromArgb(110, 255, 140, 0) : Color.FromArgb(90, 160, 90, 220);
                World.DrawMarker(MarkerType.VerticalCylinder, castle - new Vector3(0f, 0f, 1f), Vector3.Zero, Vector3.Zero,
                    new Vector3(CastleRadius * 2f, CastleRadius * 2f, 2f), c, false, false, false, null, null, false);
            }
            else if (eventKind == FmEventKind.HotProperty && caseOnGround && Flat(casePos, me) < 150f)
            {
                World.DrawMarker(MarkerType.UpsideDownCone, casePos + new Vector3(0f, 0f, 1.2f), Vector3.Zero, Vector3.Zero,
                    new Vector3(0.6f, 0.6f, 0.6f), Purple, true, false, false, null, null, false);
            }
        }

        void DrawEventHud(int now)
        {
            if (!eventOn) return;
            const float right = 1270f, w = 260f;
            float y = 500f;
            List<EventEntry> ranked = entries.OrderByDescending(e => e.Score).ToList();
            Draw.Rect(right - w, y, w, 26f, Color.FromArgb(220, 90, 40, 130));
            int left = Math.Max(0, eventEnds - now) / 1000;
            Draw.Text(EventNames[(int)eventKind].ToUpperInvariant(), right - w + 8f, y + 3f, 0.36f, Color.White, GTA.UI.Alignment.Left);
            Draw.Text(string.Format("{0}:{1:00}", left / 60, left % 60), right - 8f, y + 3f, 0.36f, Color.White, GTA.UI.Alignment.Right);
            y += 26f;
            Color[] medal = { Color.FromArgb(255, 240, 200, 80), Color.FromArgb(255, 200, 200, 200), Color.FromArgb(255, 205, 127, 50) };
            for (int i = 0; i < 3 && i < ranked.Count; i++)
            {
                if (ranked[i].Score <= 0) break;
                Draw.Rect(right - w, y, w, 22f, Color.FromArgb(170, 0, 0, 0));
                Draw.Text(string.Format("{0}. {1}", i + 1, ranked[i].Player == null ? "You" : ranked[i].Name), right - w + 8f, y + 2f, 0.32f, medal[i], GTA.UI.Alignment.Left);
                Draw.Text(FormatScore(ranked[i].Score), right - 8f, y + 2f, 0.32f, Color.White, GTA.UI.Alignment.Right);
                y += 22f;
            }
            EventEntry mine = Entry(null);
            Draw.Rect(right - w, y, w, 22f, Color.FromArgb(200, 60, 60, 20));
            Draw.Text(passive ? "You (passive)" : string.Format("You  ({0}{1})", ranked.IndexOf(mine) + 1, Ordinal(ranked.IndexOf(mine) + 1)),
                right - w + 8f, y + 2f, 0.32f, Gold, GTA.UI.Alignment.Left);
            Draw.Text(FormatScore(mine.Score), right - 8f, y + 2f, 0.32f, Color.White, GTA.UI.Alignment.Right);
        }

        string FormatScore(float s)
        {
            if (eventKind == FmEventKind.Checkpoints) return ((int)s).ToString();
            int t = (int)s;
            return string.Format("{0}:{1:00}", t / 60, t % 60);
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

        // ================================================================== feuds & crews

        void UpdateFeuds(int now)
        {
            foreach (SessionPlayer p in Players.Where(x => x.FeudWith != null).ToList())
            {
                SessionPlayer o = p.FeudWith;
                bool over = now > p.FeudUntil || !p.Materialized || o == null || !o.Materialized || o.Dead || p.Dead;
                if (over) EndFeud(p);
            }
            if (!cfg.Feuds || now < nextFeud) return;
            nextFeud = now + 45000;
            if (rng.Next(100) >= 35) return;
            List<SessionPlayer> calm = Players.Where(x => x.Materialized && x.Ped.Exists() && !x.Ped.IsDead && !x.Hostile
                && x.FeudWith == null && x.EventGoal < 0).ToList();
            foreach (SessionPlayer a in calm)
            {
                SessionPlayer b = calm.FirstOrDefault(x => x != a && x.Ped.Position.DistanceTo(a.Ped.Position) < 80f
                    && (x.Crew == null || x.Crew != a.Crew));
                if (b == null) continue;
                StartFeud(a, b, now);
                return;
            }
        }

        void StartFeud(SessionPlayer a, SessionPlayer b, int now)
        {
            a.FeudWith = b;
            b.FeudWith = a;
            a.FeudSideB = false;
            b.FeudSideB = true;
            a.FeudUntil = b.FeudUntil = now + 60000 + rng.Next(30000);
            a.Task = b.Task = AiTask.None;
            Apply(a);
            Apply(b);
            // Their crew mates nearby pile in on their side.
            foreach (SessionPlayer side in new[] { a, b })
            {
                if (side.Crew == null) continue;
                foreach (SessionPlayer mate in Players.Where(x => x != side && x.Crew == side.Crew && x.Materialized && x.FeudWith == null
                    && !x.Hostile && x.Ped.Exists() && x.Ped.Position.DistanceTo(side.Ped.Position) < 150f))
                {
                    mate.FeudWith = side.FeudWith;
                    mate.FeudSideB = side.FeudSideB;
                    mate.FeudUntil = side.FeudUntil;
                    mate.Task = AiTask.None;
                    Apply(mate);
                }
            }
            if (cfg.NameTags && a.Ped.Position.DistanceTo(Game.Player.Character.Position) < 150f)
                GTA.UI.Notification.Show(string.Format("~b~{0}~s~ and ~b~{1}~s~ are fighting.", a.Name, b.Name));
        }

        bool ThinkFeud(SessionPlayer p)
        {
            if (p.FeudWith == null) return false;
            SessionPlayer o = p.FeudWith;
            if (o.Ped == null || !o.Ped.Exists() || o.Ped.IsDead) return false;
            if (p.Task != AiTask.Fight || !p.Ped.IsInCombatAgainst(o.Ped))
            {
                p.Ped.Task.FightAgainst(o.Ped);
                p.Task = AiTask.Fight;
            }
            return true;
        }

        /// <summary>Ends the whole brawl p is in: both sides, crew mates included.</summary>
        void EndFeud(SessionPlayer p)
        {
            if (p.FeudWith == null) return;
            SessionPlayer o = p.FeudWith;
            foreach (SessionPlayer x in Players.Where(x => x == p || x == o || x.FeudWith == p || x.FeudWith == o).ToList())
            {
                x.FeudWith = null;
                x.Task = AiTask.None;
                Apply(x);
            }
            p.FeudWith = null;
            p.Task = AiTask.None;
            Apply(p);
        }

        /// <summary>You attacked someone in a crew: their crew mates nearby come after you too.</summary>
        void CallInCrew(SessionPlayer victim)
        {
            if (victim.Crew == null || passive) return;
            Vector3 at = victim.Materialized && victim.Ped.Exists() ? victim.Ped.Position : victim.Pos;
            List<SessionPlayer> mates = Players.Where(x => x != victim && x.Crew == victim.Crew && !x.Dead && !x.Hostile
                && Flat(x.Materialized && x.Ped.Exists() ? x.Ped.Position : x.Pos, at) < 600f).ToList();
            if (mates.Count == 0) return;
            foreach (SessionPlayer m in mates) SetHostile(m, true, 90000);
            GTA.UI.Notification.Show(string.Format("~r~[{0}]~s~ crew is coming after you.", victim.Crew));
        }

        // ================================================================== job invites

        bool InvitePending { get { return inviteFrom != null && Game.GameTime < inviteUntil; } }

        string InviteLabel()
        {
            if (!InvitePending) return "None";
            return string.Format("{0} ({1}s)", Session.JobNames[inviteJob], Math.Max(0, (inviteUntil - Game.GameTime) / 1000));
        }

        void UpdateInvites(int now)
        {
            if (inviteFrom != null && now >= inviteUntil) inviteFrom = null;
            if (!cfg.Invites || now < nextInvite) return;
            nextInvite = now + 180000 + rng.Next(180000);
            if (Session.MatchActive || Game.IsMissionActive || eventOn || playerDead) return;
            List<SessionPlayer> friendly = Players.Where(p => !p.Hostile && !p.Dead).ToList();
            if (friendly.Count == 0) return;
            SessionPlayer from = friendly[rng.Next(friendly.Count)];
            // DM, TDM, LTS, KotH, Gun Game, Race: weighted roughly by how popular they are.
            int[] weights = { 25, 20, 10, 10, 15, 20 };
            int roll = rng.Next(weights.Sum()), job = 0;
            while (roll >= weights[job]) { roll -= weights[job]; job++; }
            inviteFrom = from.Name;
            inviteJob = job;
            inviteUntil = now + 30000;
            Audio.PlaySoundFrontend("Text_Arrive_Tone", "Phone_SoundSet_Default");
            GTA.UI.Notification.Show(GTA.UI.NotificationIcon.Multiplayer, from.Name, "Job invite",
                string.Format("Join my {0}? Press {1} or use the interaction menu.", Session.JobNames[job], cfg.AcceptKey), false, true);
        }

        void AcceptInvite()
        {
            if (!InvitePending) return;
            if (Game.IsMissionActive)
            {
                GTA.UI.Notification.Show("~r~You can't join a job during a story mission.");
                return;
            }
            foreach (SimpleMenu m in new[] { root, sessionMenu, inventoryMenu, profileMenu }) m.Visible = false;
            GTA.UI.Notification.Show(string.Format("Joining ~b~{0}~s~'s {1}...", inviteFrom, Session.JobNames[inviteJob]));
            Session.RequestJob(inviteJob);
            inviteFrom = null;
        }
    }
}
