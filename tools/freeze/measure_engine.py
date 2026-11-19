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
