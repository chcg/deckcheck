# Changelog

## 0.2.0

**Notepad++ plugin** (`editors/notepadpp/`, one zip with its own engine; nothing else to install, no Python needed):
- *Check model* (Alt+Shift+K) and on save: checks the model the current file belongs to, across its includes.
- Red and amber squiggles on the exact field, the message on hover, and a docked findings panel whose rows jump to
  the field, opening the include if needed.
- *Main decks...* per folder; *Disable*. Checks run in the background at low priority and never make Notepad++ wait;
  a new check or closing Notepad++ ends the running one.

**Command line** (no existing option or JSON field changed):
- `--for FILE`: check the model, or models, a file belongs to: the decks in its folder, or up to two folders above
  it, that include it. A file no deck includes is checked alone.
- `--main-deck DECK` (with `--for`, repeatable): name the main decks instead.
- `--background`: on Windows, run at background priority (low CPU, disk and memory priority).
- JSON output gains `models`: each checked main deck, how many files it reached, which ones (`reached`), and whether
  the file was checked alone.

## 0.1.0 (first release)

The command line and the VS Code extension's language server.

**Checks** (each error-or-warning decision made by running LS-DYNA on a test deck for that pattern):
- DC001: references to ids that are never defined; required references left blank or 0 (`*PART` mid and secid,
  `*ELEMENT_SHELL` pid).
- DC002: the same id defined twice (a warning where LS-DYNA accepts it, such as a node repeated at the same place).
- DC003: values of the wrong type for their field, tabs in cards, cards past the end of a keyword, definitions with
  a blank or 0 id.
- DC004: include files that can't be found, and includes that include themselves.
- DC005: materials, sections and hourglass controls nothing refers to.
- DC006: references LS-DYNA accepts without checking, to ids that don't exist.
- A "Not checked" list wherever deckcheck can't see everything a check needs, instead of a guess.

**Command line:** `deckcheck main.k` follows every `*INCLUDE` and `*INCLUDE_PATH`, prints `file:line:column` findings
or JSON (`--format json`), and exits with 1 on errors.

**Language server** (`deckcheck[lsp]`, used by the VS Code extension): whole-model checks, live on small models and
on save or request on big ones; hover, go to definition, find references, outline, field hints and a field ruler.
