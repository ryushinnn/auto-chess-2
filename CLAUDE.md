# CLAUDE.md - auto-chess-2

Unity 2022.3.62f2 TFT-like auto-battler. Design: `docs/ARCHITECTURE.md`. What changed in the 2026-10-22 refactor,
behaviour changes and open questions: `docs/REFACTOR_NOTES.md`. The user writes Vietnamese.

## Rules of the codebase

- Game rules and battle logic live in `Assets/_main/Scripts/Core` (asmdef `AutoChess.Core`,
  `noEngineReferences`): never use UnityEngine, `System.Random`, `UnityEngine.Random`, real time or
  async there. Randomness = `DeterministicRandom` (match / player / battle streams), time = ticks.
- The Unity layer (`Assets/_main/Scripts/Game`) never mutates game state: input submits a
  `GameCommand` through `GameManager.Submit` / `IGameSession`; views react to `GameManager.EventRaised`
  and read `Session.State`. New player action = command class + `MatchSimulation.Submit` case +
  `CommandCodec` entry + test.
- New hero: attack / skill behaviours in `Core/Combat/Heroes`, register in `AbilityRegistry`, data in a
  `HeroTrait` asset (added to `HeroTraitDB`), animation in a `Mecanim_*` class. Re-export
  `GameData/game_data.json` (menu *AutoChess/Export Game Data*) so the real-data tests cover it.
- ScriptableObject / MonoBehaviour fields are serialized: rename with `[FormerlySerializedAs]`, move a
  script together with its `.meta` (`git mv`), never retype a field.

## Checks after a change

- `py Tools/compile_check/compile_check.py --editor` - must report 0 errors for every assembly.
- `py Tools/core_tests/run_core_tests.py` - core tests without Unity (~5 s).
- With Unity open: `AutoTest Server` toolkit (`/autotest` skill). `py -m auto_touch refresh --wait`
  recompiles the editor without focus; `unity_tests.bat`; scenarios in
  `AutoTest Server/auto_touch/scenarios/autochess/`, campaign `ac_regression_campaign.json`.
- The editor bridge listens on port 8766 (8765 belongs to another project's rig).
