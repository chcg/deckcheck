from tools.freeze.measure_engine import privacy_hits


def test_privacy_hits_finds_utf8_and_utf16_needles(tmp_path):
    (tmp_path / "a.bin").write_bytes(b"xx C:\\Users\\someone\\proj xx")
    (tmp_path / "sub").mkdir()
    (tmp_path / "sub" / "b.bin").write_bytes("path=C:\\Users\\someone".encode("utf-16-le"))
    (tmp_path / "c.bin").write_bytes(b"nothing here")
    hits = privacy_hits(tmp_path, ["C:\\Users\\someone"])
    assert sorted(hits) == [("a.bin", "C:\\Users\\someone"), ("sub/b.bin", "C:\\Users\\someone")]


def test_needles_cover_forward_slash_and_url_forms():
    from tools.freeze.measure_engine import needle_forms
    forms = needle_forms("C:\\work\\deckcheck")
    assert {"C:\\work\\deckcheck", "C:/work/deckcheck", "file:///C:/work/deckcheck"} <= set(forms)


def test_privacy_hits_finds_a_direct_url_record(tmp_path):
    from tools.freeze.measure_engine import needle_forms
    (tmp_path / "direct_url.json").write_text('{"url": "file:///C:/work/deckcheck"}')
    assert privacy_hits(tmp_path, needle_forms("C:\\work\\deckcheck"))


def test_privacy_hits_is_case_insensitive_and_skips_empty_needles(tmp_path):
    (tmp_path / "a.bin").write_bytes(b"c:\\users\\SOMEONE")
    assert privacy_hits(tmp_path, ["C:\\Users\\someone", ""]) == [("a.bin", "C:\\Users\\someone")]
