"""Drive a portable Notepad++ through Win32 messages, for the plugin's end-to-end tests (Windows only)."""
import ctypes
import json
import os
import shutil
import subprocess
import time
from ctypes import wintypes
from pathlib import Path

user32 = ctypes.WinDLL("user32", use_last_error=True)
user32.SendMessageW.restype = ctypes.c_ssize_t
user32.SendMessageW.argtypes = [wintypes.HWND, wintypes.UINT, ctypes.c_size_t, ctypes.c_ssize_t]
user32.PostMessageW.argtypes = [wintypes.HWND, wintypes.UINT, ctypes.c_size_t, ctypes.c_ssize_t]
user32.FindWindowExW.restype = wintypes.HWND
user32.FindWindowExW.argtypes = [wintypes.HWND, wintypes.HWND, wintypes.LPCWSTR, wintypes.LPCWSTR]
user32.GetMenu.restype = wintypes.HMENU
user32.GetMenu.argtypes = [wintypes.HWND]
user32.GetSubMenu.restype = wintypes.HMENU
user32.GetSubMenu.argtypes = [wintypes.HMENU, ctypes.c_int]
user32.GetMenuItemCount.argtypes = [wintypes.HMENU]
user32.GetMenuItemID.restype = wintypes.UINT
user32.GetMenuItemID.argtypes = [wintypes.HMENU, ctypes.c_int]
user32.GetMenuStringW.argtypes = [wintypes.HMENU, wintypes.UINT, wintypes.LPWSTR, ctypes.c_int, wintypes.UINT]
user32.GetMenuState.restype = wintypes.UINT
user32.GetMenuState.argtypes = [wintypes.HMENU, wintypes.UINT, wintypes.UINT]
EnumProc = ctypes.WINFUNCTYPE(wintypes.BOOL, wintypes.HWND, wintypes.LPARAM)
user32.EnumWindows.argtypes = [EnumProc, wintypes.LPARAM]
user32.GetWindowThreadProcessId.argtypes = [wintypes.HWND, ctypes.POINTER(wintypes.DWORD)]
user32.GetClassNameW.argtypes = [wintypes.HWND, wintypes.LPWSTR, ctypes.c_int]

WM_COMMAND, WM_CLOSE = 0x0111, 0x0010
MF_BYPOSITION, MF_CHECKED = 0x400, 0x8
IDM_FILE_SAVE = 41006
SCI_APPENDTEXT, SCI_GETLENGTH, SCI_GETSELECTIONSTART, SCI_GETSELECTIONEND = 2282, 2006, 2143, 2145
SCI_GETCURRENTPOS, SCI_LINEFROMPOSITION = 2008, 2166


def portable_npp(dest: Path, plugin: Path, engine: Path | None) -> Path:
    """Copy the installed Notepad++ to dest in portable mode, with the built plugin (and engine) installed."""
    src = Path(os.environ.get("ProgramFiles", r"C:\Program Files")) / "Notepad++"
    shutil.copytree(src, dest)
    (dest / "doLocalConf.xml").write_text("")
    target = dest / "plugins" / "deckcheck"
    shutil.copytree(plugin, target)
    if engine is not None:
        shutil.copytree(engine, target / "engine")
    return dest / "notepad++.exe"


class Npp:
    def __init__(self, exe: Path, files: list[Path], env: dict):
        self.proc = subprocess.Popen([str(exe), "-multiInst", "-nosession", *map(str, files)], env={**os.environ, **env})
        self.hwnd = None
        deadline = time.time() + 20
        while time.time() < deadline and not self.hwnd:
            time.sleep(0.25)
            self.hwnd = self._main_window()
        assert self.hwnd, "Notepad++ window did not appear"
        time.sleep(1.5)

    def _main_window(self):
        found = []

        def cb(h, _):
            pid = wintypes.DWORD()
            user32.GetWindowThreadProcessId(h, ctypes.byref(pid))
            name = ctypes.create_unicode_buffer(64)
            user32.GetClassNameW(h, name, 64)
            if pid.value == self.proc.pid and name.value == "Notepad++":
                found.append(h)
            return True

        user32.EnumWindows(EnumProc(cb), 0)
        return found[0] if found else None

    def _menu_text(self, menu, i: int) -> str:
        buf = ctypes.create_unicode_buffer(256)
        user32.GetMenuStringW(menu, i, buf, 256, MF_BYPOSITION)
        return buf.value

    def plugin_menu(self) -> tuple[int, list[tuple[str, int, bool]]]:
        """(handle of the deckcheck submenu, [(text, command id, checked)])."""
        bar = user32.GetMenu(self.hwnd)
        for i in range(user32.GetMenuItemCount(bar)):
            sub = user32.GetSubMenu(bar, i)
            for j in range(max(user32.GetMenuItemCount(sub), 0)):
                if self._menu_text(sub, j) == "deckcheck":
                    pm = user32.GetSubMenu(sub, j)
                    items = [(self._menu_text(pm, k).split("\t")[0], user32.GetMenuItemID(pm, k),
                              bool(user32.GetMenuState(pm, k, MF_BYPOSITION) & MF_CHECKED))
                             for k in range(user32.GetMenuItemCount(pm))]
                    return pm, items
        raise AssertionError("no deckcheck menu")

    def command(self, text: str) -> None:
        _, items = self.plugin_menu()
        cid = next(c for t, c, _ in items if t == text)
        user32.PostMessageW(self.hwnd, WM_COMMAND, cid, 0)

    def scintilla(self):
        return user32.FindWindowExW(self.hwnd, None, "Scintilla", None)

    def sci(self, msg: int, w: int = 0, l: int = 0) -> int:
        return user32.SendMessageW(self.scintilla(), msg, w, l)

    def save(self) -> None:
        user32.PostMessageW(self.hwnd, WM_COMMAND, IDM_FILE_SAVE, 0)

    def close(self) -> None:
        user32.PostMessageW(self.hwnd, WM_CLOSE, 0, 0)
        try:
            self.proc.wait(10)
        except subprocess.TimeoutExpired:
            self.kill()

    def kill(self) -> None:
        self.proc.kill()
        self.proc.wait(10)


def events(trace: Path) -> list[dict]:
    if not trace.exists():
        return []
    return [json.loads(l) for l in trace.read_text(encoding="utf-8").splitlines() if l.strip()]


def wait_event(trace: Path, name: str, timeout: float = 60, after: int = 0) -> dict:
    deadline = time.time() + timeout
    while time.time() < deadline:
        for e in events(trace)[after:]:
            if e["event"] == name:
                return e
        time.sleep(0.2)
    raise AssertionError(f"no {name} event within {timeout} s; got {[e['event'] for e in events(trace)]}")


def engine_processes(engine_dir: Path) -> list[int]:
    """Running deckcheck.exe processes started from this engine folder."""
    exe = str((engine_dir / "deckcheck.exe").resolve()).lower()
    out = subprocess.run(["powershell", "-NoProfile", "-Command",
                          "Get-Process deckcheck -ErrorAction SilentlyContinue | ForEach-Object { \"$($_.Id)|$($_.Path)\" }"],
                         capture_output=True, text=True).stdout
    return [int(l.split("|")[0]) for l in out.splitlines() if "|" in l and l.split("|", 1)[1].lower() == exe]
