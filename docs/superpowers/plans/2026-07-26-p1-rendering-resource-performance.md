# P1 Rendering and Resource Performance Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Bound edit-mode GPU uploads, edge-overlay CPU/GPU work, and text-editor timer lifetime while preserving current rendering behavior and limits.

**Architecture:** Reuse the existing partial VBO API through one changed-vertex range tracker, extract a capped edge-cache builder that returns actual-sized data, and bind the folding timer to WPF view lifecycle. This plan consumes the completed transform lifecycle from the editor/transform plan.

**Tech Stack:** C#, .NET 10, WPF Dispatcher, MonoGame graphics abstractions, NUnit

## Global Constraints

- Object-mode transforms keep full VBO uploads.
- Vertex, face, edge, and falloff transforms use bounded partial uploads.
- Bounding boxes rebuild once when a gesture completes.
- Edge overlay remains capped at exactly 50,000 unique edges.
- Small meshes upload and draw their actual edge count.
- Do not add asynchronous rendering or change visible overlay styling.
- Text views must support unload and later reload without accumulating handlers.
- Add and observe a focused RED test before each production change.
- Execute after the editor/transform correctness plan.

---

## File Structure

- Modify `GameWorld/View3D/Components/Gizmo/TransformGizmoWrapper.cs`: unified modified range tracking.
- Modify `Testing/GameWorld.Core.Test/TestUtility/TestGeometryGraphicsContextFactory.cs`: record full and partial uploads.
- Create `Testing/GameWorld.Core.Test/Components/Gizmo/TransformUploadTests.cs`: selection-mode upload tests.
- Create `GameWorld/View3D/Components/Selection/EdgeIndexCacheBuilder.cs`: capped topology deduplication.
- Create `GameWorld/View3D/Components/Selection/EdgeOverlayDataBuilder.cs`: actual-sized edge instance data.
- Modify `GameWorld/View3D/Components/Selection/SelectionManager.cs`: actual-sized edge caches.
- Modify `GameWorld/View3D/Rendering/EdgeQuadInstanceMesh.cs`: expose and honor actual instance count contract.
- Create `Testing/GameWorld.Core.Test/Components/Selection/EdgeIndexCacheBuilderTests.cs`: cap and deduplication coverage.
- Create `Testing/GameWorld.Core.Test/Rendering/EdgeOverlayDataTests.cs`: actual edge-count contract.
- Modify `Editors/Shared/Editors.Shared.Core/Editors/TextEditor/TextEditorView.xaml.cs`: timer lifecycle.
- Create `Editors/Shared/Editors.Shared.Core/Assembly.cs`: expose timer state to the test assembly.
- Create `Testing/AssetEditorTests/TextEditorViewLifecycleTests.cs`: WPF load/unload coverage.
- Modify `Testing/AssetEditorTests/AssetEditorTests.csproj`: add a direct reference to `Editors.Shared.Core`.

### Task 1: Record graphics uploads in the test context

**Files:**

- Modify: `Testing/GameWorld.Core.Test/TestUtility/TestGeometryGraphicsContextFactory.cs`
- Create: `Testing/GameWorld.Core.Test/Components/Gizmo/TransformUploadTests.cs`

**Interfaces:**

- Produces:

```csharp
public sealed record PartialUpload(int StartIndex, int Count);

public class TestGraphicsCardGeometry : IGraphicsCardGeometry
{
    public int FullVertexUploadCount { get; private set; }
    public IReadOnlyList<PartialUpload> PartialUploads { get; }
    public int IndexUploadCount { get; private set; }
    public void ResetCounters();
}
```

The factory must retain created contexts so a test can inspect the context
owned by its `MeshObject`.

- [ ] **Step 1: Make the fake record existing calls**

Increment counters in `RebuildVertexBuffer` and `RebuildIndexBuffer`. Append
`new PartialUpload(startIndex, count)` in `RebuildVertexBufferPartial`.
`ResetCounters` clears all values after mesh construction uploads.

- [ ] **Step 2: Add current-behavior RED tests**

Build face and edge selections with non-contiguous vertex indices. Apply one
translation update and assert:

```csharp
Assert.That(context.FullVertexUploadCount, Is.Zero);
Assert.That(context.PartialUploads, Is.EqualTo(
    new[] { new PartialUpload(expectedMin, expectedMax - expectedMin + 1) }));
```

Add object mode and assert one full upload and no partial upload.

- [ ] **Step 3: Run and confirm RED**

```powershell
dotnet test Testing/GameWorld.Core.Test/Test.GameWorld.Core.csproj -c Release --no-restore --filter "FullyQualifiedName~TransformUploadTests"
```

Expected: face and edge paths call the full upload.

### Task 2: Track every changed edit-mode vertex

**Files:**

- Modify: `GameWorld/View3D/Components/Gizmo/TransformGizmoWrapper.cs`
- Modify: `Testing/GameWorld.Core.Test/Components/Gizmo/TransformUploadTests.cs`

**Interfaces:**

- Produces:

```csharp
private void TrackModifiedVertex(int vertexIndex)
{
    _modifiedMin = Math.Min(_modifiedMin, vertexIndex);
    _modifiedMax = Math.Max(_modifiedMax, vertexIndex);
    _hasModifications = true;
}
```

- [ ] **Step 1: Replace vertex-only tracking**

Call `TrackModifiedVertex` immediately after every actual vertex transform in:

- Weighted vertex mode.
- Face mode without falloff.
- Face mode with falloff.
- Edge mode without falloff.
- Edge mode with falloff.

Do not mark zero-weight vertices or object-mode loops.

- [ ] **Step 2: Keep upload ownership explicit**

Reset `_modifiedMin`, `_modifiedMax`, and `_hasModifications` at the start of
each `ApplyTransform` call so a reduced falloff or changed edit selection
cannot retain a wider range from an earlier frame in the same gesture.

After each mesh update:

```csharp
if (_selectionState is ObjectSelectionState)
    geometry.RebuildVertexBuffer();
else if (_hasModifications)
    geometry.RebuildVertexBufferPartial(_modifiedMin, _modifiedMax);
```

Do not fall back to a full upload for edit mode when no vertex changed.
Also reset the range after commit/cancel.

- [ ] **Step 3: Rebuild the bounding box once**

At Begin, set `DeferBoundingBoxRebuild = true` for affected meshes. At both
Commit and Cancel, restore it to false and call `BuildBoundingBox` once after
the final vertex state is installed.

- [ ] **Step 4: Run and confirm GREEN**

Run Task 1 tests. Add falloff cases and a final bounding-box assertion.

- [ ] **Step 5: Commit partial upload work**

```powershell
git add GameWorld/View3D/Components/Gizmo/TransformGizmoWrapper.cs Testing/GameWorld.Core.Test/TestUtility/TestGeometryGraphicsContextFactory.cs Testing/GameWorld.Core.Test/Components/Gizmo/TransformUploadTests.cs
git commit -m "perf: use partial uploads for edit transforms"
```

### Task 3: Cap edge topology while building it

**Files:**

- Create: `GameWorld/View3D/Components/Selection/EdgeIndexCacheBuilder.cs`
- Create: `Testing/GameWorld.Core.Test/Components/Selection/EdgeIndexCacheBuilderTests.cs`
- Modify: `GameWorld/View3D/Components/Selection/SelectionManager.cs`

**Interfaces:**

- Produces:

```csharp
internal static class EdgeIndexCacheBuilder
{
    internal static (int V0, int V1)[] Build(
        ReadOnlySpan<ushort> indices,
        int maxEdges);
}
```

- [ ] **Step 1: Add edge-cache RED tests**

Cover:

```csharp
Assert.That(
    EdgeIndexCacheBuilder.Build([0, 1, 2], 50_000),
    Is.EquivalentTo(new[] { (0, 1), (1, 2), (0, 2) }));
```

Add two triangles sharing an edge and assert five unique normalized pairs.
Generate enough disjoint triangles to exceed a small test cap and assert the
result length equals the cap and no additional input is represented.
Assert zero or negative `maxEdges` throws `ArgumentOutOfRangeException`.

- [ ] **Step 2: Run and confirm RED**

```powershell
dotnet test Testing/GameWorld.Core.Test/Test.GameWorld.Core.csproj -c Release --no-restore --filter "FullyQualifiedName~EdgeIndexCacheBuilderTests"
```

- [ ] **Step 3: Implement allocation-bounded deduplication**

Normalize each edge with `Math.Min/Math.Max`. Use one `HashSet<(int, int)>` and
one result list pre-sized to `Math.Min(maxEdges, indices.Length)`. Add three
edges directly without constructing `new[]`. Return immediately after the
result reaches `maxEdges`.

- [ ] **Step 4: Use the builder in `SelectionManager`**

Replace `BuildEdgeIndexCache(geo)` with:

```csharp
_cachedEdgeIndices = EdgeIndexCacheBuilder.Build(
    geometry.IndexArray,
    MaxRenderEdges);
_edgeDataCache = new EdgeData[_cachedEdgeIndices.Length];
```

Remove the old full-topology builder and the late
`Math.Min(_cachedEdgeIndices.Length, MaxRenderEdges)` truncation.

- [ ] **Step 5: Run and confirm GREEN**

Run the Task 3 command.

### Task 4: Upload and draw the actual edge count

**Files:**

- Modify: `GameWorld/View3D/Components/Selection/SelectionManager.cs`
- Create: `GameWorld/View3D/Components/Selection/EdgeOverlayDataBuilder.cs`
- Modify: `GameWorld/View3D/Rendering/EdgeQuadInstanceMesh.cs`
- Create: `Testing/GameWorld.Core.Test/Rendering/EdgeOverlayDataTests.cs`

**Interfaces:**

- `SelectionManager` passes an actual-sized `EdgeData[]`, so existing
  `Update(EdgeData[] edges)` remains the only renderer update contract.
- Produces:

```csharp
internal static class EdgeOverlayDataBuilder
{
    internal static void Fill(
        Span<EdgeData> destination,
        MeshObject geometry,
        Matrix renderMatrix,
        IReadOnlyList<(int V0, int V1)> edges,
        IReadOnlyList<float> weights);
}
```

- [ ] **Step 1: Add actual-count RED tests**

Test the pure edge-data builder:

```csharp
internal static void Fill(
    Span<EdgeData> destination,
    MeshObject geometry,
    Matrix renderMatrix,
    IReadOnlyList<(int V0, int V1)> edges,
    IReadOnlyList<float> weights);
```

For one triangle, allocate a three-element destination, fill it, and assert
all three entries contain the expected endpoints and colors. Assert that a
destination whose length differs from `edges.Count` throws
`ArgumentException`.

- [ ] **Step 2: Run and confirm RED**

```powershell
dotnet test Testing/GameWorld.Core.Test/Test.GameWorld.Core.csproj -c Release --no-restore --filter "FullyQualifiedName~EdgeOverlayDataTests"
```

Expected: current SelectionManager retains and passes a 50,000-element array.

- [ ] **Step 3: Pass actual-sized data**

Allocate `_edgeDataCache` only when the mesh cache changes and size it to
`_cachedEdgeIndices.Length`. Call `EdgeOverlayDataBuilder.Fill` on dirty
updates and assign that actual-sized array to `EdgeQuadRenderItem.Edges`. This
reuses the same correctly sized allocation rather than allocating every
frame.

`EdgeQuadInstanceMesh.Update` must set:

```csharp
_currentInstanceCount = Math.Min(edges.Length, _maxInstanceCount);
```

Because `edges.Length` is now actual, GPU upload and draw instance count are
actual. Add an internal read-only `CurrentInstanceCount` property for
diagnostics, without introducing a second count input.

- [ ] **Step 4: Run edge tests GREEN**

Run Task 3 and Task 4 tests together.

- [ ] **Step 5: Commit bounded edge overlays**

```powershell
git add GameWorld/View3D/Components/Selection GameWorld/View3D/Rendering/EdgeQuadInstanceMesh.cs Testing/GameWorld.Core.Test/Components/Selection Testing/GameWorld.Core.Test/Rendering/EdgeOverlayDataTests.cs
git commit -m "perf: bound edge overlay work"
```

### Task 5: Bind the folding timer to view lifecycle

**Files:**

- Modify: `Editors/Shared/Editors.Shared.Core/Editors/TextEditor/TextEditorView.xaml.cs`
- Create: `Editors/Shared/Editors.Shared.Core/Assembly.cs`
- Create: `Testing/AssetEditorTests/TextEditorViewLifecycleTests.cs`
- Modify: `Testing/AssetEditorTests/AssetEditorTests.csproj`

**Interfaces:**

- Produces internal observable state for tests:

```csharp
internal bool IsFoldingTimerEnabled => _foldingUpdateTimer.IsEnabled;
```

- [ ] **Step 1: Add WPF lifecycle RED test**

Run the test in STA:

```csharp
[Test]
[Apartment(ApartmentState.STA)]
public void FoldingTimer_StopsWhenViewIsUnloaded_AndRestartsWhenLoaded()
{
    var view = new TextEditorView();
    view.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
    Assert.That(view.IsFoldingTimerEnabled, Is.True);

    view.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
    Assert.That(view.IsFoldingTimerEnabled, Is.False);

    view.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
    Assert.That(view.IsFoldingTimerEnabled, Is.True);
}
```

Add `InternalsVisibleTo("AssetEditorTests")` in the new owning assembly file.
Add this direct project reference to `AssetEditorTests.csproj`:

```xml
<ProjectReference Include="..\..\Editors\Shared\Editors.Shared.Core\Editors.Shared.Core.csproj" />
```

- [ ] **Step 2: Run and confirm RED**

```powershell
dotnet test Testing/AssetEditorTests/AssetEditorTests.csproj -c Release --no-restore --filter "FullyQualifiedName~TextEditorViewLifecycleTests"
```

Expected: the current local timer has no observable lifecycle and remains
enabled after unload.

- [ ] **Step 3: Own and stop the timer**

Create `_foldingUpdateTimer` once in the constructor, attach a named Tick
handler once, and register named Loaded/Unloaded handlers:

```csharp
private void OnLoaded(object sender, RoutedEventArgs e) =>
    _foldingUpdateTimer.Start();

private void OnUnloaded(object sender, RoutedEventArgs e) =>
    _foldingUpdateTimer.Stop();

private void OnFoldingTimerTick(object? sender, EventArgs e) =>
    UpdateFoldings();
```

Do not start the timer in the constructor. Reloading starts the same instance
without duplicate Tick subscriptions.

- [ ] **Step 4: Run and confirm GREEN**

Run the Task 5 command.

- [ ] **Step 5: Commit timer lifecycle fix**

```powershell
git add Editors/Shared/Editors.Shared.Core/Editors/TextEditor/TextEditorView.xaml.cs Editors/Shared/Editors.Shared.Core/Assembly.cs Testing/AssetEditorTests
git commit -m "fix: stop text folding timer when unloaded"
```

### Task 6: Verify rendering and resource performance

- [ ] **Step 1: Run owning projects**

```powershell
dotnet test Testing/GameWorld.Core.Test/Test.GameWorld.Core.csproj -c Release --no-restore
dotnet test Testing/AssetEditorTests/AssetEditorTests.csproj -c Release --no-restore
```

- [ ] **Step 2: Run a bounded-work benchmark test**

In a non-gating diagnostic test or one-off test harness, build an index array
large enough to contain more than 50,000 unique edges. Record:

```csharp
var before = GC.GetAllocatedBytesForCurrentThread();
var timer = Stopwatch.StartNew();
var edges = EdgeIndexCacheBuilder.Build(indices, 50_000);
timer.Stop();
var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
```

Report elapsed time and allocation, assert only the deterministic
`edges.Length == 50_000` contract in the normal test suite, and do not add a
machine-dependent timing threshold.

- [ ] **Step 3: Run scope checks**

```powershell
git diff master...HEAD --check
git status --short
```

- [ ] **Step 4: Perform representative visual checks**

Open a small mesh and a large mesh in vertex, face, and edge modes. Confirm
selection overlays render, transformed vertices update during dragging,
bounding boxes remain correct, and no stale zero-length edge instances are
visible.
