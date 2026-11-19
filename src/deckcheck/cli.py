"""The command line (spec §6): deckcheck MAIN.k [MAIN2.k ...] [--format text|json] [--ignore CODE ...], or
deckcheck --for FILE [--main-deck DECK ...] to check the model(s) a file belongs to (Notepad++ spec §3).
Formats reports; contains no checking logic. Exit 0: no errors; 1: an error; 2: could not run.
JSON "models": one entry per checked main deck; "files" counts every file its include tree reached, the main included,
and "reached" lists them (an editor uses it to tell whether a saved file belongs to the checked model)."""
import argparse
import json
import os
import sys
from importlib.metadata import PackageNotFoundError, version
from pathlib import Path

from deckcheck.diagnostics import Report
from deckcheck.models import models_for_file
from deckcheck.rules import CODES, check


def _version() -> str:
    try:
        return version("deckcheck")
    except PackageNotFoundError:
        return "unknown"


def _rel(path: Path) -> str:
    try:
        return Path(os.path.relpath(path)).as_posix()
    except ValueError:  # another drive on Windows: relpath has no answer
        return Path(os.path.abspath(path)).as_posix()


def _enter_background() -> bool:
    """Windows background mode for this process: low CPU, disk and memory priority, so an editor that started the
    check stays responsive (Notepad++ spec §5). Only a process can put itself in this mode. False elsewhere."""
    if sys.platform != "win32":
        return False
    import ctypes
    kernel32 = ctypes.WinDLL("kernel32")
    kernel32.GetCurrentProcess.restype = ctypes.c_void_p
    kernel32.SetPriorityClass.argtypes = [ctypes.c_void_p, ctypes.c_uint32]
    return bool(kernel32.SetPriorityClass(kernel32.GetCurrentProcess(), 0x00100000))  # PROCESS_MODE_BACKGROUND_BEGIN


def _plural(n: int, word: str) -> str:
    return f"{n} {word}{'' if n == 1 else 's'}"


def _text(reports: list[Report], for_file: Path | None = None, alone: bool = False) -> str:
    lines = []
    if for_file is not None:
        if alone:
            lines.append(f"Checked alone: no main deck includes {_rel(for_file)}")
        else:
            lines += [f"Checked as part of {_rel(r.main)}" for r in reports]
        lines.append("")
    for r in reports:
        for d in r.diagnostics:
            lines.append(f"{_rel(d.path)}:{d.line}:{d.start + 1}  {d.severity:<7}  {d.code}  {d.message}")
    errors = sum(d.severity == "error" for r in reports for d in r.diagnostics)
    warnings = sum(d.severity == "warning" for r in reports for d in r.diagnostics)
    if lines:
        lines += ["", f"{_plural(errors + warnings, 'problem')} ({_plural(errors, 'error')}, {_plural(warnings, 'warning')})"]
    else:
        lines.append("No problems found")
    for r in reports:
        head = "Not checked:" if len(reports) == 1 else f"Not checked ({_rel(r.main)}):"
        lines += ["", head]
        for n in r.not_checked:
            more = f" (+{n.count - len(n.reasons)} more)" if n.count > len(n.reasons) else ""
            lines.append(f"  {n.what}: {'; '.join(n.reasons)}{more}")
    return "\n".join(lines) + "\n"


def _json(reports: list[Report], alone: bool = False) -> str:
    diags = [d for r in reports for d in r.diagnostics]
    return json.dumps({
        "version": 1,
        "models": [{"main": _rel(r.main), "files": len(r.tree.files), "alone": alone,
                    "reached": sorted({_rel(f.path) for f in r.tree.files})} for r in reports],
        "diagnostics": [{"file": _rel(d.path), "line": d.line, "column": d.start + 1, "end_column": d.end + 1,
                         "severity": d.severity, "code": d.code, "message": d.message,
                         "related": [{"file": _rel(x.path), "line": x.line, "message": x.message} for x in d.related]}
                        for d in diags],
        "not_checked": [{"main": _rel(r.main), "what": n.what, "reasons": list(n.reasons), "count": n.count}
                        for r in reports for n in r.not_checked],
        "summary": {"errors": sum(d.severity == "error" for d in diags),
                    "warnings": sum(d.severity == "warning" for d in diags)},
    }, indent=2) + "\n"


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(prog="deckcheck", description="Check keyword decks for LS-DYNA for structural "
                                     "mistakes: dangling references, duplicate ids, bad values, include problems.")
    parser.add_argument("decks", nargs="*", type=Path, metavar="MAIN.k", help="main deck; includes are followed")
    parser.add_argument("--for", dest="for_file", type=Path, metavar="FILE",
                        help="check the model(s) this file belongs to: the decks in its folder that include it")
    parser.add_argument("--main-deck", action="append", default=[], type=Path, metavar="DECK",
                        help="with --for: use this main deck instead of scanning the folder (repeatable)")
    parser.add_argument("--format", choices=("text", "json"), default="text")
    parser.add_argument("--ignore", action="append", default=[], metavar="CODE", help="leave out a code (repeatable)")
    parser.add_argument("--background", action="store_true",
                        help="run at background priority (Windows: low CPU, disk and memory priority)")
    parser.add_argument("--version", action="version", version=f"deckcheck {_version()}")
    for stream in (sys.stdout, sys.stderr):
        if hasattr(stream, "reconfigure"):
            stream.reconfigure(errors="replace")  # a path the console can't encode must not crash the run
    args = parser.parse_args(argv)
    ignore = {c.upper() for c in args.ignore}
    unknown = sorted(ignore - set(CODES))
    if unknown:
        parser.error(f"unknown code {unknown[0]} (known: {', '.join(CODES)})")
    if args.background:
        _enter_background()
    if bool(args.decks) == (args.for_file is not None):
        parser.error("give main decks, or --for FILE, but not both")
    if args.main_deck and args.for_file is None:
        parser.error("--main-deck needs --for")
    for deck in [*args.decks, *([args.for_file] if args.for_file else []), *args.main_deck]:
        if not deck.is_file():
            print(f"deckcheck: {deck}: no such file", file=sys.stderr)
            return 2
    alone = False
    try:
        decks = args.decks
        if args.for_file is not None:
            decks, alone = models_for_file(args.for_file, args.main_deck)
        reports = [check(deck) for deck in decks]
    except Exception as e:  # outside any one block: deckcheck could not run (spec §6)
        print(f"deckcheck: internal error: {type(e).__name__}: {e} (please report this)", file=sys.stderr)
        return 2
    for r in reports:
        r.diagnostics[:] = [d for d in r.diagnostics if d.code not in ignore]
    sys.stdout.write(_json(reports, alone) if args.format == "json" else _text(reports, args.for_file, alone))
    return 1 if any(d.severity == "error" for r in reports for d in r.diagnostics) else 0
