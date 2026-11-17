# deckcheck for Notepad++: design

**Status:** approved in brainstorm 2026-11-17 (Chintan); this document is for written review.
**Builds on:** the deckcheck engine and CLI (M1), and the lessons of the VS Code extension (M2: do no harm, big-model
checks on save or on request, "Not checked" coverage, unsaved-edit messaging).

## 1. Why, and what success means

Engineers who edit keyword decks by hand mostly use Notepad++ or another plain text editor, not VS Code. The VS Code
extension reaches very few of the people deckcheck is for. This milestone puts deckcheck where they already work.

The target user often has a locked-down Windows machine with no Python and no rights to install packages. So the
whole setup must be one install from Notepad++'s Plugins Admin, with nothing else to install.

**Success:** after release, Notepad++ plugin installs and requests from engineers, compared with the VS Code
extension's real installs. That comparison is the evidence for or against the premise above.

**Bracketing:**
- **Ceiling:** the VS Code checking experience in Notepad++: squiggles, a findings list, jump to the field, the whole
  model across its includes.
- **Target:** that experience from a single Plugins Admin install, on a machine without Python.
- **Floor (the dumbest baseline):** the CLI run from NppExec with click-to-jump on `file:line:column`. It works
  today for power users and is documented as a footnote (section 7), not built as a product.

## 2. Architecture

The checking engine stays in Python. Its checks carry 1,356 tests and every error-or-warning decision was refereed
against LS-DYNA; a second implementation would duplicate that and drift. The plugin is a thin client:

```
Notepad++ ── deckcheck.dll (C#, .NET Framework 4.8) ──subprocess──> engine\deckcheck.exe --for <file> --format json
                 menu, panel, squiggles, settings                      frozen deckcheck: checks, include tree
```

- **Plugin language: C# on .NET Framework 4.8.** It ships with Windows 10 and 11, WinForms makes the docked panel and
  dialogs cheap, and JSON, processes and threads are built in. C# plugins already live in Plugins Admin (JSON Tools,
  CSV Lint). C++ was rejected for the Win32 UI cost; a native rewrite of the checks was rejected above.
- **Engine: deckcheck frozen to a standalone executable** (PyInstaller or Nuitka, decided in N0) in the plugin's
  `engine\` folder. One process per check; no long-lived server in v1.
- **Source:** `editors/notepadpp/` in the deckcheck repo, beside `editors/vscode/`.

## 3. Engine changes

- **`deckcheck --for <file>`:** check the model, or models, the file belongs to. It uses the main-deck detection the
  VS Code server already has (`lsp/workspace.py`: scan the file's folder for decks that reach it through `*INCLUDE`),
  moved or exposed so both front ends share one implementation. If the file is itself a main deck, behave as
  `deckcheck <file>`. If no deck reaches it, check the file alone and say so in the output.
- **`--main-deck <deck>` (repeatable)** for the plugin's per-folder override (section 4).
- **JSON additions only as N2 and N3 need them**, keeping `"version": 1` compatible or bumping it deliberately. The
  plugin refuses an engine whose version or schema it does not know.
- **Absolute paths in JSON** if N0 shows relative paths are ambiguous for the plugin.

## 4. What the engineer gets

- **Menu:** Plugins > deckcheck > *Check model* (with a shortcut), *Show findings*, *Main decks...*, *Disable*.
- **Which model:** the one the current file belongs to (section 3). *Main decks...* sets main decks per folder,
  stored in the plugin's configuration folder. A file no deck reaches is checked alone, and the panel says so.
- **When:** on *Check model* and on save. No checking while typing. A new save or request cancels a running check.
- **Squiggles:** errors red, warnings amber, as Scintilla indicators on the field's exact range; hovering shows the
  message in a calltip. Indicators move with edits; the panel marks results "edited since check" until the next one.
- **Findings panel (docked):** severity, code, file:line, message; a click opens the right include and selects the
  field. It shows progress while a check runs, then a summary such as "main.k: 18 files, 2 errors, 3 warnings,
  12.4 s", then the "Not checked" list.
- **Unsaved edits:** checks read files from disk. If any file of the model has unsaved changes, the panel says
  "unsaved changes in <file> are not checked".

**Not in v1:** hover field names, go to definition, find references, outline. They need a long-lived engine and come
after v1 only if engineers ask for them.

## 5. Do no harm

The extension rule from M2 applies unchanged: deckcheck must never make Notepad++ slower or less reliable. Engineers
must keep it because it helps, not tolerate it.

- The UI thread never waits on the engine. The engine runs as a below-normal-priority (background mode) process;
  results return on a worker thread and are marshalled to the UI.
- Indicators are painted only in open documents and capped per file, so a deck with tens of thousands of findings
  cannot freeze scrolling. The panel lists all findings.
- The engine process tree is killed on a new check, on Notepad++ exit and on *Disable*.
- Any failure (engine missing, quarantined, crashed, wrong version) is reported once in the panel and then deckcheck
  steps aside; the file opens and edits normally.
- **Measured gate (N3):** opening, typing in and scrolling the 169 MB Yaris include while the full Yaris model is
  being checked stays within 10 % or 20 ms (paired median) of Notepad++ without the plugin, driven by window
  messages. This is the M2 gate rule.

## 6. Column units

deckcheck reports Latin-1 byte columns. Scintilla positions are bytes in the document's encoding. When Notepad++
opens a deck as UTF-8 and a line contains a non-ASCII character (a `°` or `µ` in a comment), positions after it
differ. The plugin converts each finding's columns using the line's text in the document's actual encoding. A test
deck with non-ASCII comments before flagged fields, opened as UTF-8 and as ANSI, pins it.

## 7. Packaging and distribution

- One zip per architecture: `deckcheck.dll` and `engine\`. **x64 first**; ARM64 when the engine and .NET build for
  it are verified; no 32-bit.
- Plugin and engine always ship together from the same deckcheck tag; the engine also stays on PyPI.
- Distribution: a release asset on the public GitHub repo, then a pull request to `notepad-plus-plus/nppPluginList`.
  Each public step needs Chintan's explicit yes.
- README footnote: running the CLI from NppExec with an output filter for click-to-jump, for users who prefer it.

## 8. Risks, checked first (N0)

1. **Antivirus.** Frozen Python executables are a known source of false positives. A quarantined engine makes the
   plugin useless on exactly the machines it is for. N0 builds the engine (PyInstaller one-folder, and Nuitka if
   needed), scans it with Windows Defender and VirusTotal, and reports. Code signing costs money and is Chintan's
   decision if needed.
2. **Plugins Admin rules.** N0 confirms that a 15-30 MB zip with a subfolder is accepted, the listing requirements,
   and the licence of the C# plugin template.
3. **Start time.** A frozen engine starts slower than an imported module. N0 measures start time and the Yaris check
   time against the pip-installed CLI.

## 9. Stages

Each stage gets its own plan and ends at a gate; Chintan decides at each gate whether to continue.

| Stage | Delivers | Gate |
|---|---|---|
| N0 spike | Frozen engine; size, start and Yaris times; Defender and VirusTotal results; Plugins Admin and licence rules | Antivirus verdict acceptable, else stop and decide |
| N1 engine | `--for`, `--main-deck`, JSON additions; released as 0.1.x after Chintan's yes | Suite and corpus green |
| N2 plugin core | Menu, background run, findings panel, jump, *Main decks...*, *Disable* | End-to-end tests on a portable Notepad++ |
| N3 squiggles | Indicators, calltips, column conversion, painting caps | Do-no-harm gate (section 5) |
| N4 release | Owner trial from written instructions, GitHub release, Plugins Admin pull request | Chintan's yes at each public step |

## 10. Testing

- **Engine:** `--for` and `--main-deck` test-first in Python, plus the existing corpus gate.
- **Plugin unit tests:** JSON parsing and version refusal, column conversion, cancellation, painting caps.
- **End to end:** a portable Notepad++ in a throwaway folder with the built plugin, driven by window messages: menu,
  a check run, panel rows, a jump into an include, squiggle positions, *Disable*, a missing engine.
- **Harm gate:** section 5, on the Yaris model.
- **Owner trial:** Chintan installs and uses it from written, dry-run-verified instructions before any release.

## 11. Out of scope

- Syntax highlighting (community user-defined languages for keyword decks exist).
- Checking while typing; navigation features (section 4).
- Other editors (UltraEdit, Vim, Emacs); Notepad++ under Wine.
- Code signing, unless N0 shows it is needed and Chintan approves the cost.
