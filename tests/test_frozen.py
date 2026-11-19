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
