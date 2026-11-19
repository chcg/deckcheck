# N1: Engine `--for` and `--main-deck` Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let the Notepad++ plugin ask the engine to "check the model this file belongs to", with optional per-folder main decks, and get enough JSON back to show a model summary.

**Architecture:** A small `deckcheck/models.py` exposes `models_for_file`, built on the language server's `Workspace` include scan, so the CLI and the VS Code server share one main-deck detection (spec §3). The CLI gains `--for FILE` and a repeatable `--main-deck DECK`. The JSON output gains a `models` list (additive; `"version": 1` stays). The frozen build from N0 is rebuilt and its parity test extended.

**Tech Stack:** Python 3.12, pytest, PyInstaller (N0's `tools/freeze/build_engine.ps1`).

**Spec:** `docs/specs/2026-11-17-notepadpp-design.md` (§3 engine changes, §4 which model)

## Global Constraints

- Branch `n1-engine`, cut from `n0-spike`. Never push; the repo is public.
- No change to any check's logic. Existing CLI behaviour and JSON keys stay as they are; `models` is added.
- `deckcheck.lsp.workspace` must stay importable without pygls (the frozen engine has no `[lsp]` extra).
- The 0.1.x release of these changes needs Chintan's explicit yes; it is done with N4, not here.
- Avoid shell heredocs for code edits.

## Review Focus

1. **A file that is itself a main deck.** `--for main.k` must check `main.k` as its model, not "alone".
2. **A file reached by two main decks.** Both models are checked; `models` lists both, and diagnostics in shared includes are not double-reported per file more than the two-deck CLI already does.
3. **`--main-deck` that does not reach the file.** The file is checked alone and `alone` is true; the given deck is not checked.
4. **Paths relative to the engine's working directory.** The plugin runs the engine with the file's folder as its working directory, so `file` and `main` must resolve against it, including `../` paths.
5. **`--for` on a file that does not exist** exits 2 with a message, like a missing main deck.

---

### Task 1: `models_for_file`

**Files:**
- Create: `src/deckcheck/models.py`
- Test: `tests/test_models.py`

**Interfaces:**
- Produces: `models_for_file(path: Path, main_decks: list[Path] | None = None) -> tuple[list[Path], bool]`: the main decks whose include trees reach `path` (sorted, normalised), and `alone`. When none reaches it, returns `([path], True)`. Detection scans `path`'s folder (recursively) for `.k`, `.key` and `.dyn` decks, or uses `main_decks` when given.

- [ ] **Step 1: Write the failing tests** (`tests/test_models.py`):

```python
from pathlib import Path

from deckcheck.models import models_for_file


def _deck(path: Path, *includes: str) -> Path:
    path.parent.mkdir(parents=True, exist_ok=True)
    body = "".join(f"*INCLUDE\n{i}\n" for i in includes)
    path.write_text(f"*KEYWORD\n{body}*END\n", encoding="latin-1")
    return path


def test_a_main_deck_is_its_own_model(tmp_path):
    main = _deck(tmp_path / "main.k", "mesh/part.k")
    _deck(tmp_path / "mesh" / "part.k")
    assert models_for_file(main) == ([main], False)


def test_file_in_the_mains_folder_is_reached(tmp_path):
    main = _deck(tmp_path / "main.k", "part.k")
    part = _deck(tmp_path / "part.k")
    assert models_for_file(part) == ([main], False)


def test_two_mains_reach_one_include(tmp_path):
    a = _deck(tmp_path / "a.k", "shared.k")
    b = _deck(tmp_path / "b.k", "shared.k")
    shared = _deck(tmp_path / "shared.k")
    assert models_for_file(shared) == ([a, b], False)


def test_unreached_non_deck_file_is_alone(tmp_path):
    _deck(tmp_path / "main.k")
    lone = tmp_path / "notes.inc"
    lone.write_text("*KEYWORD\n*END\n", encoding="latin-1")
    assert models_for_file(lone) == ([lone], True)


def test_main_decks_override(tmp_path):
    a = _deck(tmp_path / "a.k", "part.k")
    _deck(tmp_path / "b.k", "part.k")
    part = _deck(tmp_path / "part.k")
    assert models_for_file(part, [a]) == ([a], False)


def test_main_deck_that_does_not_reach_the_file(tmp_path):
    a = _deck(tmp_path / "a.k")
    part = _deck(tmp_path / "part.k")
    assert models_for_file(part, [a]) == ([part], True)
```

A file in a subfolder is scanned from its own folder only, so its main deck in the parent folder is not found unless named with `--main-deck`; the plugin's *Main decks...* setting covers that case (spec §4).

- [ ] **Step 2: Run them to verify they fail**

Run: `.venv\Scripts\python -m pytest tests/test_models.py -q -p no:warnings`
Expected: collection error, `ModuleNotFoundError: No module named 'deckcheck.models'`.

- [ ] **Step 3: Implement `src/deckcheck/models.py`**

```python
"""Which model(s) a file belongs to (Notepad++ spec §3): the main decks whose include trees reach it. Shares the
language server's include scan (lsp/workspace.py) so both front ends agree."""
from pathlib import Path

from deckcheck.includes import _key, _norm
from deckcheck.lsp.workspace import Settings, Workspace


def models_for_file(path: Path, main_decks: list[Path] | None = None) -> tuple[list[Path], bool]:
    path = _norm(path)
    ws = Workspace([path.parent], Settings(main_decks=[_norm(m) for m in main_decks or []]))
    ws.scan()
    k = _key(path)
    found = sorted((p for m, p in ws._mains.items() if k in ws._reach[m]), key=str)
    return (found, False) if found else ([path], True)
```

- [ ] **Step 4: Run the tests and the workspace tests**

Run: `.venv\Scripts\python -m pytest tests/test_models.py tests/test_lsp_workspace.py -q -p no:warnings`
Expected: all pass.

- [ ] **Step 5: Commit** — `git add src/deckcheck/models.py tests/test_models.py`; message `N1: models_for_file, sharing the language server's main-deck scan`.

### Task 2: CLI `--for`, `--main-deck` and the JSON `models` list

**Files:**
- Modify: `src/deckcheck/cli.py`
- Test: `tests/test_cli.py`

**Interfaces:**
- Consumes: `models_for_file` (Task 1).
- Produces: `deckcheck --for FILE [--main-deck DECK ...] [--format json]`. JSON gains `"models": [{"main": str, "files": int, "alone": bool}]` for every run (with explicit decks, `alone` is false). Text output gains a first line `Checked as part of <main>` per model when `--for` is used, or `Checked alone: no main deck includes <file>` when alone.

- [ ] **Step 1: Write the failing tests** (append to `tests/test_cli.py`):

```python
def _deck(path, *includes):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text("*KEYWORD\n" + "".join(f"*INCLUDE\n{i}\n" for i in includes) + "*END\n", encoding="latin-1")
    return path


def test_for_checks_the_model_the_file_belongs_to(capsys, monkeypatch, tmp_path):
    _deck(tmp_path / "main.k", "part.k", "missing.k")
    _deck(tmp_path / "part.k")
    monkeypatch.chdir(tmp_path)
    code, out, _ = run(capsys, "--for", "part.k", "--format", "json")
    data = json.loads(out)
    assert code == 1 and data["models"] == [{"main": "main.k", "files": 2, "alone": False}]
    assert [d["code"] for d in data["diagnostics"]] == ["DC004"]


def test_for_a_main_deck_checks_it(capsys, monkeypatch, tmp_path):
    _deck(tmp_path / "main.k", "part.k")
    _deck(tmp_path / "part.k")
    monkeypatch.chdir(tmp_path)
    code, out, _ = run(capsys, "--for", "main.k", "--format", "json")
    assert code == 0 and json.loads(out)["models"] == [{"main": "main.k", "files": 2, "alone": False}]


def test_for_an_unreached_file_checks_it_alone(capsys, monkeypatch, tmp_path):
    (tmp_path / "notes.inc").write_text("*KEYWORD\n*END\n", encoding="latin-1")
    monkeypatch.chdir(tmp_path)
    code, out, _ = run(capsys, "--for", "notes.inc")
    assert code == 0 and out.splitlines()[0] == "Checked alone: no main deck includes notes.inc"


def test_for_with_main_deck_override(capsys, monkeypatch, tmp_path):
    _deck(tmp_path / "a.k", "part.k")
    _deck(tmp_path / "b.k", "part.k")
    _deck(tmp_path / "part.k")
    monkeypatch.chdir(tmp_path)
    _, out, _ = run(capsys, "--for", "part.k", "--main-deck", "a.k", "--format", "json")
    assert [m["main"] for m in json.loads(out)["models"]] == ["a.k"]
    _, out, _ = run(capsys, "--for", "part.k", "--format", "json")
    assert [m["main"] for m in json.loads(out)["models"]] == ["a.k", "b.k"]


def test_for_missing_file_exits_2(capsys, tmp_path):
    code, out, err = run(capsys, "--for", tmp_path / "nope.k")
    assert code == 2 and out == "" and "nope.k" in err


def test_decks_and_for_are_exclusive_and_one_is_needed(capsys, tmp_path):
    with pytest.raises(SystemExit) as e:
        main([])
    assert e.value.code == 2
    with pytest.raises(SystemExit) as e:
        main([str(SEEDED), "--for", str(SEEDED)])
    assert e.value.code == 2
    with pytest.raises(SystemExit) as e:
        main([str(SEEDED), "--main-deck", str(SEEDED)])
    assert e.value.code == 2


def test_json_models_for_explicit_decks(capsys):
    _, out, _ = run(capsys, "--format", "json", SEEDED)
    (m,) = json.loads(out)["models"]
    assert m["alone"] is False and m["files"] >= 1 and m["main"].endswith("seeded_mistakes/main.k")
```

- [ ] **Step 2: Run them to verify they fail**

Run: `.venv\Scripts\python -m pytest tests/test_cli.py -q -p no:warnings`
Expected: the new tests fail (`unrecognized arguments: --for`, `KeyError: 'models'`); the old ones pass.

- [ ] **Step 3: Implement in `src/deckcheck/cli.py`:**
  - `decks` becomes `nargs="*"`; add `--for` (`dest="for_file"`, `type=Path`, `metavar="FILE"`) and `--main-deck` (`action="append"`, `default=[]`, `type=Path`, `metavar="DECK"`).
  - Parser errors (exit 2): neither decks nor `--for`; both; `--main-deck` without `--for`.
  - With `--for`: a missing file exits 2 like a missing deck (also each `--main-deck`); `mains, alone = models_for_file(file, main_decks)`; check each main.
  - `_json` gains `models`: `[{"main": _rel(r.main), "files": len(r.tree.files), "alone": alone}]`.
  - `_text` with `--for` starts with `Checked as part of <main>` per model, or the alone line, then a blank line, then the usual output.
  - The ruling that `files` counts `r.tree.files` (every file the include tree reached, the main included) is in the docstring.

- [ ] **Step 4: Run the CLI tests, then the full suite**

Run: `.venv\Scripts\python -m pytest tests/test_cli.py tests/test_models.py -q -p no:warnings`, then the full suite.
Expected: all pass; the full suite count is the N0 count plus the new tests.

- [ ] **Step 5: Commit** — `N1: --for and --main-deck, models in the JSON output`.

### Task 3: Frozen parity for `--for`, and the docs

**Files:**
- Modify: `tests/test_frozen.py`, `README.md` (CLI section), `docs/manual/README.md` if it lists CLI options

- [ ] **Step 1: Write the failing test** (append to `tests/test_frozen.py`):

```python
@pytest.mark.parametrize("args", [["--for", "main.k"], ["--for", "main.k", "--main-deck", "main.k"]])
def test_frozen_for_matches_cli(args):
    folder = Path("tests/torture/inc_nested_main")
    run = lambda cmd: subprocess.run(cmd + args + ["--format", "json"], cwd=folder, capture_output=True, timeout=600)
    f, c = run([FROZEN]), run([sys.executable, "-m", "deckcheck"])
    assert (f.returncode, f.stdout) == (c.returncode, c.stdout)
```

- [ ] **Step 2: Run against the N0 build to verify it fails**: `$env:DECKCHECK_FROZEN=...; pytest tests/test_frozen.py -k for` — Expected: FAIL (the N0 exe has no `--for`).
- [ ] **Step 3: Rebuild** with `tools\freeze\build_engine.ps1`, rerun all of `tests/test_frozen.py`. Expected: 90 passed.
- [ ] **Step 4: Docs.** Add `--for FILE` and `--main-deck DECK` to the README's CLI usage, one line each, and the `models` key to any JSON description.
- [ ] **Step 5: Commit** — `N1: frozen parity for --for; CLI docs`.
