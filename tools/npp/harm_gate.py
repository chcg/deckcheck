"""N3 do-no-harm gate (spec §5): opening, typing in and scrolling the 169 MB Yaris include, with the plugin checking the
full Yaris model and without the plugin, in interleaved pairs. Gate: paired-median difference <= 10 % or <= 20 ms.

Usage (repo root, after editors/notepadpp/build.ps1 and tools/freeze/build_engine.ps1):
    .venv\\Scripts\\python -m tools.npp.harm_gate [--pairs 5]
Writes build-engine/harm_gate.json and prints it."""
import argparse
import ctypes
import json
import shutil
import statistics
import tempfile
import time
from ctypes import wintypes
from pathlib import Path

from tools.npp import drive

REPO = Path(__file__).resolve().parents[2]
YARIS = REPO / ".corpus/local/ccsa-yaris/2010-toyota-yaris-detailed-v2j"
MAIN, BIG = YARIS / "combine.key", YARIS / "yaris-detailed-v2j.key"
SCI_NEWLINE, SCI_LINESCROLL, SCI_UNDO = 2329, 2168, 2176


class CopyData(ctypes.Structure):
    _fields_ = [("dwData", ctypes.c_size_t), ("cbData", wintypes.DWORD), ("lpData", ctypes.c_void_p)]


def open_file(npp: drive.Npp, path: Path) -> None:
    """Ask Notepad++ to open a file the way a second instance does (WM_COPYDATA, COPYDATA_FILENAMESW)."""
    text = ctypes.create_unicode_buffer(str(path))
    cds = CopyData(2, ctypes.sizeof(text), ctypes.cast(text, ctypes.c_void_p))
    drive.user32.SendMessageW(npp.hwnd, 0x004A, 0, ctypes.addressof(cds))


def _rt(npp: drive.Npp, msg: int, w: int, l: int, n: int) -> float:
    times = []
    for _ in range(n):
        t = time.perf_counter()
        npp.sci(msg, w, l)
        times.append(time.perf_counter() - t)
    return statistics.median(times)


def trial(exe: Path, with_plugin: bool, trace: Path) -> dict:
    trace.unlink(missing_ok=True)
    npp = drive.Npp(exe, [MAIN], {"DECKCHECK_NPP_TRACE": str(trace)})
    engine_dir = exe.parent / "plugins" / "deckcheck" / "engine"
    try:
        if with_plugin:
            npp.command("Check model")
            drive.wait_event(trace, "check_started", timeout=30)
            time.sleep(1.5)
            assert drive.engine_processes(engine_dir), "engine not running"
        size = BIG.stat().st_size
        t = time.perf_counter()
        open_file(npp, BIG)
        while npp.sci(drive.SCI_GETLENGTH) != size:
            if time.perf_counter() - t > 300:
                raise AssertionError("open timed out")
            time.sleep(0.02)
        opened = time.perf_counter() - t
        time.sleep(0.5)
        typing = _rt(npp, SCI_NEWLINE, 0, 0, 50)
        scroll = _rt(npp, SCI_LINESCROLL, 0, 200, 50)
        busy = bool(drive.engine_processes(engine_dir)) if with_plugin else None
        return {"open_s": opened, "type_s": typing, "scroll_s": scroll, "engine_running_at_end": busy}
    finally:
        npp.kill()
        time.sleep(2)


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--pairs", type=int, default=5)
    args = ap.parse_args()
    work = Path(tempfile.mkdtemp(prefix="harm_gate_"))
    try:
        with_exe = drive.portable_npp(work / "with", REPO / "editors/notepadpp/out/deckcheck",
                                      REPO / "build-engine/dist/deckcheck")
        without_exe = drive.portable_npp(work / "without", REPO / "editors/notepadpp/out/deckcheck", None)
        shutil.rmtree(without_exe.parent / "plugins" / "deckcheck")
        trial(without_exe, False, work / "trace.jsonl")  # warm-up, not counted: both sides then read a warm file cache
        pairs = []
        for i in range(args.pairs):
            order = [(True, with_exe), (False, without_exe)] if i % 2 == 0 else [(False, without_exe), (True, with_exe)]
            got = {w: trial(exe, w, work / "trace.jsonl") for w, exe in order}
            pairs.append({"with": got[True], "without": got[False]})
            print(f"pair {i + 1}: {json.dumps(pairs[-1])}", flush=True)
        verdict = {}
        for m in ("open_s", "type_s", "scroll_s"):
            base = statistics.median(p["without"][m] for p in pairs)
            diff = statistics.median(p["with"][m] - p["without"][m] for p in pairs)
            verdict[m] = {"without_median": round(base, 5), "paired_median_diff": round(diff, 5),
                          "allowed": round(max(0.10 * base, 0.020), 5), "met": diff <= max(0.10 * base, 0.020)}
        out = {"pairs": pairs, "verdict": verdict, "all_met": all(v["met"] for v in verdict.values()),
               "engine_running_throughout": all(p["with"]["engine_running_at_end"] for p in pairs)}
        (REPO / "build-engine" / "harm_gate.json").write_text(json.dumps(out, indent=1))
        print(json.dumps({k: out[k] for k in ("verdict", "all_met", "engine_running_throughout")}, indent=1))
        return 0
    finally:
        shutil.rmtree(work, ignore_errors=True)


if __name__ == "__main__":
    raise SystemExit(main())
