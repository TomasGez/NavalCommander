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
- Verification evidence: local Unity project/package audit; Unity 6.3 LTS [Test Framework command-line reference](https://docs.unity3d.com/6000.3/Documentation/Manual/test-framework/reference-command-line.html), [test assembly setup](https://docs.unity3d.com/6000.3/Documentation/Manual/test-framework/workflow-create-test-assembly.html), and [assembly reference rules](https://docs.unity3d.com/6000.3/Documentation/Manual/assembly-definitions-referencing.html). At the CDM-00 commit, no test assembly/spec existed; the first runnable test and its RED evidence are recorded under CDM-01.
- Checks: project/package/repository audit and documentation review completed; Unity tests are N/A for this documentation-only prerequisite task because the test assembly/spec does not exist yet; runtime harness is N/A because this task changes no runtime behavior.
- Rollback boundary: revert only `odd/tasks/card-deck-mechanic.md`; do not revert unrelated local package, project-setting, or Unity Skills setup changes.
- Work-unit commit: `8cf6fc36066442f023578227753ec127cf56a731` (`docs(card-mechanic): reconcile setup and delivery plan`); this documentation-only commit contains no gameplay source.
- RDD review: the post-commit assessment was high/unassessable because pre-existing unrelated untracked files were not inventoried. The exact preflight isolated only `odd/tasks/card-deck-mechanic.md` and excluded that unrelated inventory; after the user's grant, native review lineage `review-10b6dfb9281b7a62` completed low-risk (`non_executable_only`) and was acknowledged as approved. Reviewed boundary: this commit. This grant was for this review only; it did not authorize push/PR.

### CDM-01A — Define the authored card catalog
- [x] Add an Editor-only Test Framework assembly (`TestAssemblies`) and run the initial catalog-count specification to RED before defining card assets.
- [x] Extend the catalog specifications for all 12 canonical names, their Defense/Movement/Attack categories, and the three confirmed damage values; observe RED before defining card assets.
- [x] Define immutable authored card data and create exactly 12 ScriptableObject assets.
- Route: delegated direct; the writer trigger applies because this touches runtime data code, assembly definitions, authored assets, and tests.
- Trigger evidence: the work spans multiple non-trivial files, so the parent delegates one bounded writer after recording the expanded RED.
- Acceptance: exactly one asset for every agreed card; serialized category and attack damage values match the accepted rules. Tests that reference gameplay types must use an explicit runtime assembly because custom asmdefs cannot reference predefined `Assembly-CSharp`.
- TDD evidence: the first filtered EditMode run compiled and failed the exact-12 catalog test: expected 12 `CardDefinition` assets, found 0 (1 total, 0 passed, 1 failed; XML `%TEMP%/NavalCommander-CardCatalog-RED.xml`, log `%TEMP%/NavalCommander-CardCatalog-RED.log`). After extending the specification, a second filtered run compiled all three tests and observed RED: 3 total, 0 passed, 3 failed. The tests reported 0 assets, then missing `Missile`/`Shield` definitions; XML `%TEMP%/NavalCommander-CardCatalog-Rules-RED.xml`, log `%TEMP%/NavalCommander-CardCatalog-Rules-RED.log`.
- Verification note: the first import attempt after authoring assets was blocked by invalid hand-written `.meta` YAML (Unity reported `Shield.asset.meta` parser failure at line 8 and invalid folder/asset GUID metadata); it emitted no test result XML, so this was not a card assertion result. All 17 new sidecars were corrected to Unity's valid importer format, preserving GUIDs. The final import/test run reported no metadata parse errors.
- Verification command: `& 'C:/Program Files/Unity/Hub/Editor/6000.3.17f1/Editor/Unity.exe' -batchmode -projectPath 'C:/Users/tobia/OneDrive/Desktop/Tobias/Facultad/Multiplayer/NavalCommander-git' -runTests -testPlatform EditMode -assemblyNames 'NavalCommander.CardMechanics.Tests' -testFilter 'NavalCommander.CardMechanics.Tests.CardDefinitionCatalogTests' -testResults "$env:TEMP/NavalCommander-CardCatalog-Rules-GREEN-AfterMetaFix.xml" -logFile "$env:TEMP/NavalCommander-CardCatalog-Rules-GREEN-AfterMetaFix.log"`. Result: passed all 3 tests (3 total, 3 passed, 0 failed); XML at `%TEMP%/NavalCommander-CardCatalog-Rules-GREEN-AfterMetaFix.xml`, log at `%TEMP%/NavalCommander-CardCatalog-Rules-GREEN-AfterMetaFix.log`. Structural inspection confirmed exactly 12 unique asset names and IDs, all asset script references target `CardDefinition.cs`, categories match Defense/Movement/Attack, and attack damage values are Missile 30, Torpedo 50, Three-Shot 40 per hit cell.
- Runtime harness: N/A for this slice because it only authors card metadata/assets; no runtime gameplay behavior is implemented yet.
- Rollback boundary: remove `Assets/Scripts/CardMechanics/` and its folder meta, `Assets/Data/CardDefinitions/` and their folder metas, and `Assets/Tests/CardMechanics/` plus its folder metas; revert only this task's tracker section. Preserve all unrelated local project/setup changes.
- Work-unit commit: pending local commit on `feat/card-deck-mechanic`; RDD assessment pending.
- Runner scope note: `Packages/manifest.json` includes the Unity-Skills package in `testables`; use Unity 6.3 `-assemblyNames NavalCommander.CardMechanics.Tests` and `-testFilter NavalCommander.CardMechanics.Tests.CardDefinitionCatalogTests` to avoid unrelated package tests.

### CDM-01B — Implement per-player deck and hand rules
- [ ] Implement independently seeded per-player deck state, automatic empty-slot refill, a 3-card hand, max-2 selection, and return-to-owner behavior.
- [ ] Add focused tests for the pure deck/hand rules and observe RED before implementation.
- Route: delegated direct; deck state and its behavior tests touch multiple non-trivial files.
- Trigger evidence: multiple non-trivial state and test files are required, so implementation will be delegated after the writer observes the deck/hand RED.
- Acceptance: a player's deck contains exactly one of each card; refill preserves unused cards and never introduces a draw/discard action; a maximum of two selections is enforced per turn; used cards return to the same owner's pool and blocked movements remain in hand while counting as a selection.

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
- Test setup: the Editor-only card mechanic test assembly and catalog tests exist; expanded catalog tests were observed RED before defining cards and now pass after implementation. Tests that reference gameplay types use the explicit `NavalCommander.CardMechanics` runtime assembly because custom asmdefs cannot reference predefined `Assembly-CSharp`.
- Delivery strategy: `ask-on-risk` (default); forecast is approximately 700 authored changed lines (exclude generated Unity files). The user selected `feature-branch-chain` on 2026-09-28 after the required ask-on-risk gate.
- Planned local work-unit / PR boundaries (provisional until authored line counts are measured; each PR slice depends on the previous one):
  1. Tracker/prerequisite closeout (CDM-00): current documentation-only commit; tracker-PR association pending, commit identity to be recorded after commit.
  2. Authored card catalog (CDM-01A): card data, 12 assets, and catalog tests; commit/child PR identity pending.
  3. Per-player deck and hand (CDM-01B): deterministic shuffle/refill/reuse and hand-limit tests; commit/child PR identity pending.
  4. Authoritative hidden turn submission (CDM-02): private player state, submission validation, and turn gating; commit/child PR identity pending.
  5. Deterministic card effects (CDM-03): defense, movement, attacks, damage, and their tests; commit/child PR identity pending.
  6. Private hand UI and setup documentation (CDM-04): three slots, selection/targets, informational deck view; commit/child PR identity pending.
  7. Integrated host/client verification (CDM-05): smoke test and recorded evidence; commit/child PR identity pending.
- Feature-branch-chain remote tracker/child PRs have not been created. Push, PR creation, and other remote operations remain unauthorized; do not perform them without explicit permission.
- Git boundary: implementation checkout is C:/Users/tobia/OneDrive/Desktop/Tobias/Facultad/Multiplayer/NavalCommander-git on feat/card-deck-mechanic; source snapshot NavalComander remains outside Git. Local commits are possible on this branch. Push is not authorized and remote write access is unknown; do not push.

## Progress and Next Step
- Requirements mapping: complete. Clone/branch, dependencies, package assets, test package, and documented runner path are reconciled. Pivot-based movement, hidden-ship blocking, forward-point projectile origin, ship-relative movement/aim directions, blocked movement/rotation outcomes, one-cell movement distance, and board-boundary rules are confirmed.
- Source implementation: CDM-01A is complete locally: immutable `CardDefinition` ScriptableObject data and exactly 12 authored card assets are implemented and catalog-tested. Per-player deck/hand state, turn submission, effects, and UI are not implemented yet. No push occurred.
- Next step: add focused pure deck/hand behavior tests and observe RED before implementing per-player deck state under CDM-01B.

## Relevant Files
- C:/Users/tobia/OneDrive/Desktop/Tobias/Facultad/Multiplayer/NavalCommander-git — primary Git checkout and feature branch.
- C:/Users/tobia/OneDrive/Desktop/Tobias/Facultad/Multiplayer/NavalComander — separate no-Git source snapshot used only for comparison.
- `Packages/manifest.json` and `Packages/packages-lock.json` — reconciled direct/resolved package baseline in the implementation checkout.
- `Assets/DefaultNetworkPrefabs.asset` — currently empty NGO prefab list; networking integration remains pending.
- `Assets/Scripts/CardMechanics/CardDefinition.cs` and `NavalCommander.CardMechanics.asmdef` — static card data model and explicit runtime assembly.
- `Assets/Data/CardDefinitions/` — the 12 authored card ScriptableObject assets.
- `Assets/Tests/CardMechanics/` — Editor-only catalog specifications for names, categories, and damage.
- `Assets/Scenes/GameScene.unity` — current scene baseline.
- `ProjectSettings/ProjectVersion.txt` — Unity 6000.3.17f1.
- Project GDD, pages 4–13 — turn phases, hidden selections, authority expectations; legacy draw rules superseded by current user decisions.








