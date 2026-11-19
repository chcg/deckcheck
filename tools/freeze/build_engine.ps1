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
# pip records where it installed from (a local file:/// URL with this machine's path): not shipped.
Get-ChildItem build-engine\dist\deckcheck\_internal -Recurse -Filter direct_url.json | Remove-Item
.venv\Scripts\python -c "from pathlib import Path; from tools.freeze.measure_engine import privacy_hits, needle_forms; n = [x for b in (str(Path.home()), str(Path.cwd().resolve())) for x in needle_forms(b)]; h = privacy_hits(Path('build-engine/dist/deckcheck'), n); print('personal paths:', [f for f, _ in h]); raise SystemExit(1 if h else 0)"
if ($LASTEXITCODE -ne 0) { throw "a home or repo path is embedded in the build" }
.venv-freeze\Scripts\python -m pip show pyinstaller | Select-String "^Version"
