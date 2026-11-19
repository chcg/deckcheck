"""Which model(s) a file belongs to (Notepad++ spec §3): the main decks whose include trees reach it. Shares the
language server's include scan (lsp/workspace.py) so both front ends agree."""
from pathlib import Path

from deckcheck.includes import _key, _norm
from deckcheck.lsp.workspace import Settings, Workspace


def models_for_file(path: Path, main_decks: list[Path] | None = None) -> tuple[list[Path], bool]:
    """The main decks reaching `path` (its folder scanned, or `main_decks` when given), and whether none does: then
    the file is checked alone."""
    path = _norm(path)
    ws = Workspace([path.parent], Settings(main_decks=[_norm(m) for m in main_decks or []]))
    ws.scan()
    k = _key(path)
    found = sorted((p for m, p in ws._mains.items() if k in ws._reach[m]), key=str)
    return (found, False) if found else ([path], True)
