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

## Flujo

`LobbyScene` (`LobbyView` -> `SessionService` o `NetworkLauncher`) -> se conecta y nace un `PlayerNet` por jugador
(el servidor aprueba la conexión con `ConnectionGate`: rechaza si la sala está llena o la partida ya empezó; luego asigna `Slot`)
-> `LobbyFlow` cuenta atrás cuando hay `RequiredPlayers` -> carga `GameScene` para todos
-> `MatchController` arranca al terminar la carga -> bucle: `Selecting` (manos privadas, jugadas, 30 s o todos
confirmados) -> `Resolving` (resultado y pausa de 3 s) -> `Selecting` o `Finished`.

## Mensajes y estado replicado

| Elemento | Quién lo origina | Quién lo ejecuta | Datos |
|---|---|---|---|
| `PlayerNet.SubmitPlaysRpc` | Dueño del jugador | Servidor | `PlayDto[]` (carta, dirección); se valida en `MatchRules` |
| `PlayerNet.ReceiveHandRpc` | Servidor | Solo el dueño | Ids de las cartas de su mano (privada) |
| `MatchController.NotifyTurnResolvedRpc` | Servidor | Todos | `ResolutionDto[]` + `HitDto[]` para animar |
| `MatchController.Phase/Turn/TurnEndTime/PlayerCount/WinnerSlot` | Servidor (escribe) | Todos (leen) | `NetworkVariable` |
| `MatchController.Ships` | Servidor | Todos (leen) | `NetworkList<ShipSnapshot>` |
| `PlayerNet.Slot/Confirmed` | Servidor | Todos (leen) | `NetworkVariable` |

Los RPC usan el atributo `[Rpc]` de Netcode 2.x con `InvokePermission` explícito (`Owner` o `Server`).

## Cómo ejecutar

1. Abrir en Unity `6000.3.17f1` y la escena `LobbyScene` (la conexión se hace allí).
2. Con servicios: configurar el proyecto en Unity Cloud (Project Settings > Services), luego un jugador pulsa
   "Crear partida (Relay)" y comparte el código; el otro usa "Unirse con código" o "Buscar partida".
3. Sin servicios: activar "Modo desarrollo (IP directa)" y usar Host / Client / Servidor dedicado.
4. Para probar en una sola PC usar Multiplayer Play Mode (virtual players) o dos builds (`-profile nombre`).

## Escenas

- Flujo multijugador: `LobbyScene` -> `GameScene`.
- Fuera del flujo: `GameSceneTesting` (sandbox local de `CardMechanics`), `MapTesting`, `EndScene`, `SampleScene`.

## Limitaciones conocidas

- Sin migración de host ni reconexión: si el host se va, la partida termina; quien se desconecta pierde (`Forfeit`).
- Interfaz provisional con `OnGUI` (`LobbyView`, `MatchHud`).
- El mapa es fijo (`_map01`); su selección queda pendiente de otro integrante.
- El sandbox local de CardMechanics y `GameSceneTesting` no usan red.
- Las pruebas unitarias están en `Assets/Scripts/CardMechanics/Tests` (EditMode y PlayMode); la capa de red no tiene pruebas.
