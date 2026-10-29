using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Multiplayer;
using UnityEngine;

namespace NavalCommander.Networking
{
    /// <summary>
    /// RUTA PRINCIPAL de conexión (topología cliente-host sobre Relay). Corre en todas las instancias,
    /// invocado desde LobbyView; quien crea la sesión es el host y por tanto el servidor autoritativo.
    /// La ruta alternativa de desarrollo por IP directa es NetworkLauncher.
    /// Responsabilidad única: hablar con los servicios de Unity (Multiplayer Services SDK).
    ///  - Authentication: identidad anónima del jugador.
    ///  - Lobby: crear, buscar (Quick Join) y unirse a partidas por código.
    ///  - Relay: conexión por internet sin abrir puertos (lo configura la sesión con WithRelayNetwork).
    /// Al crear o unirse a una sesión, el SDK inicia solo Netcode (host o cliente).
    /// Se crea solo la primera vez que se usa y sobrevive al cambio de escena.
    /// </summary>
    public class SessionService : MonoBehaviour
    {
        private const string SessionName = "Strategy Naval";
        private const double QuickJoinTimeoutSeconds = 5;

        private static SessionService _instance;
        private static bool _quitting;

        private readonly List<string> _previousSessionIds = new List<string>();

        /// <summary>Instancia única; se crea sola si todavía no existe.</summary>
        public static SessionService Instance
        {
            get
            {
                if (_instance == null && !_quitting)
                    _instance = new GameObject("SessionService").AddComponent<SessionService>();
                return _instance;
            }
        }

        /// <summary>Instancia si ya existe (no la crea). Útil para consultar sin efectos.</summary>
        public static SessionService ExistingInstance => _instance;

        public ISession Current { get; private set; }
        public bool IsBusy { get; private set; }
        public string Status { get; private set; } = "Servicios sin iniciar";
        public string JoinCode => Current != null ? Current.Code : "";

        /// <summary>Cantidad de partidas anteriores en las que el servidor todavía te tiene registrado.</summary>
        public int PreviousSessionCount => _previousSessionIds.Count;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() { _instance = null; _quitting = false; }

        private void Awake()
        {
            if (_instance != null && _instance != this) { Destroy(gameObject); return; }
            _instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void OnApplicationQuit() { _quitting = true; }

        // ---------------- Operaciones ----------------

        /// <summary>Crea una sesión pública con Relay para "maxPlayers" jugadores; quien la crea es el host.</summary>
        public async Task CreateSessionAsync(int maxPlayers)
        {
            await RunAsync("Creando partida...", async () =>
            {
                Attach(await MultiplayerService.Instance.CreateSessionAsync(BuildSessionOptions(maxPlayers)));
                Status = $"Partida creada. Código: {Current.Code}";
            });
        }

        /// <summary>Se une a una partida usando el código que comparte el host.</summary>
        public async Task JoinByCodeAsync(string code)
        {
            string cleaned = (code ?? "").Trim().ToUpperInvariant();
            if (cleaned.Length == 0) { Status = "Escribí un código de partida"; return; }

            await RunAsync("Uniéndose a la partida...", async () =>
            {
                Attach(await MultiplayerService.Instance.JoinSessionByCodeAsync(cleaned));
                Status = $"Unido a la partida {Current.Code}";
            });
        }

        /// <summary>"Buscar partida": entra a una con lugar libre o, si no hay, crea una nueva para "maxPlayers" jugadores.</summary>
        public async Task QuickJoinAsync(int maxPlayers)
        {
            await RunAsync("Buscando partida...", async () =>
            {
                var quickJoin = new QuickJoinOptions
                {
                    Filters = new List<FilterOption>
                    {
                        new FilterOption(FilterField.AvailableSlots, "1", FilterOperation.GreaterOrEqual)
                    },
                    Timeout = TimeSpan.FromSeconds(QuickJoinTimeoutSeconds),
                    CreateSession = true
                };
                Attach(await MultiplayerService.Instance.MatchmakeSessionAsync(quickJoin, BuildSessionOptions(maxPlayers)));
                Status = Current.IsHost
                    ? $"No había partidas: se creó una. Código: {Current.Code}"
                    : $"Unido a la partida {Current.Code}";
            });
        }

        /// <summary>Sale de la sesión (el host la elimina). El SDK cierra Relay y Netcode.</summary>
        public async Task LeaveAsync()
        {
            ISession session = Current;
            Detach(session);
            Current = null;

            try
            {
                if (session != null)
                {
                    if (session.IsHost) await session.AsHost().DeleteAsync();
                    else await session.LeaveAsync();
                }
            }
            catch (Exception e)
            {
                // Es normal si la sesión ya no existía (por ejemplo, el host la cerró antes).
                Debug.LogWarning($"[Sesión] Al salir: {e.Message}");
            }

            var manager = NetworkManager.Singleton;
            if (manager != null && manager.IsListening) manager.Shutdown(); // por si el SDK no lo cerró
            Status = "Saliste de la partida";
        }

        /// <summary>
        /// Reparación manual: si el servidor todavía te tiene registrado en partidas viejas
        /// (por ejemplo porque cortaste el juego sin salir), te reconecta a cada una y sale correctamente.
        /// </summary>
        public async Task CleanUpPreviousSessionsAsync()
        {
            if (IsBusy || Current != null) return;

            IsBusy = true;
            try
            {
                Status = "Limpiando partidas anteriores...";
                await EnsureSignedInAsync();
                await RefreshPreviousSessionsAsync();

                foreach (string sessionId in _previousSessionIds.ToArray())
                {
                    try
                    {
                        ISession old = await MultiplayerService.Instance.ReconnectToSessionAsync(sessionId, new ReconnectSessionOptions());
                        if (old.IsHost) await old.AsHost().DeleteAsync();
                        else await old.LeaveAsync();
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning($"[Sesión] No se pudo limpiar {sessionId}: {e.Message}");
                    }
                }

                var manager = NetworkManager.Singleton;
                if (manager != null && manager.IsListening) manager.Shutdown();

                await RefreshPreviousSessionsAsync();
                Status = _previousSessionIds.Count == 0
                    ? "Listo: no quedan partidas anteriores"
                    : $"Quedan {_previousSessionIds.Count} partidas anteriores (se cierran solas en unos minutos)";
            }
            catch (Exception e)
            {
                Status = "No se pudo limpiar: " + e.Message;
                Debug.LogError($"[Sesión] {e}");
            }
            finally
            {
                IsBusy = false;
            }
        }

        // ---------------- Estado de la sesión actual ----------------

        private void Attach(ISession session)
        {
            Current = session;
            session.RemovedFromSession += HandleRemovedFromSession;
        }

        private void Detach(ISession session)
        {
            if (session != null) session.RemovedFromSession -= HandleRemovedFromSession;
        }

        // Se dispara cuando el host cierra la partida o te saca. Sin esto, "Current" quedaba colgada
        // y el Lobby creía que seguías adentro ("Ya estás en una partida").
        private void HandleRemovedFromSession()
        {
            Debug.Log("[Sesión] Saliste de la partida (el host la cerró o te sacó).");
            Detach(Current);
            Current = null;
            Status = "Saliste de la partida (el host la cerró o te sacó)";
        }

        // ---------------- Internos ----------------

        private static SessionOptions BuildSessionOptions(int maxPlayers)
        {
            return new SessionOptions
            {
                Name = SessionName,
                MaxPlayers = maxPlayers,
                IsPrivate = false // pública: aparece en la búsqueda
            }.WithRelayNetwork();
        }

        /// <summary>Valida, inicia sesión en los servicios y ejecuta la operación, mostrando el estado.</summary>
        private async Task RunAsync(string busyMessage, Func<Task> operation)
        {
            if (IsBusy) return;

            string configError = NetworkLauncher.FindConfigurationError(NetworkManager.Singleton);
            if (configError != null) { Status = "ERROR: " + configError; return; }

            IsBusy = true;
            try
            {
                if (Current != null)
                {
                    var manager = NetworkManager.Singleton;
                    if (manager != null && manager.IsListening) { Status = "Ya estás en una partida"; return; }

                    // La conexión ya no existe pero la sesión quedó registrada: se cierra antes de seguir.
                    Status = "Cerrando la partida anterior...";
                    await LeaveAsync();
                }

                Status = "Iniciando sesión en los servicios de Unity...";
                await EnsureSignedInAsync();
                await RefreshPreviousSessionsAsync();

                Status = busyMessage;
                await operation();
            }
            catch (Exception e)
            {
                Current = null;
                Status = "No se pudo completar la operación: " + e.Message;
                Debug.LogError($"[Sesión] {e}");
                await RefreshPreviousSessionsAsync(); // por si el motivo es que seguís registrado en otra partida
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task RefreshPreviousSessionsAsync()
        {
            _previousSessionIds.Clear();
            try
            {
                var ids = await MultiplayerService.Instance.GetJoinedSessionIdsAsync();
                if (ids != null) _previousSessionIds.AddRange(ids);
                if (_previousSessionIds.Count > 0)
                    Debug.Log($"[Sesión] El servidor te tiene registrado en {_previousSessionIds.Count} partida(s) anterior(es).");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Sesión] No se pudo consultar las partidas anteriores: {e.Message}");
            }
        }

        private static async Task EnsureSignedInAsync()
        {
            if (UnityServices.State == ServicesInitializationState.Uninitialized)
                await UnityServices.InitializeAsync();

            if (!AuthenticationService.Instance.IsSignedIn)
            {
                // Un perfil distinto por instancia: así los Virtual Players de Multiplayer Play Mode
                // (o dos builds en la misma PC) no comparten el mismo jugador.
                AuthenticationService.Instance.SwitchProfile(BuildProfileName());
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
                Debug.Log($"[Sesión] Autenticado. PlayerID: {AuthenticationService.Instance.PlayerId}");
            }
        }

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
