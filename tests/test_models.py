from pathlib import Path

from deckcheck.models import models_for_file


def _deck(path: Path, *includes: str) -> Path:
    path.parent.mkdir(parents=True, exist_ok=True)
    body = "".join(f"*INCLUDE\n{i}\n" for i in includes)
    path.write_text(f"*KEYWORD\n{body}*END\n", encoding="latin-1")
    return path


def test_a_main_deck_is_its_own_model(tmp_path):
    main = _deck(tmp_path / "main.k", "mesh/part.k")
    _deck(tmp_path / "mesh" / "part.k")
    assert models_for_file(main) == ([main], False)


def test_file_in_the_mains_folder_is_reached(tmp_path):
    main = _deck(tmp_path / "main.k", "part.k")
    part = _deck(tmp_path / "part.k")
    assert models_for_file(part) == ([main], False)


def test_two_mains_reach_one_include(tmp_path):
    a = _deck(tmp_path / "a.k", "shared.k")
    b = _deck(tmp_path / "b.k", "shared.k")
    shared = _deck(tmp_path / "shared.k")
    assert models_for_file(shared) == ([a, b], False)


def test_unreached_non_deck_file_is_alone(tmp_path):
    _deck(tmp_path / "main.k")
    lone = tmp_path / "notes.inc"
    lone.write_text("*KEYWORD\n*END\n", encoding="latin-1")
    assert models_for_file(lone) == ([lone], True)


def test_main_decks_override(tmp_path):
    a = _deck(tmp_path / "a.k", "part.k")
    _deck(tmp_path / "b.k", "part.k")
    part = _deck(tmp_path / "part.k")
    assert models_for_file(part, [a]) == ([a], False)


def test_include_in_a_subfolder_finds_its_main_above(tmp_path):
    main = _deck(tmp_path / "main.k", "mesh/part.k")
    part = _deck(tmp_path / "mesh" / "part.k")
    assert models_for_file(part) == ([main], False)


def test_a_deck_that_a_main_above_includes_is_not_its_own_model(tmp_path):
    main = _deck(tmp_path / "main.k", "sub/a.k")
    a = _deck(tmp_path / "sub" / "a.k", "b.k")
    _deck(tmp_path / "b.k")
    assert models_for_file(a) == ([main], False)


def test_search_goes_up_two_folders_at_most(tmp_path):
    _deck(tmp_path / "main.k", "a/b/c/part.k")
    part = _deck(tmp_path / "a" / "b" / "c" / "part.k")
    assert models_for_file(part) == ([part], False)  # three levels up: not searched; a deck is its own model


def test_folders_below_the_file_are_not_scanned(tmp_path, monkeypatch):
    part = _deck(tmp_path / "part.k")
    _deck(tmp_path / "deep" / "deeper" / "main.k", "../../part.k")
    assert models_for_file(part) == ([part], False)


def test_main_deck_that_does_not_reach_the_file(tmp_path):
    a = _deck(tmp_path / "a.k")
    part = _deck(tmp_path / "part.k")
    assert models_for_file(part, [a]) == ([part], True)
