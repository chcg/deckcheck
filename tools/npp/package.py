"""Package the Notepad++ plugin: <out>/deckcheck-npp-x64.zip.

Layout (Plugins Admin's validator looks for <folder-name>.dll at the zip root; entries use forward slashes):
    deckcheck.dll, DeckcheckNpp.dll, engine/..., LICENSE.txt, NOTICE.txt, THIRD-PARTY-LICENSES.txt
Builds the plugin with the given version, checks the engine runs as the plugin runs it, refuses a local path inside,
and prints the zip's size and sha256 (the plugin list's "id").

Usage (repo root): .venv\\Scripts\\python -m tools.npp.package --version 0.1.0 [--out editors/notepadpp/out]
"""
import argparse
import hashlib
import json
import subprocess
import sys
import zipfile
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
PLUGIN = REPO / "editors" / "notepadpp" / "out" / "deckcheck"
ENGINE = REPO / "build-engine" / "dist" / "deckcheck"
FREEZE_PY = REPO / ".venv-freeze" / "Scripts" / "python.exe"

HEADER = """Third-party software in this package
=====================================

engine/ is deckcheck frozen with PyInstaller. It contains:

- Python 3.12 runtime (python312.dll, base_library.zip, *.pyd): Python Software Foundation License. Its licence,
  which also covers the bzip2 and libffi libraries Python ships, is reproduced below.
- libcrypto-3.dll (OpenSSL 3): Apache License 2.0, the same licence as deckcheck (LICENSE.txt).
- _decimal (mpdecimal): BSD 2-Clause licence. _lzma (liblzma, XZ Utils): public domain / 0BSD.
- VCRUNTIME140.dll: Microsoft Visual C++ runtime, redistributed under the Visual Studio redistribution terms.
- The PyInstaller bootloader: GPL 2.0 with an exception that permits distributing it with any program; its licence
  is reproduced below.

"""


def _local_path_forms() -> list[str]:
    out = []
    for base in (str(REPO), str(Path.home())):
        fwd = base.replace("\\", "/")
        out += [base, fwd, "file:///" + fwd]
    return out


def third_party_text() -> str:
    base = subprocess.run([str(FREEZE_PY), "-c", "import sys; print(sys.base_prefix)"],
                          capture_output=True, text=True, check=True).stdout.strip()
    python_licence = (Path(base) / "LICENSE.txt").read_text(encoding="utf-8", errors="replace")
    pyi = next((REPO / ".venv-freeze" / "Lib" / "site-packages").glob("pyinstaller-*.dist-info/licenses/COPYING.txt"))
    return (HEADER + "=" * 70 + "\nPython\n" + "=" * 70 + "\n" + python_licence + "\n\n" + "=" * 70
            + "\nPyInstaller\n" + "=" * 70 + "\n" + pyi.read_text(encoding="utf-8", errors="replace"))


def build(version: str, out: Path) -> Path:
    p = subprocess.run(["powershell", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File",
                        str(REPO / "editors/notepadpp/build.ps1"), "-Version", version], capture_output=True, text=True)
    if p.returncode != 0:
        raise SystemExit("plugin build failed:\n" + p.stdout + p.stderr)
    exe = ENGINE / "deckcheck.exe"
    if not exe.exists():
        raise SystemExit("build the engine first: tools\\freeze\\build_engine.ps1")
    run = subprocess.run([str(exe), "--for", "main.k", "--format", "json", "--background"],
                         cwd=REPO / "tests" / "torture" / "base", capture_output=True, text=True)
    if run.returncode != 0 or "reached" not in json.loads(run.stdout)["models"][0]:
        raise SystemExit("the engine in build-engine is stale: rebuild it (tools\\freeze\\build_engine.ps1)")
    out.mkdir(parents=True, exist_ok=True)
    zpath = out / "deckcheck-npp-x64.zip"
    files = [(PLUGIN / "deckcheck.dll", "deckcheck.dll"), (PLUGIN / "DeckcheckNpp.dll", "DeckcheckNpp.dll"),
             (REPO / "LICENSE", "LICENSE.txt"), (REPO / "NOTICE", "NOTICE.txt")]
    files += [(f, "engine/" + f.relative_to(ENGINE).as_posix()) for f in sorted(ENGINE.rglob("*")) if f.is_file()]
    forms = [f.lower() for f in _local_path_forms()]
    with zipfile.ZipFile(zpath, "w", zipfile.ZIP_DEFLATED) as z:
        for src, name in files:
            data = src.read_bytes()
            low = data.lower()
            hit = [f for f in forms if f.encode("utf-8") in low or f.encode("utf-16-le") in low]
            if hit:
                raise SystemExit(f"{name} contains a local path; not packaged")
            z.writestr(name, data)
        z.writestr("THIRD-PARTY-LICENSES.txt", third_party_text())
    return zpath


def main(argv=None) -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--version", required=True)
    ap.add_argument("--out", type=Path, default=REPO / "editors" / "notepadpp" / "out")
    args = ap.parse_args(argv)
    z = build(args.version, args.out)
    print(f"{z.name}  {z.stat().st_size / 2**20:.1f} MB  sha256 {hashlib.sha256(z.read_bytes()).hexdigest()}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
