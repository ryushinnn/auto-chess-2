# Architecture

The game is split into an **engine-free simulation** (`AutoChess.Core`) and a **Unity client** that only
draws the simulation and turns input into commands. Nothing in the client changes game state directly.

```
                 commands (BuyUnit, MoveUnit, EquipItem, Ready, ...)
  Unity input  ─────────────────────────────────────────────►  IGameSession
  (UI buttons,                                                  │  LocalGameSession today,
   drag & drop)                                                 │  a networked session later
                                                                ▼
                                                     MatchSimulation (AutoChess.Core)
                                                     fixed 30 Hz ticks, seeded RNG
                                                     MatchState: players, phase, battles
                                                                │
  Unity views  ◄────────────────────────────────────────────────┘
  (roster, battle,        events (GoldChanged, UnitMoved, UnitDamaged, ...)
   HUD, shop ...)         + read-only state (positions, HP, shop slots ...)
```

## Assemblies

| assembly | folder | depends on | content |
|---|---|---|---|
| `AutoChess.Core` | `Assets/_main/Scripts/Core` | nothing (`noEngineReferences`, no plugins) | rules, match flow, battle simulation, commands, events, serialization |
| `AutoChess.Core.Tests` | `Assets/_main/Tests/EditMode` | Core, NUnit | rule / combat / match / determinism / real-data tests |
| Assembly-CSharp | `Assets/_main/Scripts/Game` | Core, DOTween, Odin, TMP, RExt | ScriptableObject data, composition root, views, UI, input |
| Assembly-CSharp-Editor | `Assets/_main/Scripts/Editor` | Game, Core, AutoTest.Editor | data exporter, migration, AutoTest game adapter, drawers |
| `AutoTest.Editor` | `Assets/AutoTest/Editor` | Unity only | generic test bridge (HTTP server in the editor) |

Because Core cannot see UnityEngine, it can run on a dedicated .NET server, in a headless test runner
(`Tools/core_tests`) or inside Unity unchanged.

## Core (`AutoChess.Core`)

* **Data** - `UnitDefinition`, `ItemDefinition`, `TraitDefinition`, `StageDefinition`, `GameRules` (every
  tunable number; defaults = the original `GameConfigs`), `GameDatabase` (validated, indexed, shared
  read-only by all matches). Built from the ScriptableObjects by `Game/Data/GameDataBuilder.cs`, or
  loaded from `GameData/game_data.json` (`GameDataJson`, exported by menu *AutoChess/Export Game Data*).
* **State** - `MatchState` (phase, timers, stage/round, players, running battles), `PlayerState` (gold,
  level, xp, bench, board, item bench, shop, starter offers, its own RNG stream).
* **Rules** - stateless systems: `Economy`, `Shop`, `Roster` (merges, moves, auto-fill), `Items`
  (equip / combine), `Traits`, `Rewards`.
* **Match** - `MatchSimulation.Tick()` runs the phase loop
  Setup → Preparation → Transition → Battle → Summary → … → Finished; `Submit(GameCommand)` validates and
  applies player intents (phase rules included) and returns a `CommandResult`.
* **Combat** - `BattleSimulation` (one per player per round, built from a `BattleSetup` = two teams +
  seed): `CombatUnit` (stats with ordered modifiers, HP / energy, crowd control, marks, DoT / HoT,
  attack / skill / movement state machines), A* pathing on the hex grid with cell reservation,
  projectiles, damage areas, synergies (`TraitEffects`), one attack + skill behaviour per hero
  (`Combat/Heroes/*`, registered in `AbilityRegistry`).
* **Determinism** - `DeterministicRandom` (xoshiro128**) everywhere, fixed tick, no engine time. The
  same seed + the same commands at the same ticks reproduce a match bit for bit (`StateHasher`,
  `MatchRecorder.Replay`).
* **Networking prep** - `CommandCodec` (compact binary commands), `NetWriter/NetReader`, `StateHasher`
  (desync detection), `MatchRecording` (replays / audits), per-player RNG streams (one player's rerolls
  never change another's luck), N players per match, PvP-ready `BattleSetups.FromBoardMirrored`,
  `IUnitPool` (unlimited today, shared TFT pool later).

## Unity client (`Assets/_main/Scripts/Game`)

* `GameManager` - composition root: builds the database, owns the `LocalGameSession`, feeds it frame
  time (`Advance(Time.deltaTime)`), binds every `ISessionView`, relays session events
  (`GameManager.EventRaised`, survives `Restart`), offers dev cheats (Odin buttons).
* Views read state and react to events; they never modify the model:
  `RosterView` / `RosterUnitView` (owned units on bench and board), `BattleView` / `BattleUnitView`
  (interpolated between ticks, animations from battle events, projectiles / areas / effect VFX),
  `MapVisual` (tiles, occupancy pulled from the state every frame, portal), UI panels under `UI/`.
* Input produces commands: `UnitDragHandler` (EventSystem pointer events on 3D units through the
  camera's `PhysicsRaycaster`; previews with `session.CanMove`), `InventoryUI` (item drag → `EquipItemCommand`),
  shop / HUD buttons.
* Authoring data stays in ScriptableObjects (`HeroTrait`, `Item`, `Stage`, `RoleConfig`, `RealmConfig`,
  DBs in `Resources`) with unchanged serialized fields.

## Going multiplayer (next steps)

1. Pick the authority: a dedicated C# server (.NET or headless Unity) running `MatchSimulation` for
   N players; Nakama (already in `Assets/Packages`) for accounts, matchmaking and relaying.
2. Implement `IGameSession` as a network session: `Submit` sends `CommandCodec.Encode(cmd)`; the server
   answers with results and streams events (or snapshots) back; the client keeps a read-only mirror of
   `MatchState` for its views. Views need no change.
3. PvP rounds: pair players and build battles with `BattleSetups.FromBoard(home)` +
   `BattleSetups.FromBoardMirrored(visitor)`; add player health / elimination to `MatchSimulation`.
4. Replace `UnlimitedUnitPool` with a shared pool; compare `StateHasher` hashes to catch desyncs.

## Tests

* `py Tools/core_tests/run_core_tests.py` - Core tests without Unity (Unity's Roslyn + .NET 6, an
  NUnit-compatible shim). Same files run in Unity's Test Runner (`AutoTest Server/unity_tests.bat`).
* `py Tools/compile_check/compile_check.py --editor` - compiles every assembly with Unity's Roslyn.
* `AutoTest Server/` - scenario runner driving the real editor through the bridge (see its README).
