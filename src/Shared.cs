using System;
using System.Collections.Generic;
using System.Linq;
using GTA;
using GTA.Math;
using GTA.Native;
using Color = System.Drawing.Color;

namespace OfflinePvP
{
    /// <summary>
    /// Story mode's death flow (WASTED → hospital) is run by the respawn_controller script. Online-style modes
    /// suspend it while they own the player's deaths. Several scripts can hold it; it comes back when the last lets go.
    /// </summary>
    internal static class DeathControl
    {
        static readonly HashSet<string> owners = new HashSet<string>();

        public static bool IsHeldBy(string owner)
        {
            return owners.Contains(owner);
        }

        public static void Acquire(string owner)
        {
            if (owners.Count == 0) Function.Call(Hash.TERMINATE_ALL_SCRIPTS_WITH_THIS_NAME, "respawn_controller");
            owners.Add(owner);
            KeepSuppressed();
        }

        /// <summary>Call every frame while held.</summary>
        public static void KeepSuppressed()
        {
            if (owners.Count == 0) return;
            Function.Call(Hash.PAUSE_DEATH_ARREST_RESTART, true);
            Function.Call(Hash.IGNORE_NEXT_RESTART, true);
            Function.Call(Hash.SET_FADE_OUT_AFTER_DEATH, false);
        }

        public static void Release(string owner, bool canWait)
        {
            if (!owners.Remove(owner) || owners.Count > 0) return;
            Function.Call(Hash.PAUSE_DEATH_ARREST_RESTART, false);
            Function.Call(Hash.IGNORE_NEXT_RESTART, false);
            Function.Call(Hash.SET_FADE_OUT_AFTER_DEATH, true);
            int running = Function.Call<int>(Hash.GET_NUMBER_OF_THREADS_RUNNING_THE_SCRIPT_WITH_THIS_HASH, Game.GenerateHash("respawn_controller"));
            if (running > 0) return;
            Function.Call(Hash.REQUEST_SCRIPT, "respawn_controller");
            int deadline = Game.GameTime + 3000;
            while (canWait && !Function.Call<bool>(Hash.HAS_SCRIPT_LOADED, "respawn_controller") && Game.GameTime < deadline) Script.Wait(0);
            if (Function.Call<bool>(Hash.HAS_SCRIPT_LOADED, "respawn_controller"))
            {
                Function.Call(Hash.START_NEW_SCRIPT, "respawn_controller", 1424);
                Function.Call(Hash.SET_SCRIPT_AS_NO_LONGER_NEEDED, "respawn_controller");
            }
        }

        public static void Resurrect(Vector3 pos, float heading)
        {
            Function.Call(Hash.NETWORK_RESURRECT_LOCAL_PLAYER, pos.X, pos.Y, pos.Z, heading, false, false, false, 0, 0);
            Ped me = Game.Player.Character;
            if (me.IsDead) me.Resurrect();
            me.Task.ClearAllImmediately();
            me.Position = pos;
            me.Heading = heading;
            me.Health = me.MaxHealth;
            Function.Call(Hash.ANIMPOSTFX_STOP_ALL);
            Game.TimeScale = 1f;
            if (GTA.UI.Screen.IsFadedOut) GTA.UI.Screen.FadeIn(300);
        }
    }

    /// <summary>State shared between the freemode session and jobs (deathmatch etc.).</summary>
    internal static class Session
    {
        /// <summary>True while a job (deathmatch, ...) owns the player. Freemode stands down.</summary>
        public static bool MatchActive;

        /// <summary>AI players currently in the freemode session.</summary>
        public static readonly List<SessionPlayer> Players = new List<SessionPlayer>();

        /// <summary>Set by the interaction menu to ask the jobs script to open its menu.</summary>
        public static bool OpenJobsMenu;

        /// <summary>Like Online, people in your session are the ones who join your job.</summary>
        public static string TakeJobName(ICollection<string> taken, Random rng)
        {
            List<SessionPlayer> free = Players.Where(p => !taken.Contains(p.Name)).ToList();
            return free.Count > 0 ? free[rng.Next(free.Count)].Name : null;
        }

        public static readonly string[] PlayerModels = {
            "a_m_y_hipster_01", "a_m_y_hipster_02", "a_m_y_business_01", "a_m_y_beach_01", "a_m_y_skater_01",
            "a_m_y_stbla_01", "a_m_y_stwhi_01", "a_m_y_downtown_01", "a_m_y_vinewood_01", "a_m_y_ktown_01",
            "a_f_y_hipster_01", "a_f_y_tourist_01", "a_f_y_bevhills_01", "g_m_y_ballaeast_01", "g_m_y_famca_01",
            "g_m_y_mexgoon_01", "g_m_y_lost_01", "g_m_y_korean_01", "g_m_y_salvagoon_01", "g_m_m_armgoon_01",
            "s_m_y_blackops_01", "s_m_m_highsec_01" };

        public static Model LoadModel(Random rng, string[] pool, Func<Model, bool> kind)
        {
            for (int attempt = 0; attempt < 4; attempt++)
            {
                Model m = new Model(pool[rng.Next(pool.Length)]);
                if (!m.IsInCdImage || !kind(m)) continue;
                if (m.Request(1000)) return m;
            }
            return null;
        }
    }

    /// <summary>Persistent RP, rank, stats and inventory, saved to OfflinePvP_Profile.ini.</summary>
    internal static class Progression
    {
        public const int MaxRank = 8000;
        public static readonly string[] SnackNames = { "P's & Q's", "EgoChaser", "Meteorite", "eCola", "Sprunk" };
        public static readonly int[] SnackHeal = { 15, 25, 40, 10, 10 };
        public static readonly string[] ArmorNames = { "Super Light Armor", "Light Armor", "Standard Armor", "Heavy Armor", "Super Heavy Armor" };
        public static readonly int[] ArmorValue = { 20, 40, 60, 80, 100 };
        public static readonly int[] SnackMax = { 30, 15, 5, 10, 10 };
        public const int ArmorMax = 10;

        public static int RP, Kills, Deaths, MatchesPlayed, MatchesWon;
        public static readonly int[] Snacks = new int[5];
        public static readonly int[] Armor = new int[5];

        static ScriptSettings file;
        static int cachedRank = -1, cachedRankRp = -1;
        static int barUntil, barGain;
        static string barReason;
        static readonly BigMessage shard = new BigMessage();

        public static void Init(string path)
        {
            if (file != null) return;
            file = ScriptSettings.Load(path);
            RP = Math.Max(0, file.GetValue("Profile", "RP", 0));
            Kills = file.GetValue("Profile", "Kills", 0);
            Deaths = file.GetValue("Profile", "Deaths", 0);
            MatchesPlayed = file.GetValue("Profile", "MatchesPlayed", 0);
            MatchesWon = file.GetValue("Profile", "MatchesWon", 0);
            for (int i = 0; i < 5; i++)
            {
                Snacks[i] = file.GetValue("Inventory", "Snack" + i, SnackMax[i] / 2);
                Armor[i] = file.GetValue("Inventory", "Armor" + i, 2);
            }
        }

        public static void Save()
        {
            if (file == null) return;
            file.SetValue("Profile", "RP", RP);
            file.SetValue("Profile", "Kills", Kills);
            file.SetValue("Profile", "Deaths", Deaths);
            file.SetValue("Profile", "MatchesPlayed", MatchesPlayed);
            file.SetValue("Profile", "MatchesWon", MatchesWon);
            for (int i = 0; i < 5; i++)
            {
                file.SetValue("Inventory", "Snack" + i, Snacks[i]);
                file.SetValue("Inventory", "Armor" + i, Armor[i]);
            }
            file.Save();
        }

        /// <summary>
        /// Total RP needed for a rank. Above 99 this is Online's published formula; below it, a curve fitted to the
        /// first ten Online thresholds that meets the formula near rank 100.
        /// </summary>
        public static int RpForRank(int rank)
        {
            if (rank <= 1) return 0;
            if (rank >= 100) return (int)Math.Min(int.MaxValue, 25L * rank * rank + 23575L * rank - 1023150L);
            int[] first = { 0, 0, 800, 2100, 3800, 6100, 9500, 12500, 16000, 19800, 24000 };
            if (rank <= 10) return first[rank];
            int n = rank - 10;
            return 24000 + n * 4800 + 140 * n * n;
        }

        public static int Rank
        {
            get
            {
                if (cachedRankRp == RP) return cachedRank;
                int r = 1;
                while (r < MaxRank && RpForRank(r + 1) <= RP) r++;
                cachedRank = r;
                cachedRankRp = RP;
                return r;
            }
        }

        public static void AddRP(int amount, string reason)
        {
            if (amount <= 0) return;
            int before = Rank;
            RP = (int)Math.Min(int.MaxValue, (long)RP + amount);
            barGain = (Game.GameTime < barUntil ? barGain : 0) + amount;
            barReason = reason;
            barUntil = Game.GameTime + 4000;
            if (Rank > before)
            {
                shard.Show("SHOW_SHARD_CENTERED_MP_MESSAGE", "~b~RANK UP", "Rank " + Rank, 4000);
                Audio.PlaySoundFrontend("RANK_UP", "HUD_AWARDS");
            }
            Save();
        }

        public static void AddCash(int amount, string reason)
        {
            if (amount == 0) return;
            Game.Player.Money += amount;
            GTA.UI.Notification.Show(string.Format("{0}{1}${2:N0}~s~ {3}", amount > 0 ? "~g~+" : "~r~-", "", Math.Abs(amount), reason));
        }

        public static void RecordKill() { Kills++; }
        public static void RecordDeath() { Deaths++; }

        public static void RecordMatch(bool won)
        {
            MatchesPlayed++;
            if (won) MatchesWon++;
            Save();
        }

        /// <summary>Draws the Online-style RP bar after a gain, and rank-up shards. Call every frame from one script.</summary>
        public static void Tick()
        {
            shard.Tick();
            if (Game.GameTime >= barUntil) return;
            int rank = Rank;
            int lo = RpForRank(rank), hi = RpForRank(rank + 1);
            float fill = hi > lo ? (RP - lo) / (float)(hi - lo) : 1f;
            const float w = 420f, x = 640f - w / 2f, y = 18f;
            Color blue = Color.FromArgb(255, 93, 182, 229);
            Draw.Rect(x - 40f, y - 4f, w + 80f, 22f, Color.FromArgb(150, 0, 0, 0));
            Draw.Rect(x, y + 4f, w, 6f, Color.FromArgb(200, 40, 40, 40));
            Draw.Rect(x, y + 4f, w * Math.Max(0f, Math.Min(1f, fill)), 6f, blue);
            Draw.Text(rank.ToString(), x - 20f, y - 3f, 0.42f, Color.White, GTA.UI.Alignment.Center);
            Draw.Text((rank + 1).ToString(), x + w + 20f, y - 3f, 0.42f, Color.White, GTA.UI.Alignment.Center);
            Draw.Text(string.Format("+{0:N0} RP  {1}", barGain, barReason ?? ""), 640f, y + 14f, 0.34f, blue, GTA.UI.Alignment.Center);
            Draw.Text(string.Format("{0:N0} / {1:N0}", RP, hi), 640f, y - 8f, 0.26f, Color.White, GTA.UI.Alignment.Center);
        }
    }
}
