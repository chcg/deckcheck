# N0: frozen-engine spike

**Date:** 2026-11-19. **Plan:** `docs/plans/2026-11-17-n0-engine-spike.md`. **Spec:** `docs/specs/2026-11-17-notepadpp-design.md` §8-9.
Numbers come from `build-engine/measure.json` (written by `tools/freeze/measure_engine.py`) and the printed test and scan results.

## 1. Verdict

| Threshold | Value | Status |
|---|---|---|
| Defender: 0 detections | 0 (folder, zip, run with Mark of the Web) | met |
| Zip ≤ 30 MB | 7.3 MB | met |
| `--version` start median ≤ 1.5 s | 0.128 s | met |
| Yaris check median ≤ 1.2 × pip CLI | 70.92 s against 70.85 s, ratio 1.001 | met |
| Frozen output identical to the CLI on all torture decks | 88 of 88 parity tests | met |

VirusTotal: not scanned yet. It is an upload, so the owner scans the release zip himself before any public step (carried to N4).

## 2. Build

- PyInstaller 6.22.3, one-folder (`--onedir --console --collect-data deckcheck`), from its own venv (`.venv-freeze`).
- `build-engine/dist/deckcheck/`: 28 files, 22.5 MB; zipped 7.3 MB.
- Script: `tools/freeze/build_engine.ps1`.

## 3. Parity

`tests/test_frozen.py` with `DECKCHECK_FROZEN` set: 88 passed. Each case compares stdout (JSON) and the exit code against
`python -m deckcheck`: all 86 torture decks, the base deck copied into a folder named `Prüfung µm`, and `--version`.
No difference was found. Without the variable the file is skipped; the suite then gives 1,356 passed, 97 skipped,
2 xfailed.

## 4. Speed

| Check | Frozen median | CLI median | Ratio |
|---|---|---|---|
| `--version` start (10 runs) | 0.128 s | | |
| LS-DYNA example bumper deck (5 interleaved runs each) | 0.82 s | 0.91 s | 0.903 |
| Yaris, main deck `combine.key` with the 169 MB include (3 runs each) | 70.92 s | 70.85 s | 1.001 |

The frozen engine costs nothing measurable on a large model. The plan named `set-yaris-detailed-v2j.key` as the Yaris
deck; it is a 46 KB fragment, so the measurement uses the main deck `combine.key`.

## 5. Antivirus

- Windows Defender: product 4.18.26100.4, signatures 1.463.374.0 updated the same day, real-time and tamper protection on.
- `MpCmdRun -Scan -ScanType 3` on the build folder and on the zip: no threats. No detection recorded afterwards.
- Mark of the Web: the build copied to a temporary folder with a `Zone.Identifier` (ZoneId=3) stream on every file.
  `--version` and a torture-deck check ran normally (exit 0, output identical to the CLI), with no prompt or block.
- VirusTotal: pending (section 1).

## 6. Plugins Admin and the template licence (read 2026-11-19)

From `github.com/notepad-plus-plus/nppPluginList` (`validator.py`, `src/pl.x64.json`):
- An entry has `folder-name`, `display-name`, `version`, `id` (sha256 of the zip), `repository` (the zip URL),
  `description`, `author`, `homepage`, and optionally `npp-compatible-versions` and `old-versions-compatibility`.
- The validator downloads the zip, checks its sha256 against `id`, and requires `<folder-name>.dll` at the zip's
  root (case-insensitive). The DLL's file-version resource must equal `version`, padded to four parts.
- It does not check size, other files, or the DLL's architecture. There is no stated size limit and no rule against
  bundled executables. Precedent: the QueryFiles entry describes running SQL through a bundled `duckdb.exe`.
- So `deckcheck.dll` at the root plus an `engine\` subfolder fits the rules.

From `github.com/molsonkiko/NppCSharpPluginPack` (the template JSON Tools' author maintains):
- Licence Apache-2.0, the same as deckcheck. It credits kbilsted's archived NotepadPlusPlusPluginPack.Net as the
  base of most of its code.
- Target .NET Framework 4.8; x86 and x64 configurations; no ARM64 mentioned.
- Old-style csproj built in Visual Studio 2022; exports through the `UnmanagedExports.Repack.Upgrade` NuGet build
  task. This machine has no Visual Studio and no .NET SDK, so N2 must settle the build toolchain first.

## 7. Personal paths

The scan looked for the home path, the user name and the repo path, in UTF-8 and UTF-16LE, case-insensitively. One
hit: the 4-letter user name matched inside a public keyword name in `deckcheck/data/keywords.json` (a substring of an
LS-DYNA keyword). The same bytes ship in the PyPI package. No home or repo path is embedded. No fix needed.

## 8. Carried forward

- **N2 toolchain:** build the plugin without Visual Studio, or install a toolchain. To decide in the N2 plan.
- **VirusTotal:** the owner scans the release zip before any public step.
- **ARM64:** not attempted; x64 only for v1, as the spec says.
- **Engine start is fast** (0.13 s), so a process per check is fine; no long-lived server needed for v1.
