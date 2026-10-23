# CLAUDE.md - auto-chess-2

Unity 2022.3.62f2 TFT-like auto-battler. Design: `docs/ARCHITECTURE.md`. What changed in the 2026-10-22 refactor,
behaviour changes and open questions: `docs/REFACTOR_NOTES.md`. The user writes Vietnamese.

## Quy ước làm việc (user: TienLD)

- **Commit message**: dòng tiêu đề ngắn gọn. Nhiều task trong 1 commit thì chỉ ghi task lớn nhất kèm `...`
  (vd `refactor core simulation...`). Phần description viết đầy đủ tuỳ ý.
- **Không tự tạo branch mới.** Nếu user không chỉ định khác thì commit và push thẳng lên branch đang chọn
  (thường là `dev/main`, hiện repo dùng `main`).
- **Sau mỗi lần sửa code** chạy `py Tools/compile_check/compile_check.py --editor` (mục "Checks after a change");
  phải 0 error mới báo xong. Tool lấy từ `D:\PROJECT\_COMPANY\chirpy-sort\Tools`; cần thêm tool thì tham khảo ở đó.
- **Sửa prefab / scene / asset YAML bằng script Python**, giữ nguyên `fileID` / GUID (cách làm như chirpy-sort).
  Override của prefab instance có thể xuống dòng trước `type: 3}`, nên match bằng regex `,\s*type:`. Script dài thì viết
  file `.py` tạm rồi chạy, đừng dùng heredoc bash.
- **Tên session**: mỗi khi bắt đầu tác vụ mới, đổi tên session (`set_session_title` "self") thành tóm tắt siêu ngắn của
  tác vụ. Yêu cầu vẫn thuộc tác vụ mà tên session đang nói tới (fix, update, làm tiếp...) thì giữ nguyên tên; chỉ đổi khi
  chuyển sang tác vụ mới hẳn.

## Rules of the codebase

- Game rules and battle logic live in `Assets/_main/Scripts/Core` (asmdef `AutoChess.Core`,
  `noEngineReferences`): never use UnityEngine, `System.Random`, `UnityEngine.Random`, real time or
  async there. Randomness = `DeterministicRandom` (match / player / battle streams), time = ticks.
- The Unity layer (`Assets/_main/Scripts/Game`) never mutates game state: input submits a
  `GameCommand` through `GameManager.Submit` / `IGameSession`; views react to `GameManager.EventRaised`
  and read `Session.State`. New player action = command class + `MatchSimulation.Submit` case +
  `CommandCodec` entry + test.
- New hero: attack / skill behaviours in `Core/Combat/Heroes`, register in `AbilityRegistry` (+ `HeroIds`), a
  `Mecanim_*` class, then an entry in `Tools/heroes/new_heroes.json` and menu *AutoChess/Hero Models/Set Up New
  Heroes* (prefab, animator, `HeroTrait` in `HeroTraitDB`; docs/ARCHITECTURE.md "Unity client"). Stats: knob in
  `Tools/balance` (`balance.py calibrate` / `apply`). Re-export `GameData/game_data.json` (menu *AutoChess/Export
  Game Data*) so the real-data tests cover it. Skill VFX: `PlayVisual` in Core + a recipe in `VfxBuilder`.
- Items: edit `Tools/items/items.json` (stats, passive params / text; a new passive = an `ItemEffect` in
  `Core/Combat/ItemEffects.cs`), check with `balance.py report --candidate --sections items`, then menu
  *AutoChess/Items/Apply Item Config*, re-export, `py Tools/items/item_doc.py`.
- ScriptableObject / MonoBehaviour fields are serialized: rename with `[FormerlySerializedAs]`, move a
  script together with its `.meta` (`git mv`), never retype a field.
- Online = lockstep (docs/ARCHITECTURE.md "Online play"): every client and the server run the same Core on the
  same frames, so Core must give bit-identical results on Unity Mono, IL2CPP and .NET 8. Numbers in Core are
  `double` (never `float` / `MathF` / Sin / Pow...); no Dictionary / HashSet iteration whose order matters
  (string hashes are randomized per process on .NET). A Core change that touches the protocol bumps
  `Lockstep.Version`. Server: sibling repo `auto-chess-2-server` (compiles Core + Net from this repo).
- Changing units / items / stages: re-export `GameData/game_data.json` - the server refuses clients whose data
  hash differs from the file it loaded.
- 2+ players = TFT rules (docs/ARCHITECTURE.md "TFT layer"), 1 player = the old PvE campaign (many tests and
  the AutoTest `restart` op default to it: `bots=0 untimed=true`; the 8-player lobby is `bots=7 untimed=false`).
  Gameplay numbers: `GameRules` (code) + ScriptableObjects. Unit stats and synergy params come from
  `Tools/balance` (edit `balance_config.json`, then `balance.py apply` + re-export; docs/ARCHITECTURE.md "Balance").
- New HUD / screens: build them in code with `UiBuilder` like `MatchHud` / `LobbyUI` (no prefab YAML needed).
- Scenes: Lobby (menus, matchmaking) -> Arena (the match only). Stages / cell size / UI font: `ClientConfig`
  (Resources), not the scene. AutoTest scenarios run in the Arena; `ac_lobby_flow` starts from the Lobby.

## Checks after a change

- `py Tools/compile_check/compile_check.py --editor` - must report 0 errors for every assembly.
- `py Tools/core_tests/run_core_tests.py` - core tests without Unity (~5 s).
- Online changes: the bots of `auto-chess-2-server` (`dotnet run --project tools/AutoChess.Bots -- --self-host`)
  and, for cross-runtime determinism, `DeterminismProbeTests` in Unity + core_tests (files must match).
- With Unity open: `AutoTest Server` toolkit (`/autotest` skill). `py -m auto_touch refresh --wait`
  recompiles the editor without focus; `unity_tests.bat`; scenarios in
  `AutoTest Server/auto_touch/scenarios/autochess/`, campaign `ac_regression_campaign.json`.
- The editor bridge listens on port 8766 (8765 belongs to another project's rig).
