# Ghi chú bàn giao: refactor Core + hệ thống AutoTest (2026-10-22)

Tài liệu này ghi lại **mọi thứ đã thay đổi** trong đợt refactor, **vì sao**, **chạy kiểm tra thế nào**, và
**những việc còn để ngỏ** để bạn (hoặc một phiên Claude sau) làm tiếp. Thiết kế chi tiết: `docs/ARCHITECTURE.md`.
Quy ước cho Claude: `CLAUDE.md`.

## 0. Trạng thái repo

- Nhánh **`refactor/core-simulation`**, commit `refactor` (+ commit ghi chú này). `main` chưa bị đụng tới.
  Gộp vào main: `git checkout main && git merge refactor/core-simulation`.
- **Không commit** (vẫn nằm trong working tree, là thay đổi có sẵn của bạn trước khi tôi bắt đầu, do nâng
  Unity 2022.3.22f1 → 2022.3.62f2): `ProjectSettings/ProjectVersion.txt`, `Packages/manifest.json`,
  `Packages/packages-lock.json`, `Assets/Settings/URP-HighFidelity-Renderer.asset` (và vài file chỉ khác
  line-ending). Bạn tự quyết định commit chúng.
- Kết quả kiểm tra lúc bàn giao: compile 5 assembly 0 lỗi / 0 warning; 58/58 test Core (offline và trong
  Unity Test Runner); 135/135 test của bộ Python; campaign hồi quy 11/11 PASS trên Unity thật.

## 1. Kiến trúc mới trong một đoạn

Toàn bộ luật chơi + mô phỏng trận đấu chuyển sang assembly C# thuần **`AutoChess.Core`**
(`Assets/_main/Scripts/Core`, asmdef `noEngineReferences`, không plugin): tick cố định 30 Hz, RNG có seed
(xoshiro128**), nhận **Command** (mua tướng, di chuyển, trang bị, sẵn sàng, cheat...) và phát **Event**
(GoldChanged, UnitMoved, UnitDamaged...). Lớp Unity (`Assets/_main/Scripts/Game`) chỉ còn: dữ liệu
ScriptableObject, `GameManager` (composition root), các View đọc state + nghe event, và Input sinh Command.
Cùng seed + cùng chuỗi command ⇒ trận giống hệt từng bit (đã có test chứng minh) — nền cho server-authoritative /
lockstep sau này.

## 2. File cũ → file mới

Mọi script được **đổi tên/di chuyển kèm `.meta`** (giữ GUID ⇒ prefab, scene, asset không mất tham chiếu).
Tên field serialized giữ nguyên; 3 field đổi tên dùng `[FormerlySerializedAs]` (`Mecanim.renderer` →
`rendererRoot`, `*_Destiny.name` → `nameText`).

| Cũ | Mới | Ghi chú |
|---|---|---|
| `Hero/Hero.cs` | `Game/Units/UnitView.cs` | view gốc của tướng (model + các part) |
| `Hero/BattleHero.cs` | `Game/Units/BattleUnitView.cs` | bám theo `CombatUnit`, nội suy giữa 2 tick |
| `Hero/LineUpHero.cs` | `Game/Units/RosterUnitView.cs` | tướng sở hữu trên bench/bàn |
| `Hero/HeroPicker.cs` | `Game/Units/UnitDragHandler.cs` | kéo-thả qua EventSystem (thay OnMouse*) |
| `Hero/Abilities/HeroAbility.cs` | `Game/Units/UnitPart.cs` | |
| `HeroAttributes.cs` | `Game/Units/UnitStatusBars.cs` | chỉ còn thanh máu/năng lượng + số nổi |
| `HeroInventory.cs` | `Game/Units/UnitItemsView.cs` | chỉ còn icon trang bị |
| `HeroMark.cs` | `Game/Units/UnitMarksView.cs` | |
| `HeroRotation.cs` | `Game/Units/UnitRotation.cs` | |
| `HeroStatusEffects.cs` | `Game/Units/UnitAirborneView.cs` | chỉ còn hiệu ứng hất tung |
| `Hero/Mecanim/*` | `Game/Units/Mecanim/*` | `UseSkill(int variant)`, `SetAttackSpeed` |
| `Hero/HeroTrait.cs`, `Features/Item`, `Stage`, `Destinies/*Config`, `Attributes/AttributeModifier.cs`, `DB/*` | `Game/Data/...` | ScriptableObject, field không đổi; DB tra cứu bằng Dictionary |
| `GameManager.cs` | `Game/GameManager.cs` | composition root, `Restart(seed)`, nút cheat (Odin) |
| `Features/BattleField.cs` | `Game/Battle/BattleView.cs` | spawn view trận, VFX, đạn, vùng |
| `Features/LineUp.cs` | `Game/Roster/RosterView.cs` | đồng bộ view với model sau mỗi event |
| `Features/Environment/DamageOverTimeArea.cs` | `Game/Battle/AreaView.cs` | chỉ còn hình vùng |
| `Map/Map.cs`, `Map/Deck.cs` | `Game/Board/BoardSpace.cs`, `BenchSpace.cs` | toạ độ thế giới của bàn/bench |
| `Map/MapVisual.cs`, `Cell`, `HexCell`, `SquareCell` | `Game/Board/` | ô bị chiếm lấy trực tiếp từ state mỗi frame |
| `UI/*`, `UI/Hero/*`, `VFX/*` | `Game/UI/*`, `Game/UI/Unit/*`, `Game/Vfx/*` | UI gửi Command, nghe Event |
| `Translator.cs`, `Common/TextureChanger.cs`, `MouseDebugger.cs`, `_Editor/*` | `Game/Common`, `Game/Dev`, `Editor/` | |

**Đã xoá** (logic chuyển vào Core hoặc là code chết): `GameConfigs.cs` (→ `Core/Data/GameRules.cs`),
`Consts.cs`, `Common/Factories/*` (→ `AbilityRegistry`, `TraitEffects`), `AStar/*` (→ `Core/Board/PathFinder.cs`),
`BehaviourTree/*`, `Hero/BTNodes/*`, `HeroBT.cs` (→ `BattleSimulation.Think`), 32 `AttackProcessor_*`/`SkillProcessor_*`
(→ `Core/Combat/Heroes/*.cs`), `HeroAttack/HeroSkill/HeroMovement` (→ state machine trong `CombatUnit`),
`Damage.cs`, `EffectOverTime.cs`, `DestinyProcessor*` (→ `Core/Combat/TraitEffects.cs`), `Destinies.cs`,
`Inventory.cs`, `Level.cs`, `Progress.cs`, `Shop.cs` (→ `Core/Rules/*`, `MatchSimulation`), `Map/Node*`,
`Direction.cs` (→ `HexCoord`, `UnitLocation`), `__Groceries/TexturePostprocessor.cs` (trùng lặp).

**Prefab/scene** (chạy menu *AutoChess/Migrate Scene and Prefabs*, idempotent): gỡ 3 component logic khỏi
`Hero.prefab`, gỡ 5 component logic + 1 script đã mất từ trước khỏi `Arena.unity`, gán `GameManager.stages`,
thêm `PhysicsRaycaster` vào Main Camera.

## 3. Bản đồ Core (`Assets/_main/Scripts/Core`)

- `Common/` enum dùng chung (`Realm`, `Role`, `Reputation`, `HeroRank`, `DamageType`, `TeamSide`, `MatchPhase`...), `Vec2`, `DeterministicRandom`.
- `Data/` `UnitDefinition`, `ItemDefinition`, `TraitDefinition`, `StageDefinition`, `GameRules` (mọi con số;
  mặc định = game gốc), `GameDatabase`, `StatKeys`, `MiniJson`, `GameDataJson`.
- `Board/` `HexCoord` (even-r, khoảng cách cube), `HexGrid`, `PathFinder` (A* + binary heap).
- `Players/` `PlayerState`, `UnitInstance`, `UnitLocation` (`bench:3` / `board:1,4`), `ShopState`...
- `Rules/` `Economy`, `Shop`, `Roster`, `Items`, `Traits`, `Rewards`, `MatchContext`, `IUnitPool`.
- `Match/` `MatchSimulation` (vòng pha + `Submit`), `MatchState`, `MatchConfig`, `MatchRecorder` (ghi/phát lại).
- `Combat/` `BattleSimulation`, `CombatUnit`, `UnitStats`, `Effects`, `Abilities`, `AbilityRegistry`,
  `TraitEffects`, `Heroes/*` (Aatrox, Marksmen, Fighters, Mages), `BattleEvents`.
- `Commands/`, `Events/`, `Net/` (`CommandCodec`, `NetWriter/Reader`, `StateHasher`), `Session/` (`IGameSession`, `LocalGameSession`).

## 4. Thay đổi hành vi (sửa lỗi) so với bản gốc

1. Tướng đã chết vẫn đánh/tung chiêu nốt (processor chạy tiếp) → nay huỷ khi chết.
2. Nhận sát thương khi đã chết gọi `Die()` lần nữa (DoT, đòn nhiều phần) → `OnDeath` lặp, Marksman buff lặp → đã chặn.
3. Đạn Ashe trúng **mục tiêu hiện tại** của người bắn lúc chạm (NRE nếu null) → nay trúng đúng mục tiêu được bắn, bỏ qua nếu đã chết.
4. Bàn cờ của trận và của đội hình dùng chung trạng thái ô → ô "Targeted" sót lại chặn kéo-thả ở vòng sau → nay mỗi trận có lưới riêng.
5. Thời gian từng đoạn di chuyển tính từ vị trí xuất phát (đoạn sau chậm dần) → nay tốc độ đều.
6. Đường đi A* đi xuyên qua tướng khác → nay tránh ô đang có tướng (có phương án dự phòng nếu bị chặn hết).
7. Jinx: khoảng cách giữa các tên lửa bị ép kiểu `int` (0.2 → 0) nên bắn cùng frame → nay cách 0.2 s đúng tham số.
8. Trang bị: sau khi sắp xếp lại có thể ghép đè mất chỉ số của item ghép; khi gộp 3 tướng thì trang bị của 2 bản bị mất → nay chỉ số tính lại từ danh sách item; trang bị được chuyển sang tướng giữ lại (thừa thì về kho).
9. `Mecanim` con dùng biến coroutine riêng nên `InterruptSkill` không dừng được animation chiêu → đã hợp nhất (`PlaySkillRoutine` + `OnSkillInterrupted`).
10. `VfxAutoDestroy` chỉ chạy ở `Start` → VFX lấy lại từ pool không tự huỷ → nay chạy ở `OnEnable`.
11. Hiệu ứng (vd da đá Malphite) bị thay thế không huỷ VFX cũ → đã huỷ.
12. Đội hình trống: trận cũ chờ hết 60 s mới thua → nay kết thúc ngay.
13. Phím Space: pha chuẩn bị gửi `ReadyCommand` (khi mọi người chơi sẵn sàng, pha rút còn 1 s — giống hack cũ `timeLeft = 1`); trong trận chỉ là cheat bỏ qua (chỉ khi cho phép cheat).
14. Mua tướng trong lúc đánh: gộp chỉ dùng tướng trên bench; gộp với tướng trên bàn được thực hiện đầu vòng chuẩn bị kế tiếp (đúng kiểu TFT) — bản cũ gộp cả tướng đang ẩn trên bàn.
15. Trang bị cho tướng **trên bàn** chỉ được trong pha chuẩn bị; tướng trên bench lúc nào cũng được.
16. Mỗi người chơi có luồng RNG riêng (shop, phần thưởng, gói khởi đầu); trận đấu có seed riêng.
17. Click UI không còn "xuyên" xuống tướng 3D phía sau (OnMouse* cũ bỏ qua UI).
18. `MouseDebugger` mặc định không log nữa (bật bằng field `logClicks`).

## 5. Giữ nguyên có chủ đích nhưng nghi là lỗi dữ liệu/thiết kế — cần bạn quyết định

- **Aatrox_Dark / Aatrox_Light không bao giờ dùng được kỹ năng**: `energyRegenPerAttack = energyRegenPerHit = 0` trong HeroTrait.
- **Zed**: kỹ năng bỏ qua `base_dmg` (param 0) và toàn bộ phần AoE (`range`, `stun_others`) — code gốc đã comment "review later".
- **Aatrox_Light**: sát thương cơ bản của kỹ năng luôn 0 (field chưa từng được gán); mô tả trong asset đánh số `#[...]` lệch so với tham số.
- **Yone**: cộng dồn "Suy yếu" chỉ hiển thị, hiệu ứng không tăng theo cộng dồn; hồi năng lượng khi chém hạ gục bị nuốt (không hồi năng lượng khi đang tung chiêu).
- **Vòng 1-1 của Stage 1** là đợt "boss" (Malphite S với `hpScale` 7 ≈ 88k máu, Zed/Yone S...): đội hình thường gần như không thể thắng.
- Pha chuẩn bị/khởi đầu kéo dài 99999 s (giữ như cũ; người chơi bấm Space để bắt đầu).
- `GameManager.hackItems` (nút dev "AddHackItems") đang trống — field cũ trên `Inventory` chưa từng được lưu trong scene.
- Cảnh báo TMP *"Unable to add the requested character to font asset [gi SDF]..."* có từ trước (atlas font không readable).

## 6. Hệ thống AutoTest (mô phỏng theo chirpy-sort)

- **Bridge trong Editor**: `Assets/AutoTest/Editor` (asmdef `AutoTest.Editor`, không phụ thuộc code game), HTTP
  `127.0.0.1:8766` (8765 là của chirpy-sort). Hợp đồng: `AutoTest Server/PROTOCOL.md`. Menu *Tools/AutoTest*.
- **Game adapter**: `Assets/_main/Scripts/Editor/AutoTest/AutoChessAutoTestAdapter.cs` — state JSON, op chơi/cheat
  (`restart`, `buy`, `move`, `equip`, `ready`, `gold`, `unit`, `set-shop`, `fast-forward`...), 12 invariant (gồm
  kiểm tra **view khớp model**), target để click/kéo (`shop:i`, `bench:i`, `board:r,c`, `unit:id`, `item:id`).
- **Bộ Python** `AutoTest Server/auto_touch` (stdlib): CLI, chạy scenario/campaign, matrix, MCP server
  (`.mcp.json`), 135 self-test; các file `.bat`; skill `/autotest` (`.claude/skills/autotest`, nguồn ở
  `AutoTest Server/skills/autotest/SKILL.md` — sửa nguồn rồi chạy `install_skill.bat`).
- **Kịch bản game** `AutoTest Server/auto_touch/scenarios/autochess/`: boot, gói khởi đầu qua UI, mua/gộp tướng,
  kéo-thả bàn cờ, kéo trang bị, mua XP, khoá/làm mới shop, bảng thông tin, trọn 1 vòng, thắng trận, chơi hết
  stage; campaign `ac_regression_campaign.json` (~1 phút).
- Lệnh hay dùng (từ thư mục `AutoTest Server/`, đặt `PYTHONIOENCODING=utf-8`):
  `py -m auto_touch status` · `refresh --wait` (compile lại Unity không cần focus) · `play on|off` ·
  `state --path player` · `op restart seed=7` · `run auto_touch/scenarios/autochess/ac_round_flow.json` ·
  `campaign auto_touch/scenarios/autochess/ac_regression_campaign.json` · `unity-tests` · `screenshot`.
- Lần đầu sau khi mở Unity, server chỉ chạy khi Unity đã compile (cần focus cửa sổ Unity một lần); sau đó dùng `refresh`.

## 7. Công cụ (`Tools/`)

- `compile_check/compile_check.py --editor`: compile mọi asmdef dự án + Assembly-CSharp(-Editor) bằng Roslyn của
  Unity, không cần Editor. `--only <Assembly>`, `--warnings`, `--overlay <dir>` (compile thử một thay đổi lớn
  nằm ngoài Assets trước khi áp dụng).
- `core_tests/run_core_tests.py [--filter X]`: chạy test Core không cần Unity (Roslyn + .NET 6 của Unity, shim
  NUnit tương thích nguồn). Test dữ liệu thật đọc `GameData/game_data.json`.
- `staging/apply_staging.py`: áp một thay đổi lớn từ `.staging/` vào project (git mv kèm `.meta`).
- Menu Editor: *AutoChess/Export Game Data* (xuất `GameData/game_data.json` — chạy lại mỗi khi sửa HeroTrait/
  Item/Stage/Destiny), *AutoChess/Migrate Scene and Prefabs*.

## 8. Cách làm các việc thường gặp

- **Thêm/sửa tướng**: kỹ năng/đòn đánh ở `Core/Combat/Heroes/*.cs`, đăng ký trong `AbilityRegistry`; dữ liệu ở
  `HeroTrait` (thêm vào `HeroTraitDB`); animation ở `Mecanim_*`. Rồi Export Game Data + `run_core_tests.py`
  (test "mọi tướng đánh đủ trận, tướng có năng lượng đều tung chiêu").
- **Thêm hành động người chơi**: lớp Command mới (`Core/Commands/GameCommands.cs`) + nhánh trong
  `MatchSimulation.Submit` + mã hoá trong `CommandCodec` + test round-trip; UI chỉ gọi `GameManager.Submit`.
- **Thêm hiển thị**: nghe `GameManager.EventRaised`, đọc `Session.State`; không sửa state từ view. View có
  trạng thái riêng thì implement `ISessionResettable` (để `Restart` dọn sạch).
- **Đổi số cân bằng chung**: `Core/Data/GameRules.cs` (giá, xp, odds shop, thời gian pha, hằng số chiến đấu).

## 9. Việc tiếp theo gợi ý (multiplayer)

1. Chọn authority: server C# riêng (.NET hoặc Unity headless) chạy `MatchSimulation` cho N người; Nakama
   (đã có trong `Assets/Packages`) lo tài khoản, matchmaking, relay.
2. Viết `IGameSession` dạng mạng: `Submit` gửi `CommandCodec.Encode(cmd)`, server trả kết quả + stream event
   hoặc snapshot; client giữ bản sao `MatchState` chỉ-đọc cho view — **view không phải sửa**.
3. Vòng PvP: ghép cặp người chơi, `BattleSetups.FromBoard(home)` + `BattleSetups.FromBoardMirrored(visitor)`;
   thêm máu người chơi / loại khỏi trận vào `MatchSimulation`.
4. Pool tướng dùng chung (`IUnitPool` mới thay `UnlimitedUnitPool`); so `StateHasher` để phát hiện lệch đồng bộ.
5. (Tuỳ chọn) Tạo asmdef cho DOTween (DOTween Utility Panel → Create ASMDEF) rồi đưa `Game/` vào asmdef riêng
   để compile nhanh hơn và khoá phụ thuộc chặt hơn.
