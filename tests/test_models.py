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


def test_main_deck_that_does_not_reach_the_file(tmp_path):
    a = _deck(tmp_path / "a.k")
    part = _deck(tmp_path / "part.k")
    assert models_for_file(part, [a]) == ([part], True)
