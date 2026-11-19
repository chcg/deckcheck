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
