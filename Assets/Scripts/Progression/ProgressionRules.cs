using System;

namespace NavalCommander.Progression
{
    /// <summary>
    /// Reglas puras de progresión (sin Unity ni red): experiencia por partida, niveles y rangos de emparejamiento.
    /// </summary>
    public static class ProgressionRules
    {
        public const int MinWinXp = 80;
        public const int MaxWinXp = 120;
        public const int XpPerLevelStep = 100;
        public const int BracketSize = 5;

        /// <summary>Experiencia necesaria para pasar del nivel indicado al siguiente.</summary>
        public static int XpToNextLevel(int level) => XpPerLevelStep * level;

        public static int LevelForTotalXp(int totalXp) => SplitTotalXp(totalXp, out _);

        /// <summary>Experiencia acumulada dentro del nivel que corresponde a ese total.</summary>
        public static int XpIntoLevel(int totalXp)
        {
            SplitTotalXp(totalXp, out int xpIntoLevel);
            return xpIntoLevel;
        }

        private static int SplitTotalXp(int totalXp, out int xpIntoLevel)
        {
            int level = 1;
            xpIntoLevel = totalXp;
            while (xpIntoLevel >= XpToNextLevel(level))
            {
                xpIntoLevel -= XpToNextLevel(level);
                level++;
            }
            return level;
        }

        /// <summary>Rango de emparejamiento: con BracketSize 5 da "1-5", "6-10", etc.</summary>
        public static string BracketForLevel(int level)
        {
            int low = (level - 1) / BracketSize * BracketSize + 1;
            return $"{low}-{low + BracketSize - 1}";
        }

        /// <summary>Experiencia de un jugador al terminar la partida: solo gana el ganador; sin ganador (-1) nadie gana.</summary>
        public static int RollMatchXp(int playerSlot, int winnerSlot, Random rng)
        {
            if (winnerSlot < 0 || playerSlot != winnerSlot) return 0;
            return rng.Next(MinWinXp, MaxWinXp + 1);
        }
    }
}
