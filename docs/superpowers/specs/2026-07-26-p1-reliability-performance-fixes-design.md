# P1 Reliability, Security, and Performance Fixes

## Context

The CN release is on `master` with the three P0 data-integrity fixes merged.
The complete Release test baseline passes, but the seven remaining P1 audit
categories are still present:

1. Duplicate basenames can resolve to the wrong Pack path.
2. A failed Pack save can leave active `DataSource` objects pointing at
   uncommitted offsets.
3. Unknown animation metadata and failed AnimPack child parsing are not
   lossless.
4. Editor cancellation and Close All have deterministic lifecycle bugs.
5. Transform Redo and edit-mode negative scaling can corrupt 3D state.
6. An elevated updater relaunches executable content from a user-writable
   directory.
7. Face/edge transforms, edge overlays, text folding, and IPC contain
   confirmed resource or performance problems.

## Decision

Use an incremental architecture approach, referred to as A+:

- Establish a small, explicit boundary where the defect is caused by an
  implicit contract.
- Keep existing binary formats, user workflows, and public interfaces unless
  a narrow internal contract must change.
- Avoid a repository-wide transaction framework, command-system rewrite,
  concurrent IPC redesign, Windows service, or packaging migration.
- Add a failing regression test before each production fix.

This provides durable ownership boundaries without the regression risk of a
large rewrite.

## Goals

- Make Pack path lookup deterministic and identity-based.
- Keep the active Pack model unchanged until the new file is committed.
- Preserve unknown animation content byte for byte.
- Make every editor close path cancel-safe and destroy each closed editor once.
- Give live 3D transforms an explicit Begin, Commit, Cancel, Undo, and Redo
  lifecycle.
- Never reverse an entire mesh winding order for an edit-mode transform.
- Bound per-frame geometry uploads and edge-overlay work.
- Stop view-owned timers when their view is unloaded.
- Ensure an elevated updater only executes and consumes protected workspace
  content.
- Ensure a conflicting or slow IPC client cannot create a hot loop or hold the
  only channel indefinitely.

## Non-goals

- Changing Pack, metadata, AnimPack, or rigid-model binary formats.
- Resolving detached `PackFile` objects by guessing from their basename.
- Adding local face-orientation reconstruction for partially mirrored
  edit-mode topology.
- Replacing the complete command framework or storing full mesh snapshots in
  the undo stack.
- Adding Authenticode, a signed release manifest, MSIX, or a Windows service.
- Making IPC handlers concurrent.
- Rendering more than the existing 50,000-edge overlay limit.
- Refactoring unrelated warnings, nullability annotations, or editor code.

## Design Invariants

The implementation must preserve these invariants:

1. A failed Pack save does not replace any active file `DataSource`, pack path,
   or saved-size metadata.
2. A `PackFile` path is returned only for the exact object stored by a
   container.
3. Unknown child data emitted after a load/save round trip is byte-identical
   to that child's original payload.
4. A cancelled close does not remove, close, or destroy any affected editor.
5. A committed 3D gesture can be undone and redone without depending on an
   already-applied UI side effect.
6. Edit-mode transforms do not modify the mesh index buffer.
7. Elevated update code and archives are never executed or consumed from a
   medium-integrity writable tree.
8. IPC work has explicit time, size, and retry bounds.

## Component Design

### 1. Pack identity and transactional save commit

`PackFileService.GetFullPath` will search by object identity only. Both global
and explicit-container searches will use `ReferenceEquals`. A missing object
will continue to throw rather than returning a guessed path.

`PackFileSerializerWriter` will write bytes and produce an immutable pending
result describing the `PackedFileSource` that each `PackFile` should receive.
It will not assign those sources while serialization is in progress.

`PackFileService.SavePackContainerCore` will own the commit sequence:

1. Serialize into the existing unique temporary file and collect pending
   source assignments.
2. Flush and dispose the temporary output.
3. Close old shared parent streams so Windows can replace the destination.
   The original `DataSource` objects remain installed and can reopen their
   original file if replacement fails.
4. Replace the destination file.
5. Apply all pending `DataSource` assignments as one in-memory commit.
6. Update `SystemFilePath` and `OriginalLoadByteSize`.

Any exception before step 5 leaves every active `DataSource` reference
unchanged. Temporary-file cleanup remains best effort and must not mask the
original exception.

### 2. Lossless unknown animation content

`MetaDataFileParser.Serialize` will recognize
`ParsedUnknownMetadataAttribute` and return its stored raw `Data` directly.
Missing raw data is an invalid state and will fail explicitly rather than
silently writing a truncated record. Known metadata continues through the
existing reflection serializer so edited fields are retained.

When a known AnimPack child serializer throws, `AnimationPackSerializer` will
construct `UnknownAnimFile` from only the child's `StartOffset` and `Size`.
It will preserve the current tolerant behavior of opening a parent pack that
contains an unsupported or damaged child.

### 3. Editor close lifecycle

`EditorManager` will use one private destruction path that:

- Removes an editor only if it is still present.
- Calls `IEditorDatabase.DestroyEditor` once.
- Calls the editor's `Close` method once.

The Pack-removal callback will return immediately after a negative
confirmation. A positive Pack-level confirmation will use the destruction
path without showing a second per-editor prompt.

Close All will enumerate a snapshot, matching the existing Close Other
behavior. If an individual unsaved editor cancels, that editor remains open
while the snapshot iteration safely continues.

A narrow internal prompt delegate will make the Pack cancellation path
testable without introducing a scoped dialog service into the singleton
manager.

### 4. Transform command and gesture lifecycle

Add a narrow `IRedoableCommand` contract. `CommandExecutor.Redo` will call its
`Redo` method when implemented and will keep calling `Execute` for all existing
commands. This avoids modifying commands whose current replay behavior is
already correct.

The transform gesture will have explicit phases:

- Begin creates and configures the active transform command from the
  pre-gesture state.
- Live updates modify the preview geometry or animation frame.
- Commit captures the final state and places the command on the undo stack.
- Cancel restores the backup and discards the active command.

`TransformVertexCommand.Redo` will replay the stored forward matrix using the
same object, vertex-weight, face/edge, and falloff branches as Undo. It will
not store full before/after mesh copies.

`TransformBoneCommand` will retain cloned initial and final frames. Undo
restores the initial frame; Redo restores the final frame; both notify the
same selected bones.

Whole-mesh winding reversal will occur only for object-mode transforms with an
odd number of negative axes. Vertex, face, and edge transforms will leave the
index buffer unchanged. A single winding helper will be shared by initial
application, Undo, and Redo.

### 5. Bounded rendering and view resources

All edit-mode transform branches will report changed vertex indices through
one range tracker. Face, edge, vertex, and falloff updates will use the
existing partial VBO upload for the resulting inclusive range. Object mode
will keep the full upload. Gesture completion will rebuild the bounding box
once after deferred live updates.

The edge topology builder will stop as soon as 50,000 unique edges have been
collected. It will avoid allocating a temporary three-edge array for every
triangle. Edge data passed to the renderer will have the actual cached edge
length, so a three-edge mesh uploads and draws three instances rather than
50,000 default instances.

`TextEditorView` will own its folding timer in a field, start it on `Loaded`,
and stop it on `Unloaded`. Reloading the same view will start the same timer
again without accumulating handlers.

### 6. Protected updater workspace

The initial updater remains the protected executable launched by the main
application. Workspace selection will depend on the updater token:

- A non-elevated update keeps the current LocalAppData workspace because it
  crosses no privilege boundary.
- An elevated update creates a random transaction root under
  `CommonApplicationData`.

The elevated transaction root will:

- Disable inherited access rules.
- Be owned by `BUILTIN\Administrators`.
- Grant full control only to `BUILTIN\Administrators` and `SYSTEM`.
- Reject pre-existing roots, reparse points, or invalid ownership and ACLs.

The update directory, archive, extraction staging, and backup root will all
remain inside that protected transaction root. The chosen path will be passed
explicitly to the second-stage updater instead of being recomputed.

Updater payload files will be copied with exclusive file access and verified
against SHA-256 hashes computed from the protected installation source before
the copied updater is launched. The protected ACL prevents replacement after
verification. Downloaded archives will also be written with `FileShare.None`
and consumed only inside the same protected root.

Cleanup will only remove updater-marked transaction roots after validating
their path, marker, ownership, ACL, and non-reparse status. Supply-chain
authenticity beyond HTTPS remains a separate future signing project.

### 7. Bounded IPC server behavior

The named-pipe server will remain single-instance and sequential so handlers
do not unexpectedly execute concurrent UI or Pack mutations.

The loop will add:

- `PipeOptions.CurrentUserOnly`.
- A cancellation-aware 500 ms delay after bind or unexpected loop failures.
- Rate-limited error logging for repeated bind conflicts.
- A five-second request-read deadline.
- A five-second response-write deadline.
- A 64 KiB maximum newline-delimited request.
- Cancellation that promptly releases a pending connection, read, delay, or
  write during disposal.

An oversized, silent, invalid, or disconnected client will receive a failure
when possible, be disconnected, and release the only server instance for the
next client.

## Error Handling

- Ambiguous or detached Pack files fail instead of selecting a basename match.
- Serialization and final replacement exceptions preserve the original active
  model and original exception.
- Unknown formats preserve raw content; missing raw content fails explicitly.
- Closing a missing editor is a no-op rather than an invalid collection index.
- Transform cancellation restores backups and does not enter undo history.
- Invalid secure-workspace state fails closed before a privileged child or
  installation copy is started.
- IPC timeouts, oversize frames, and bind conflicts are contained within the
  server loop and remain cancellation-aware.

## Test Strategy

Each production change will be preceded by a regression test that is observed
failing for the expected reason.

### Pack and format tests

- Duplicate basenames in one directory tree, an explicit container, and
  separate containers resolve only the requested instance.
- A detached same-name `PackFile` throws.
- A later serialization failure leaves every original `DataSource` reference
  unchanged.
- Unknown metadata performs an exact byte round trip.
- A failed known AnimPack child parser preserves only that child's bytes and
  does not absorb a sibling or the parent pack.

### Editor and transform tests

- Negative Pack-close confirmation preserves tabs and avoids Close/Destroy.
- Close All safely closes multiple editors exactly once.
- Object, weighted vertex, face, edge, and falloff transforms survive
  Execute/Undo/Redo within numeric tolerance.
- Bone translation, rotation, and scale preserve initial and final frames
  through Undo/Redo.
- Edit-mode negative scale leaves indices unchanged; object-mode winding
  reverses, restores, and reverses again.

### Performance and lifecycle tests

- Face and edge transforms use the expected partial upload range; object mode
  remains a full upload.
- Edge cache deduplication stops at the configured maximum.
- Small meshes upload and draw their actual edge count.
- The folding timer follows the Loaded/Unloaded lifecycle.

### Updater and IPC tests

- Non-elevated and elevated path selection use LocalApplicationData and
  CommonApplicationData respectively.
- Secure-workspace descriptors have protected inheritance, owner, and rules.
- Tampering between payload copy and launch fails verification and prevents
  process start.
- A bind conflict performs one retry only after the injected delay.
- A silent or oversized client is released and a following valid client
  succeeds.
- Disposal cancels pending IPC work promptly.

### Repository verification

Run targeted owning projects first, followed by:

```powershell
dotnet test AssetEditor.CN.sln -c Release --no-restore
dotnet build AssetEditor.CN.sln -c Release --no-restore
git diff --check
```

Manual verification will cover a writable portable install, a Program Files
install with UAC, normal-privilege application restart, and representative 3D
object/vertex/face/edge transform gestures.

## Delivery Sequence

Implementation will be split into independently reviewable commits:

1. Pack identity, transactional save commit, and unknown-format preservation.
2. Editor close lifecycle and transform command correctness.
3. Protected updater workspace and bounded IPC.
4. Partial geometry uploads, bounded edge overlays, and folding timer
   lifecycle.
5. Cross-cutting verification adjustments, if required.

No commit will combine unrelated cleanup or warning suppression.

## Acceptance Criteria

- All new regression tests pass and were demonstrated RED before production
  changes.
- The complete solution test command exits successfully.
- The Release build exits successfully with no new errors.
- Failed Pack saves preserve active model references.
- Unknown-format fixtures round trip byte for byte.
- Transform Undo/Redo and winding tests cover every selection mode.
- Elevated update execution and content stay outside LocalAppData.
- IPC conflict, timeout, oversize, recovery, and disposal tests pass.
- Edge and VBO tests prove bounded actual work rather than only equivalent
  pixels.
- The worktree contains only scoped P1 changes and approved documentation.
