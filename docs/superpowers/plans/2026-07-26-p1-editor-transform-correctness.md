# P1 Editor and Transform Correctness Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make editor closure cancel-safe and give live 3D transformations correct Commit, Cancel, Undo, and Redo behavior without corrupting edit-mode topology.

**Architecture:** Centralize editor destruction, add an optional redo contract for commands whose first execution is a live preview, and restore a single pre-gesture transform command lifecycle. Reuse matrix replay for vertices and cloned frames for bones instead of storing complete mesh snapshots.

**Tech Stack:** C#, .NET 10, WPF, MonoGame/XNA math, NUnit, Moq

## Global Constraints

- Preserve editor command signatures and visible confirmation behavior.
- A cancelled close must not remove, close, or destroy an editor.
- Existing commands that replay correctly through `Execute` must remain unchanged.
- Edit-mode negative scaling must never modify the mesh index buffer.
- Do not store complete mesh snapshots in the undo stack.
- Add and observe a focused RED test before each production change.
- This plan executes before the rendering performance plan because both touch `TransformGizmoWrapper`.

---

## File Structure

- Modify `AssetEditor/Services/EditorManager.cs`: prompt seam, cancel return, snapshot iteration, single destruction path.
- Create `AssetEditor/Assembly.cs`: expose internal test seams only to `AssetEditorTests`.
- Create `Testing/AssetEditorTests/EditorManagerTests.cs`: close lifecycle coverage.
- Modify `GameWorld/View3D/Commands/ICommand.cs`: optional `IRedoableCommand`.
- Modify `GameWorld/View3D/Services/CommandExecutor.cs`: redo dispatch.
- Create `Testing/GameWorld.Core.Test/Commands/CommandExecutorTests.cs`: default and explicit redo behavior.
- Modify `GameWorld/View3D/Commands/Vertex/TransformVertexCommand.cs`: symmetric forward replay and shared winding helper.
- Modify `GameWorld/View3D/Commands/Bone/TransformBoneCommand.cs`: initial/final frame lifecycle.
- Modify `GameWorld/View3D/Components/Gizmo/TransformGizmoWrapper.cs`: Begin/Commit/Cancel ownership and object-only winding.
- Modify `GameWorld/View3D/Components/Gizmo/GizmoComponent.cs`: call the explicit gesture lifecycle.
- Create `Testing/GameWorld.Core.Test/Commands/TransformVertexCommandTests.cs`: selection-mode and winding behavior.
- Create `Testing/GameWorld.Core.Test/Commands/TransformBoneCommandTests.cs`: frame behavior.
- Create `Testing/GameWorld.Core.Test/Components/Gizmo/TransformGestureTests.cs`: live gesture lifecycle.

### Task 1: Make editor closure cancel-safe

**Files:**

- Create: `AssetEditor/Assembly.cs`
- Create: `Testing/AssetEditorTests/EditorManagerTests.cs`
- Modify: `AssetEditor/Services/EditorManager.cs`

**Interfaces:**

- Produces internal constructor:

```csharp
internal EditorManager(
    IGlobalEventHub eventHub,
    IPackFileService packFileService,
    IEditorDatabase editorDatabase,
    Func<string, string, MessageBoxButton, MessageBoxResult> showMessage);
```

- Keeps the existing public three-argument constructor for dependency injection.

- [ ] **Step 1: Expose the narrow internal test seam**

Add:

```csharp
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("AssetEditorTests")]
```

The public constructor must delegate to the internal constructor with
`MessageBox.Show`.

- [ ] **Step 2: Add failing Pack cancellation coverage**

Capture the registered callback:

```csharp
Action<BeforePackFileContainerRemovedEvent>? beforeRemove = null;
eventHub
    .Setup(hub => hub.Register(
        It.IsAny<object>(),
        It.IsAny<Action<BeforePackFileContainerRemovedEvent>>()))
    .Callback<object, Action<BeforePackFileContainerRemovedEvent>>(
        (_, callback) => beforeRemove = callback);
```

Insert a fake `IFileEditor` associated with the removed container, invoke the
callback with a prompt returning `MessageBoxResult.No`, and assert:

```csharp
Assert.That(args.AllowClose, Is.False);
Assert.That(manager.CurrentEditorsList, Does.Contain(editor.Object));
editor.Verify(item => item.Close(), Times.Never);
editorDatabase.Verify(
    database => database.DestroyEditor(editor.Object),
    Times.Never);
```

- [ ] **Step 3: Add failing Close All coverage**

Add two clean fake editors, call `CloseAllTools`, and assert it does not throw,
the collection is empty, and each editor is closed and destroyed exactly once.
The current implementation must fail with collection modification.

- [ ] **Step 4: Run and confirm RED**

```powershell
dotnet test Testing/AssetEditorTests/AssetEditorTests.csproj -c Release --no-restore --filter "FullyQualifiedName~EditorManagerTests"
```

- [ ] **Step 5: Centralize destruction and return on cancel**

Use:

```csharp
private void DestroyEditor(IEditorInterface editor)
{
    if (!CurrentEditorsList.Remove(editor))
        return;

    _editorDatabase.DestroyEditor(editor);
    editor.Close();
}
```

After setting `AllowClose = false`, return immediately. Positive Pack closure
and `CloseTool` must use `DestroyEditor`. Close All must enumerate
`CurrentEditorsList.ToList()`.

- [ ] **Step 6: Run and confirm GREEN**

Run the Task 1 test command.

- [ ] **Step 7: Commit editor lifecycle fixes**

```powershell
git add AssetEditor/Assembly.cs AssetEditor/Services/EditorManager.cs Testing/AssetEditorTests/EditorManagerTests.cs
git commit -m "fix: make editor closure cancel safe"
```

### Task 2: Add optional explicit Redo

**Files:**

- Modify: `GameWorld/View3D/Commands/ICommand.cs`
- Modify: `GameWorld/View3D/Services/CommandExecutor.cs`
- Create: `Testing/GameWorld.Core.Test/Commands/CommandExecutorTests.cs`

**Interfaces:**

- Produces:

```csharp
public interface IRedoableCommand : ICommand
{
    void Redo();
}
```

- [ ] **Step 1: Add executor RED tests**

Create one fake `ICommand` and one fake `IRedoableCommand`. Execute, Undo, and
Redo both. Assert:

```csharp
plain.Verify(command => command.Execute(), Times.Exactly(2));
redoable.Verify(command => command.Execute(), Times.Once);
redoable.Verify(command => command.Redo(), Times.Once);
```

Also assert redo returns the command to the undo stack and publishes the
existing stack-changed event.

- [ ] **Step 2: Run and confirm RED**

```powershell
dotnet test Testing/GameWorld.Core.Test/Test.GameWorld.Core.csproj -c Release --no-restore --filter "FullyQualifiedName~CommandExecutorTests"
```

Expected: `IRedoableCommand` does not exist and the executor always calls
`Execute`.

- [ ] **Step 3: Dispatch Redo by capability**

Inside the existing try block:

```csharp
if (command is IRedoableCommand redoable)
    redoable.Redo();
else
    command.Execute();
```

Do not change exception logging, stack moves, or published events.

- [ ] **Step 4: Run and confirm GREEN**

Run the Task 2 command.

### Task 3: Give vertex transforms symmetric Undo and Redo

**Files:**

- Modify: `GameWorld/View3D/Commands/Vertex/TransformVertexCommand.cs`
- Create: `Testing/GameWorld.Core.Test/Commands/TransformVertexCommandTests.cs`

**Interfaces:**

- `TransformVertexCommand` implements `IRedoableCommand`.
- Produces internal helpers that operate on the command's captured selection:

```csharp
private void ApplyTransform(bool inverse);
internal static void ReverseWindingOrder(MeshObject geometry);
```

- [ ] **Step 1: Add object and vertex-weight RED tests**

For a test mesh, simulate the current live-preview contract:

1. Configure the command from the initial selection.
2. Apply the forward matrix to the geometry as the UI currently does.
3. Call `Execute`, then `Undo`, then `Redo`.

Assert initial and final positions, normals, and tangents within a fixed
epsilon. Add separate object and weighted-vertex cases.

- [ ] **Step 2: Add face, edge, and falloff RED tests**

Set `AffectedVertexIndices` and `FalloffWeights` exactly as the wrapper does.
Assert only affected vertices move and the final state is restored by Redo.

- [ ] **Step 3: Run and confirm RED**

```powershell
dotnet test Testing/GameWorld.Core.Test/Test.GameWorld.Core.csproj -c Release --no-restore --filter "FullyQualifiedName~TransformVertexCommandTests"
```

Expected: Undo succeeds in supported branches but Redo leaves geometry at its
initial state.

- [ ] **Step 4: Extract symmetric application**

Move selection capture into `Configure`, which runs during gesture Begin:

```csharp
public void Configure(List<MeshObject> geometryList, Vector3 pivotPoint)
{
    _geometryList = geometryList;
    PivotPoint = pivotPoint;
    _oldSelectionState = _selectionManager.GetStateCopy();
}
```

`Execute` becomes the commit hook for the already-applied preview and must not
replace `_oldSelectionState`.

`ApplyTransform(false)` must apply the stored forward transform.
`ApplyTransform(true)` must apply its inverse. For weighted branches, decompose
the original transform and build per-vertex forward or inverse matrices using
the same weights. Rebuild the vertex buffer once per affected mesh after the
loop.

Implement:

```csharp
public void Redo()
{
    ApplyTransform(inverse: false);
    _selectionManager.SetState(_oldSelectionState);
}
```

Keep `Execute` as the commit point for the already-applied preview and ensure
the old selection is captured before preview through Task 5's Begin phase.

- [ ] **Step 5: Run and confirm GREEN**

Run the Task 3 command.

### Task 4: Restrict winding reversal to object mode

**Files:**

- Modify: `GameWorld/View3D/Components/Gizmo/TransformGizmoWrapper.cs`
- Modify: `GameWorld/View3D/Commands/Vertex/TransformVertexCommand.cs`
- Modify: `Testing/GameWorld.Core.Test/Commands/TransformVertexCommandTests.cs`

**Interfaces:**

- Consumes: `TransformVertexCommand.ReverseWindingOrder`
- Produces object-only `InvertWindingOrder`

- [ ] **Step 1: Add negative-scale RED tests**

Cover:

- Vertex, face, and edge mode with one negative axis: the full index array is
  unchanged after preview, Undo, and Redo.
- Object mode with one negative axis: preview reverses each triangle once,
  Undo restores it, and Redo reverses it again.
- Object mode with two negative axes: indices remain unchanged.

- [ ] **Step 2: Run and confirm RED**

Run the Task 3 command. Edit-mode cases must show whole-mesh index reversal.

- [ ] **Step 3: Gate winding by selection mode**

In `GizmoScaleEvent`, compare the determinant sign before and after composing
the new total transform:

```csharp
var wasInverted = _totalGizomTransform.Determinant() < 0;
var updatedTransform = _totalGizomTransform * scaleMatrix;
var isInverted = updatedTransform.Determinant() < 0;
```

Reverse winding only when the selection is `ObjectSelectionState` and
`wasInverted != isInverted`. Store `isInverted` in
`_invertedWindingOrder` only for object mode; keep it false in edit modes.
Replace duplicated triangle swap loops with
`TransformVertexCommand.ReverseWindingOrder`. Add a test that crosses the
determinant sign twice and verifies exactly two winding reversals.

- [ ] **Step 4: Run and confirm GREEN**

Run the Task 3 command.

### Task 5: Restore Begin, Commit, and Cancel gesture ownership

**Files:**

- Modify: `GameWorld/View3D/Components/Gizmo/TransformGizmoWrapper.cs`
- Modify: `GameWorld/View3D/Components/Gizmo/GizmoComponent.cs`
- Modify: `GameWorld/View3D/Commands/Vertex/TransformVertexCommand.cs`
- Create: `Testing/GameWorld.Core.Test/Components/Gizmo/TransformGestureTests.cs`

**Interfaces:**

- Produces:

```csharp
public void BeginTransform();
public void CommitTransform(CommandExecutor commandExecutor);
public void CancelTransform();
public void RestoreInitialPreviewState();
```

- Replaces unused `Start`, `Stop`, and `ConfirmModalTransform` behavior after
  all call sites are migrated.

- [ ] **Step 1: Add vertex gesture lifecycle RED tests**

Assert:

- Begin captures pre-gesture selection and geometry.
- Commit creates one undo entry.
- Cancel restores geometry and creates no undo entry.
- Modal restore returns to the same original backup before recalculating.

- [ ] **Step 2: Run and confirm RED**

```powershell
dotnet test Testing/GameWorld.Core.Test/Test.GameWorld.Core.csproj -c Release --no-restore --filter "FullyQualifiedName~TransformGestureTests"
```

- [ ] **Step 3: Implement one active command lifecycle**

`BeginTransform` must reset transform accumulators, create the correct command
from the current state, and back up non-bone geometry. `CommitTransform` must
populate the command's transform, pivot, winding, affected indices, and
falloff before `ExecuteCommand`. `CancelTransform` must restore the backup and
discard `_activeCommand`.

`GizmoTransformStart` calls `BeginTransform`. `GizmoTransformEnd` calls
`CancelTransform` or `CommitTransform` based on `IsModalCancelled`. Remove the
extra backup call from `StartModalTransform`. Replace
`OnRequestRestoreInitialState` with an unconditional call to
`_activeTransformation?.RestoreInitialPreviewState()`; the wrapper restores
geometry or the active bone command according to selection mode.

- [ ] **Step 4: Run and confirm vertex gesture GREEN**

Run the Task 5 command.

### Task 6: Make bone preview, Undo, and Redo reliable

**Files:**

- Modify: `GameWorld/View3D/Commands/Bone/TransformBoneCommand.cs`
- Modify: `GameWorld/View3D/Components/Gizmo/TransformGizmoWrapper.cs`
- Create: `Testing/GameWorld.Core.Test/Commands/TransformBoneCommandTests.cs`
- Modify: `Testing/GameWorld.Core.Test/Components/Gizmo/TransformGestureTests.cs`

**Interfaces:**

- `TransformBoneCommand` implements `IRedoableCommand`.
- Produces:

```csharp
internal void RestoreInitialFrame();
```

- [ ] **Step 1: Add frame lifecycle RED tests**

Configure from an initial frame, apply translation, rotation, and scale to
produce a final frame, then commit, Undo, and Redo. Assert exact selected-bone
position/scale and quaternion equivalence, plus one modified notification per
state transition.

- [ ] **Step 2: Add bone gesture RED tests**

Assert a live bone gesture has an active command before the first update,
Cancel restores the initial frame, and Commit captures a distinct final frame.

- [ ] **Step 3: Run and confirm RED**

```powershell
dotnet test Testing/GameWorld.Core.Test/Test.GameWorld.Core.csproj -c Release --no-restore --filter "FullyQualifiedName~TransformBoneCommandTests|FullyQualifiedName~TransformGestureTests"
```

- [ ] **Step 4: Capture and restore cloned frames**

Keep `_oldFrame` from `Configure`. On first committed `Execute`, capture:

```csharp
_newFrame = _boneSelectionState
    .CurrentAnimation
    .DynamicFrames[_currentFrame]
    .Clone();
```

Undo assigns `_oldFrame.Clone()`. Redo assigns `_newFrame.Clone()`.
`RestoreInitialFrame` performs the same initial restore without entering
history. The wrapper must call it for Cancel and modal preview reset.

- [ ] **Step 5: Run and confirm GREEN**

Run Task 3, Task 5, and Task 6 filters together.

- [ ] **Step 6: Commit transform correctness**

```powershell
git add GameWorld/View3D/Commands GameWorld/View3D/Services/CommandExecutor.cs GameWorld/View3D/Components/Gizmo Testing/GameWorld.Core.Test
git commit -m "fix: make 3D transforms undoable and redoable"
```

### Task 7: Verify the editor and transform batch

- [ ] **Step 1: Run owning test projects**

```powershell
dotnet test Testing/AssetEditorTests/AssetEditorTests.csproj -c Release --no-restore
dotnet test Testing/GameWorld.Core.Test/Test.GameWorld.Core.csproj -c Release --no-restore
```

- [ ] **Step 2: Run scope checks**

```powershell
git diff master...HEAD --check
git status --short
```

- [ ] **Step 3: Perform focused manual gestures**

In object, vertex, face, edge, and bone modes, perform translate, rotate, and
scale; confirm, cancel, Undo, and Redo each. Verify edit-mode negative scale
does not reverse unrelated faces and object-mode negative scale restores
winding through Undo.
