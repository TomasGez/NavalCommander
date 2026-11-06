using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NavalCommander.Progression;
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
        private const string BracketPropertyKey = "matchBracket";
        private const float RivalCheckMinSeconds = 5f;
        private const float RivalCheckMaxSeconds = 8f;
        private const float ClientConnectTimeoutSeconds = 15f;
        private const int LeaveTimeoutMilliseconds = 5000;
        private const float ForgetFailedRoomsSeconds = 60f;

        private static SessionService _instance;
        private static bool _quitting;

        private readonly List<string> _previousSessionIds = new List<string>();

        // Salas a las que ya falló la mudanza en esta búsqueda: no se reintentan.
        private readonly HashSet<string> _failedRoomIds = new HashSet<string>();
        private float _forgetFailedRoomsTime;
        private bool _lookingForRival;
        private bool _checkingForRival;
        private float _nextRivalCheck;
        // Tras entrar como cliente por búsqueda: si la conexión no llega a establecerse, se vuelve a buscar.
        private bool _awaitingClientConnection;
        private float _clientConnectDeadline;
        private int _searchMaxPlayers;

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

        private void Update()
        {
            if (_awaitingClientConnection) CheckClientConnection();
            if (!_lookingForRival || IsBusy || _checkingForRival) return;

            if (Current == null || !Current.IsHost || Current.PlayerCount > 1)
            {
                _lookingForRival = false;
                return;
            }

            if (Time.unscaledTime < _nextRivalCheck) return;
            _nextRivalCheck = Time.unscaledTime + UnityEngine.Random.Range(RivalCheckMinSeconds, RivalCheckMaxSeconds);
            _ = JoinLowerRoomIfAloneAsync();
        }

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
                        new FilterOption(FilterField.AvailableSlots, "1", FilterOperation.GreaterOrEqual),
                        new FilterOption(FilterField.StringIndex1, PlayerProgression.MatchBracket, FilterOperation.Equal)
                    },
                    Timeout = TimeSpan.FromSeconds(QuickJoinTimeoutSeconds),
                    CreateSession = true
                };
                Attach(await MultiplayerService.Instance.MatchmakeSessionAsync(quickJoin, BuildSessionOptions(maxPlayers)));
                Status = Current.IsHost
                    ? $"No había partidas: se creó una. Código: {Current.Code}"
                    : $"Unido a la partida {Current.Code}";

                // Dos búsquedas simultáneas crean una sala cada una: el host solo sigue buscando a otros.
                _lookingForRival = Current.IsHost;
                _awaitingClientConnection = !Current.IsHost;
                _clientConnectDeadline = Time.unscaledTime + ClientConnectTimeoutSeconds;
                _searchMaxPlayers = maxPlayers;
                _nextRivalCheck = Time.unscaledTime + UnityEngine.Random.Range(RivalCheckMinSeconds, RivalCheckMaxSeconds);
            });
        }

        // Si otro host de este rango espera en una sala con id menor, se cierra la propia y se entra a la suya.
        private async Task JoinLowerRoomIfAloneAsync()
        {
            ISession own = Current;
            _checkingForRival = true;
            string target = null;
            try
            {
                var results = await MultiplayerService.Instance.QuerySessionsAsync(new QuerySessionsOptions
                {
                    FilterOptions = new List<FilterOption>
                    {
                        new FilterOption(FilterField.AvailableSlots, "1", FilterOperation.GreaterOrEqual),
                        new FilterOption(FilterField.StringIndex1, PlayerProgression.MatchBracket, FilterOperation.Equal)
                    }
                });

                var roomIds = new List<string>();
                foreach (ISessionInfo info in results.Sessions) roomIds.Add(info.Id);
                if (Time.unscaledTime >= _forgetFailedRoomsTime) _failedRoomIds.Clear();
                target = MatchmakingRules.RoomToJoin(own.Id, roomIds, _failedRoomIds);
                Debug.Log($"[Sesión] Salas visibles: {string.Join(", ", roomIds)}. Propia: {own.Id}. Destino: {target ?? "ninguno"}.");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Sesión] No se pudo consultar otras partidas: {e.Message}");
            }
            finally
            {
                _checkingForRival = false;
            }

            // Durante la consulta pudo entrar un rival o el jugador pudo salir: entonces no se mueve.
            if (target == null || !_lookingForRival || IsBusy || Current != own || own.PlayerCount > 1) return;

            _lookingForRival = false;
            _searchMaxPlayers = own.MaxPlayers;
            IsBusy = true;
            bool joined = false;
            try
            {
                await LeaveAsync();
                Status = "Uniéndose a una partida encontrada...";
                Debug.Log($"[Sesión] Mudanza: de la sala {own.Id} a {target}.");
                Attach(await MultiplayerService.Instance.JoinSessionByIdAsync(target));
                _awaitingClientConnection = true;
                _clientConnectDeadline = Time.unscaledTime + ClientConnectTimeoutSeconds;
                joined = true;
                Status = $"Unido a la partida {Current.Code}";
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Sesión] No se pudo entrar a la sala {target}: {e.Message}");
                MarkRoomFailed(target);
                Current = null;
            }
            finally
            {
                IsBusy = false;
            }

            if (!joined) await CreateSearchRoomAsync(_searchMaxPlayers);
        }

        // Una sala fantasma acepta la unión pero nadie responde: Netcode se apaga sin llegar a conectar.
        private void CheckClientConnection()
        {
            var manager = NetworkManager.Singleton;
            if (Current == null) { _awaitingClientConnection = false; return; }
            if (IsBusy || manager == null) return;

            // Quedar de host sin haber conectado con nadie significa que el Lobby migró una sala sin host.
            bool inheritedDeadRoom = Current.IsHost;
            if (!inheritedDeadRoom)
            {
                if (manager.IsConnectedClient) { _awaitingClientConnection = false; return; }
                // Justo después de unirse, Netcode todavía no escucha: solo el plazo decide que la sala no responde.
                if (Time.unscaledTime < _clientConnectDeadline) return;
            }

            _awaitingClientConnection = false;
            MarkRoomFailed(Current.Id);
            Debug.LogWarning($"[Sesión] La sala {Current.Id} no respondió: se busca de nuevo.");
            _ = RetrySearchAsync();
        }

        // El olvido evita que una sala viva, marcada por un fallo pasajero, bloquee a dos jugadores para siempre.
        private void MarkRoomFailed(string roomId)
        {
            _failedRoomIds.Add(roomId);
            _forgetFailedRoomsTime = Time.unscaledTime + ForgetFailedRoomsSeconds;
        }

        private async Task RetrySearchAsync()
        {
            await LeaveAsync();
            await CreateSearchRoomAsync(_searchMaxPlayers);
        }

        // Quick Join no permite excluir salas y devolvería la misma sala muerta: se crea una propia
        // y se sigue buscando rivales desde ella, ignorando las salas que ya fallaron.
        private async Task CreateSearchRoomAsync(int maxPlayers)
        {
            await RunAsync("Buscando partida...", async () =>
            {
                Attach(await MultiplayerService.Instance.CreateSessionAsync(BuildSessionOptions(maxPlayers)));
                Status = $"Esperando rivales. Código: {Current.Code}";
                _lookingForRival = true;
                _nextRivalCheck = Time.unscaledTime + UnityEngine.Random.Range(RivalCheckMinSeconds, RivalCheckMaxSeconds);
            });
        }

        /// <summary>Sale de la sesión (el host la elimina). El SDK cierra Relay y Netcode.</summary>
        public async Task LeaveAsync()
        {
            _lookingForRival = false;
            _awaitingClientConnection = false;
            ISession session = Current;
            Detach(session);
            Current = null;

            try
            {
                if (session != null)
                {
                    if (session.IsHost) await session.AsHost().DeleteAsync();
                    else
                    {
                        // En una sala sin host esta llamada puede no terminar nunca: no debe trabar la salida.
                        Task leave = session.LeaveAsync();
                        if (await Task.WhenAny(leave, Task.Delay(LeaveTimeoutMilliseconds)) == leave) await leave;
                        else Debug.LogWarning("[Sesión] Al salir: el servidor no respondió a tiempo.");
                    }
                }
            }
            catch (Exception e)
            {
                // Es normal si la sesión ya no existía (por ejemplo, el host la cerró antes).
                Debug.LogWarning($"[Sesión] Al salir: {e.Message}");
            }

            var manager = NetworkManager.Singleton;
            if (manager != null && manager.IsListening) manager.Shutdown(); // por si el SDK no lo cerró
            // El apagado termina en un frame posterior: una unión inmediata fallaría si no se espera.
            while (manager != null && manager.ShutdownInProgress) await Task.Yield();
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
            _lookingForRival = false;
            _awaitingClientConnection = false;
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
                IsPrivate = false, // pública: aparece en la búsqueda
                // Rango del creador, indexado para que "Buscar partida" solo encuentre partidas de su rango.
                SessionProperties = new Dictionary<string, SessionProperty>
                {
                    [BracketPropertyKey] = new SessionProperty(PlayerProgression.MatchBracket, VisibilityPropertyOptions.Public, PropertyIndex.String1)
                }
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
                AuthenticationService.Instance.SwitchProfile(PlayerProgression.ProfileName);
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
                Debug.Log($"[Sesión] Autenticado. PlayerID: {AuthenticationService.Instance.PlayerId}");
            }
        }
    }
}
