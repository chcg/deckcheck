# N3: Squiggles, Calltips, Column Conversion and the Do-No-Harm Gate Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Findings appear in the editor as red and amber squiggles on the field's exact bytes, show their message on hover, and the plugin passes the measured do-no-harm gate on the Yaris model.

**Architecture:**
- **Column conversion.** `ColumnMap` converts the engine's Latin-1 byte columns into Scintilla byte positions. It reads the buffer's encoding (`NPPM_GETBUFFERENCODING`) and code page.
- **Squiggles.** `Painter` paints indicators (`NPPM_ALLOCATEINDICATOR`, with fixed fallbacks) in the active document after a check and whenever a buffer is activated. It paints at most 2,000 per file.
- **Calltips.** Mouse dwell shows the finding's message in a calltip.
- **Harm gate.** `tools/npp/harm_gate.py` measures opening, typing and scrolling in the 169 MB Yaris include, with the plugin checking the model and without the plugin, in paired runs.

**Tech Stack:** C# 5 (Framework csc), Scintilla messages, Python + ctypes for the end-to-end tests and the gate.

**Spec:** `docs/specs/2026-11-17-notepadpp-design.md` (§4 squiggles, §5 do no harm, §6 column units)

## What was measured before this plan (2026-11-19, portable Notepad++ 8.9.3)

| File on disk | Scintilla code page | `NPPM_GETBUFFERENCODING` | Buffer bytes |
|---|---|---|---|
| Latin-1, `°` in a comment | 0 (ANSI) | 0 | identical to disk |
| UTF-8, no BOM | 65001 | 4 | identical to disk |
| UTF-8 with BOM | 65001 | 1 | the 3 BOM bytes dropped: line 1 shifts by 3 |

So the engine's byte columns equal buffer positions except:
- on line 1 of a file with a BOM;
- when Notepad++ holds an ANSI file converted to UTF-8 (code page 65001 with encoding 0 or 5), where each disk byte is one character;
- for UTF-16 files, which cannot be mapped and are not painted.

The spec §6 case, a UTF-8 file with `°`, needs no shift, because the engine counts the same UTF-8 bytes.

## Global Constraints

- Branch `n3-squiggles`, cut from `n2-plugin`. Never push. C# 5 only. No downloads or installs.
- Paint only in the active document; at most 2,000 indicators per file (the panel lists all findings).
- **Do-no-harm gate (spec §5):** opening, typing in and scrolling the 169 MB Yaris include while the full Yaris model is checked stays within 10 % or 20 ms (paired median) of Notepad++ without the plugin.
- One heavy job at a time on this laptop.

## Review Focus

1. **A finding past the end of its line** (the line was shortened since the check). Clamp to the line; never paint into the next line.
2. **Switching tabs while a check runs.** Squiggles appear in the tab that becomes active once the result arrives, not in the tab that was active when the check started.
3. **Disable** clears every squiggle in the active document and paints none after.
4. **Hover over plain text** shows no calltip; moving away cancels it.
5. **The 2,000 cap** paints the first 2,000 findings in file order, and the trace says it was capped.

---

### Task 1: `ColumnMap`

**Files:** `editors/notepadpp/src/ColumnMap.cs`, `editors/notepadpp/tests/Run.cs`

**Interfaces:** `ColumnMap.ToBuffer(int line, int column, int encoding, int codepage, byte[] bufferLine) -> int`. It returns a 0-based byte offset within the buffer line, given the engine's 1-based line and 1-based Latin-1 column, or -1 when it cannot be mapped (UTF-16). The result is clamped to `[0, bufferLine.Length]`.

- [ ] **Step 1: Failing tests:**
  - identity for encoding 0 / code page 0;
  - identity for encoding 4 / 65001 (a `°` before the field counts 2 bytes on both sides);
  - BOM, encoding 1: line 1 minus 3, line 2 unchanged;
  - an ANSI buffer converted to UTF-8 (encoding 0, code page 65001, buffer line `"20°C x"`): column 6 (disk byte 5, after one-byte `°`) maps to 6 (UTF-8 `°` is 2 bytes);
  - UTF-16 (encodings 2, 3, 6, 7) gives -1;
  - a column past the end clamps to the length.
- [ ] **Step 2-4:** red, implement, green (`build.ps1 -Test`).
- [ ] **Step 5:** Commit `N3: ColumnMap from engine byte columns to Scintilla positions`.

### Task 2: Squiggles, and the jump through ColumnMap

**Files:** `src/Painter.cs`, `src/Plugin.cs`, `src/Entry.cs` (raw notification pointer), `src/NppApi.cs`

- Indicators: two numbers from `NPPM_ALLOCATEINDICATOR` (`NPPMSG + 113`), else 9 and 10. Style `INDIC_SQUIGGLEPIXMAP` (13). Colours: error `#E51400`, warning `#D79B00`. Both are set on each view at `NPPN_READY`.
- `Painter.Paint(IntPtr sci, string path, IList<Finding> findings, string folder)`: clear both indicators over the whole document, then fill each finding's range (through `ColumnMap`, line text from `SCI_GETLINE`) up to the cap. Trace `painted` with `file`, `count`, `capped`, and the first range's text.
- Paint after `check_done` for the active file, and on `NPPN_BUFFERACTIVATED` for the newly active file. The result is kept per absolute path.
- *Disable* and a new check's start leave the current squiggles in place until the result; *Disable* clears the active document's squiggles. The jump uses the same mapping, and its trace gains the selected text.
- [ ] Tests: end to end in Task 4. The cap logic is unit-tested: `Painter.Select(IList<Finding>, string relPath, int cap)` returns findings for that file in file order, capped.

### Task 3: Calltips on hover

- `SCI_SETMOUSEDWELLTIME(500)` on both views at `NPPN_READY`.
- `SCN_DWELLSTART` (2016): read `position` from the notification (offset 24 on x64). Look up the painted range containing it, then `SCI_CALLTIPSHOW` with the message as UTF-8 bytes in the document's code page. `SCN_DWELLEND` (2017): `SCI_CALLTIPCANCEL`.
- Trace `calltip` with the message.
- [ ] Tests: end to end in Task 4. Dwell is simulated by `SCI_CALLTIPSHOW` being reached through a test hook `DECKCHECK_NPP_TEST_DWELL=<line>:<col>` after `painted`; the trace shows the message.

### Task 4: End-to-end tests

**Files:** `tests/test_npp_e2e.py`, `tests/data/npp/` (generated in the test, not committed)

- [ ] Tests:
  - squiggles land on the field bytes in a Latin-1 deck with `°` before the field, a UTF-8 one and a UTF-8-with-BOM one. The flagged field is on line 1 for the BOM case, using a deck whose first line is `*PART`.
  - a deck with 2,500 dangling `*PART` references paints 2,000, capped;
  - *Disable* clears the squiggles (trace count 0);
  - the calltip shows the message;
  - switching to another tab and back repaints.

### Task 5: The do-no-harm gate

**Files:** `tools/npp/harm_gate.py`, `docs/n3-harm-gate.md`

- **Measures**, each one run 5 times as pairs, with plugin and without, interleaved:
  - **open:** time from launch with `yaris-detailed-v2j.key` until `SCI_GETLENGTH` equals the file size;
  - **type:** median round trip of 50 `SCI_NEWLINE` sends;
  - **scroll:** median round trip of 50 `SCI_LINESCROLL(0, 200)` sends.
- With the plugin, a full-model check is running throughout. It is started with *Check model* on the include, and the trace's `check_started` and `deckcheck.exe` being alive are checked before measuring.
- Without the plugin, a portable copy has no `plugins\deckcheck`.
- **Gate:** the paired-median difference is ≤ 10 % or ≤ 20 ms for each measure.
- [ ] Run it, write `docs/n3-harm-gate.md` with every number from the script's JSON, and commit. A miss is a stop: report to Chintan before tuning.
