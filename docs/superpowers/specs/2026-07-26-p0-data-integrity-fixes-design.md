# P0 Data Integrity Fixes

## Context

The current CN release passes its build and test suite, but three uncovered
editor paths can still discard or corrupt user data:

1. AnimPack table edits are not committed by the outer save command and are
   not considered when changing the selected entry.
2. File Save As treats confirmation as cancellation, while animation callers
   clear their dirty state even when no file was saved.
3. UTF-8 string writers store character counts where the formats require byte
   counts.

## Goals

- Never report an animation edit as clean unless its save returned a file.
- Commit the active AnimPack child editor before serializing the parent pack.
- Warn before discarding pending table or XML edits when changing entries.
- Track direct table header and row edits as dirty without marking freshly
  loaded data dirty.
- Write byte-accurate UTF-8 length prefixes for CA strings and DAT strings.
- Preserve the existing UTF-16 CA string length convention.
- Add regression tests that fail on the current implementation.

## Non-goals

- Refactoring unrelated save paths.
- Changing AnimPack binary formats or editor layout.
- Addressing the lower-priority unknown-format round-trip findings.
- Optimizing AnimPack loading or 3D rendering.

## Design

### AnimPack edit lifecycle

`AnimSetTableEditorViewModel` will mark itself dirty when a user changes a
header value, a row value, or the row collection. Loading will suppress those
notifications and finish with a clean state.

`AnimPackViewModel` will use one pending-change check for both views:

- XML mode uses `SelectedItemViewModel.HasUnsavedChanges()`.
- Table mode uses `TableEditorVM.IsDirty`.

The outer `Save()` method will commit the active child through
`SaveActiveFile()` before serializing the parent AnimPack. If child conversion
fails, parent saving stops and dirty state remains set. A successful child
commit clears that child editor's dirty marker and marks the parent AnimPack
dirty until the parent save succeeds.

Changing the selected entry will apply the existing discard confirmation to
either XML or table changes. Rejecting the confirmation keeps the current
entry selected. Accepting it preserves the current explicit discard behavior.

The editor-level `HasUnsavedChanges` value will include pending active child
changes so closing the editor cannot bypass the prompt.

### Save and Save As result handling

`FileSaveService.SaveAs()` will return `null` only for cancellation or an
invalid selected path. Confirmation will create or overwrite the selected
PackFile and return it.

Animation save callers will clear `IsDirty` only when `Save()` or `SaveAs()`
returns a non-null `PackFile`. Cancellation and failure leave the editor dirty.

### UTF-8 length prefixes

`StringParser` will encode the value before building its prefix. UTF-8
variants will store the encoded byte count. UTF-16 variants will continue to
store the number of UTF-16 code units because their reader multiplies the
stored count by two.

`DatFileParser.WriteStr32()` will store the UTF-8 byte array length rather than
`string.Length`.

## Error handling

- AnimPack child serialization errors abort parent saving.
- Save dialog cancellation produces no file mutation and no dirty-state reset.
- Existing parser error reporting remains unchanged.

## Test strategy

Tests will be added before production changes and observed failing for the
expected reasons.

- File save tests cover Save As confirmation, cancellation, new files, and
  overwriting existing files.
- Animation save tests verify cancellation keeps dirty state.
- AnimPack tests verify direct table edits become dirty, fresh loads remain
  clean, selection checks table changes, and outer save commits the active
  table before writing the parent pack.
- String parser tests cover Chinese, emoji, optional UTF-8 strings, and the
  unchanged UTF-16 length convention.
- DAT tests perform non-ASCII round trips and verify the stored byte count.

After targeted tests pass, run the complete `AssetEditor.CN.sln` test suite,
the Release build, and a clean worktree check.
