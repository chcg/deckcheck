# N3: do-no-harm gate on the Yaris model

**Date:** 2026-11-19. **Script:** `tools/npp/harm_gate.py` (writes `build-engine/harm_gate.json`). **Spec:** §5.

## Method

- **Models and files:** the Yaris model (`combine.key`, which includes the 169 MB `yaris-detailed-v2j.key`).
- **Notepad++:** two portable copies of Notepad++ 8.9.3, one with the plugin and engine and one without.
- **Trials:** five interleaved pairs after one uncounted warm-up, so both sides read a warm file cache.
- **With the plugin:**
  - *Check model* on `combine.key` starts a full-model check.
  - The trace's `check_started` and a live `deckcheck.exe` are confirmed before measuring.
  - The engine was still running at the end of every trial.
- **Measures:**
  - **open:** time from asking Notepad++ to open the 169 MB include (`WM_COPYDATA`, as a second instance does) until the editor holds all its bytes;
  - **type:** median round trip of 50 `SCI_NEWLINE` messages;
  - **scroll:** median round trip of 50 `SCI_LINESCROLL(0, 200)` messages.
- **Gate:** the paired-median difference is ≤ 10 % of the no-plugin median, or ≤ 20 ms.

## Results (engine in background mode)

| Pair | open with (s) | open without (s) | type with (ms) | type without (ms) | scroll with (ms) | scroll without (ms) |
|---|---|---|---|---|---|---|
| 1 | 0.423 | 0.242 | 16.66 | 16.74 | 16.68 | 16.40 |
| 2 | 0.315 | 0.239 | 16.59 | 16.74 | 16.65 | 16.48 |
| 3 | 0.299 | 0.245 | 16.72 | 16.67 | 16.68 | 16.63 |
| 4 | 0.281 | 0.242 | 16.59 | 16.71 | 16.70 | 16.44 |
| 5 | 0.308 | 0.244 | 16.69 | 16.62 | 16.65 | 16.54 |

| Measure | Without (median) | Paired median difference | Allowed | Verdict |
|---|---|---|---|---|
| open | 0.242 s | +0.065 s | 0.024 s | **missed, accepted by the owner** |
| type | 16.7 ms | −0.1 ms | 20 ms | met |
| scroll | 16.5 ms | +0.2 ms | 20 ms | met |

## What happened along the way

1. **First run** (engine at below-normal CPU priority only): open +0.117 s against 0.031 s allowed; type and scroll met.
2. **Diagnosis:** with the plugin installed but no check running, the open medians were 0.269 s against 0.266 s.
   The plugin's own code costs nothing measurable; the cost is the running engine, which reads the same model from
   disk.
3. **Fix chosen by the owner:** the engine's new `--background` flag puts its process into Windows background mode
   (low CPU, disk and memory priority; only a process can do this to itself), and the plugin passes it. The open
   cost halved, to +0.065 s.
4. **Decision (the owner, 2026-11-19):** accept the remaining +65 ms. It occurs only while a full-vehicle check
   runs, and only when opening a very large file. Typing and scrolling are unaffected. The owner trial is the
   real judge.

## Limits of this measurement

- The type and scroll round trips sit at about 16.7 ms on both sides, one display frame, so these measures
  detect a slowdown of a frame or more, not a few milliseconds.
- One machine: a 4-core laptop with 7.7 GB of memory.
