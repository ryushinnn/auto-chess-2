### AUTO CHESS

A personal project I worked on in my free time, the idea borrowed from Teamfight Tactics.

https://github.com/user-attachments/assets/ef6108f1-7f0f-447c-b8fa-d3e81be2fab3

### Project layout

| folder | what |
|---|---|
| `Assets/_main/Scripts/Core` | `AutoChess.Core`: engine-free game rules and battle simulation (deterministic, server-ready) |
| `Assets/_main/Scripts/Game` | Unity client: data assets, composition root (`GameManager`), views, UI, input |
| `Assets/_main/Scripts/Editor` | data exporter, scene / prefab migration, AutoTest game adapter |
| `Assets/_main/Tests/EditMode` | NUnit tests of the core |
| `Assets/AutoTest` | editor bridge of the auto-test rig (HTTP, port 8766) |
| `AutoTest Server` | auto-test toolkit: CLI, scenario runner, campaigns, MCP server (`README.md` inside) |
| `GameData/game_data.json` | game data exported from the ScriptableObjects (menu *AutoChess/Export Game Data*) |
| `Tools/` | `compile_check` (compile without the editor), `core_tests` (run the core tests without Unity) |

See [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) for the design and the path to multiplayer, and
[docs/REFACTOR_NOTES.md](docs/REFACTOR_NOTES.md) for the refactor hand-over notes.

### Checks

```
py Tools/compile_check/compile_check.py --editor
py Tools/core_tests/run_core_tests.py
"AutoTest Server/unity_tests.bat"
"AutoTest Server/campaign.bat" auto_touch/scenarios/autochess/ac_regression_campaign.json
```
