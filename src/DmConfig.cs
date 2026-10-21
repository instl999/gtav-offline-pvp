using System;
using GTA;
using GTA.Math;
using Keys = System.Windows.Forms.Keys;

namespace OfflinePvP
{
    internal enum MatchMode
    {
        FreeForAll = 0,
        TeamDeathmatch = 1
    }

    internal sealed class ArenaPreset
    {
        public readonly string Name;
        public readonly Vector3 Center; // Z is a hint only; we snap to ground after streaming in.

        public ArenaPreset(string name, float x, float y, float z)
        {
            Name = name;
            Center = new Vector3(x, y, z);
        }
    }

    internal sealed class Loadout
    {
        public readonly string Name;
        public readonly WeaponHash[] Weapons;

        public Loadout(string name, params WeaponHash[] weapons)
        {
            Name = name;
            Weapons = weapons;
        }
    }

    internal sealed class DifficultyProfile
    {
        public readonly string Name;
        public readonly int CombatAbility;   // 0 poor, 1 average, 2 professional
        public readonly int CombatMovement;  // 0 stationary, 1 defensive, 2 will advance, 3 suicidal
        public readonly int ShootRate;
        public readonly int ExtraHealth;     // health above the 100 "death" threshold
        public readonly int Armor;
        public readonly bool UseCover;
        public readonly int DefaultAccuracy;

        public DifficultyProfile(string name, int ability, int movement, int shootRate, int extraHealth, int armor, bool cover, int accuracy)
        {
            Name = name;
            CombatAbility = ability;
            CombatMovement = movement;
            ShootRate = shootRate;
            ExtraHealth = extraHealth;
            Armor = armor;
            UseCover = cover;
            DefaultAccuracy = accuracy;
        }
    }

    /// <summary>All user-tunable settings. Everything marked "live" is re-applied to bots mid-match.</summary>
    internal sealed class DmConfig
    {
        public static readonly ArenaPreset[] Arenas =
        {
            new ArenaPreset("Current location", 0f, 0f, 0f),
            new ArenaPreset("LS Docks (Elysian Island)", 1010f, -3105f, 6f),
            new ArenaPreset("Sandy Shores Airfield", 1718f, 3254f, 41f),
            new ArenaPreset("Paleto Bay Sawmill", -570f, 5300f, 70f),
            new ArenaPreset("O'Neil Farm (Grapeseed)", 2440f, 4975f, 46f),
            new ArenaPreset("Vespucci Beach", -1450f, -1250f, 3f),
            new ArenaPreset("Del Perro Pier", -1640f, -1080f, 13f),
            new ArenaPreset("Rancho Projects", 335f, -2040f, 21f),
            new ArenaPreset("Grove Street", -110f, -1600f, 31f),
            new ArenaPreset("Murrieta Oil Fields", 1500f, -2150f, 77f),
        };

        public static readonly Loadout[] Loadouts =
        {
            new Loadout("Mixed (Online style)", WeaponHash.Pistol50, WeaponHash.CombatPistol, WeaponHash.SMG, WeaponHash.AssaultSMG,
                WeaponHash.CarbineRifle, WeaponHash.AssaultRifle, WeaponHash.SpecialCarbine, WeaponHash.PumpShotgun, WeaponHash.CombatMG),
            new Loadout("Pistols", WeaponHash.Pistol, WeaponHash.CombatPistol, WeaponHash.Pistol50, WeaponHash.HeavyPistol),
            new Loadout("SMGs", WeaponHash.MicroSMG, WeaponHash.SMG, WeaponHash.AssaultSMG, WeaponHash.CombatPDW),
            new Loadout("Rifles", WeaponHash.AssaultRifle, WeaponHash.CarbineRifle, WeaponHash.AdvancedRifle, WeaponHash.SpecialCarbine, WeaponHash.BullpupRifle),
            new Loadout("Shotguns", WeaponHash.PumpShotgun, WeaponHash.AssaultShotgun, WeaponHash.SawnOffShotgun, WeaponHash.HeavyShotgun),
            new Loadout("Snipers", WeaponHash.SniperRifle, WeaponHash.HeavySniper, WeaponHash.MarksmanRifle),
            new Loadout("Explosives (chaos)", WeaponHash.RPG, WeaponHash.GrenadeLauncher, WeaponHash.CarbineRifle),
            new Loadout("Melee only", WeaponHash.Knife, WeaponHash.Bat, WeaponHash.Machete),
        };

        public static readonly DifficultyProfile[] Difficulties =
        {
            new DifficultyProfile("Easy",   0, 1,  50, 100,   0, false, 15),
            new DifficultyProfile("Normal", 1, 2,  90, 150,   0, true,  30),
            new DifficultyProfile("Hard",   2, 2, 130, 200,  50, true,  45),
            new DifficultyProfile("Elite",  2, 2, 180, 250, 100, true,  65),
        };

        // Index 0 of both lists means "leave the world as it is".
        public static readonly string[] TimeNames = { "Current", "Morning", "Noon", "Afternoon", "Evening", "Night" };
        public static readonly int[] TimeHours = { -1, 8, 12, 16, 19, 0 };
        public static readonly string[] WeatherNames = { "Current", "Extra sunny", "Clear", "Cloudy", "Smog", "Foggy", "Overcast",
            "Rain", "Thunder", "Clearing", "Light snow", "Snow (Xmas)", "Halloween" };
        public static readonly string[] WeatherIds = { null, "EXTRASUNNY", "CLEAR", "CLOUDS", "SMOG", "FOGGY", "OVERCAST",
            "RAIN", "THUNDER", "CLEARING", "SNOWLIGHT", "XMAS", "HALLOWEEN" };

        public MatchMode Mode = MatchMode.FreeForAll;
        public int Arena = 0;
        public int ArenaRadius = 90;
        public int EnemyCount = 8;       // live
        public int AllyCount = 3;        // live (TDM)
        public int Difficulty = 1;       // live
        public int Accuracy = 30;        // live
        public int PlayerBias = 35;      // live: % chance an enemy bot hunts you instead of the nearest target
        public int Loadout = 0;          // live (applies on next respawn)
        public int KillLimit = 30;       // live, 0 = none
        public int TimeLimit = 10;       // live, minutes, 0 = none
        public int RespawnDelay = 3;     // live, seconds
        public bool Blips = true;        // live
        public bool NameTags = true;     // live
        public bool ClearAmbient = true; // live
        public bool PlayerLoadout = true;
        public bool ReturnAfterMatch = true;
        public bool Pickups = true;      // live
        public int PickupRespawn = 30;   // live, seconds
        public bool SuicidePenalty = true; // live: -1 kill for dying by your own hand or the world, like Online
        public int TimeOfDay = 0;        // live, index into TimeNames
        public int Weather = 0;          // live, index into WeatherNames
        public bool HealthBars = true;   // live
        public Keys MenuKey = Keys.F7;
        public Keys ScoreboardKey = Keys.Z;

        public DifficultyProfile DifficultyProfile { get { return Difficulties[Difficulty]; } }

        public void Load(ScriptSettings s)
        {
            Mode = (MatchMode)Clamp(s.GetValue("Match", "Mode", (int)Mode), 0, 1);
            Arena = Clamp(s.GetValue("Match", "Arena", Arena), 0, Arenas.Length - 1);
            ArenaRadius = Clamp(s.GetValue("Match", "ArenaRadius", ArenaRadius), 40, 250);
            KillLimit = Clamp(s.GetValue("Match", "KillLimit", KillLimit), 0, 500);
            TimeLimit = Clamp(s.GetValue("Match", "TimeLimitMinutes", TimeLimit), 0, 60);
            RespawnDelay = Clamp(s.GetValue("Match", "RespawnDelaySeconds", RespawnDelay), 1, 15);
            Loadout = Clamp(s.GetValue("Match", "Loadout", Loadout), 0, Loadouts.Length - 1);
            PlayerLoadout = s.GetValue("Match", "GivePlayerLoadout", PlayerLoadout);
            ReturnAfterMatch = s.GetValue("Match", "ReturnAfterMatch", ReturnAfterMatch);
            ClearAmbient = s.GetValue("Match", "ClearAmbient", ClearAmbient);
            Pickups = s.GetValue("Match", "Pickups", Pickups);
            PickupRespawn = Clamp(s.GetValue("Match", "PickupRespawnSeconds", PickupRespawn), 5, 120);
            SuicidePenalty = s.GetValue("Match", "SuicidePenalty", SuicidePenalty);
            TimeOfDay = Clamp(s.GetValue("Match", "TimeOfDay", TimeOfDay), 0, TimeNames.Length - 1);
            Weather = Clamp(s.GetValue("Match", "Weather", Weather), 0, WeatherNames.Length - 1);

            EnemyCount = Clamp(s.GetValue("AI", "EnemyCount", EnemyCount), 1, 40);
            AllyCount = Clamp(s.GetValue("AI", "AllyCount", AllyCount), 0, 20);
            Difficulty = Clamp(s.GetValue("AI", "Difficulty", Difficulty), 0, Difficulties.Length - 1);
            Accuracy = Clamp(s.GetValue("AI", "Accuracy", Accuracy), 0, 100);
            PlayerBias = Clamp(s.GetValue("AI", "PlayerBias", PlayerBias), 0, 100);

            Blips = s.GetValue("HUD", "Blips", Blips);
            NameTags = s.GetValue("HUD", "NameTags", NameTags);
            HealthBars = s.GetValue("HUD", "HealthBars", HealthBars);

            MenuKey = ParseKey(s.GetValue("Keys", "Menu", MenuKey.ToString()), MenuKey);
            ScoreboardKey = ParseKey(s.GetValue("Keys", "Scoreboard", ScoreboardKey.ToString()), ScoreboardKey);
        }

        public void Save(ScriptSettings s)
        {
            s.SetValue("Match", "Mode", (int)Mode);
            s.SetValue("Match", "Arena", Arena);
            s.SetValue("Match", "ArenaRadius", ArenaRadius);
            s.SetValue("Match", "KillLimit", KillLimit);
            s.SetValue("Match", "TimeLimitMinutes", TimeLimit);
            s.SetValue("Match", "RespawnDelaySeconds", RespawnDelay);
            s.SetValue("Match", "Loadout", Loadout);
            s.SetValue("Match", "GivePlayerLoadout", PlayerLoadout);
            s.SetValue("Match", "ReturnAfterMatch", ReturnAfterMatch);
            s.SetValue("Match", "ClearAmbient", ClearAmbient);
            s.SetValue("Match", "Pickups", Pickups);
            s.SetValue("Match", "PickupRespawnSeconds", PickupRespawn);
            s.SetValue("Match", "SuicidePenalty", SuicidePenalty);
            s.SetValue("Match", "TimeOfDay", TimeOfDay);
            s.SetValue("Match", "Weather", Weather);
            s.SetValue("AI", "EnemyCount", EnemyCount);
            s.SetValue("AI", "AllyCount", AllyCount);
            s.SetValue("AI", "Difficulty", Difficulty);
            s.SetValue("AI", "Accuracy", Accuracy);
            s.SetValue("AI", "PlayerBias", PlayerBias);
            s.SetValue("HUD", "Blips", Blips);
            s.SetValue("HUD", "NameTags", NameTags);
            s.SetValue("HUD", "HealthBars", HealthBars);
            s.SetValue("Keys", "Menu", MenuKey.ToString());
            s.SetValue("Keys", "Scoreboard", ScoreboardKey.ToString());
            s.Save();
        }

        public static int Clamp(int v, int min, int max)
        {
            return v < min ? min : (v > max ? max : v);
        }

        static Keys ParseKey(string text, Keys fallback)
        {
            Keys k;
            return Enum.TryParse(text, true, out k) ? k : fallback;
        }
    }
}
