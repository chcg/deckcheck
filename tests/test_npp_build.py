"""N2: the Notepad++ plugin build: version resource, exports, menu entry points."""
import struct
import subprocess
import sys
from pathlib import Path

import pytest

from tools.npp.version_res import write

NPP = Path("editors/notepadpp")
pytestmark = pytest.mark.skipif(sys.platform != "win32", reason="Windows build")


def test_version_res_round_trips(tmp_path):
    write(tmp_path / "v.res", "0.1.0", "deckcheck")
    data = (tmp_path / "v.res").read_bytes()
    # every .res starts with an empty entry: DataSize 0, HeaderSize 32, type and name ordinal 0, then 16 zero bytes
    assert data[:32] == struct.pack("<IIHHHH", 0, 32, 0xFFFF, 0, 0xFFFF, 0) + bytes(16)
    assert "0.1.0.0".encode("utf-16-le") in data and "deckcheck".encode("utf-16-le") in data
    # VS_FIXEDFILEINFO: signature, struct version, FileVersionMS = (0 << 16) | 1, FileVersionLS = (0 << 16) | 0
    assert struct.pack("<IIII", 0xFEEF04BD, 0x00010000, 0x00000001, 0x00000000) in data


def test_version_res_rejects_a_bad_version(tmp_path):
    with pytest.raises(ValueError):
        write(tmp_path / "v.res", "0.1.x", "deckcheck")


def _exports(dll: Path):
    d = dll.read_bytes()
    pe = struct.unpack_from("<I", d, 0x3C)[0]
    mach, nsec, optsz = (struct.unpack_from("<H", d, pe + o)[0] for o in (4, 6, 20))
    opt = pe + 24
    erva = struct.unpack_from("<I", d, opt + 112)[0]
    secs = [struct.unpack_from("<8sIIII", d, opt + optsz + i * 40) for i in range(nsec)]

    def off(rva):
        return next(rva - va + rp for _, vs, va, rs, rp in secs if va <= rva < va + max(vs, rs))

    e = off(erva)
    n, names = struct.unpack_from("<I", d, e + 24)[0], off(struct.unpack_from("<I", d, e + 32)[0])
    return mach, sorted(d[off(struct.unpack_from("<I", d, names + 4 * i)[0]):].split(b"\0")[0].decode()
                        for i in range(n))


@pytest.fixture(scope="module")
def built():
    p = subprocess.run(["powershell", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", str(NPP / "build.ps1"),
                        "-Version", "0.1.0"], capture_output=True, text=True)
    assert p.returncode == 0, p.stdout + p.stderr
    return NPP / "out" / "deckcheck"


def test_build_exports_the_six_entry_points(built):
    mach, names = _exports(built / "deckcheck.dll")
    assert mach == 0x8664
    assert names == ["beNotified", "getFuncsArray", "getName", "isUnicode", "messageProc", "setInfo"]


def test_build_has_the_version_resource(built):
    cmd = f"(Get-Item '{(built / 'deckcheck.dll').resolve()}').VersionInfo.FileVersion"
    out = subprocess.run(["powershell", "-NoProfile", "-Command", cmd], capture_output=True, text=True).stdout.strip()
    assert out == "0.1.0.0"
