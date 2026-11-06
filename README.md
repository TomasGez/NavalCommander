# Naval Commander (Strategy Naval)

Juego de cartas naval por turnos para 2 a 4 jugadores. Cada turno todos eligen en secreto hasta 2 cartas
(defensa, movimiento, ataque), el servidor las resuelve en orden Defensa -> Movimiento -> Ataque y gana el
último barco con vida. Unity 6 (`6000.3.17f1`), Netcode for GameObjects 2.13.3, Unity Multiplayer Services 2.4.1
(Authentication, Lobby/Sessions y Relay).

## Topología

- **Cliente-host con servidor autoritativo sobre Unity Relay** (ruta principal, `SessionService`): quien crea la
  sesión es el host y ejecuta toda la lógica; los demás son clientes. Relay evita abrir puertos y las partidas
  se encuentran por código o por "Buscar partida".
- **Ruta de desarrollo por IP directa** (`NetworkLauncher`, UnityTransport sin servicios): Host, Client o
  Servidor dedicado. El build `UNITY_SERVER` arranca solo y lee `-port` y `-players`.
- Por qué encaja: 2-4 jugadores y turnos de 30 s no necesitan servidores dedicados ni predicción de cliente;
  el host autoritativo es barato, y como el cliente solo envía intenciones, no puede hacer trampa sobre el estado.
- Ambas rutas ejecutan el mismo código de partida (`MatchController` solo actúa si `IsServer`).

## Servicios de Unity

| Servicio | Para qué |
|---|---|
| Authentication | Identidad anónima, un perfil por instancia (`-profile` o hash de la ruta) |
| Lobby / Sessions | Crear, buscar (Quick Join) y unirse por código; limita `MaxPlayers` |
| Relay | Transporte por internet; lo configura la sesión con `WithRelayNetwork()` |

## Capas (dependencias hacia abajo)

```
Presentation  -> Networking -> Gameplay -> CardMechanics
```
- `Presentation/`: vistas (`BoardView`, `ShipView`, `TurnEffectsView`, `MatchHud`). Solo leen estado replicado y eventos.
- `Networking/`: `Connection` (`LobbyFlow`, `LobbyView`, `NetworkLauncher`, `ConnectionGate`), `Services` (`SessionService`),
  `Match` (`MatchController`, `PlayerNet`), `Shared` (DTO, `MatchPhase`, `MatchEvents`, constantes en `SessionSettings`).
- `Gameplay/`: `MatchRules` y `TurnTracer`, reglas sin Netcode (se pueden probar solas).
- `CardMechanics/`: cartas, mazo y `CardEffectResolver`.
- `Progression/`: experiencia, nivel y rango de emparejamiento, sin Netcode. La usan `Presentation` y `Networking`.

## Scripts por grupo

**Conexión y sala** (`Networking/Connection`, `Networking/Services`)
- `SessionService`: envuelve Authentication, Lobby/Sessions y Relay; crea, busca y une sesiones.
- `LobbyView`: pantalla de desarrollo (crear, unirse por código, IP directa); se abre con "Modo desarrollo" en el menú.
- `LobbyFlow`: en el servidor, cuenta atrás y carga de `GameScene` cuando hay jugadores suficientes.
- `ConnectionGate`: aprueba o rechaza conexiones (sala llena o partida ya empezada).
- `NetworkLauncher`: ruta de desarrollo por IP directa (Host, Client, servidor dedicado).

**Partida en red** (`Networking/Match`)
- `MatchController`: autoridad de la partida; fases, temporizador, jugadas recibidas y resolución del turno.
- `PlayerNet`: un objeto por jugador; asigna el `Slot`, recibe su mano privada y envía sus jugadas.

**Datos compartidos** (`Networking/Shared`)
- `PlayDto`, `ResolutionDto`, `HitDto`, `ShipSnapshot`: estructuras serializables que viajan entre servidor y clientes.
- `MatchPhase`, `SessionSettings`: fases de la partida y constantes de la sala.
- `MatchEvents`: eventos locales que desacoplan la interfaz de los mensajes de red.

**Reglas de juego sin red** (`Gameplay`)
- `MatchRules`: reparte manos, valida y arma los comandos, resuelve turnos y decide el ganador.
- `TurnTracer`: reconstruye el detalle de cada carta (celdas objetivo, impactos, daño) para poder animarlo, porque el resolver solo devuelve el resultado final.

**Interfaz y feedback visual** (`Presentation`)
- `MainMenuView`: menú inicial (nombre, nivel, "Buscar partida", búsqueda y sala de espera). Durante la búsqueda el botón queda visible y desactivado, y la sala de espera aparece recién cuando esa búsqueda termina. `MenuNetworkManagerGuard` evita un segundo `NetworkManager` al volver de la partida.
- `MatchHud`: fase, temporizador, mano, botón de confirmar y mensajes de fin de partida o error. Conserva el resultado y la XP si la conexión se cierra después del final, y se reduce en ventanas menores a 1280x720.
- `MatchHudStyles`: arma una sola vez los estilos IMGUI del HUD con las texturas de `Assets/Resources/HudArt`; si falta una, ese grupo conserva el aspecto por defecto.
- `HudScale`, `HudPanelLayout`, `HudArt`, `BoardPresence`, `MenuScreenState`, `MatchResultMemo` (`Presentation/Rules`): reglas puras de estas vistas, en un assembly propio para poder probarlas.
- `CardHandGui`, `CardArtCatalog`: dibujan la mano con las imágenes de `Assets/Cards`; el catálogo asocia cada `cardId` con su sprite.
- `BoardView`, `BoardLayout`, `ShipView`: dibujan el tablero y los barcos desde el estado replicado. `BoardView` también muestra `Map_ReefWall` solo mientras el tablero está dibujado.
- `TurnEffectsView`: muestra el feedback visual de cada turno (proyectiles, impactos, daño flotante, escudos, espejos, movimientos y hundimientos).
- `Primitives2D`: formas 2D simples que usan las vistas, sin prefabs.

**Cartas y mapas** (`CardMechanics`, `Maps`; zona compartida, no se modifican desde la capa de red)
- `CardDefinition`, `PlayerCardDeck`, `CardEffectResolver`: datos, mazo y reglas de efectos de las cartas.
- `LocalCardPlaytestPresenter`, `LocalCardUiInstaller`: sandbox local de cartas, fuera del flujo multijugador.
- `MapRender`, `_map01`, `_map02`: dibujo y datos de los mapas.

**Progresión** (`Progression`)
- `ProgressionRules`: reglas puras. Solo el ganador recibe entre 80 y 120 XP; el nivel N pide 100×N XP; los rangos son de 5 niveles (1-5, 6-10...).
- `PlayerProgression`: nombre, nivel y XP del jugador, guardados con `PlayerPrefs` por perfil.

## Progresión y emparejamiento

El servidor sortea la XP al terminar la partida y se la envía solo al ganador. Cada sesión guarda el rango de su creador
en una propiedad indexada y "Buscar partida" filtra por ese rango, así un nivel 1 no se empareja con un nivel 20.
Unirse con código no filtra, porque es una invitación. El progreso vive en la PC del jugador: un cliente modificado
puede falsear su nivel.

Dos búsquedas simultáneas pueden crear una sala cada una, así que el host que espera solo sigue consultando el Lobby
cada pocos segundos y se muda a la sala de id menor (`MatchmakingRules`): de dos hosts que se ven, se mueve uno solo.
Si una sala no responde en 15 segundos, el jugador crea la suya y la ignora en adelante.

## Flujo

`MenuScene` (`MainMenuView` -> `SessionService`, o `LobbyView` -> `NetworkLauncher` en modo desarrollo) -> se conecta y nace un `PlayerNet` por jugador
(el servidor aprueba la conexión con `ConnectionGate`: rechaza si la sala está llena o la partida ya empezó; luego asigna `Slot`)
-> `LobbyFlow` cuenta atrás cuando hay `RequiredPlayers` -> carga `GameScene` para todos
-> `MatchController` arranca al terminar la carga -> bucle: `Selecting` (manos privadas, jugadas, 30 s o todos
confirmados) -> `Resolving` (resultado y pausa de 3 s) -> `Selecting` o `Finished`.

## Mensajes y estado replicado

| Elemento | Quién lo origina | Quién lo ejecuta | Datos |
|---|---|---|---|
| `PlayerNet.SubmitPlaysRpc` | Dueño del jugador | Servidor | `PlayDto[]` (carta, dirección); se valida en `MatchRules` |
| `PlayerNet.ReceiveHandRpc` | Servidor | Solo el dueño | Ids de las cartas de su mano (privada) |
| `PlayerNet.ReceiveWinXpRpc` | Servidor | Solo el dueño | XP ganada al terminar la partida |
| `MatchController.NotifyTurnResolvedRpc` | Servidor | Todos | `ResolutionDto[]` + `HitDto[]` para animar |
| `MatchController.Phase/Turn/TurnEndTime/PlayerCount/WinnerSlot` | Servidor (escribe) | Todos (leen) | `NetworkVariable` |
| `MatchController.Ships` | Servidor | Todos (leen) | `NetworkList<ShipSnapshot>` |
| `PlayerNet.Slot/Confirmed` | Servidor | Todos (leen) | `NetworkVariable` |

Los RPC usan el atributo `[Rpc]` de Netcode 2.x con `InvokePermission` explícito (`Owner` o `Server`).

## Cómo ejecutar

1. Abrir en Unity `6000.3.17f1` y la escena `MenuScene` (la conexión se hace allí).
2. Con servicios: configurar el proyecto en Unity Cloud (Project Settings > Services) y pulsar "Buscar partida" en cada
   instancia. Solo se encuentran jugadores del mismo rango de nivel.
3. Sin servicios: marcar "Modo desarrollo" en el menú y usar Host / Client / Servidor dedicado. Crear partida y unirse
   con código también están allí.
4. Para probar en una sola PC usar Multiplayer Play Mode (virtual players) o dos builds (`-profile nombre`).

## Escenas

- Flujo multijugador: `MenuScene` -> `GameScene`.
- Fuera del flujo: `LobbyScene` (flujo anterior, deshabilitada en el build), `GameSceneTesting` (sandbox local de `CardMechanics`), `MapTesting`, `EndScene`, `SampleScene`.

## Limitaciones conocidas

- Sin migración de host ni reconexión: si el host se va, la partida termina; quien se desconecta pierde (`Forfeit`).
- Interfaz provisional con `OnGUI` en `LobbyView` y `MatchHud`; el menú inicial usa uGUI.
- El mapa es fijo (`_map01`); su selección queda pendiente de otro integrante.
- El sandbox local de CardMechanics y `GameSceneTesting` no usan red.
- Las pruebas unitarias están en `Assets/Scripts/CardMechanics/Tests` (EditMode y PlayMode), `Assets/Scripts/Progression/Tests`, `Assets/Scripts/Presentation/Cards/Tests` y `Assets/Scripts/Presentation/Rules/Tests`; la capa de red no tiene pruebas.

## Deuda técnica conocida

- Estado estático global: `MatchController.Instance` y `PlayerNet.All` (lista pública mutable que nunca se reinicia).
- Eventos duplicados en `MatchEvents`: `TurnResolved` y `TurnResolvedDetailed`.
- Caída del host a mitad de partida: los clientes ven "Sin conexión" en el HUD y vuelven al menú solo con "Ir al menú". Si la partida ya había terminado, conservan el resultado.
- La sesión del Lobby no se bloquea al empezar la partida; los que entran tarde son rechazados recién por `ConnectionGate`.
- `SessionService` no hace `Detach` de la sesión cuando falla la creación o unión, y puede dejar el manejador registrado.
- El flag `movementOnly` es de prueba y sigue en código de producción (`MatchController` y `MatchRules`).
- Sobran `Debug.Log` (`BoardView`, `SessionService`, `NetworkLauncher`) y quedan textos de interfaz con voseo regional.
