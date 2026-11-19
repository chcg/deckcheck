"""N2 end to end: the built plugin in a portable copy of the installed Notepad++, driven by Win32 messages.
Opt-in (it opens Notepad++ windows): DECKCHECK_NPP_E2E=1. Needs the frozen engine in build-engine/dist/deckcheck."""
import json
import os
import shutil
import subprocess
import sys
import time
from pathlib import Path

import pytest

pytestmark = pytest.mark.skipif(os.environ.get("DECKCHECK_NPP_E2E") != "1" or sys.platform != "win32",
                                reason="set DECKCHECK_NPP_E2E=1 to drive Notepad++")

if os.environ.get("DECKCHECK_NPP_E2E") == "1":
    from tools.npp import drive

REPO = Path(__file__).resolve().parents[1]
ENGINE = REPO / "build-engine" / "dist" / "deckcheck"
TORTURE = REPO / "tests" / "torture"
YARIS = REPO / ".corpus/local/ccsa-yaris/2010-toyota-yaris-detailed-v2j/combine.key"
SCI_NEWLINE = 2329


@pytest.fixture(scope="module")
def plugin():
    p = subprocess.run(["powershell", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File",
                        str(REPO / "editors/notepadpp/build.ps1")], capture_output=True, text=True)
    assert p.returncode == 0, p.stdout + p.stderr
    assert (ENGINE / "deckcheck.exe").exists(), "build the engine first: tools/freeze/build_engine.ps1"
    return REPO / "editors/notepadpp/out/deckcheck"


@pytest.fixture(scope="module")
def npp_exe(plugin, tmp_path_factory):
    return drive.portable_npp(tmp_path_factory.mktemp("npp") / "npp", plugin, ENGINE)


@pytest.fixture
def trace(tmp_path, npp_exe):
    settings = npp_exe.parent / "plugins" / "Config" / "deckcheck" / "settings.json"
    if settings.exists():
        settings.unlink()
    return tmp_path / "trace.jsonl"


def _copy(case: str, tmp_path: Path) -> Path:
    dest = tmp_path / case
    shutil.copytree(TORTURE / case, dest)
    return dest


def _cli(main: Path) -> dict:
    out = subprocess.run([sys.executable, "-m", "deckcheck", main.name, "--format", "json"], cwd=main.parent,
                         capture_output=True, text=True).stdout
    return json.loads(out)


def test_menu_has_the_commands(npp_exe, trace):
    npp = drive.Npp(npp_exe, [], {"DECKCHECK_NPP_TRACE": str(trace)})
    try:
        _, items = npp.plugin_menu()
        assert [t for t, _, _ in items if t] == ["Check model", "Show findings", "Main decks...", "Disable", "About"]
    finally:
        npp.close()


def test_check_model_matches_the_cli(npp_exe, trace, tmp_path):
    main = _copy("seeded_mistakes", tmp_path) / "main.k"
    npp = drive.Npp(npp_exe, [main], {"DECKCHECK_NPP_TRACE": str(trace)})
    try:
        npp.command("Check model")
        done = drive.wait_event(trace, "check_done")
        cli = _cli(main)
        assert (done["errors"], done["warnings"]) == (cli["summary"]["errors"], cli["summary"]["warnings"])
        assert done["models"] == ["main.k"] and done["alone"] is False
    finally:
        npp.close()


def test_an_include_is_checked_through_its_main(npp_exe, trace, tmp_path):
    inc = _copy("inc_nested_main", tmp_path) / "b.k"
    npp = drive.Npp(npp_exe, [inc], {"DECKCHECK_NPP_TRACE": str(trace)})
    try:
        npp.command("Check model")
        assert drive.wait_event(trace, "check_done")["models"] == ["main.k"]
    finally:
        npp.close()


def test_jump_selects_the_field(npp_exe, trace, tmp_path):
    main = _copy("seeded_mistakes", tmp_path) / "main.k"
    first = _cli(main)["diagnostics"][0]
    npp = drive.Npp(npp_exe, [main], {"DECKCHECK_NPP_TRACE": str(trace), "DECKCHECK_NPP_TEST_JUMP": "0"})
    try:
        npp.command("Check model")
        j = drive.wait_event(trace, "jumped")
        assert (j["line"], j["sel_start"], j["sel_end"]) == (first["line"], first["column"] - 1, first["end_column"] - 1)
        assert npp.sci(drive.SCI_LINEFROMPOSITION, npp.sci(drive.SCI_GETCURRENTPOS)) == first["line"] - 1
    finally:
        npp.close()


def test_save_checks_and_disable_stops_it(npp_exe, trace, tmp_path):
    main = _copy("inc_nested_main", tmp_path) / "main.k"
    npp = drive.Npp(npp_exe, [main], {"DECKCHECK_NPP_TRACE": str(trace)})
    try:
        npp.sci(SCI_NEWLINE)
        npp.save()
        drive.wait_event(trace, "check_done")
        npp.command("Disable")
        assert drive.wait_event(trace, "disabled")["on"] is True
        n = len(drive.events(trace))
        npp.sci(SCI_NEWLINE)
        npp.save()
        time.sleep(3)
        assert [e["event"] for e in drive.events(trace)[n:]] == []
        assert [c for t, _, c in npp.plugin_menu()[1] if t == "Disable"] == [True]
    finally:
        npp.close()


def test_missing_engine_is_reported_and_the_editor_still_works(plugin, trace, tmp_path):
    exe = drive.portable_npp(tmp_path / "npp_noengine", plugin, None)
    main = _copy("seeded_mistakes", tmp_path) / "main.k"
    npp = drive.Npp(exe, [main], {"DECKCHECK_NPP_TRACE": str(trace)})
    try:
        npp.command("Check model")
        assert "engine not found" in drive.wait_event(trace, "check_error")["error"]
        before = npp.sci(drive.SCI_GETLENGTH)
        npp.sci(SCI_NEWLINE)
        assert npp.sci(drive.SCI_GETLENGTH) > before
    finally:
        npp.close()


def _seeded_variant(tmp_path: Path, name: str, encoding: str, bom: bool) -> Path:
    """The seeded deck with a non-ASCII comment line, in a given encoding (N3: column conversion)."""
    text = (TORTURE / "seeded_mistakes" / "main.k").read_text(encoding="latin-1")
    text = text.replace("*KEYWORD\n", "*KEYWORD\n$ bumper tested at 20°C, gap 5µm\n", 1)
    folder = tmp_path / name
    folder.mkdir()
    (folder / "main.k").write_bytes((b"\xef\xbb\xbf" if bom else b"") + text.encode(encoding))
    return folder / "main.k"


@pytest.mark.parametrize("name,encoding,bom", [("latin1", "latin-1", False), ("utf8", "utf-8", False),
                                               ("utf8bom", "utf-8", True)])
def test_squiggles_and_jump_land_on_the_field(npp_exe, trace, tmp_path, name, encoding, bom):
    main = _seeded_variant(tmp_path, name, encoding, bom)
    npp = drive.Npp(npp_exe, [main], {"DECKCHECK_NPP_TRACE": str(trace), "DECKCHECK_NPP_TEST_JUMP": "0"})
    try:
        npp.command("Check model")
        painted = drive.wait_event(trace, "painted")
        assert painted["count"] >= 2 and painted["first_text"] == "missing_file.k"
        assert drive.wait_event(trace, "jumped")["text"] == "missing_file.k"
    finally:
        npp.close()


def test_the_dangling_mid_is_squiggled(npp_exe, trace, tmp_path):
    main = _seeded_variant(tmp_path, "mid", "utf-8", False)
    npp = drive.Npp(npp_exe, [main], {"DECKCHECK_NPP_TRACE": str(trace), "DECKCHECK_NPP_TEST_JUMP": "1"})
    try:
        npp.command("Check model")
        assert drive.wait_event(trace, "jumped")["text"] == "99"
    finally:
        npp.close()


def test_painting_is_capped(npp_exe, trace, tmp_path):
    cards = "".join(f"*PART\np{i}\n{i:>10}         2        99\n" for i in range(1, 2501))
    main = tmp_path / "many.k"
    main.write_text(f"*KEYWORD\n{cards}*SECTION_SHELL\n         2        16\n*END\n", encoding="latin-1")
    npp = drive.Npp(npp_exe, [main], {"DECKCHECK_NPP_TRACE": str(trace)})
    try:
        npp.command("Check model")
        done = drive.wait_event(trace, "check_done")
        painted = drive.wait_event(trace, "painted")
        assert done["rows"] >= 2500 and painted["count"] == 2000 and painted["capped"] is True
        assert done["panel_ms"] < 200  # a virtual list: rows are built only when shown
    finally:
        npp.close()


def test_calltip_and_disable_clears(npp_exe, trace, tmp_path):
    main = _copy("seeded_mistakes", tmp_path) / "main.k"
    npp = drive.Npp(npp_exe, [main], {"DECKCHECK_NPP_TRACE": str(trace), "DECKCHECK_NPP_TEST_DWELL": "0"})
    try:
        npp.command("Check model")
        assert drive.wait_event(trace, "calltip")["text"].startswith("DC004 error: include file 'missing_file.k'")
        n = len(drive.events(trace))
        npp.command("Disable")
        assert drive.wait_event(trace, "painted", after=n)["count"] == 0
    finally:
        npp.close()


def test_switching_tabs_repaints(npp_exe, trace, tmp_path):
    folder = _copy("seeded_mistakes", tmp_path)
    other = tmp_path / "other.txt"
    other.write_text("notes\n")
    npp = drive.Npp(npp_exe, [folder / "main.k", other], {"DECKCHECK_NPP_TRACE": str(trace)})
    try:
        user32 = drive.user32
        user32.PostMessageW(npp.hwnd, drive.WM_COMMAND, 44095, 0)  # IDM_VIEW_TAB1: first tab (main.k)
        time.sleep(1)
        npp.command("Check model")
        drive.wait_event(trace, "check_done")
        n = len(drive.events(trace))
        user32.PostMessageW(npp.hwnd, drive.WM_COMMAND, 44096, 0)  # IDM_VIEW_TAB2: other.txt
        time.sleep(1)
        user32.PostMessageW(npp.hwnd, drive.WM_COMMAND, 44095, 0)  # back to main.k
        time.sleep(1.5)
        repaints = [e for e in drive.events(trace)[n:] if e["event"] == "painted"]
        # main.k keeps the indicators painted for this result (they move with edits); it is not repainted
        assert [(Path(e["file"]).name, e["count"]) for e in repaints] == [("other.txt", 0)]
    finally:
        npp.close()


def test_an_edit_then_a_tab_switch_keeps_the_squiggles_on_their_fields(npp_exe, trace, tmp_path):
    folder = _copy("seeded_mistakes", tmp_path)
    other = tmp_path / "other.txt"
    other.write_text("notes\n")
    npp = drive.Npp(npp_exe, [folder / "main.k", other], {"DECKCHECK_NPP_TRACE": str(trace)})
    try:
        drive.user32.PostMessageW(npp.hwnd, drive.WM_COMMAND, 44095, 0)
        time.sleep(1)
        npp.command("Check model")
        drive.wait_event(trace, "painted")
        npp.sci(2025, 0)  # SCI_GOTOPOS 0
        npp.sci(SCI_NEWLINE)  # every finding is now one line lower in the buffer
        n = len(drive.events(trace))
        drive.user32.PostMessageW(npp.hwnd, drive.WM_COMMAND, 44096, 0)
        time.sleep(1)
        drive.user32.PostMessageW(npp.hwnd, drive.WM_COMMAND, 44095, 0)
        time.sleep(1.5)
        assert [Path(e["file"]).name for e in drive.events(trace)[n:] if e["event"] == "painted"] == ["other.txt"]
    finally:
        npp.kill()


def test_saving_a_note_beside_the_model_starts_no_check(npp_exe, trace, tmp_path):
    folder = _copy("seeded_mistakes", tmp_path)
    note = folder / "notes.txt"
    note.write_text("notes\n")
    npp = drive.Npp(npp_exe, [folder / "main.k", note], {"DECKCHECK_NPP_TRACE": str(trace)})
    try:
        drive.user32.PostMessageW(npp.hwnd, drive.WM_COMMAND, 44095, 0)
        time.sleep(1)
        npp.command("Check model")
        drive.wait_event(trace, "check_done")
        drive.user32.PostMessageW(npp.hwnd, drive.WM_COMMAND, 44096, 0)  # notes.txt
        time.sleep(1)
        n = len(drive.events(trace))
        npp.sci(SCI_NEWLINE)
        npp.save()
        time.sleep(3)
        assert [e["event"] for e in drive.events(trace)[n:] if e["event"].startswith("check")] == []
    finally:
        npp.kill()


def test_a_save_triggered_check_does_not_open_the_panel(npp_exe, trace, tmp_path):
    main = _copy("inc_nested_main", tmp_path) / "main.k"
    npp = drive.Npp(npp_exe, [main], {"DECKCHECK_NPP_TRACE": str(trace)})
    try:
        npp.sci(SCI_NEWLINE)
        npp.save()
        drive.wait_event(trace, "check_done")
        assert "panel_shown" not in [e["event"] for e in drive.events(trace)]
    finally:
        npp.close()


def test_disable_clears_squiggles_in_other_tabs_too(npp_exe, trace, tmp_path):
    a = _copy("seeded_mistakes", tmp_path) / "main.k"
    b = _seeded_variant(tmp_path, "second", "latin-1", False)
    npp = drive.Npp(npp_exe, [a, b], {"DECKCHECK_NPP_TRACE": str(trace)})
    try:
        for tab in (44095, 44096):  # check both, so both documents carry squiggles
            drive.user32.PostMessageW(npp.hwnd, drive.WM_COMMAND, tab, 0)
            time.sleep(1)
            n = len(drive.events(trace))
            npp.command("Check model")
            drive.wait_event(trace, "painted", after=n)
        drive.user32.PostMessageW(npp.hwnd, drive.WM_COMMAND, 44095, 0)  # back to a: painted from the second result
        time.sleep(1.5)
        npp.command("Disable")
        drive.wait_event(trace, "disabled")
        n = len(drive.events(trace))
        drive.user32.PostMessageW(npp.hwnd, drive.WM_COMMAND, 44096, 0)
        cleared = drive.wait_event(trace, "painted", after=n)
        assert Path(cleared["file"]).parent.name == "second" and cleared["count"] == 0
    finally:
        npp.close()


@pytest.mark.skipif(not YARIS.exists(), reason="Yaris corpus not present")
def test_a_new_check_replaces_the_old_and_closing_kills_the_engine(npp_exe, trace):
    engine_dir = npp_exe.parent / "plugins" / "deckcheck" / "engine"
    npp = drive.Npp(npp_exe, [YARIS], {"DECKCHECK_NPP_TRACE": str(trace)})
    try:
        npp.command("Check model")
        drive.wait_event(trace, "check_started")
        time.sleep(2)
        first = drive.engine_processes(engine_dir)
        assert len(first) == 1
        npp.command("Check model")
        time.sleep(3)
        second = drive.engine_processes(engine_dir)
        assert len(second) == 1 and second != first
    finally:
        npp.kill()  # a hard kill, as in a crash: the Job object must still take the engine down
    time.sleep(3)
    assert drive.engine_processes(engine_dir) == []
