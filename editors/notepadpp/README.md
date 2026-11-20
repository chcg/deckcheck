# deckcheck for Notepad++

Checks LS-DYNA keyword decks for structural mistakes, inside Notepad++: dangling references, duplicate ids, bad
values, include problems. It checks the whole model a file belongs to, across its includes. Nothing else to install:
the checking engine is bundled.

## Use

- **Plugins > deckcheck > Check model** (Alt+Shift+K) checks the model the current file belongs to. Saving a deck
  (`.k`, `.key`, `.dyn`, `.inc`) checks it again.
- Findings appear as red (error) and amber (warning) squiggles on the field; hover for the message. The **findings
  panel** lists them all; double-click a row to jump to the field, opening the include if needed.
- **Which model:** the decks in the file's folder that include it. If none does, the file is checked alone and the
  panel says so. **Main decks...** names the main decks for a folder when they live elsewhere or the scan picks
  the wrong one.
- Checks read the files on disk. If a file of the model has unsaved changes, the panel says they are not checked.
- **Disable** stops all checking and clears the squiggles until you turn it back on.

deckcheck never makes Notepad++ wait: the engine runs in the background at low priority, a new check cancels the
running one, and closing Notepad++ ends it. Large models (a full vehicle) take about a minute.

## Install

Plugins Admin (once listed), or by hand: close Notepad++, create the folder
`C:\Program Files\Notepad++\plugins\deckcheck`, unzip `deckcheck-npp-x64.zip` into it (so that
`plugins\deckcheck\deckcheck.dll` exists), and start Notepad++.
Windows 10 or 11 x64; the .NET Framework 4.8 it needs is part of Windows.

## Without the plugin

The same engine runs from a terminal (`pip install deckcheck`, then `deckcheck main.k`). Power users can run it from
NppExec with an output filter on `%FILE%:%LINE%:%CHAR%` for click-to-jump.

## Build (developers)

`powershell -ExecutionPolicy Bypass -File editors\notepadpp\build.ps1 [-Version 0.2.0] [-Test]`

It uses only tools that ship with Windows: the .NET Framework's `csc.exe` (C# 5) and `ilasm.exe`. `src/Exports.il`
holds the six native entry points Notepad++ calls and forwards them to the C# code in `DeckcheckNpp.dll`.
End-to-end tests: `DECKCHECK_NPP_E2E=1 pytest tests/test_npp_e2e.py` (opens a portable copy of the installed
Notepad++).
