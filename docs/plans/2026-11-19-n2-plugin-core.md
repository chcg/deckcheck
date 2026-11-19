# N2: Notepad++ Plugin Core Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A Notepad++ plugin (x64) that checks the model the current file belongs to, on request and on save, in the background, and lists the findings in a docked panel whose rows jump to the field.

**Architecture:**
- **Toolchain, proven by a spike (2026-11-19), with nothing downloaded or installed.** The plugin logic is C# 5, compiled by the .NET Framework's own `csc.exe` into `DeckcheckNpp.dll`. A hand-written IL file holds the six native exports Notepad++ calls; each installs an `AssemblyResolve` hook and forwards to C#. The Framework's `ilasm.exe` assembles it into `deckcheck.dll`. A generated `.res` supplies the version resource Plugins Admin checks.
- **Engine.** The plugin runs `engine\deckcheck.exe --for <file> [--main-deck ...] --format json` (N1), with the file's folder as the working directory. The process runs at below-normal priority inside a Job object, so killing the job kills the whole tree.
- **Testing.**
  - Unit tests: a small C# console runner (`tests/Run.cs`) against a fake engine.
  - End-to-end: pytest drives a portable copy of Notepad++ through Win32 messages. It reads plugin events from a trace file that the plugin writes only when `DECKCHECK_NPP_TRACE` is set.

**Tech Stack:** C# 5 (.NET Framework 4.8 `csc.exe`), IL (`ilasm.exe`), WinForms, `System.Web.Extensions` `JavaScriptSerializer`, Win32 Job objects; Python 3.12 + pytest for the build helper, the version resource and the end-to-end tests.

**Spec:** `docs/specs/2026-11-17-notepadpp-design.md` (§2 architecture, §4 what the engineer gets, §5 do no harm, §7 packaging)

## Global Constraints

- Branch `n2-plugin`, cut from `n1-engine`. Never push. Source in `editors/notepadpp/`; build output in `editors/notepadpp/out/` (git-ignored).
- C# 5 only: no string interpolation, `?.`, `nameof`, expression-bodied members, or `out var`.
- x64 only. The DLL at the zip root is `deckcheck.dll`; the plugin folder is `deckcheck`; the engine is `engine\deckcheck.exe`.
- **Do no harm (spec §5).** The UI thread never waits on the engine. The engine runs at below-normal priority. Its process tree is killed on a new check, on Notepad++ shutdown and on *Disable*. Any failure is reported once in the panel, then the plugin steps aside.
- The plugin refuses JSON whose `version` is not 1.
- End-to-end runs use a portable copy of the installed Notepad++ in the scratch directory, started with `-multiInst -nosession`. They never touch the real install or its settings.
- No downloads and no installs. Avoid shell heredocs for code edits.

## Review Focus

1. **A check started while another runs.** The first engine process tree must be dead before the second starts, and the first's results must never reach the panel.
2. **Notepad++ closing during a check.** No orphaned `deckcheck.exe` stays behind (the Job object's kill-on-close covers a crash of Notepad++ too).
3. **A missing, crashing or wrong-version engine.** One message in the panel; Notepad++ stays usable; the next check tries again.
4. **A finding in an include that is not open.** The jump opens that file, then goes to the line and selects the field.
5. **Saving a file that belongs to no checked model and is not a deck** (a `.txt` note). No check starts.

---

### Task 0: Branch, ignores, folder layout

- [ ] **Step 1:** `git checkout -b n2-plugin` (from `n1-engine`); add `editors/notepadpp/out/` to `.gitignore`; commit `N2: ignore the plugin build output`.

Layout created over the tasks:

```
editors/notepadpp/
  build.ps1              # csc + version.res + ilasm -> out/deckcheck/{deckcheck.dll, DeckcheckNpp.dll}; -Test runs the unit runner
  src/Exports.il         # six native exports, forwarding to [DeckcheckNpp]Deckcheck.Npp.Entry
  src/Entry.cs           # the C# side of the exports: menu, notifications
  src/NppApi.cs          # message numbers, structs, SendMessage, FuncItem marshalling
  src/EngineRun.cs       # run the engine in a Job object, cancel, parse JSON
  src/Findings.cs        # result model + JSON parsing + version check
  src/Panel.cs           # docked WinForms panel
  src/Settings.cs        # main decks per folder, disabled flag; stored in the plugins config dir
  src/Trace.cs           # event trace for tests (only when DECKCHECK_NPP_TRACE is set)
  tests/Run.cs           # unit test runner (exit code = failures)
  tests/FakeEngine.cs    # prints a canned JSON file, optional sleep, exit code
tools/npp/version_res.py # writes a VS_VERSIONINFO .res for a version string
tests/test_npp_build.py  # pytest: version_res, the build, exports, unit runner
tests/test_npp_e2e.py    # pytest: portable Notepad++ driven by Win32 messages
```

### Task 1: Build pipeline, version resource, menu

**Files:** `tools/npp/version_res.py`, `editors/notepadpp/build.ps1`, `editors/notepadpp/src/Exports.il`, `src/Entry.cs`, `src/NppApi.cs`, `tests/test_npp_build.py`

**Interfaces:**
- `version_res.write(path: Path, version: str, name: str) -> None`: a `.res` holding one `VS_VERSIONINFO` with FileVersion and ProductVersion `a.b.c.d`, plus string values FileDescription, ProductName and FileVersion.
- `build.ps1 [-Version 0.1.0] [-Test]` writes `out/deckcheck/deckcheck.dll` and `out/deckcheck/DeckcheckNpp.dll`.
- `Exports.il` forwards `isUnicode, setInfo, getName, getFuncsArray, messageProc, beNotified` to `Deckcheck.Npp.Entry` methods with primitive signatures (`int`, `IntPtr`, `uint`, `UIntPtr`), exactly as in the spike.
- Menu (`Entry`): `Check model` (Alt+Shift+K), `Show findings`, `Main decks...`, separator, `Disable`, `About`.

- [ ] **Step 1: Failing tests** (`tests/test_npp_build.py`):

```python
import os, struct, subprocess, sys
from pathlib import Path
import pytest
from tools.npp.version_res import write

NPP = Path("editors/notepadpp")
pytestmark = pytest.mark.skipif(sys.platform != "win32", reason="Windows build")


def test_version_res_round_trips(tmp_path):
    write(tmp_path / "v.res", "0.1.0", "deckcheck")
    data = (tmp_path / "v.res").read_bytes()
    assert data[:32] == bytes(32)[:0] + b"\x00\x00\x00\x00\x20\x00\x00\x00\xff\xff\x00\x00\xff\xff\x00\x00" + bytes(16)
    assert "0.1.0.0".encode("utf-16-le") in data and "deckcheck".encode("utf-16-le") in data
    assert struct.pack("<IIII", 0xFEEF04BD, 0x00010000, 0x00000001, 0x00000000) in data


def _exports(dll: Path) -> tuple[int, list[str]]:
    d = dll.read_bytes()
    pe = struct.unpack_from("<I", d, 0x3C)[0]
    mach, nsec, optsz = struct.unpack_from("<H", d, pe + 4)[0], struct.unpack_from("<H", d, pe + 6)[0], struct.unpack_from("<H", d, pe + 20)[0]
    opt = pe + 24
    erva = struct.unpack_from("<I", d, opt + 112)[0]
    secs = [struct.unpack_from("<8sIIII", d, opt + optsz + i * 40) for i in range(nsec)]
    off = lambda rva: next(rva - va + rp for _, vs, va, rs, rp in secs if va <= rva < va + max(vs, rs))
    e = off(erva)
    n, names = struct.unpack_from("<I", d, e + 24)[0], off(struct.unpack_from("<I", d, e + 32)[0])
    return mach, sorted(d[off(struct.unpack_from("<I", d, names + 4 * i)[0]):].split(b"\0")[0].decode() for i in range(n))


@pytest.fixture(scope="module")
def built():
    subprocess.run(["powershell", "-ExecutionPolicy", "Bypass", "-File", str(NPP / "build.ps1"), "-Version", "0.1.0"],
                   check=True, capture_output=True)
    return NPP / "out" / "deckcheck"


def test_build_exports_the_six_entry_points(built):
    mach, names = _exports(built / "deckcheck.dll")
    assert mach == 0x8664 and names == ["beNotified", "getFuncsArray", "getName", "isUnicode", "messageProc", "setInfo"]


def test_build_has_the_version_resource(built):
    out = subprocess.run(["powershell", "-Command", f"(Get-Item '{built / 'deckcheck.dll'}').VersionInfo.FileVersion"],
                         capture_output=True, text=True).stdout.strip()
    assert out == "0.1.0.0"
```

(The first assertion of `test_version_res_round_trips` checks the 32-byte empty resource that opens every `.res` file: DataSize 0, HeaderSize 32, type and name 0xFFFF.)

- [ ] **Step 2:** Run `pytest tests/test_npp_build.py`; expect the import error.
- [ ] **Step 3: Implement.**
  - `version_res.py` writes the `.res` header and `VS_VERSIONINFO`: `VS_FIXEDFILEINFO`, a StringFileInfo `040904B0` and a VarFileInfo `Translation`. Each child is DWORD-aligned.
  - `Exports.il` is the spike's IL, retargeted to `[DeckcheckNpp]Deckcheck.Npp.Entry`.
  - `build.ps1`:
    - `csc /target:library /platform:x64 /optimize /out:out\deckcheck\DeckcheckNpp.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll src\*.cs`
    - `python -m tools.npp.version_res out\version.res $Version deckcheck`
    - `ilasm src\Exports.il /DLL /PE64 /X64 /QUIET /RESOURCE=out\version.res /OUTPUT=out\deckcheck\deckcheck.dll`
    - Each step checks `$LASTEXITCODE`. With `-Test` it also compiles and runs `tests\Run.cs` and `tests\FakeEngine.cs`.
  - `Entry.cs` builds the menu through `NppApi.FuncItems`, a pinned array of `FuncItem` structs with delegates kept alive.
- [ ] **Step 4:** Rerun; expect 3 passed. Then the menu end-to-end check (Task 6's driver, run early): the Plugins menu shows `deckcheck` with the six items.
- [ ] **Step 5:** Commit `N2: build pipeline (csc, IL exports, version resource) and the plugin menu`.

### Task 2: Findings model and JSON parsing

**Files:** `src/Findings.cs`, `tests/Run.cs`

**Interfaces:** `Findings.Parse(string json) -> CheckResult` (`Models: List<Model{Main, Files, Alone}>`, `Diagnostics: List<Finding{File, Line, Column, EndColumn, Severity, Code, Message}>`, `NotChecked: List<NotChecked{Main, What, Reasons, Count}>`, `Errors`, `Warnings`). It throws `EngineError("unsupported engine output version N")` when `version != 1`, and `EngineError("engine output is not valid JSON")` on parse failure.

- [ ] **Step 1: Failing unit tests** in `tests/Run.cs`. These are a minimal runner: each test is a static method, and failures are counted and printed.
  - parse a real `--for` JSON produced by the N1 engine on `tests/torture/seeded_mistakes` (saved as `tests/data/seeded.json` by the test setup step in `build.ps1 -Test`);
  - `version: 2` throws;
  - `not json` throws;
  - a missing `models` key (an old engine) throws `EngineError("engine too old: no models")`.
- [ ] **Step 2:** `build.ps1 -Test` fails to compile (no `Findings`).
- [ ] **Step 3:** Implement with `JavaScriptSerializer.DeserializeObject` and explicit field reads.
- [ ] **Step 4:** `build.ps1 -Test`; expect all pass. Commit `N2: findings model and engine JSON parsing with version refusal`.

### Task 3: Running the engine (background, Job object, cancel)

**Files:** `src/EngineRun.cs`, `tests/FakeEngine.cs`, `tests/Run.cs`

**Interfaces:** `EngineRun.Start(string exe, string file, IList<string> mainDecks, Action<RunOutcome> done) -> EngineRun`, plus `Cancel()`. `RunOutcome` holds `Result` (CheckResult), `Error` (string), `Seconds` and `Cancelled`. `done` runs on a worker thread, and callers marshal to the UI. The process starts with `CreateNoWindow`, working directory = the file's folder, and `PriorityClass = BelowNormal` right after start. It is assigned to a Job object with `JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE`. `Cancel()` closes the job handle, which kills the tree, and sets `Cancelled`.

- [ ] **Step 1: Failing unit tests.** Use FakeEngine. It reads `FAKE_JSON`, `FAKE_SLEEP_MS`, `FAKE_EXIT` and `FAKE_CHILD`; with `FAKE_CHILD=1` it starts a sleeping child copy of itself and records the child's pid in a file.
  - a normal run returns the parsed result;
  - exit 2 with stderr gives `Error` containing the stderr text;
  - a missing exe gives `Error` "engine not found";
  - `Cancel()` during a 30-second fake makes `done` report `Cancelled`, and the fake and its child are gone within 2 s;
  - the process priority is `BelowNormal` (the fake writes `Process.GetCurrentProcess().PriorityClass` to a file).
- [ ] **Step 2-4:** Red, implement (P/Invoke `CreateJobObject`, `SetInformationJobObject`, `AssignProcessToJobObject`), green.
- [ ] **Step 5:** Commit `N2: engine runner in a Job object: below-normal priority, cancel kills the tree`.

### Task 4: Docked findings panel and the jump

**Files:** `src/Panel.cs`, `src/Entry.cs`, `src/NppApi.cs`

**Interfaces:**
- `Panel.ShowRunning(string what)`, `Panel.ShowResult(CheckResult r, double seconds, IEnumerable<string> unsavedFiles)` and `Panel.ShowError(string message)`.
- The rows (a `ListView`) hold severity, code, file:line and message. A double-click or Enter calls `Entry.Jump(Finding)`.
- The summary line reads `<main>: <files> files, <e> errors, <w> warnings, <s> s`. For a file checked alone it reads `Checked alone: no main deck in this folder includes <file> (Plugins > deckcheck > Main decks...)`.
- The `Not checked` lines go below the rows. `Unsaved changes in <file> are not checked` is shown when needed.
- `Entry.Jump`:
  - `NPPM_DOOPEN` with the absolute path (the working directory joined with `file`);
  - `SCI_GOTOLINE(line-1)`;
  - select `[column-1, end_column-1)` bytes from the line start (N3 converts columns);
  - `SCI_SCROLLCARET`;
  - focus the editor.
- Docking uses `NPPM_DMMREGASDCKDLG` with a `tTbData` (bottom container), registered on first show.

- [ ] **Step 1-4:** The panel logic that needs no window, the summary text and the row text, is unit-tested in `Run.cs` against `tests/data/seeded.json`. The docking and the jump are covered end to end in Task 6.
- [ ] **Step 5:** Commit `N2: docked findings panel and jump to the field`.

### Task 5: Triggers, unsaved edits, main decks, disable, shutdown

**Files:** `src/Entry.cs`, `src/Settings.cs`, `src/Trace.cs`

- **Check model:** take the current file's full path (`NPPM_GETFULLCURRENTPATH`). If it is unsaved and has no path, the panel says "save the file first". Cancel any running check, start a new one, and show the panel.
- **On save** (`NPPN_FILESAVED`): check when the saved file's suffix is `.k`, `.key`, `.dyn` or `.inc`, or when the last result reached it (its path appears in the last run's files). Otherwise do nothing.
- **Unsaved edits:** `SCN_SAVEPOINTLEFT` and `SCN_SAVEPOINTREACHED` keep the set of modified buffer paths. The panel lists those in the checked models' folders.
- **Main decks...:** a dialog showing the current file's folder and its main decks (one per line, relative to the folder), with Browse, OK and Cancel. Stored in `<plugins config dir>\deckcheck\settings.json` as `{"folders": {"<abs folder>": ["main.k"]}, "disabled": false}`. Passed as `--main-deck` for files in that folder.
- **Disable:** toggles a check mark, cancels any run, and hides squiggles (N3). While disabled, nothing runs on save. Persisted.
- **Shutdown** (`NPPN_SHUTDOWN`): cancel the run. The Job object also covers a crash.
- **Trace:** when `DECKCHECK_NPP_TRACE` names a file, append JSON lines `{"event": "...", ...}`: `check_started`, `check_done` (with counts and models), `check_error`, `check_cancelled`, `jumped` (file, line, sel_start, sel_end), `disabled`.

- [ ] Unit tests for `Settings` (round trip, per-folder lookup, a corrupt file falls back to defaults). Commit `N2: triggers on request and save, main decks per folder, disable, shutdown`.

### Task 6: End-to-end tests on a portable Notepad++

**Files:** `tests/test_npp_e2e.py` (skipped unless `DECKCHECK_NPP_E2E=1`), `tools/npp/drive.py` (Win32 helpers through `ctypes`)

The fixture copies `C:\Program Files\Notepad++` to a temp dir, adds `doLocalConf.xml`, installs `out\deckcheck` plus `build-engine\dist\deckcheck` as `engine\`, and sets `DECKCHECK_NPP_TRACE`. Each test starts Notepad++ with `-multiInst -nosession <file>` and kills it at the end.

- [ ] Tests:
  - the menu items exist;
  - *Check model* on `tests/torture/seeded_mistakes/main.k` gives a `check_done` with the CLI's error count;
  - a check on an include of `inc_nested_main` reports its main;
  - a jump selects the expected bytes (read back with `SCI_GETSELECTIONSTART` and `SCI_GETSELECTIONEND` on the Scintilla handle);
  - *Disable*, then saving a deck, starts no check;
  - with the engine folder renamed, *Check model* traces `check_error` "engine not found" and the editor still accepts text;
  - closing Notepad++ during a long check leaves no `deckcheck.exe` (process list).
- [ ] Commit `N2: end-to-end tests on a portable Notepad++`.
