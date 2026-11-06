using System;
using System.Runtime.CompilerServices;
using UnityEngine;

[assembly: InternalsVisibleTo("NavalCommander.Progression.Tests")]

namespace NavalCommander.Progression
{
    /// <summary>
    /// Progreso del jugador de ESTA instancia del juego, guardado en PlayerPrefs por perfil de autenticación.
    /// PlayerPrefs se comparte entre instancias de la misma PC, por eso cada clave lleva el perfil.
    /// El servidor decide la experiencia (MatchController) y el cliente solo la guarda (PlayerNet).
    /// </summary>
    public static class PlayerProgression
    {
        private const string KeyPrefix = "NavalCommander.Progression.";
        private const string DefaultDisplayName = "Jugador";

        private static string _profileName;
        private static int _lastXpGain;

        public static event Action Changed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _profileName = null;
            _lastXpGain = 0;
            Changed = null;
        }

        /// <summary>Perfil de esta instancia: distinto por Virtual Player o build, y el mismo que usa Authentication.</summary>
        public static string ProfileName => _profileName ??= BuildProfileName();

        public static string DisplayName
        {
            get => PlayerPrefs.GetString(Key("DisplayName"), DefaultDisplayName);
            set
            {
                string cleaned = (value ?? "").Trim();
                PlayerPrefs.SetString(Key("DisplayName"), cleaned.Length > 0 ? cleaned : DefaultDisplayName);
                PlayerPrefs.Save();
                Changed?.Invoke();
            }
        }

        public static int Level => ProgressionRules.LevelForTotalXp(TotalXp);

        /// <summary>Experiencia dentro del nivel actual.</summary>
        public static int Xp => ProgressionRules.XpIntoLevel(TotalXp);

        public static int XpForNextLevel => ProgressionRules.XpToNextLevel(Level);

        /// <summary>Última experiencia ganada en esta ejecución; 0 hasta la primera victoria.</summary>
        public static int LastXpGain => _lastXpGain;

        /// <summary>Valor que usa el filtro de "Buscar partida".</summary>
        public static string MatchBracket => ProgressionRules.BracketForLevel(Level);

        /// <summary>Guarda la experiencia que otorgó el servidor. Se ignora si no es positiva.</summary>
        public static void AddXp(int amount)
        {
            if (amount <= 0) return;

            PlayerPrefs.SetInt(Key("TotalXp"), TotalXp + amount);
            PlayerPrefs.Save();
            _lastXpGain = amount;
            Changed?.Invoke();
        }

        internal static void UseProfileForTests(string profileName)
        {
            _profileName = profileName;
            _lastXpGain = 0;
        }

        private static int TotalXp => Mathf.Max(0, PlayerPrefs.GetInt(Key("TotalXp"), 0));

        private static string Key(string name) => KeyPrefix + ProfileName + "." + name;

        private static string BuildProfileName()
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == "-profile") return args[i + 1];

            unchecked
            {
                uint hash = 2166136261;
                foreach (char c in Application.dataPath) { hash ^= c; hash *= 16777619; }
                return "p" + hash.ToString("x8");
            }
        }
    }
}
