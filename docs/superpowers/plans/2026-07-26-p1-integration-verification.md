# P1 Integration Verification Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Prove the four P1 implementation batches work together, satisfy every approved invariant, and introduce no solution-level build or test regression.

**Architecture:** Run owning tests before solution-wide verification, audit the combined diff by invariant and trust boundary, and keep any integration-only correction in its own reviewed commit. This plan changes no production behavior unless combined verification exposes a concrete integration defect.

**Tech Stack:** Git, PowerShell, .NET 10 test/build tooling, Windows UAC, existing application

## Global Constraints

- Execute only after all four subsystem plans are complete.
- Do not weaken a focused regression test to make integration pass.
- Do not suppress existing warnings or refactor unrelated code.
- A solution-level failure must be traced to a specific subsystem before any correction.
- Any integration correction requires its own RED reproduction and focused GREEN verification.
- The final worktree must contain only approved P1 documentation, tests, and production changes.

---

## Inputs

- `docs/superpowers/plans/2026-07-26-p1-pack-reliability.md`
- `docs/superpowers/plans/2026-07-26-p1-editor-transform-correctness.md`
- `docs/superpowers/plans/2026-07-26-p1-updater-ipc-hardening.md`
- `docs/superpowers/plans/2026-07-26-p1-rendering-resource-performance.md`

### Task 1: Verify every owning project

**Files:**

- No file changes.

**Interfaces:**

- Consumes the final code and tests from all four subsystem plans.
- Produces recorded pass/fail totals for each owning test project.

- [ ] **Step 1: Run Pack and format projects**

```powershell
dotnet test Testing/Shared.Core.Test/Test.Shared.Core.csproj -c Release --no-restore
dotnet test Editors/AnimationMeta/Test.AnimationMeta/Test.AnimationMeta.csproj -c Release --no-restore
dotnet test Testing/FileTypesTests/FileTypesTests.csproj -c Release --no-restore
```

- [ ] **Step 2: Run editor and rendering projects**

```powershell
dotnet test Testing/AssetEditorTests/AssetEditorTests.csproj -c Release --no-restore
dotnet test Testing/GameWorld.Core.Test/Test.GameWorld.Core.csproj -c Release --no-restore
```

- [ ] **Step 3: Run updater and IPC projects**

```powershell
dotnet test Testing/AssetEditorUpdaterTests/AssetEditorUpdaterTests.csproj -c Release --no-restore
dotnet test Editors/Ipc/Test.Ipc/Test.Ipc.csproj -c Release --no-restore
```

- [ ] **Step 4: Stop on any failure**

For a failure, rerun only the failing fully qualified test with
`--verbosity normal`, preserve the complete assertion or stack trace, and
return to the owning subsystem task. Do not continue to the solution suite
until every owning project is GREEN.

### Task 2: Run solution-wide automated verification

**Files:**

- No planned file changes.

**Interfaces:**

- Produces a Release test result and Release build result for
  `AssetEditor.CN.sln`.

- [ ] **Step 1: Run the full test solution**

```powershell
dotnet test AssetEditor.CN.sln -c Release --no-restore --verbosity minimal
```

Record every test assembly's passed, failed, and skipped totals. Exit code must
be zero.

- [ ] **Step 2: Run the full Release build**

```powershell
dotnet build AssetEditor.CN.sln -c Release --no-restore --verbosity minimal
```

Exit code must be zero. Compare warnings in modified projects against the
pre-implementation baseline and investigate every newly introduced warning;
do not attempt to clear unrelated existing warnings.

- [ ] **Step 3: Check patch integrity**

```powershell
git diff master...HEAD --check
git status --short
git diff --stat master...HEAD
```

The status must contain no generated build output, temporary update
workspace, test fixture output, or untracked production artifacts.

### Task 3: Audit the combined diff against approved invariants

**Files:**

- No planned file changes.

**Interfaces:**

- Consumes `docs/superpowers/specs/2026-07-26-p1-reliability-performance-fixes-design.md`.
- Produces an evidence checklist for each invariant.

- [ ] **Step 1: Audit Pack mutation points**

```powershell
rg -n "GetFullPath|DataSource =|\\.Commit\\(|File\\.Move" Shared/SharedCore/PackFiles
```

Confirm:

- `GetFullPath` uses exact identity only.
- Serialization loops do not assign active sources.
- The only pending-source commit occurs after destination replacement.
- Failure tests assert reference identity, path, and saved-size preservation.

- [ ] **Step 2: Audit animation fallback boundaries**

```powershell
rg -n "ParsedUnknownMetadataAttribute|GetBytesFromBuffer|data\\.Buffer" Shared/GameFiles/AnimationMeta Shared/GameFiles/AnimationPack
```

Confirm unknown metadata returns raw `Data`, failed children use
`StartOffset/Size`, and no fallback stores the full parent buffer.

- [ ] **Step 3: Audit editor and transform lifecycle**

```powershell
rg -n "DestroyEditor|ToList\\(\\)|IRedoableCommand|BeginTransform|CommitTransform|CancelTransform|InvertWindingOrder" AssetEditor/Services GameWorld/View3D
```

Confirm cancellation returns before destruction, batch closing uses a
snapshot, transform commands begin from pre-state, and winding reversal is
object-only.

- [ ] **Step 4: Audit privilege and IPC boundaries**

```powershell
rg -n "LocalApplicationData|CommonApplicationData|DirectorySecurity|SHA256|Process\\.Start|CurrentUserOnly|CancelAfter|64 \\* 1024|FromMilliseconds\\(500\\)" AssetEditorUpdater Editors/Ipc
```

Confirm elevated paths remain protected, payload launch follows hash
verification, and IPC bounds match the approved exact values.

- [ ] **Step 5: Audit bounded rendering and timer work**

```powershell
rg -n "TrackModifiedVertex|RebuildVertexBufferPartial|50_000|50000|EdgeOverlayDataBuilder|Loaded|Unloaded|DispatcherTimer" GameWorld/View3D Editors/Shared
```

Confirm edit-mode paths do not fall back to full uploads, topology collection
stops at the cap, actual-sized edge data reaches the renderer, and the folding
timer stops on unload.

### Task 4: Run behavior and security integration checks

**Files:**

- No planned file changes.

**Interfaces:**

- Produces manual verification evidence for behavior that cannot be fully
  represented by unit tests.

- [ ] **Step 1: Verify Pack behavior**

Open or construct two directories containing the same basename. Edit and save
the later object and confirm only its full path changes. Trigger the tested
replacement failure and confirm the original Pack remains readable without
reloading.

- [ ] **Step 2: Verify 3D gestures**

For object, vertex, face, edge, and bone modes, exercise translate, rotate, and
scale through confirm, cancel, Undo, and Redo. Confirm edit-mode negative
scaling leaves unrelated triangle winding unchanged and object-mode negative
scaling reverses/restores correctly.

- [ ] **Step 3: Verify edge and timer lifecycle**

Open representative small and large meshes, inspect vertex/edge overlays, and
drag face/edge selections. Open and close text editor tabs repeatedly, force a
GC during a diagnostic run, and confirm unloaded views are not retained by an
enabled folding timer.

- [ ] **Step 4: Verify updater privilege modes**

Run one update from a user-writable portable installation and one from a
Program Files installation:

- Portable mode must avoid UAC and use LocalAppData.
- Protected mode must request UAC and use the protected CommonApplicationData
  transaction.
- A separate medium-integrity process must fail to modify the protected
  payload.
- The final application restart must run without administrator privileges.

- [ ] **Step 5: Verify IPC recovery**

Hold the pipe name with another server and observe bounded retry without CPU or
log flooding. Connect a silent client until timeout, then send a valid request
from another client and confirm recovery.

### Task 5: Final review and clean-state proof

- [ ] **Step 1: Request independent code review**

Dispatch reviewers by domain:

- Pack and animation data integrity.
- Editor and 3D command correctness.
- Updater and IPC security.
- Rendering and resource performance.

Reviewers must report file-and-line findings ordered by severity and must not
edit files.

- [ ] **Step 2: Resolve actionable findings through RED/GREEN**

For every P0 or P1 review finding, reproduce it in a focused test before
editing. P2 findings outside the approved scope are recorded but not folded
into this branch.

- [ ] **Step 3: Re-run final commands**

```powershell
dotnet test AssetEditor.CN.sln -c Release --no-restore --verbosity minimal
dotnet build AssetEditor.CN.sln -c Release --no-restore --verbosity minimal
git diff master...HEAD --check
git status --short --branch
```

- [ ] **Step 4: Record final evidence**

Report:

- Branch and commit list.
- Test totals, failures, and skips.
- Release build errors and warnings.
- Automated evidence for all seven P1 categories.
- Manual checks completed and any environment-limited checks not completed.
- Confirmation that the worktree is clean and nothing was pushed or merged
  without explicit authorization.
