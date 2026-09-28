# Card and Deck Mechanic — Authoritative Multiplayer

## Objective
Implement only the card/deck mechanic for NavalComander in Unity 6.3, including per-player decks, turn-based hand management, card effects, authoritative multiplayer handling, and the sketch-inspired hand UI.

## Problem and Why
The project has no gameplay/card system yet. The user wants the 12-card mechanic implemented cleanly, with static card definitions in ScriptableObjects, focused scripts (no God Script), and server authority so opponents cannot inspect each other's hand or hidden choices. The original no-Git Unity folder is only a source snapshot; implementation will use the newly cloned Git checkout after reconciling verified project differences.


## Working Checkout and Snapshot Reconciliation
- Primary implementation checkout: C:/Users/tobia/OneDrive/Desktop/Tobias/Facultad/Multiplayer/NavalCommander-git.
- Local branch: feat/card-deck-mechanic, created from origin/main at base commit fbc31ba16a157c9d44f8a3bfcb2ec252ea3db1f7.
- The original NavalComander folder is a separate Unity project snapshot with no .git; do not use it as the implementation checkout.
- Both trees use Unity 6000.3.17f1 and share the gameplay scenes and NetworkManager asset. Their manifests/lockfiles are reconciled: NGO 2.13.3 is a direct dependency, Unity Transport 2.7.3 is resolved transitively, and Unity Test Framework 1.6.0 is present in the clone. The package versions match the source snapshot; no new package install is required.
- `Assets/DefaultNetworkPrefabs.asset` exists in both trees but its prefab list is empty. No gameplay scripts/tests/asmdefs or NGO components are present in scenes/prefabs yet, so the required networking integration remains implementation work.
- ProjectSettings/ProjectSettings.asset differs in product and Unity Cloud metadata; do not copy it between trees.
- Clone used public HTTPS without credentials; no push has occurred and remote write access is unverified.
- The checkout already contains unrelated local Unity/project setup changes and untracked Unity Skills files. Preserve them and stage only card-feature paths for work-unit commits.

## Authorized Scope
- In-match deck, hand, action selection/submission, action resolution, card effects, and minimal host/client wiring needed to exercise the mechanic.
- UI for the map area, a decorative deck indicator (not a draw button), and three private hand slots; at most two selected cards.
- No lobby, matchmaking, progression, full ship-placement flow, or online service setup in this feature.

## Accepted Requirements and Constraints
- All ship types use the same 12 cards; every player has their own individual 12-card deck.
- Match-seeded randomization; derive independent per-player random streams from the match seed and stable player identity so players do not receive mirrored deck orders.
- Hand limit 3. At each turn start, preserve unused hand cards and automatically fill empty slots. No player-initiated draw, action-count draw, or discard. At most 2 cards are played per turn. Played cards return to their owner's general deck pool for future reuse. A movement card blocked by an invalid destination does not execute and remains in hand instead of returning to the deck; its selection still counts as one of the two permitted card uses that turn.
- The opponent must not see another player's hand, remaining deck, or selected actions before resolution. The server owns authoritative state and validates submitted actions.
- Card set: Defense—Shield, Mirror; Movement—Rotate Right 90°, Rotate Left 90°, Rotate 180°, Move Left, Move Right, Move Up, Move Down; Attack—Missile, Torpedo, Three-Shot.
- Ship health is 100. Missile damage 30, Torpedo damage 50, Three-Shot damage 40 for every ship-occupied cell hit.
- Missile and Torpedo target input is direction-only: the player chooses a direction of travel, not an exact board cell. Each projectile starts at the attacking ship's forward point, travels toward the board edge unless a wall/obstacle stops it, damages only the first ship encountered, and is destroyed on ship impact. No damage to walls/obstacles is specified. Movement and Missile/Torpedo directions are relative to the ship's orientation, not fixed to the board axes.
- For a spawned NGO projectile, the server must call `NetworkObject.Despawn()` on ship impact (default behavior destroys the associated GameObject and synchronizes despawn); do not call client-side `Object.Destroy` on a NetworkObject. For a local-only projectile, Unity's runtime API is `Destroy(gameObject)`. References: [Unity Object.Destroy](https://docs.unity3d.com/6000.0/ScriptReference/Object.Destroy.html), [NGO object spawning and despawning](https://docs-multiplayer.unity3d.com/netcode/current/basics/object-spawning/).
- Networking dependency baseline: NGO 2.13.3 (direct) and Unity Transport 2.7.3 (transitive) are reconciled in the implementation clone and source snapshot. No networking behavior is wired yet; authoritative turn submission and network prefab registration are feature work.
- All selected cards execute when the turn resolves at turn end; preserve the GDD phase order Defense → Movement → Attack and simultaneous/hidden selection. Movement/rotation actions operate on the ship GameObject's central pivot. Move-card directions and Missile/Torpedo aiming directions are relative to the ship's orientation. Each Move Left/Right/Up/Down action translates the central pivot exactly one board cell in the selected ship-relative direction. A translation is invalid if it would move into an obstacle, wall, or another ship's occupied cells, including hidden ships. Movement and rotation are also invalid if any part of the ship's final occupied footprint would end outside the playable board. If a selected movement is blocked, it does not execute and its card remains in hand rather than returning to the deck; selecting it still counts toward the two-card limit for that turn. New user decisions override legacy draw rules.
- Shield and Mirror last only for the current turn, cover one occupied cell of the ship rather than its full footprint, and are consumed by the first incoming attack against that protected cell. The player chooses which occupied cell to defend. Mirror returns the incoming attack toward its origin.
- The local Git workflow is established in NavalCommander-git on feature branch feat/card-deck-mechanic; the original NavalComander snapshot has no .git. The user reports they may lack remote write access; no push has occurred. The user chose test-first TDD; Unity Test Framework 1.6.0 is verified in the clone and source snapshot.

## Acceptance Criteria
- A player has a private, independently seeded deck containing exactly one of each of the 12 configured cards.
- At turn start, only empty hand slots are auto-filled up to 3; used cards return to the same player's deck, while a blocked movement card remains in hand. No draw/discard UI or command exists.
- A player can submit no more than 2 selected cards per turn; a blocked movement selection still counts against this limit. Invalid, late, unauthorized, or duplicate submissions are rejected by the server.
- Server resolves submitted actions only after all active players confirm or the configured turn timeout is reached; secret choices and private hand/deck state are not broadcast to other players.
- Resolution follows the agreed phases and card values. Defense lasts through the current turn, covers the one occupied ship cell chosen by its owner, and is consumed by its first incoming attack; Mirror returns that attack toward its origin. Missile/Torpedo use direction-only targeting and launch from the ship's forward point; they can travel as far as the board edge, are stopped by walls/obstacles, damage only the first ship encountered, and are destroyed on ship impact. For a spawned NGO projectile, destruction is server-authoritative through `NetworkObject.Despawn()`. A blocked movement or rotation does not execute, leaves its card in hand, and still counts as one of the two card uses for that turn. Rotation validity checks the ship's full final occupied footprint against walls, obstacles, other ships, and the playable board boundary; no part of the ship may end outside the board. Other invalid-action outcomes remain unresolved and must not be invented.
- Hand UI shows exactly 3 private slots, supports selection of up to 2 cards, lets the player choose one occupied ship cell for Shield/Mirror and a travel direction for Missile/Torpedo, and presents the deck as informational only.
- Focused automated tests cover deck seeding/refill/reuse/limits, server-side submission/privacy/resolution, and damage/effect rules; multiplayer smoke test verifies host plus at least one client.

## Checklist

### CDM-00 — Resolve implementation prerequisites and remaining rules
- [x] Confirm TDD mode: test-first (write a failing test before implementation, then GREEN and REFACTOR), explicitly chosen by the user.
- [x] Verify Unity Test Framework 1.6.0 in the clone manifest, lockfile, and local PackageCache; Unity 6000.3.17f1 Editor is installed.
- [x] Verify the supported EditMode runner invocation from Unity 6.3 LTS documentation: `Unity.exe -batchmode -projectPath <project> -runTests -testPlatform EditMode -testResults <xml-path> -logFile <log-path>`. Do not pass `-quit` while tests run. A first run is still pending until the test assembly/spec exists.
- [x] Establish a Git checkout and feature branch for local work: NavalCommander-git, branch feat/card-deck-mechanic, based on origin/main commit fbc31ba16a157c9d44f8a3bfcb2ec252ea3db1f7. No push was attempted; remote write access is unverified.
- [x] Reconcile the clone with the source snapshot: NGO, Transport, and Test Framework versions match; the default network prefab list is empty in both and no gameplay NGO components are present. No package copy is needed; do not copy differing ProjectSettings/ProjectSettings.asset.
- [x] Confirm defense timing and coverage: all cards resolve at turn end by Defense → Movement → Attack; Shield/Mirror cover one occupied cell and consume on the first incoming attack during that turn; Mirror returns the attack toward its origin.
- [x] Resolve defense cell targeting: the player chooses which occupied ship cell receives Shield or Mirror.
- [x] Resolve Missile/Torpedo aim input: the player chooses a direction only, not an exact board cell.
- [x] Resolve Missile/Torpedo maximum range, first-ship damage, ship-impact destruction, and wall collision: each can travel to the board edge, only the first ship encountered can be damaged, the projectile object is destroyed on ship impact, and walls/obstacles stop it. Wall/obstacle damage and destruction are not specified.
- [x] Clarify movement pivot and blockers: movement/rotation acts on the ship GameObject's central pivot; translation may not enter an obstacle, wall, or any other ship's occupied cells, including hidden ships.
- [x] Clarify projectile origin: Missile/Torpedo launch from the ship's forward point.
- [x] Resolve blocked movement outcome: the movement does not execute, the card remains in hand instead of returning to the deck, and selecting it still counts as one of the two card uses allowed that turn.
- [x] Resolve movement and attack direction frame: directional moves and Missile/Torpedo aim are relative to the ship's orientation.
- [x] Resolve blocked rotation outcome: validate the ship's full final occupied footprint; if it intersects a wall, obstacle, or another ship, the rotation does not occur, the card stays in hand, and the selection counts toward the two-card limit.
- [x] Resolve board limits: no part of the ship may move or rotate outside the playable area; the boundary blocks out-of-bounds poses.
- Route: direct inline (parent-owned process/product decisions).
- Trigger evidence: this prerequisite closeout changed one documentation file; the 4+ file project map was supplied by the prior delegated read-only audit and reused rather than repeated.
- [x] Confirm movement distance: each directional movement card translates the central pivot exactly one board cell in the selected ship-relative direction.
- Acceptance: blockers are recorded with evidence and no source assumptions are introduced; package/test prerequisites are reconciled and an EditMode runner path is documented.
- Verification evidence: local Unity project/package audit; Unity 6.3 LTS [Test Framework command-line reference](https://docs.unity3d.com/6000.3/Documentation/Manual/test-framework/reference-command-line.html), [test assembly setup](https://docs.unity3d.com/6000.3/Documentation/Manual/test-framework/workflow-create-test-assembly.html), and [assembly reference rules](https://docs.unity3d.com/6000.3/Documentation/Manual/assembly-definitions-referencing.html). The command has not yet been run because no test assembly/spec exists.
- Checks: project/package/repository audit and documentation review completed; Unity tests are N/A for this documentation-only prerequisite task because the test assembly/spec does not exist yet; runtime harness is N/A because this task changes no runtime behavior.
- Rollback boundary: revert only `odd/tasks/card-deck-mechanic.md`; do not revert unrelated local package, project-setting, or Unity Skills setup changes.
- Work-unit commit: pending local documentation commit; no gameplay source changes are included.

### CDM-01 — Model card definitions and per-player deck/hand
- [ ] Add an Editor-only Test Framework assembly (`TestAssemblies`) and run the first asset-catalog specification to an observed RED result before defining the card assets.
- [ ] Define immutable authored card data and the 12 ScriptableObject assets.
- [ ] Implement independently seeded per-player deck state, auto-refill, 3-card hand, max-2 selection, and return-to-owner behavior.
- [ ] Add focused tests for the pure deck/hand rules.
- Route: delegated direct; writer trigger applies because this spans multiple non-trivial scripts/assets/tests.
- Acceptance: deck/hand behaviors satisfy the rules above and can be tested without scene lookups. Test assemblies use `TestAssemblies` and Editor-only platform settings. Before tests reference gameplay types, put those types in an explicit runtime assembly; Unity custom asmdefs cannot reference predefined `Assembly-CSharp`.
- TDD evidence: the initial catalog test must compile and report an assertion failure for the currently absent 12-card asset catalog; do not add the card data or deck implementation until that RED is observed.

### CDM-02 — Add authoritative hidden turn submission
- [ ] Integrate a server-owned turn/action coordinator with NGO.
- [ ] Keep hand/deck/action choices private to the owning client; expose only permitted results.
- [ ] Validate ownership, phase, action count, card ownership, and confirmation on the server.
- Route: delegated direct; multiplayer boundary and multiple non-trivial files require a bounded writer after CDM-00.
- Acceptance: server is the sole authority and unconfirmed selections remain private until resolution.

### CDM-03 — Resolve card effects
- [ ] Implement defense, movement, rotations, missile, torpedo, and three-cell attacks in phase order.
- [ ] Apply 100 HP and the confirmed damage values; make invalid/out-of-bounds outcomes explicit.
- [ ] Add effect/resolution tests.
- Route: delegated direct; multiple non-trivial scripts and tests.
- Acceptance: all card effects are deterministic and tested against agreed targeting/defense rules.

### CDM-04 — Build private hand UI
- [ ] Add a focused UGUI hand presenter and three-slot layout based on the supplied sketch.
- [ ] Show the deck as a non-interactive visual; allow selecting at most two cards and displaying selected state.
- [ ] Let the player choose the defended occupied ship cell when selecting Shield or Mirror.
- [ ] Let the player choose a ship-relative direction of travel for Missile and Torpedo; do not target an exact board cell.
- [ ] Add concise setup/use documentation.
- Route: delegated direct; UI, data binding, and setup span multiple non-trivial files.
- Acceptance: UI never exposes other players' hands and has no draw/discard actions.

### CDM-05 — Verify multiplayer flow and close delivery
- [ ] Run focused tests and applicable full Unity checks.
- [ ] Run host/client smoke test and inspect console for compile/runtime errors.
- [ ] Record verification and work-unit commit identities here.
- Route: delegated test/verification actor where useful; no source-writing unless a separately routed fix task is needed.
- Acceptance: all passed/failed/skipped checks are recorded honestly and each implementation task has its work-unit commit evidence.

## Effective TDD, Runner, and Delivery
- TDD mode: enabled; test-first (RED → GREEN → REFACTOR), explicitly selected by the user.
- Test framework: Unity Test Framework 1.6.0 is present in the clone manifest/lockfile and PackageCache. The installed editor is `C:/Program Files/Unity/Hub/Editor/6000.3.17f1/Editor/Unity.exe`.
- EditMode runner (PowerShell): `& 'C:/Program Files/Unity/Hub/Editor/6000.3.17f1/Editor/Unity.exe' -batchmode -projectPath 'C:/Users/tobia/OneDrive/Desktop/Tobias/Facultad/Multiplayer/NavalCommander-git' -runTests -testPlatform EditMode -testResults "$env:TEMP/NavalCommander-EditMode.xml" -logFile "$env:TEMP/NavalCommander-EditMode.log"`. Unity's Test Framework command-line reference documents these arguments; `-quit` is not supported while tests are running. This invocation remains to be exercised after the test assembly/spec is added.
- Test setup still needed: create a focused EditMode test assembly and verify a real RED result before writing the corresponding production behavior. No `.asmdef`, test source, `.runsettings`, CI test command, or preconfigured task runner exists yet.
- Delivery strategy: `ask-on-risk` (default); forecast is approximately 700 authored changed lines (exclude generated Unity files). The user selected `feature-branch-chain` on 2026-09-28 after the required ask-on-risk gate.
- Planned local work-unit / PR boundaries (provisional until authored line counts are measured; each PR slice depends on the previous one):
  1. Tracker/prerequisite closeout (CDM-00): current documentation-only commit; tracker-PR association pending, commit identity to be recorded after commit.
  2. Card definitions and per-player deck/hand (CDM-01): tests with card data and deck behavior; commit/child PR identity pending.
  3. Authoritative hidden turn submission (CDM-02): private player state, submission validation, and turn gating; commit/child PR identity pending.
  4. Deterministic card effects (CDM-03): defense, movement, attacks, damage, and their tests; commit/child PR identity pending.
  5. Private hand UI and setup documentation (CDM-04): three slots, selection/targets, informational deck view; commit/child PR identity pending.
  6. Integrated host/client verification (CDM-05): smoke test and recorded evidence; commit/child PR identity pending.
- Feature-branch-chain remote tracker/child PRs have not been created. Push, PR creation, and other remote operations remain unauthorized; do not perform them without explicit permission.
- Git boundary: implementation checkout is C:/Users/tobia/OneDrive/Desktop/Tobias/Facultad/Multiplayer/NavalCommander-git on feat/card-deck-mechanic; source snapshot NavalComander remains outside Git. Local commits are possible on this branch. Push is not authorized and remote write access is unknown; do not push.

## Progress and Next Step
- Requirements mapping: complete. Clone/branch, dependencies, package assets, test package, and documented runner path are reconciled. Pivot-based movement, hidden-ship blocking, forward-point projectile origin, ship-relative movement/aim directions, blocked movement/rotation outcomes, one-cell movement distance, and board-boundary rules are confirmed.
- Source implementation: not started. No code edits or pushes.
- Next step: add the focused EditMode test assembly and a first executable RED specification, run it with the documented Unity 6.3 Editor invocation, then implement only the behavior covered by that failing test.

## Relevant Files
- C:/Users/tobia/OneDrive/Desktop/Tobias/Facultad/Multiplayer/NavalCommander-git — primary Git checkout and feature branch.
- C:/Users/tobia/OneDrive/Desktop/Tobias/Facultad/Multiplayer/NavalComander — separate no-Git source snapshot used only for comparison.
- `Packages/manifest.json` and `Packages/packages-lock.json` — reconciled direct/resolved package baseline in the implementation checkout.
- `Assets/DefaultNetworkPrefabs.asset` — currently empty NGO prefab list; networking integration remains pending.
- `Assets/Scenes/GameScene.unity` — current scene baseline.
- `ProjectSettings/ProjectVersion.txt` — Unity 6000.3.17f1.
- Project GDD, pages 4–13 — turn phases, hidden selections, authority expectations; legacy draw rules superseded by current user decisions.








