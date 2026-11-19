from tools.freeze.measure_engine import privacy_hits


def test_privacy_hits_finds_utf8_and_utf16_needles(tmp_path):
    (tmp_path / "a.bin").write_bytes(b"xx C:\\Users\\someone\\proj xx")
    (tmp_path / "sub").mkdir()
    (tmp_path / "sub" / "b.bin").write_bytes("path=C:\\Users\\someone".encode("utf-16-le"))
    (tmp_path / "c.bin").write_bytes(b"nothing here")
    hits = privacy_hits(tmp_path, ["C:\\Users\\someone"])
    assert sorted(hits) == [("a.bin", "C:\\Users\\someone"), ("sub/b.bin", "C:\\Users\\someone")]


def test_privacy_hits_is_case_insensitive_and_skips_empty_needles(tmp_path):
    (tmp_path / "a.bin").write_bytes(b"c:\\users\\SOMEONE")
    assert privacy_hits(tmp_path, ["C:\\Users\\someone", ""]) == [("a.bin", "C:\\Users\\someone")]
