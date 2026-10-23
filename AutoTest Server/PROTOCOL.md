# AutoTest wire protocol (v1)

Contract between the Unity Editor bridge (`Assets/AutoTest/Editor`, asmdef `AutoTest.Editor`) and the Python
toolkit (`AutoTest Server/auto_touch`). Both sides are generic; everything game-specific goes through the game
adapter (`IAutoTestGameAdapter` and friends) on the Unity side and through scenario files / `op` calls on the
Python side.

Design lineage: same architecture as the chirpy-sort rig (`E:/Projects/chirpy-sort/Assets/AutoTest`,
`E:/Projects/chirpy-sort/AutoTest Server/auto_touch`): HttpListener in the editor + main-thread job queue +
log ring buffer + compile status + adapter discovered through `TypeCache`, driven by a stdlib-only Python CLI,
scenario runner and MCP server. It is a smaller, cleaner subset tailored to this project: no OS-level mouse
input, no calibration, no IEC cheat panel / AB / remote-config / Kanboard layers.

## 1. Transport

* Base URL `http://127.0.0.1:8766/` (8765 belongs to the chirpy-sort rig, which may run at the same time).
  Unity: `EditorPrefs` int `AutoTest.Port` (default 8766). Python: `autotest.config.json` `port`, env
  `AUTOTEST_PORT`, CLI `--port`.
* Parameters: query string. `POST` may also carry a JSON object body; its keys are merged over the query keys
  (body wins). Values in the query are strings; the server parses numbers/bools (`1/0/true/false`).
* HTTP method: `GET` for reads, `POST` for anything that mutates (the server accepts both for every route, the
  Python client always uses the conventional one; mutating calls are never retried automatically).
* Every reply is a JSON object, UTF-8, serialized with Newtonsoft (`NullValueHandling.Ignore`).
  Success: `{"ok": true, ...}`. Failure: `{"ok": false, "error": "<message>", "kind": "<kind>"}` with status:

  | status | kind | when |
  |---|---|---|
  | 400 | `bad_request` | missing / invalid parameter (`ArgumentException`) |
  | 404 | `not_found` | unknown route, or the object / member asked for does not exist |
  | 409 | `play_mode_required` / `edit_mode_required` | route needs the other editor mode |
  | 501 | `not_supported` | no game adapter / adapter does not implement the interface |
  | 503 | `timeout` | the main thread did not run the job in time (job is abandoned, never runs late) |
  | 500 | `error` | anything else (message + first stack lines in `"stack"`) |

* Coordinates: screen pixels of the Game view render target, origin **bottom-left** (Unity `Screen` space).

## 2. Unity side architecture (reference for implementers)

* `[InitializeOnLoad]` server, `HttpListener` on `http://127.0.0.1:<port>/`, one accept thread, requests handled
  on the thread pool. Stop on `AssemblyReloadEvents.beforeAssemblyReload` and `EditorApplication.quitting`;
  restart after every domain reload when enabled (`EditorPrefs` bool `AutoTest.AutoStart`, default true).
  Restart must not depend on `EditorApplication.delayCall` only (it does not fire while Unity is unfocused):
  use an `EditorApplication.update` retry loop.
* Main-thread dispatcher: `MainThread.Run<T>(Func<T>, timeoutMs)` enqueues a job pumped from
  `EditorApplication.update`; a job that timed out is marked abandoned and is skipped, never executed late.
  Jobs must never wait for frames. Multi-frame work (drag, screenshot file, test run, play transitions) is an
  `EditorApplication.update` state machine that the client polls.
* Routes are registered in a table (`path -> handler`, plus flags `MainThread`, `PlayModeRequired`,
  `TimeoutMs`) so new endpoints are one line. `/logs`, `/compile-status`, `/ping` never touch the main thread.
* Log ring buffer (`Application.logMessageReceivedThreaded`): 2000 entries, monotonic `index`, message clipped to
  2000 chars, stack to 1500.
* Compile status: `CompilationPipeline.compilationStarted / assemblyCompilationFinished / compilationFinished`,
  last result persisted in `SessionState` so it survives the reload after a successful compile.
* Menu `Tools/AutoTest/`: `Start Server`, `Stop Server`, `Autostart Server` (toggle), `Open Output Folder`.
* Output folder for screenshots and Unity test reports: `<project>/AutoTest Server/auto_touch/out/` (created on
  demand; never under `Assets/`).
* Game adapter discovery: first concrete type from `TypeCache.GetTypesDerivedFrom<IAutoTestGameAdapter>()`,
  instantiated with `Activator.CreateInstance`, cached per domain. The adapter lives in the game's own editor
  code (Assembly-CSharp-Editor) so `AutoTest.Editor` never references game code.

### Adapter interfaces (namespace `AutoTest`)

```csharp
public interface IAutoTestGameAdapter {
    string GameId { get; }                 // echoed in /status and /state
    bool IsAvailable();                    // false while the game scene / session is not ready
    object GetState();                     // READ-ONLY snapshot, JSON-serializable (dictionaries, lists, DTOs)
}
public interface IAutoTestGameOps {        // optional
    IReadOnlyList<string> Ops { get; }     // names listed by /game-op?op=list
    object GameOp(string op, IReadOnlyDictionary<string, string> args); // throw ArgumentException for bad args
}
public interface IAutoTestInvariants {     // optional
    IReadOnlyList<string> Rules { get; }
    List<InvariantFinding> Check(ISet<string> only);   // only == null -> all rules
}
public sealed class InvariantFinding { public string rule, severity /* high|medium|low */, detail; public object data; }
public interface IAutoTestTargets {        // optional: things a scenario may click/drag by id
    List<TargetInfo> GetTargets();
}
public sealed class TargetInfo {
    public string id, kind, label, path;   // id is stable for the current state, e.g. "shop:2", "bench:0", "board:1,3"
    public float[] center;                  // screen px, bottom-left origin
    public float[] rect;                    // xMin,yMin,xMax,yMax screen px
    public bool visible, interactable;
    public Dictionary<string, object> data;
}
```

## 3. Routes

| route | method | mode | params | reply (besides `ok`) |
|---|---|---|---|---|
| `/ping` | GET | any, no main thread | – | `serverVersion` |
| `/status` | GET | any | – | `serverVersion, project, unityVersion, isPlaying, isPaused, isCompiling, isUpdating, playModeChanging, timeScale, frameCount, activeScene, lastCompileErrors, adapter{present, available, gameId}, port` |
| `/compile-status` | GET | any, no main thread | – | `isCompiling, lastCompile{startedAtUtc, finishedAtUtc, errorCount, warningCount, errors[{assembly,file,line,column,message}] (max 50)}` |
| `/refresh` | POST | edit (exits play first when `exitPlay=1`) | `exitPlay` | `requested: true` — runs `AssetDatabase.Refresh()` + `CompilationPipeline.RequestScriptCompilation()`; works while the editor is unfocused. Client polls `/status` (`isCompiling`) then `/compile-status`. |
| `/play` | POST | any | `on` (1/0), `scene` (asset path, optional: opened in edit mode before entering) | `isPlaying, requested, changed` — returns before the domain reload; client polls `/status` until `isPlaying == on` and `playModeChanging == false`. |
| `/pause` | POST | play | `on` | `isPaused` |
| `/step` | POST | play | – | `frameCount` (EditorApplication.Step) |
| `/timescale` | GET/POST | any | `value` (0–100) | `timeScale` |
| `/logs` | GET | any, no main thread | `since` (index), `level` (`error`/`warning`/`all`, default all), `limit` (default 200) | `nextIndex, dropped, redCount, entries[{index, time, type, message, stackTrace}]` — `type` ∈ Log, Warning, Error, Assert, Exception; `redCount` = Error+Assert+Exception among returned entries |
| `/logs/clear` | POST | any | – | `nextIndex` |
| `/screenshot` | POST | play | `name` (file stem, default timestamp), `superSize` (1–4) | `path` (absolute), `pending: true` — the file appears at end of frame; client polls for it. |
| `/hierarchy` | GET | any | `root` (path), `depth` (default 3), `includeInactive` (default 1), `components` (1/0) | `nodes[{name, path, active, activeInHierarchy, components[], children[...]}]` |
| `/ui/components` | GET | play | `kinds` (csv of `button,toggle,slider,input,dropdown,scroll,text,image`; default selectables), `root`, `match` (substring of path or caption), `includeInactive` | `items[{kind, name, path, caption, active, interactable, rect[4], center[2], visible}]` |
| `/ui/find` | GET | play | `path` (exact hierarchy path, `/`-separated, scene roots first) or `text` (caption substring) or `match` (path substring) | `found, path, active, activeInHierarchy, interactable, caption, components[], rect, center` (`found:false` is still 200) |
| `/ui/click` | POST | play | `path` / `text` / (`x`,`y`) | `executed, targetPath, mode ("ui"/"world"/"none"), message` — EventSystem raycast + `ExecuteEvents` pointerEnter/Down/Up/Click/Exit. A target given by path is clicked at its rect center (and the raycast must reach that object or a child, else `executed:false, message:"blocked by <path>"`). World objects are reached through a `PhysicsRaycaster` on the camera. |
| `/input/drag` | POST | play | start `x0,y0` or `from` (path), end `x1,y1` or `to` (path), `steps` (2–120, default 12), `frames` per step (1–10, default 1) | `started, dragTarget` — state machine: pointerDown, initializePotentialDrag, beginDrag, drag…, pointerUp, drop, endDrag. |
| `/input/drag-status` | GET | any | – | `active, done, result{executed, dragTarget, dropTarget, fired[]}` |
| `/world/raycast` | GET | play | `x`, `y` | `eventSystem[{path}], physics[{path, distance, point[3]}]` |
| `/world/screen-point` | GET | play | `path` | `found, world[3], screen[2], onScreen` |
| `/reflect` | GET/POST | `types`/`members` any, others play-or-edit | `op` = `types\|members\|get\|set\|call`, `type` (full or short name), `member`/`method`, `args` (JSON array), `value` (JSON), `static` (1 = static member), `path` (GameObject path, for an instance component of `type`) | `types[]` / `members[]` / `value` / `returned` — a method that throws returns 200 with `threw:true, exception, stack` |
| `/menu` | POST | edit | `item` (menu path) | `executed` |
| `/state` | GET | play (adapter decides) | `path` (optional dotted path into the state) | `gameId, available, state` (or `value` when `path` given) |
| `/game-op` | POST | play | `op` + any params (`op=list` lists ops) | `result` (adapter object) |
| `/invariants` | GET | play | `rules` (csv) | `rules[], findings[{rule, severity, detail, data}], highCount` |
| `/targets` | GET | play | `kind` (filter) | `targets[TargetInfo]` |
| `/tests/run` | POST | edit | `mode` (`EditMode` default / `PlayMode`), `filter` (test name substring or full name), `assembly` | `started, runId` — uses `UnityEditor.TestTools.TestRunner.Api.TestRunnerApi`. |
| `/tests/status` | GET | any | `runId` | `running, finished, total, passed, failed, skipped, durationSec, results[{name, fullName, result, message, durationSec}]` (failed first; results of the last run survive domain reloads via `SessionState`/file) |

## 4. Python toolkit conventions

* Package `auto_touch` (stdlib only, Python 3.10+, run as `py -m auto_touch ...` from `AutoTest Server/`).
* Exit codes: `0` ok/PASS, `2` server unreachable, `3` invalid input (scenario file, arguments), `4` FAIL
  (assertion / step / red logs / invariant), `5` Unity compile errors, `10` installed/needs Unity compile.
* Config `auto_touch/autotest.config.json` (JSONC): `host, port, http_timeout, server_retries,
  server_retry_delay, poll_ms, play_timeout_s, compile_timeout_s, default_timescale, red_log_ignore[],
  fail_on_red_logs, invariants_every_step, fail_on_invariants (never|high|medium|low), keep_diagnostics,
  scenarios_dir, out_dir`. Precedence: defaults < file < env (`AUTOTEST_HOST`, `AUTOTEST_PORT`) < CLI flags.
* Outputs (all under `AutoTest Server/auto_touch/`, gitignored): `out/screens/`, `out/diagnostics/`,
  `out/reports/`, `out/unity-tests/`.
