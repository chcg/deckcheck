"""Write a Win32 .res file holding one VS_VERSIONINFO, for ilasm /RESOURCE (it adds no version resource itself).
Plugins Admin requires the DLL's file version to equal the plugin list's version.

Usage: python -m tools.npp.version_res OUT.res VERSION NAME
"""
import struct
import sys
from pathlib import Path


def _pad(b: bytes) -> bytes:
    return b + bytes(-len(b) % 4)


def _node(key: str, value: bytes = b"", text: bool = False, children: bytes = b"") -> bytes:
    """One version-info block: wLength, wValueLength, wType, szKey, padding, value, padding, children."""
    k = (key + "\0").encode("utf-16-le")
    value_len = len(value) // 2 if text else len(value)
    head = _pad(struct.pack("<HHH", 0, value_len, 1 if text else 0) + k)
    body = _pad(head + value) + children
    return struct.pack("<H", len(body)) + body[2:]


def _numbers(version: str) -> list[int]:
    parts = version.split(".")
    if not 1 <= len(parts) <= 4 or not all(p.isdigit() for p in parts):
        raise ValueError(f"version must be up to four dot-separated numbers: {version!r}")
    return [int(p) for p in parts] + [0] * (4 - len(parts))


def write(path: Path, version: str, name: str) -> None:
    a, b, c, d = _numbers(version)
    full = f"{a}.{b}.{c}.{d}"
    ms, ls = (a << 16) | b, (c << 16) | d
    fixed = struct.pack("<13I", 0xFEEF04BD, 0x00010000, ms, ls, ms, ls, 0x3F, 0, 0x00040004, 2, 0, 0, 0)
    strings = b"".join(_node(k, (v + "\0").encode("utf-16-le"), text=True)
                       for k, v in (("FileDescription", name), ("FileVersion", full), ("ProductName", name),
                                    ("ProductVersion", full), ("OriginalFilename", name + ".dll")))
    sfi = _node("StringFileInfo", text=True, children=_node("040904B0", text=True, children=strings))
    vfi = _node("VarFileInfo", text=True, children=_node("Translation", struct.pack("<HH", 0x0409, 0x04B0)))
    data = _node("VS_VERSION_INFO", fixed, children=sfi + vfi)
    empty = struct.pack("<IIHHHH", 0, 32, 0xFFFF, 0, 0xFFFF, 0) + bytes(16)
    header = struct.pack("<IIHHHHIHHII", len(data), 32, 0xFFFF, 16, 0xFFFF, 1, 0, 0x0030, 0x0409, 0, 0)
    Path(path).write_bytes(empty + header + _pad(data))


if __name__ == "__main__":
    write(Path(sys.argv[1]), sys.argv[2], sys.argv[3])
