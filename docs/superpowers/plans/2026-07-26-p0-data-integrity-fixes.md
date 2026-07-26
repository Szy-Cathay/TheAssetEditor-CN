# P0 Data Integrity Fixes Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Eliminate the three confirmed P0 data-loss and data-corruption paths in AnimPack editing, Save/Save As handling, and UTF-8 length-prefixed serialization.

**Architecture:** Keep each fix inside its current ownership boundary. View models remain responsible for dirty-state transitions, `FileSaveService` remains responsible for dialog result semantics, and format parsers remain responsible for byte-accurate prefixes. Add focused regression tests before each production change and avoid unrelated refactoring.

**Tech Stack:** C#, .NET, WPF, ReactiveUI, NUnit/MSTest, existing test utilities

## Global Constraints

- Preserve the existing AnimPack binary and XML formats.
- Preserve the existing UTF-16 CA string convention of storing UTF-16 code-unit counts.
- Treat save dialog cancellation and serialization failure as unsuccessful saves.
- Do not clear dirty state until the corresponding save operation returns a non-null file.
- Keep changes scoped to the three approved P0 findings.
- Observe every new regression test failing for the expected reason before changing production code.

---

## Task 1: Make AnimPack child edits part of the save lifecycle

**Files:**

- Modify: `Editors/AnimationFragmentEditor/Editor.AnimationFragmentEditor/AnimationPack/ViewModels/AnimSetTableEditorViewModel.cs`
- Modify: `Editors/AnimationFragmentEditor/Editor.AnimationFragmentEditor/AnimationPack/AnimPackViewModel.cs`
- Add: `Testing/AssetEditorTests/AnimSetTableEditorViewModelTests.cs`
- Add: `Testing/AssetEditorTests/AnimPackViewModelTests.cs`
- Modify only if required for project references: `Testing/AssetEditorTests/AssetEditorTests.csproj`

### Step 1: Add failing table dirty-state tests

- [ ] Add tests showing a freshly loaded table is clean.
- [ ] Add tests showing direct header edits set `IsDirty`.
- [ ] Add tests showing direct row property edits set `IsDirty`.
- [ ] Add tests showing row collection changes set `IsDirty`.
- [ ] Run:

```powershell
dotnet test Testing/AssetEditorTests/AssetEditorTests.csproj -c Release --filter "FullyQualifiedName~AnimSetTableEditorViewModelTests"
```

- [ ] Confirm RED because direct header/row/collection edits are not all tracked.

### Step 2: Implement precise table dirty tracking

- [ ] Subscribe to `Rows.CollectionChanged` in the table view model.
- [ ] Subscribe and unsubscribe row `PropertyChanged` handlers when rows enter or leave the collection.
- [ ] Mark header and mode property changes dirty through the existing notification setters.
- [ ] Suppress dirty notifications while `LoadFromBinary` rebuilds the model.
- [ ] Finish every successful or failed load cleanup path with notification suppression disabled and the loaded model clean.
- [ ] Re-run the targeted table tests and confirm GREEN.

### Step 3: Add failing AnimPack lifecycle tests

- [ ] Add a test showing pending table edits are considered by the selection-change guard.
- [ ] Add a test showing the outer `Save()` commits the active table before serializing the parent.
- [ ] Add a test showing failed child conversion aborts the parent save and preserves dirty state.
- [ ] Add a test showing successful child commit clears the child dirty flag and leaves the parent dirty until parent persistence succeeds.
- [ ] Run:

```powershell
dotnet test Testing/AssetEditorTests/AssetEditorTests.csproj -c Release --filter "FullyQualifiedName~AnimPackViewModelTests"
```

- [ ] Confirm RED because the current outer save only considers XML editor state.

### Step 4: Implement the unified pending-change lifecycle

- [ ] Add one pending-active-child check that selects table `IsDirty` or XML `HasUnsavedChanges()` according to the active mode.
- [ ] Use it in entry switching, editor-level unsaved-state reporting, and outer save.
- [ ] Make outer `Save()` call `SaveActiveFile()` before parent serialization when the active child is pending.
- [ ] Abort parent serialization when the child commit fails.
- [ ] Clear the committed child dirty marker only after successful conversion.
- [ ] Treat a null parent save result as failure and keep the parent dirty.
- [ ] Re-run both AnimPack test classes and confirm GREEN.

### Step 5: Commit the isolated AnimPack fix

```powershell
git add Editors/AnimationFragmentEditor/Editor.AnimationFragmentEditor/AnimationPack Testing/AssetEditorTests
git commit -m "fix: preserve pending AnimPack edits"
```

---

## Task 2: Correct Save As results and animation dirty-state transitions

**Files:**

- Modify: `Shared/SharedCore/Services/FileSaveService.cs`
- Modify: `Testing/Shared.Core.Test/Services/FileSaveServiceTests.cs`
- Modify: `Editors/AnimationEditor/AnimationKeyframeEditor/AnimationKeyframeEditorViewModel.cs`
- Add or modify: a focused animation keyframe view-model test under an existing editor test project

### Step 1: Add failing FileSaveService tests

- [ ] Add a confirmed-new-path test that expects a returned file and written data.
- [ ] Add a confirmed-existing-path test that expects overwrite and a returned file.
- [ ] Add a cancellation test that expects null and no pack mutation.
- [ ] Run:

```powershell
dotnet test Testing/Shared.Core.Test/Test.Shared.Core.csproj -c Release --filter "FullyQualifiedName~FileSaveServiceTests"
```

- [ ] Confirm RED because confirmed Save As currently returns null.

### Step 2: Fix `FileSaveService.SaveAs`

- [ ] Return null only when the dialog is cancelled or the selected path is invalid.
- [ ] Preserve the current create-or-overwrite behavior for confirmed paths.
- [ ] Re-run the targeted service tests and confirm GREEN.

### Step 3: Add failing animation save-state tests

- [ ] Add a test showing cancelled ordinary Save keeps `IsDirty` true.
- [ ] Add a test showing cancelled Save As keeps `IsDirty` true.
- [ ] Add a test showing a successful save clears `IsDirty`.
- [ ] Run the owning test project with a class-name filter.
- [ ] Confirm RED because both animation callers currently clear dirty state unconditionally.

### Step 4: Fix animation callers

- [ ] Capture the result of `IFileSaveService.Save()` and `SaveAs()`.
- [ ] Clear `IsDirty` only when the result is non-null.
- [ ] Re-run the animation and service tests and confirm GREEN.

### Step 5: Commit the isolated save fix

```powershell
git add Shared/SharedCore/Services/FileSaveService.cs Testing/Shared.Core.Test/Services/FileSaveServiceTests.cs Editors/AnimationEditor
git commit -m "fix: retain dirty state when save is cancelled"
```

---

## Task 3: Write byte-accurate UTF-8 string lengths

**Files:**

- Modify: `Shared/ByteParsing/Shared.ByteParsing/Parsers/StringParser.cs`
- Modify: `Shared/ByteParsing/Shared.ByteParsingTest/Parsers/StringParserTest.cs`
- Modify: `Shared/GameFiles/Dat/DatFileParser.cs`
- Add: `Testing/FileTypesTests/DatFileParserTests.cs`

### Step 1: Add failing CA string parser tests

- [ ] Add UTF-8 round-trip and prefix assertions for Chinese text.
- [ ] Add UTF-8 round-trip and prefix assertions for an emoji.
- [ ] Add the same byte-count assertion for optional UTF-8 strings.
- [ ] Add a UTF-16 assertion proving its prefix remains the UTF-16 code-unit count.
- [ ] Run:

```powershell
dotnet test Shared/ByteParsing/Shared.ByteParsingTest/Shared.ByteParsingTest.csproj -c Release --filter "FullyQualifiedName~StringParserTest"
```

- [ ] Confirm RED because UTF-8 prefixes currently use `string.Length`.

### Step 2: Fix CA string prefix calculation

- [ ] Encode the value before writing its prefix.
- [ ] Use encoded byte length for UTF-8 variants.
- [ ] Use `string.Length` for UTF-16 variants to preserve reader compatibility.
- [ ] Re-run the targeted parser tests and confirm GREEN.

### Step 3: Add failing DAT serialization tests

- [ ] Create a `SoundDatFile` containing a non-ASCII event string.
- [ ] Assert `WriteData()` stores `Encoding.UTF8.GetByteCount(value)` in the first string prefix.
- [ ] Read the produced string through `DatFileParser.ReadStr32` and assert an exact round trip.
- [ ] Run:

```powershell
dotnet test Testing/FileTypesTests/FileTypesTests.csproj -c Release --filter "FullyQualifiedName~DatFileParserTests"
```

- [ ] Confirm RED because `WriteStr32()` currently stores `string.Length`.

### Step 4: Fix DAT prefix calculation

- [ ] Encode the string once.
- [ ] Store the encoded byte array length.
- [ ] Append the same encoded bytes.
- [ ] Re-run DAT and CA parser tests and confirm GREEN.

### Step 5: Commit the isolated serialization fix

```powershell
git add Shared/ByteParsing Shared/GameFiles/Dat/DatFileParser.cs Testing/FileTypesTests/DatFileParserTests.cs
git commit -m "fix: write UTF-8 byte lengths"
```

---

## Task 4: Cross-cutting verification and review

### Step 1: Run all targeted regression tests together

```powershell
dotnet test Testing/AssetEditorTests/AssetEditorTests.csproj -c Release --filter "FullyQualifiedName~AnimPack"
dotnet test Testing/Shared.Core.Test/Test.Shared.Core.csproj -c Release --filter "FullyQualifiedName~FileSaveServiceTests"
dotnet test Shared/ByteParsing/Shared.ByteParsingTest/Shared.ByteParsingTest.csproj -c Release --filter "FullyQualifiedName~StringParserTest"
dotnet test Testing/FileTypesTests/FileTypesTests.csproj -c Release --filter "FullyQualifiedName~DatFileParserTests"
```

- [ ] Confirm every targeted regression test passes.

### Step 2: Run repository-wide verification

```powershell
dotnet test AssetEditor.CN.sln -c Release --no-restore
dotnet build AssetEditor.CN.sln -c Release --no-restore
```

- [ ] Record test totals, failures, skips, and build warnings.
- [ ] If the full suite exposes an unrelated pre-existing failure, prove the three targeted suites still pass and report the blocker precisely.

### Step 3: Review the final diff

```powershell
git diff --check
git status --short
git diff --stat
```

- [ ] Verify no unrelated files changed.
- [ ] Verify all dirty-state resets are gated by successful persistence.
- [ ] Verify UTF-8 writers use byte counts and UTF-16 behavior is unchanged.
- [ ] Verify loading an AnimPack table remains clean.

