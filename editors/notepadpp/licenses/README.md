# Licences of libraries bundled in the frozen engine

`tools/npp/package.py` appends these files, verbatim, to `THIRD-PARTY-LICENSES.txt` in the release zip. The versions
are those in the engine built on 2026-11-20 (Python 3.12.9 for Windows). Update them when the build's Python changes.

| File | Library in `engine\_internal` | Source |
|---|---|---|
| `openssl-3.0.15.txt` | `libcrypto-3.dll`, OpenSSL 3.0.15 | https://raw.githubusercontent.com/openssl/openssl/openssl-3.0.15/LICENSE.txt |
| `mpdecimal-2.5.1.txt` | `_decimal.pyd`, libmpdec 2.5.1 | https://raw.githubusercontent.com/python/cpython-source-deps/mpdecimal-2.5.1/LICENSE.txt |
| `xz-5.2.5.txt` | `_lzma.pyd`, liblzma (XZ Utils 5.2.5) | https://raw.githubusercontent.com/tukaani-project/xz/v5.2.5/COPYING |
