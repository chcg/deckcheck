"""The Notepad++ release zip: layout Plugins Admin checks, licences, no personal paths, and an engine that runs."""
import json
import subprocess
import sys
import zipfile
from pathlib import Path

import pytest

REPO = Path(__file__).resolve().parents[1]
ENGINE = REPO / "build-engine" / "dist" / "deckcheck" / "deckcheck.exe"
pytestmark = pytest.mark.skipif(sys.platform != "win32" or not ENGINE.exists(), reason="needs the built engine on Windows")


@pytest.fixture(scope="module")
def package(tmp_path_factory):
    out = tmp_path_factory.mktemp("pkg")
    p = subprocess.run([sys.executable, "-m", "tools.npp.package", "--version", "0.1.0", "--out", str(out)],
                       cwd=REPO, capture_output=True, text=True)
    assert p.returncode == 0, p.stdout + p.stderr
    return out / "deckcheck-npp-x64.zip", p.stdout


def test_layout_for_plugins_admin(package):
    zip_path, _ = package
    names = zipfile.ZipFile(zip_path).namelist()
    assert "deckcheck.dll" in names and "DeckcheckNpp.dll" in names  # the DLL named after the folder, at the root
    assert "engine/deckcheck.exe" in names and not any("\\" in n for n in names)


def test_licences_are_included(package):
    names = zipfile.ZipFile(package[0]).namelist()
    assert {"LICENSE.txt", "THIRD-PARTY-LICENSES.txt"} <= set(names)
    text = zipfile.ZipFile(package[0]).read("THIRD-PARTY-LICENSES.txt").decode("utf-8")
    assert "Python Software Foundation" in text and "PyInstaller" in text
    assert "Stefan Krah" in text  # mpdecimal (_decimal)
    assert "liblzma is in the public domain" in text  # xz 5.2.5 (_lzma)
    assert "OpenSSL 3.0.15" in text and "Apache License" in text  # libcrypto-3.dll


def test_no_local_path_inside(package):
    z = zipfile.ZipFile(package[0])
    assert not any(n.endswith("direct_url.json") for n in z.namelist())
    forms = [str(REPO), str(REPO).replace("\\", "/"), str(Path.home()), str(Path.home()).replace("\\", "/")]
    for n in z.namelist():
        data = z.read(n).lower()
        for f in forms:
            assert f.lower().encode("utf-8") not in data and f.lower().encode("utf-16-le") not in data, (n, "local path")


def test_the_packaged_engine_runs_like_the_plugin_runs_it(package, tmp_path):
    zipfile.ZipFile(package[0]).extractall(tmp_path / "plugin")
    exe = tmp_path / "plugin" / "engine" / "deckcheck.exe"
    p = subprocess.run([str(exe), "--for", "main.k", "--format", "json", "--background"],
                       cwd=REPO / "tests" / "torture" / "base", capture_output=True, text=True)
    assert p.returncode == 0
    (m,) = json.loads(p.stdout)["models"]
    assert m["main"] == "main.k" and "reached" in m


def test_prints_size_and_sha256(package):
    _, stdout = package
    assert "sha256 " in stdout and "MB" in stdout
