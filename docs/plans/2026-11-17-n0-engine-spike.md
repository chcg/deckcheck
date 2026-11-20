# N0: Frozen-Engine Spike Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Find out, before any plugin code exists, whether deckcheck can ship to Notepad++ as a frozen Windows executable: same findings as the CLI, acceptable size and speed, not flagged by antivirus, and allowed by Plugins Admin's rules.

**Architecture:** Freeze the installed deckcheck package with PyInstaller (one-folder build) into a git-ignored `build-engine/` folder, using a separate build venv. A parity test proves the frozen engine prints byte-identical JSON to `python -m deckcheck` on all 86 torture decks. A measurement script records size, start time, check times and any personal paths embedded in the build. Defender is run locally; VirusTotal only with the owner's yes. The Plugins Admin and template-licence facts are read from their sources. Everything ends in `docs/n0-spike.md` and a gate decision.

**Tech Stack:** Python 3.12 (`.venv`), PyInstaller (latest at execution time, recorded), pytest, PowerShell, Windows Defender (`MpCmdRun.exe`, `Get-MpComputerStatus`).

**Spec:** `docs/specs/2026-11-17-notepadpp-design.md` (§8 risks, §9 stage N0)

## Global Constraints

- Work on branch `n0-spike`, cut from `npp-spec`. Never push; the repo is public. Every public step needs the owner's explicit yes.
- No change to deckcheck's engine code in N0. `--for` and `--main-deck` are N1.
- Build output goes to `build-engine/` (git-ignored), never committed. The build venv is `.venv-freeze/` (git-ignored).
- Nothing personal in committed files or uploads: no home path, user name or email. The report uses repo-relative paths.
- No upload to VirusTotal or any other service without the owner's explicit yes at the Task 4 checkpoint.
- Thresholds (gate, Task 6): Defender 0 detections; zip ≤ 30 MB; `--version` start median ≤ 1.5 s; Yaris check median ≤ 1.2 × the pip CLI's median; frozen output identical to the CLI on all torture decks.
- On this laptop (7.7 GB, 4 cores) run one Yaris check at a time.
- Avoid shell heredocs for code edits; use the editor tools.

## Review Focus

1. **Embedded personal paths.** PyInstaller writes build paths into the bootloader and `.pyc` files. A home path or user name in the exe would leak on publish. Task 2's scan must look for them in UTF-8 and UTF-16LE, computed at run time, never typed into the repo.
2. **Package data missing from the build.** `keywords.json`, `definers.toml`, `overrides.toml` and `dc005_audit.toml` are read through `importlib.resources`. A build without `--collect-data deckcheck` starts but fails on the first check. The parity test covers it because every torture deck needs the keyword data.
3. **Exit codes, not only output.** The CLI exits 0, 1 or 2. A frozen bootloader that swallows `sys.exit` codes would break CI use. The parity test compares return codes as well as stdout.
4. **Mark of the Web.** A zip downloaded by a browser marks its files as coming from the internet, which can trigger SmartScreen or stricter scanning. Task 3 runs the engine with a Zone.Identifier stream attached.
5. **Non-ASCII paths.** A deck in a folder whose name has a non-ASCII character must check the same frozen as from the CLI. The parity test includes one copied into such a folder.

---

### Task 0: Branch and ignores

**Files:**
- Modify: `.gitignore`

- [ ] **Step 1: Branch and ignore the build folders**

```powershell
git checkout npp-spec; git checkout -b n0-spike
Add-Content .gitignore "build-engine/"
Add-Content .gitignore ".venv-freeze/"
git add .gitignore; git commit -m "N0: ignore the frozen-engine build folders"
```
Expected: `Switched to a new branch 'n0-spike'`, one commit.

### Task 1: Freeze the engine and prove parity with the CLI

**Files:**
- Create: `tools/freeze/__init__.py` (empty)
- Create: `tools/freeze/build_engine.ps1`
- Test: `tests/test_frozen.py`

**Interfaces:**
- Produces: `build-engine\dist\deckcheck\deckcheck.exe` (one-folder build); env var `DECKCHECK_FROZEN` = absolute path to it, used by the tests.

- [ ] **Step 1: Write the failing test**

Create `tests/test_frozen.py`:

```python
"""N0 parity: the frozen engine must print the same JSON and exit code as `python -m deckcheck`."""
import os
import shutil
import subprocess
import sys
from pathlib import Path

import pytest

FROZEN = os.environ.get("DECKCHECK_FROZEN")
pytestmark = pytest.mark.skipif(not FROZEN, reason="set DECKCHECK_FROZEN to the frozen deckcheck.exe")
TORTURE = sorted(p.parent for p in Path("tests/torture").glob("*/main.k"))


def _run(cmd, folder):
    p = subprocess.run(cmd + ["main.k", "--format", "json"], cwd=folder, capture_output=True, timeout=600)
    return p.returncode, p.stdout


@pytest.mark.parametrize("folder", TORTURE, ids=lambda p: p.name)
def test_frozen_engine_matches_cli(folder):
    assert _run([FROZEN], folder) == _run([sys.executable, "-m", "deckcheck"], folder)


def test_frozen_engine_matches_cli_in_a_non_ascii_folder(tmp_path):
    folder = tmp_path / "Prüfung µm"
    shutil.copytree("tests/torture/base", folder)
    assert _run([FROZEN], folder) == _run([sys.executable, "-m", "deckcheck"], folder)


def test_frozen_version_matches():
    frozen = subprocess.run([FROZEN, "--version"], capture_output=True, text=True).stdout
    cli = subprocess.run([sys.executable, "-m", "deckcheck", "--version"], capture_output=True, text=True).stdout
    assert frozen == cli and frozen.startswith("deckcheck ")
```

- [ ] **Step 2: Run it to verify it fails**

```powershell
$env:DECKCHECK_FROZEN = "$PWD\build-engine\dist\deckcheck\deckcheck.exe"
.venv\Scripts\python -m pytest tests/test_frozen.py -q -p no:warnings -x
```
Expected: FAIL with `FileNotFoundError` (the exe does not exist yet). Without `DECKCHECK_FROZEN` the file is skipped, so the normal suite stays green.

- [ ] **Step 3: Write the build script**

Create `tools/freeze/build_engine.ps1`:

```powershell
# N0: freeze the installed deckcheck package into build-engine\dist\deckcheck (one-folder PyInstaller build).
# Run from the repo root. Uses its own venv so the dev venv is untouched.
$ErrorActionPreference = "Stop"
if (-not (Test-Path .venv-freeze)) { .venv\Scripts\python -m venv .venv-freeze }
.venv-freeze\Scripts\python -m pip install -q --upgrade pip pyinstaller
.venv-freeze\Scripts\python -m pip install -q .
if (Test-Path build-engine) { Remove-Item -Recurse -Force build-engine }
.venv-freeze\Scripts\pyinstaller --noconfirm --onedir --console --name deckcheck --collect-data deckcheck `
    --distpath build-engine\dist --workpath build-engine\work --specpath build-engine `
    src\deckcheck\__main__.py
.venv-freeze\Scripts\python -m pip show pyinstaller | Select-String "^Version"
```

Also create the empty `tools/freeze/__init__.py`.

- [ ] **Step 4: Build and run the tests**

```powershell
powershell -ExecutionPolicy Bypass -File tools\freeze\build_engine.ps1
$env:DECKCHECK_FROZEN = "$PWD\build-engine\dist\deckcheck\deckcheck.exe"
.venv\Scripts\python -m pytest tests/test_frozen.py -q -p no:warnings
```
Expected: the PyInstaller version line, then `88 passed` (86 torture decks, the non-ASCII folder, the version). A mismatch on any deck is a finding: record which decks and how the output differs; never loosen the comparison.

- [ ] **Step 5: Full suite without the variable, then commit**

```powershell
Remove-Item Env:DECKCHECK_FROZEN
.venv\Scripts\python -m pytest -q -p no:warnings
git add tools/freeze tests/test_frozen.py
git commit -m "N0: PyInstaller one-folder build of the engine, parity test against the CLI"
```
Expected: the suite's usual count passes with `test_frozen.py` skipped (record the count in the ledger).

### Task 2: Measure size, speed and embedded paths

**Files:**
- Create: `tools/freeze/measure_engine.py`
- Test: `tests/test_measure_engine.py`

**Interfaces:**
- Consumes: `build-engine\dist\deckcheck\deckcheck.exe` from Task 1.
- Produces: `privacy_hits(root: Path, needles: list[str]) -> list[tuple[str, str]]` (relative path, needle) and `build-engine\measure.json` with keys `zip_mb, folder_mb, files, start_median_s, checks: {name: {frozen_median_s, cli_median_s, ratio}}, privacy_hits`.

- [ ] **Step 1: Write the failing test**

Create `tests/test_measure_engine.py`:

```python
from tools.freeze.measure_engine import privacy_hits


def test_privacy_hits_finds_utf8_and_utf16_needles(tmp_path):
    (tmp_path / "a.bin").write_bytes(b"xx C:\\Users\\someone\\proj xx")
    (tmp_path / "sub").mkdir()
    (tmp_path / "sub" / "b.bin").write_bytes("path=C:\\Users\\someone".encode("utf-16-le"))
    (tmp_path / "c.bin").write_bytes(b"nothing here")
    hits = privacy_hits(tmp_path, ["C:\\Users\\someone"])
    assert sorted(hits) == [("a.bin", "C:\\Users\\someone"), ("sub/b.bin", "C:\\Users\\someone")]


def test_privacy_hits_is_case_insensitive_and_skips_empty_needles(tmp_path):
    (tmp_path / "a.bin").write_bytes(b"c:\\users\\SOMEONE")
    assert privacy_hits(tmp_path, ["C:\\Users\\someone", ""]) == [("a.bin", "C:\\Users\\someone")]
```

- [ ] **Step 2: Run it to verify it fails**

Run: `.venv\Scripts\python -m pytest tests/test_measure_engine.py -q -p no:warnings`
Expected: collection error, `ModuleNotFoundError: No module named 'tools.freeze.measure_engine'`.

- [ ] **Step 3: Implement `tools/freeze/measure_engine.py`**

```python
"""N0 measurements of the frozen engine: size, start time, check times against the pip CLI, embedded personal paths.

Usage (repo root): .venv\\Scripts\\python -m tools.freeze.measure_engine
Writes build-engine/measure.json and prints it. Needles for the privacy scan are computed here, never committed.
"""
import json
import os
import shutil
import statistics
import subprocess
import sys
import tempfile
import time
from pathlib import Path

DIST = Path("build-engine/dist/deckcheck")
EXE = DIST / "deckcheck.exe"
YARIS = Path(".corpus/local/ccsa-yaris/2010-toyota-yaris-detailed-v2j/set-yaris-detailed-v2j.key")
BUMPER = Path(r"C:\Program Files\LS-DYNA Suite R16.1 Student\documentation\examples\Bumper_impact_test\bumper.k")


def privacy_hits(root: Path, needles: list[str]) -> list[tuple[str, str]]:
    pats = [(n, enc) for n in needles if n for enc in (n.lower().encode("utf-8"), n.lower().encode("utf-16-le"))]
    hits = set()
    for p in sorted(Path(root).rglob("*")):
        if p.is_file():
            data = p.read_bytes().lower()
            for n, enc in pats:
                if enc in data:
                    hits.add((p.relative_to(root).as_posix(), n))
    return sorted(hits)


def _time(cmd, cwd=None) -> float:
    t = time.perf_counter()
    subprocess.run(cmd, cwd=cwd, capture_output=True, timeout=3600)
    return time.perf_counter() - t


def _check_times(deck: Path, n: int) -> dict:
    frozen, cli = [], []
    for _ in range(n):  # interleaved, one process at a time
        frozen.append(_time([str(EXE.resolve()), deck.name, "--format", "json"], cwd=deck.parent))
        cli.append(_time([sys.executable, "-m", "deckcheck", deck.name, "--format", "json"], cwd=deck.parent))
    f, c = statistics.median(frozen), statistics.median(cli)
    return {"frozen_median_s": round(f, 2), "cli_median_s": round(c, 2), "ratio": round(f / c, 3)}


def main() -> int:
    files = [p for p in DIST.rglob("*") if p.is_file()]
    with tempfile.TemporaryDirectory() as tmp:
        z = shutil.make_archive(str(Path(tmp) / "deckcheck-engine"), "zip", DIST.parent, DIST.name)
        zip_mb = Path(z).stat().st_size / 2**20
    _time([str(EXE), "--version"])  # first run warms the file cache; not counted
    start = statistics.median(_time([str(EXE), "--version"]) for _ in range(10))
    checks = {}
    if BUMPER.exists():
        checks["bumper"] = _check_times(BUMPER, 5)
    if YARIS.exists():
        checks["yaris"] = _check_times(YARIS, 3)
    needles = [str(Path.home()), os.environ.get("USERNAME", ""), str(Path.cwd().resolve())]
    out = {"zip_mb": round(zip_mb, 1), "folder_mb": round(sum(p.stat().st_size for p in files) / 2**20, 1),
           "files": len(files), "start_median_s": round(start, 3), "checks": checks,
           "privacy_hits": [[f, "home" if n == needles[0] else "user" if n == needles[1] else "repo"]
                            for f, n in privacy_hits(DIST, needles)]}
    Path("build-engine/measure.json").write_text(json.dumps(out, indent=1))
    print(json.dumps(out, indent=1))
    return 0


if __name__ == "__main__":
    sys.exit(main())
```

(The privacy output names the kind of needle, `home`, `user` or `repo`, never the needle itself, so the JSON can be quoted in the report.)

- [ ] **Step 4: Run the tests, then measure**

```powershell
.venv\Scripts\python -m pytest tests/test_measure_engine.py -q -p no:warnings
.venv\Scripts\python -m tools.freeze.measure_engine
```
Expected: `2 passed`; then the JSON. The Yaris checks take several minutes (3 frozen + 3 CLI). Read every number against the thresholds in Global Constraints. If `privacy_hits` is not empty, Task 2b runs before anything else.

- [ ] **Step 5: Commit**

```powershell
git add tools/freeze/measure_engine.py tests/test_measure_engine.py
git commit -m "N0: measure the frozen engine (size, start, check times, embedded personal paths)"
```

### Task 2b (only if `privacy_hits` is not empty): Remove embedded paths

- [ ] **Step 1:** List the hits by file. Typical sources: `.pyc` files holding absolute source paths, and the bootloader holding the build path.
- [ ] **Step 2:** Rebuild from a neutral location. Copy the repo's `src` and `pyproject.toml` to a short neutral folder such as `C:\dcbuild`, run the build script there, and copy `dist` back. Alternatively add PyInstaller's path-stripping options available in the recorded version. Write the change into `build_engine.ps1`.
- [ ] **Step 3:** Rerun Task 1 Step 4 and Task 2 Step 4. Expected: parity still passes, `privacy_hits: []`.
- [ ] **Step 4:** Commit the changed script with message `N0: build from a neutral path so no personal path is embedded`.

### Task 3: Windows Defender

**Files:** none committed; results go to the report (Task 6).

- [ ] **Step 1: Record Defender's state**

```powershell
Get-MpComputerStatus | Select-Object AMProductVersion, AntivirusSignatureVersion, AntivirusSignatureLastUpdated, RealTimeProtectionEnabled, IsTamperProtected
```
Expected: real-time protection enabled and signatures less than a day old. If they are older, run `Update-MpSignature` first (a Microsoft update, not an upload).

- [ ] **Step 2: Scan the build folder and the zip**

```powershell
$mp = "$env:ProgramFiles\Windows Defender\MpCmdRun.exe"
& $mp -Scan -ScanType 3 -File "$PWD\build-engine\dist\deckcheck" -DisableRemediation
Compress-Archive -Force build-engine\dist\deckcheck build-engine\deckcheck-engine.zip
& $mp -Scan -ScanType 3 -File "$PWD\build-engine\deckcheck-engine.zip" -DisableRemediation
Get-MpThreatDetection | Select-Object -First 5
```
Expected: `found no threats` for both, and no recent detection naming `build-engine`.

- [ ] **Step 3: Run with a Mark of the Web, as a browser download would have**

```powershell
$t = "$env:TEMP\n0_motw"; if (Test-Path $t) { Remove-Item -Recurse -Force $t }
Copy-Item -Recurse build-engine\dist\deckcheck $t
Get-ChildItem $t -Recurse -File | ForEach-Object { Set-Content -Path $_.FullName -Stream Zone.Identifier -Value "[ZoneTransfer]`r`nZoneId=3" }
& "$t\deckcheck.exe" --version; $LASTEXITCODE
Push-Location tests\torture\base; & "$t\deckcheck.exe" main.k --format json | Select-Object -First 3; Pop-Location
Get-MpThreatDetection | Select-Object -First 5
Remove-Item -Recurse -Force $t
```
Expected: the version line, exit 0, JSON output, and no new detection. A SmartScreen prompt or a block is a finding.

- [ ] **Step 4: If Defender flags anything:** stop and ask the owner (AskUserQuestion) before trying alternatives. The options are a PyInstaller rebuild with a self-built bootloader, a Nuitka build (which needs a C compiler download), or code signing (which costs money). Record the detection name and the step that triggered it.

### Task 4: CHECKPOINT: VirusTotal

- [ ] **Step 1:** Report Tasks 1-3 to the owner: parity, the measurements and Defender. Then ask with AskUserQuestion whether to scan `build-engine\deckcheck-engine.zip` on VirusTotal. Explain that VirusTotal shares uploaded files with security vendors and premium users. The build is made from public source, and Task 2 found no personal paths in it (or Task 2b removed them). Options:
  - "The owner uploads it" (recommended): the owner drags the zip into the VirusTotal web page and pastes the result link.
  - "Skip VirusTotal": record "not scanned" in the report.
- [ ] **Step 2:** Record the result: the number of engines that flagged it, and which ones. Treat any flag from Microsoft, CrowdStrike, Symantec, Sophos, Trend Micro, McAfee, Kaspersky, ESET or Bitdefender as a gate finding.

### Task 5: Plugins Admin rules and the C# template licence

**Files:** none committed; facts go to the report with source URLs and the date read.

- [ ] **Step 1: Read the plugin list's rules** from `https://github.com/notepad-plus-plus/nppPluginList`: its README and any contribution notes. Look at the x64 list file (`src/pl.x64.json`) for an existing entry with extra files or subfolders, such as a plugin that ships a runtime. Answer these questions:
  - Is a zip with a subfolder (`engine\`) installed as-is into `plugins\deckcheck\`?
  - Does the DLL's file name have to match the folder name?
  - What does an entry need: `folder-name`, `display-name`, `version`, `id` (the zip's sha256), `repository`, `npp-compatible-versions`?
  - Is there a stated size limit, and what is the largest current entry if one can be found?
  - Is a third-party executable inside a plugin allowed?
- [ ] **Step 2: Read the C# plugin template's licence.** Read the template JSON Tools and CSV Lint use, starting from those repositories' READMEs. Answer these questions:
  - What is the template's licence, and is it compatible with Apache-2.0?
  - What does it require in a distributed binary?
  - What .NET Framework version does it target?
  - Does it support an x64 build and an ARM64 build?
- [ ] **Step 3:** If a rule rules out the bundled-engine design (for example, no executables allowed), mark it as a gate finding.

### Task 6: Report and gate

**Files:**
- Create: `docs/n0-spike.md`

- [ ] **Step 1: Write the report**, every number from `build-engine\measure.json` or a printed result, none typed from memory. Sections:
  1. Verdict, one line per threshold in Global Constraints: met or missed, with the value.
  2. Build: PyInstaller version, one-folder layout, file count, folder and zip size.
  3. Parity: the test result (decks, non-ASCII folder, version), and any differences found.
  4. Speed: start median; bumper and Yaris medians, frozen against CLI.
  5. Antivirus: Defender versions and results, the Mark-of-the-Web run, VirusTotal (result or "not scanned").
  6. Plugins Admin and licence facts, with URLs and the date read.
  7. Personal paths: the scan result and any fix (Task 2b).
  8. What N1 and N2 need from this: open questions carried forward.

  Use repo-relative paths only; no home path, user name or email.
- [ ] **Step 2: Commit**

```powershell
git add docs/n0-spike.md
git commit -m "N0: frozen-engine spike report"
```

- [ ] **Step 3: CHECKPOINT (stage gate).** Present the verdict to the owner and ask with AskUserQuestion: "Go to N1" (only if every threshold is met), "Fix and re-measure", or "Stop and rethink the bundled engine". Do not start N1 without the answer. Merging `n0-spike` is a separate decision.
