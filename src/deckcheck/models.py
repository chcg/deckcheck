"""Which model(s) a file belongs to (Notepad++ spec §3): the main decks whose include trees reach it.

Searched (final review, N3): the decks directly in the file's folder, then in its parent, then its grandparent; the
nearest folder whose decks reach the file wins. No folder is scanned recursively, so a file under a drive root or
Documents does not walk that tree. Within the decks found, one included by another is not a main deck. Uses the
language server's light include walk (only *INCLUDE* blocks are read)."""
from pathlib import Path

from deckcheck.includes import _key, _norm, resolve_tree
from deckcheck.lsp.workspace import DECK_SUFFIXES
from deckcheck.reader import read_includes

LEVELS = 3  # the file's folder and two above it


def _reach(decks: list[Path], light: dict) -> dict[str, tuple[Path, set[str]]]:
    out = {}
    for deck in decks:
        try:
            tree = resolve_tree(deck, cache=light, reader=read_includes)
        except (OSError, ValueError):
            continue
        out[_key(deck)] = (deck, {_key(f.path) for f in tree.files})
    return out


def _mains_reaching(k: str, reach: dict) -> list[Path]:
    included = set().union(*(r - {m} for m, (_, r) in reach.items())) if reach else set()
    return sorted((p for m, (p, r) in reach.items() if m not in included and k in r and m != k), key=str)


def models_for_file(path: Path, main_decks: list[Path] | None = None) -> tuple[list[Path], bool]:
    """The main decks reaching `path`, and whether the file is checked alone (no main deck reaches it, and it is not
    a deck itself, or `main_decks` were given and none reaches it)."""
    path = _norm(path)
    k = _key(path)
    light: dict = {}
    if main_decks:
        reach = _reach([_norm(m) for m in main_decks], light)
        found = sorted((p for m, (p, r) in reach.items() if k in r), key=str)
        return (found, False) if found else ([path], True)
    folder = path.parent
    for _ in range(LEVELS):
        decks = sorted(_norm(p) for p in folder.iterdir()
                       if p.suffix.lower() in DECK_SUFFIXES and p.is_file()) if folder.is_dir() else []
        found = _mains_reaching(k, _reach(decks, light))
        if found:
            return found, False
        if folder.parent == folder:
            break
        folder = folder.parent
    return [path], path.suffix.lower() not in DECK_SUFFIXES
