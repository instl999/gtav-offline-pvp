using System;
using System.Collections.Generic;
using GTA;

namespace OfflinePvP
{
    /// <summary>
    /// One participant in a match: the player or an AI bot. Teams are plain ints so the same model
    /// can later back CEO organisations (boss + associates vs rival orgs).
    /// </summary>
    internal sealed class Combatant
    {
        public string Name;
        public int Team;             // FFA: unique per combatant. TDM: 0 = your team, 1 = enemy team.
        public bool IsPlayer;
        public bool IsAlly;          // on the player's team (TDM)
        public Ped Ped;
        public Blip Blip;
        public bool Alive;
        public int RespawnAt;        // Game.GameTime when a dead bot/player respawns
        public int Kills, Deaths, Streak;
        public int Points;           // King of the Hill: seconds held the hill alone
        public bool HeadingToHill;
        public int NextThink;
        public int AppliedConfigVersion = -1;
        public Combatant Target;
        public int TargetPickedAt;
        public bool VisibleToPlayer; // cached line-of-sight for name tags
        public int SpawnProtectUntil;
        public bool PendingRemoval;  // bot count lowered: leave after current life

        public int Score { get { return Kills; } }

        public bool IsHostileTo(Combatant other)
        {
            return other != this && other.Team != Team;
        }
    }

    internal static class BotNames
    {
        static readonly string[] A = { "Dark", "Silent", "Crazy", "Toxic", "Lucky", "Ghost", "Neon", "Rogue", "Savage", "Frosty",
            "Viper", "Shadow", "Turbo", "Mighty", "Lazy", "Atomic", "Rusty", "Wild", "Smooth", "Deadly", "Salty", "Sneaky" };
        static readonly string[] B = { "Wolf", "Sniper", "Gamer", "Ninja", "Tiger", "Reaper", "Falcon", "Panda", "Cobra", "Hunter",
            "Raven", "Bandit", "Outlaw", "Maverick", "Phoenix", "Kraken", "Jester", "Nomad", "Duke", "Rider", "Tryhard", "Camper" };

        public static string Make(Random rng, ICollection<string> taken)
        {
            for (int attempt = 0; attempt < 50; attempt++)
            {
                string a = A[rng.Next(A.Length)], b = B[rng.Next(B.Length)], name;
                switch (rng.Next(5))
                {
                    case 0: name = a + b + rng.Next(10, 99); break;
                    case 1: name = "xX_" + b + "_Xx"; break;
                    case 2: name = a.ToLowerInvariant() + "_" + b.ToLowerInvariant() + rng.Next(0, 9); break;
                    case 3: name = b + rng.Next(1990, 2010); break;
                    default: name = "Its" + a + b; break;
                }
                if (!taken.Contains(name)) return name;
            }
            return "Bot" + rng.Next(1000, 9999);
        }
    }
}
