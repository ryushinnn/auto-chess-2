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
* **Synergies** - `TraitEffects` holds one `TraitEffect` per role / realm, applied when the battle starts.
  Params come from `Data/Destinies/*` in asset order (the description's `#[i]`; each class documents its
  layout). Hooks for them: `IHitHook` (before / after every hit, with its `HitKind` = attack / ability /
  effect), tickers, `SkillCompleted`, `UnitDied`, `Summon` (Chaos summons `Aatrox_TrueForm`, which must stay
  in `HeroTraitDB`), `MakeInvulnerable`. `TeamSetup.DisabledTraits` turns single synergies off (balance tests).
* **Determinism** - `DeterministicRandom` (xoshiro128**) everywhere, fixed tick, no engine time. The
  same seed + the same commands at the same ticks reproduce a match bit for bit (`StateHasher`,
  `MatchRecorder.Replay`).
* **Networking prep** - `CommandCodec` (compact binary commands), `NetWriter/NetReader`, `StateHasher`
  (desync detection), `MatchRecording` (replays / audits), per-player RNG streams (one player's rerolls
  never change another's luck), N players per match, PvP-ready `BattleSetups.FromBoardMirrored`,
  `IUnitPool` (unlimited today, shared TFT pool later).

## TFT layer (matches of 2+ players)

A match with one player is the original PvE campaign of the authored stages. With 2+ players
(`MatchSimulation.IsMultiplayer`: offline = you + `GameManager.offlineBots` bots, online = the server's lobby)
the Teamfight Tactics rules apply; their numbers live in `GameRules` (TFT values folded onto the three unit tiers):

* Rounds: the authored stages (all PvE, stage 1 = TFT 1-2..1-4), then generated stages of 7 rounds, all PvP
  except x-7, which replays an authored PvE round scaled by `PveScalingPerStage` per stage (`RoundAt`).
* PvP: `Pairing` makes random pairs avoiding last round's opponent; an odd player out fights a ghost (copy of
  a random other board, `TeamSetup.Ghost`, its owner takes nothing). One `BattleSimulation` per pair, shared in
  `MatchState.Battles` by both players and listed once in `BattleList`; the visitor's board is mirrored.
* Damage: the loser of a round (draw = both lose) takes `StageDamage[stage] + 1 per surviving enemy unit`
  (`PlayerDamagedEvent`); at 0 health a player is eliminated (`PlayerEliminatedEvent`, placement = players left;
  units / shop go back to the pool; commands refused with `Eliminated`). Last player standing wins
  (`MatchFinishedEvent.WinnerId`); `MaxStages` caps a match.
* Economy: each preparation pays `IncomeOfRound` (2, 2, 3, 4 then 5) + interest (1 per 10 gold, max 5) + streak
  bonus (PvP streaks: 2-4 = 1, 5 = 2, 6+ = 3) as an `IncomeEvent`; a PvP win pays +1 at once.
* `SharedUnitPool` (29 / 18 / 10 copies by tier): shops take copies, rerolls / sales / eliminations put them back.
  `SellUnitCommand`: full price for 1-star, copies x price - 1 above; items return to the bench.
* `BotBrain` (Core/AI) plays offline bots (driven by `GameManager`), the server's fill-in bots and test bots.

UI (`Game/UI/Match/MatchHud.cs`, built in code on its own canvas): one panel with two tabs (players: health /
level / streak / placement, the viewed player's opponent outlined in red, click = scout; damage:
health removed by the viewed player's units, `UnitDamagedEvent.HpLost`), round banner, toasts (damage, income,
sales, eliminations), sell zone while dragging a unit, placement screen. Scouting = `GameManager.ViewPlayer`:
the roster, the map occupancy and the battle follow `ViewedPlayerId` (Esc = home); a battle where the viewed
player visits is drawn rotated (`BoardSpace.BattleFlipped`) so their units stand on the near half.

## Unity client (`Assets/_main/Scripts/Game`)

* Scenes: **Lobby** (build index 0) - `LobbyUI` (built in code): name, server address, play offline against N
  bots, or find an online match (connect, queue, cancel); **Arena** - the match only, back to the Lobby from the
  HUD or the placement screen. `AppSession` (static, `DontDestroyOnLoad` driver) outlives the scenes: it owns the
  `OnlineClient`, remembers the offline bot count, and loads the Arena when the server starts a match.
  `ClientConfig` (Resources) holds the stages, the board cell size and the UI font both scenes need; the game
  database is built from it once per run. Playing the Arena directly in the editor starts an offline lobby.

* `GameManager` - composition root: builds the database, owns the session (`LocalGameSession` offline,
  `LockstepSession` online through `OnlineClient`), feeds it frame time (`Advance(Time.deltaTime)`), binds every
  `ISessionView`, relays session events (`GameManager.EventRaised`, survives `Restart` and session swaps),
  offers dev cheats (Odin buttons).
* Views read state and react to events; they never modify the model:
  `RosterView` / `RosterUnitView` (owned units on bench and board), `BattleView` / `BattleUnitView`
  (interpolated between ticks, animations from battle events, projectiles / areas / effect VFX),
  `MapVisual` (tiles, occupancy pulled from the state every frame, portal), UI panels under `UI/`.
* Input produces commands: `UnitDragHandler` (EventSystem pointer events on 3D units through the
  camera's `PhysicsRaycaster`; previews with `session.CanMove`), `InventoryUI` (item drag → `EquipItemCommand`),
  shop / HUD buttons.
* Authoring data stays in ScriptableObjects (`HeroTrait`, `Item`, `Stage`, `RoleConfig`, `RealmConfig`,
  DBs in `Resources`) with unchanged serialized fields.

## Online play (lockstep)

* Server: the separate repo `auto-chess-2-server` (.NET 8, ASP.NET Core WebSocket at `/ws`, `/health`). It
  compiles `AutoChess.Core` and `AutoChess.Net` from this repo's sources (sibling checkout), loads
  `GameData/game_data.json`, matches players and runs the authoritative `MatchSimulation` of every match.
* Protocol: `Core/Net/Lockstep.cs` (one binary WebSocket message = [type byte][payload]). Hello (version + game
  data hash) -> Welcome; FindMatch -> Queue updates -> Start (seed, names, phase clocks, local player id) ->
  Frame per tick. A frame = the accepted commands in order, then one tick; every client applies it to its own
  replica, so all replicas stay identical. Refused commands only go back to their sender (CommandRejected).
* `LockstepHost` (Core/Net) is the server side of one match without sockets: applies commands, relays frames,
  compares the StateHasher reports the clients send every `HashInterval` frames (Desync on mismatch).
* `LockstepSession` (Core/Session) is the client side: an `IGameSession` whose `Submit` sends the command and
  whose replica plays buffered frames (`TargetBuffer`, catch-up above `MaxBuffer`); result callbacks fire when
  the frame carrying the command is played.
* `OnlineClient` (`Scripts/Net`, asmdef `AutoChess.Net`, engine-free) is the WebSocket client: socket I/O on
  background tasks, everything observable changes in `Update()` (main thread). Used by `GameManager` (Online
  panel `OnlineLobbyUI`) and by the server repo's headless bots.
* Matchmaking (server): 8 seats; a match starts with 8 queued players, or with >= 2 once the oldest waited
  30 s, and server-side `BotBrain` bots (Core/AI) take the empty seats.
* Determinism across runtimes: Unity Mono keeps float intermediates in double precision, CoreCLR / IL2CPP do
  not, so the simulation uses `double` only (+ - * / and Sqrt are exact IEEE on every runtime). Never add
  `float`, `MathF` or transcendental functions (Sin, Atan2, Pow...) to Core gameplay code.
  `DeterminismProbeTests` writes a per-tick fingerprint to `Temp/determinism_<runtime>.txt` from Unity's Test
  Runner (Mono) and from `Tools/core_tests` (CoreCLR): the two files must be identical.

## Tests

* `py Tools/core_tests/run_core_tests.py` - Core tests without Unity (Unity's Roslyn + .NET 6, an
  NUnit-compatible shim). Same files run in Unity's Test Runner (`AutoTest Server/unity_tests.bat`).
* `py Tools/compile_check/compile_check.py --editor` - compiles every assembly with Unity's Roslyn.
* `AutoTest Server/` - scenario runner driving the real editor through the bridge (see its README).

## Balance

* `BalanceReportTests` (opt-in: `AUTOCHESS_BALANCE=1`, data file in `AUTOCHESS_GAME_DATA`) simulates 1v1
  duels, mirrored 3v3, a training dummy, random team fights and each synergy on vs off; it writes
  `Temp/balance_report.md` + `.json`.
* `Tools/balance/balance.py` - `report [--candidate]`, `calibrate` (per-unit knobs until each tier is even, attack
  speed until level-7 fights last ~20 s), `apply` (writes `HeroTrait` stats / evolutions and Destinies params,
  YAML text, GUIDs kept). Design numbers live in `Tools/balance/balance_config.json`: unit stats = role
  template x reputation tier x realm (Chaos +8%, Mortal -8%) x knob; synergy params per trait. After `apply`,
  re-export the game data.
