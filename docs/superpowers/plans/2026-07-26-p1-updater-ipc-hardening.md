# P1 Updater and IPC Hardening Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Remove the updater's elevated user-writable execution boundary and give the single-instance IPC server explicit retry, size, timeout, and cancellation limits.

**Architecture:** Introduce one updater-owned workspace boundary that selects LocalAppData only for non-elevated work and protected CommonApplicationData for elevated work. Keep IPC sequential, but centralize protocol bounds and inject only the factory/delay seams needed for deterministic lifecycle tests.

**Tech Stack:** C#, .NET 10 Windows, Windows ACL APIs, SHA-256, named pipes, NUnit, Moq

## Global Constraints

- Elevated update execution, archives, extraction, and backups must stay outside LocalAppData.
- Protected directories must deny inherited medium-integrity user writes.
- Reject reparse points, invalid ownership, invalid ACLs, and unowned cleanup targets.
- Do not introduce a service, MSIX, Authenticode, or signed manifest.
- Keep IPC handler execution single-instance and sequential.
- IPC request read and response write deadlines are five seconds.
- IPC maximum newline-delimited request size is 64 KiB.
- IPC retry delay is cancellation-aware and 500 ms.
- Add and observe a focused RED test before each production change.

---

## File Structure

- Create `AssetEditorUpdater/UpdaterWorkspace.cs`: elevation detection, workspace paths, ACL creation/validation, ownership marker.
- Create `AssetEditorUpdater/UpdaterPayloadCopier.cs`: exclusive copy and SHA-256 verification.
- Modify `AssetEditorUpdater/AssetEditorUpdater.cs`: explicit workspace arguments and protected payload launch.
- Modify `AssetEditorUpdater/UpdateInstaller.cs`: protected transaction validation and safe cleanup integration.
- Modify `Testing/AssetEditorUpdaterTests/UpdateInstallerTests.cs`: replace the all-LocalAppData path assumption.
- Create `Testing/AssetEditorUpdaterTests/UpdaterWorkspaceTests.cs`: path and ACL descriptor tests.
- Create `Testing/AssetEditorUpdaterTests/UpdaterPayloadCopierTests.cs`: copy integrity and tamper tests.
- Modify `Editors/Ipc/IpcEditor/AssetEditorIpcServer.cs`: bounded loop and frame protocol.
- Modify `Editors/Ipc/Test.Ipc/AssetEditorIpcServerTests.cs`: real and injected server lifecycle tests.

### Task 1: Define elevated and non-elevated workspaces

**Files:**

- Create: `AssetEditorUpdater/UpdaterWorkspace.cs`
- Create: `Testing/AssetEditorUpdaterTests/UpdaterWorkspaceTests.cs`
- Modify: `Testing/AssetEditorUpdaterTests/UpdateInstallerTests.cs`

**Interfaces:**

- Produces:

```csharp
internal sealed record UpdaterWorkspace(
    string TransactionRoot,
    string UpdateDirectory,
    bool IsProtected);

internal sealed record UpdaterWorkspaceLayout(
    string TransactionRoot,
    string UpdateDirectory,
    bool IsProtected);

internal static class UpdaterWorkspaceFactory
{
    internal static bool IsProcessElevated();
    internal static UpdaterWorkspaceLayout GetLayout(
        bool isElevated,
        Guid transactionId,
        string? localApplicationDataRoot = null,
        string? commonApplicationDataRoot = null);
    internal static UpdaterWorkspace Create(UpdaterWorkspaceLayout layout);
}

internal static class UpdaterWorkspaceSecurity
{
    internal static DirectorySecurity CreateProtectedDescriptor();
    internal static void ValidateProtectedDirectory(string path);
}
```

- [ ] **Step 1: Add path-selection RED tests**

Assert:

```csharp
var normal = UpdaterWorkspaceFactory.GetLayout(
    false, Guid.Empty, localRoot, commonRoot);
Assert.That(normal.UpdateDirectory, Does.StartWith(localRoot));
Assert.That(normal.IsProtected, Is.False);

var transactionId = Guid.NewGuid();
var elevated = UpdaterWorkspaceFactory.GetLayout(
    true, transactionId, localRoot, commonRoot);
Assert.That(elevated.TransactionRoot, Does.StartWith(commonRoot));
Assert.That(elevated.TransactionRoot, Does.Not.StartWith(localRoot));
Assert.That(elevated.IsProtected, Is.True);
Assert.That(
    Path.GetFileName(elevated.TransactionRoot),
    Is.EqualTo(transactionId.ToString("N")));
```

Path selection is pure and requires no ACL privileges. Separate creation tests
call `Create(layout)`; protected creation runs under an elevated token and
reports an explicit skip under a medium-integrity token.

- [ ] **Step 2: Add ACL descriptor RED tests**

Inspect the descriptor without applying it:

```csharp
var security = UpdaterWorkspaceSecurity.CreateProtectedDescriptor();
Assert.That(security.AreAccessRulesProtected, Is.True);
AssertOwnerIsBuiltinAdministrators(security);
AssertOnlyFullControlAllows(
    security,
    WellKnownSidType.BuiltinAdministratorsSid,
    WellKnownSidType.LocalSystemSid);
```

Assert no allow rule grants the current user SID, `BuiltinUsersSid`,
`AuthenticatedUserSid`, or `WorldSid`.

- [ ] **Step 3: Run and confirm RED**

```powershell
dotnet test Testing/AssetEditorUpdaterTests/AssetEditorUpdaterTests.csproj -c Release --no-restore --filter "FullyQualifiedName~UpdaterWorkspaceTests|FullyQualifiedName~UpdateDirectories"
```

Expected: the current implementation always returns LocalApplicationData and
has no protected descriptor.

- [ ] **Step 4: Implement workspace selection and descriptor**

Non-elevated layout remains under:

```text
<LocalApplicationData>\AssetEditor.CN\Temp\Update
```

Elevated layout is:

```text
<CommonApplicationData>\AssetEditor.CN\UpdaterTransactions\<guid>\Update
```

`GetLayout` only canonicalizes the approved roots and composes paths. `Create`
builds a `DirectorySecurity` with protected inheritance, administrator owner,
and inheritable full-control rules for Administrators and SYSTEM only. It
creates and validates the fixed protected parent before atomically creating a
new GUID transaction directory. Reject any reparse point or pre-existing
random transaction path.

- [ ] **Step 5: Run path and descriptor tests GREEN**

Run the Task 1 command. The real ACL application test runs when the test token
is elevated and reports an explicit skip otherwise; descriptor tests always
run.

### Task 2: Pass the chosen workspace across updater stages

**Files:**

- Modify: `AssetEditorUpdater/AssetEditorUpdater.cs`
- Modify: `AssetEditorUpdater/UpdateInstaller.cs`
- Modify: `Testing/AssetEditorUpdaterTests/UpdateInstallerTests.cs`
- Modify: `Testing/AssetEditorUpdaterTests/UpdaterWorkspaceTests.cs`

**Interfaces:**

- First stage has zero arguments.
- Second stage has exactly:

```text
<installationDirectory> <updateDirectory>
```

- [ ] **Step 1: Add argument and layout RED tests**

Extract argument interpretation into an internal pure method:

```csharp
internal sealed record UpdaterInvocation(
    bool IsInitialLaunch,
    string InstallationDirectory,
    string? UpdateDirectory);

internal static UpdaterInvocation ParseInvocation(
    string currentDirectory,
    string[] args);
```

Test zero arguments, two valid arguments, and every other argument count. The
latter must throw `ArgumentException`.

- [ ] **Step 2: Run and confirm RED**

```powershell
dotnet test Testing/AssetEditorUpdaterTests/AssetEditorUpdaterTests.csproj -c Release --no-restore --filter "FullyQualifiedName~UpdaterWorkspaceTests"
```

- [ ] **Step 3: Use an explicit second-stage update path**

On initial launch:

```csharp
var layout = UpdaterWorkspaceFactory.GetLayout(
    UpdaterWorkspaceFactory.IsProcessElevated(),
    Guid.NewGuid());
var workspace = UpdaterWorkspaceFactory.Create(layout);
CopyUpdaterPayload(currentDirectory, workspace.UpdateDirectory, installationDirectory);
LaunchUpdater(
    copiedUpdaterPath,
    workspace.UpdateDirectory,
    installationDirectory,
    workspace.UpdateDirectory);
```

Add both paths to `ArgumentList`. On the second stage, use `args[1]` and run
`UpdateInstaller.ValidateDirectoryLayout` before network or filesystem
mutation. When elevated, validate the transaction root's marker, ACL, owner,
and non-reparse state.

- [ ] **Step 4: Run updater tests GREEN**

```powershell
dotnet test Testing/AssetEditorUpdaterTests/AssetEditorUpdaterTests.csproj -c Release --no-restore
```

### Task 3: Copy and verify the updater payload

**Files:**

- Create: `AssetEditorUpdater/UpdaterPayloadCopier.cs`
- Create: `Testing/AssetEditorUpdaterTests/UpdaterPayloadCopierTests.cs`
- Modify: `AssetEditorUpdater/AssetEditorUpdater.cs`

**Interfaces:**

- Produces:

```csharp
internal static class UpdaterPayloadCopier
{
    internal static IReadOnlyDictionary<string, string> CopyAndVerify(
        string sourceDirectory,
        string destinationDirectory);

    internal static void Verify(
        string sourceDirectory,
        string destinationDirectory,
        IReadOnlyDictionary<string, string> expectedHashes);
}
```

Hash strings are uppercase hexadecimal SHA-256 values keyed by normalized
relative path.

- [ ] **Step 1: Add copy and tamper RED tests**

Create a source tree containing the updater executable and a dependency. Assert
the destination bytes and manifest hashes match. Then modify, remove, and add
destination files in separate tests and assert `Verify` throws
`InvalidDataException`.

- [ ] **Step 2: Run and confirm RED**

```powershell
dotnet test Testing/AssetEditorUpdaterTests/AssetEditorUpdaterTests.csproj -c Release --no-restore --filter "FullyQualifiedName~UpdaterPayloadCopierTests"
```

- [ ] **Step 3: Implement exclusive copy and verification**

For each source file:

```csharp
using var source = new FileStream(
    sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);
using var destination = new FileStream(
    destinationPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
source.CopyTo(destination);
destination.Flush(flushToDisk: true);
```

Compute SHA-256 from a rewound source stream and from the completed destination
under exclusive or protected access. Normalize relative paths with
`Path.GetRelativePath`. Reject reparse points in either tree.

- [ ] **Step 4: Launch only after verification**

Replace the updater payload's generic directory copy with
`UpdaterPayloadCopier.CopyAndVerify`. Do not call `Process.Start` until the
method returns successfully and `UpdaterWorkspaceSecurity` has revalidated a
protected workspace when elevated.

- [ ] **Step 5: Run and confirm GREEN**

Run all updater tests.

### Task 4: Keep archive, staging, backup, and cleanup inside the trust boundary

**Files:**

- Modify: `AssetEditorUpdater/UpdateInstaller.cs`
- Modify: `AssetEditorUpdater/AssetEditorUpdater.cs`
- Modify: `Testing/AssetEditorUpdaterTests/UpdateInstallerTests.cs`
- Modify: `Testing/AssetEditorUpdaterTests/UpdaterWorkspaceTests.cs`

**Interfaces:**

- Consumes: explicit `updateDirectory`
- Produces:

```csharp
internal static void ValidateOwnedTransactionRoot(
    string updateDirectory,
    bool requireProtectedAcl);
```

- [ ] **Step 1: Add unsafe-root RED tests**

Cover an update root that:

- Overlaps the installation directory.
- Has no ownership marker.
- Is a directory reparse point.
- Uses an invalid protected descriptor when protected mode is required.

Each must fail before `BackupInstallation` or `ClearDirectory`.

- [ ] **Step 2: Run and confirm RED**

Run:

```powershell
dotnet test Testing/AssetEditorUpdaterTests/AssetEditorUpdaterTests.csproj -c Release --no-restore --filter "FullyQualifiedName~UpdaterWorkspaceTests|FullyQualifiedName~UpdateInstallerTests"
```

- [ ] **Step 3: Mark and validate transaction ownership**

Create a constant marker file inside the random transaction root with
`FileMode.CreateNew` and `FileShare.None`. Validate the marker contents,
canonical root, non-overlap, non-reparse status, and protected ACL before
extraction, installation, rollback, or cleanup.

The archive download already uses `FileShare.None`; retain that behavior and
ensure its path is under the explicit update directory. `staging` and
`UpdateBackups` must remain siblings within the validated transaction root.

- [ ] **Step 4: Restrict cleanup**

Cleanup must enumerate only the fixed protected `UpdaterTransactions` parent.
Before deleting a child, validate its GUID name, marker, canonical containment,
owner, ACL, and non-reparse status. Invalid children are logged and preserved.

- [ ] **Step 5: Run all updater tests GREEN**

```powershell
dotnet test Testing/AssetEditorUpdaterTests/AssetEditorUpdaterTests.csproj -c Release --no-restore
```

- [ ] **Step 6: Commit updater hardening**

```powershell
git add AssetEditorUpdater Testing/AssetEditorUpdaterTests
git commit -m "fix: protect elevated updater workspace"
```

### Task 5: Define bounded IPC options and frame reading

**Files:**

- Modify: `Editors/Ipc/IpcEditor/AssetEditorIpcServer.cs`
- Modify: `Editors/Ipc/Test.Ipc/AssetEditorIpcServerTests.cs`

**Interfaces:**

- Produces:

```csharp
internal sealed record AssetEditorIpcServerOptions(
    TimeSpan RetryDelay,
    TimeSpan ReadTimeout,
    TimeSpan WriteTimeout,
    int MaxRequestChars)
{
    internal static AssetEditorIpcServerOptions Default { get; } =
        new(
            TimeSpan.FromMilliseconds(500),
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(5),
            64 * 1024);
}
```

- Produces internal bounded reader:

```csharp
internal static Task<string?> ReadBoundedLineAsync(
    TextReader reader,
    int maxChars,
    CancellationToken cancellationToken);
```

- [ ] **Step 1: Add bounded-reader RED tests**

Use `StringReader` and a custom blocking `TextReader` to cover:

- Valid line with CRLF.
- Exactly 64 KiB.
- 64 KiB plus one character throws `InvalidDataException`.
- Cancellation interrupts a blocked read.

- [ ] **Step 2: Run and confirm RED**

```powershell
dotnet test Editors/Ipc/Test.Ipc/Test.Ipc.csproj -c Release --no-restore --filter "FullyQualifiedName~AssetEditorIpcServerTests"
```

- [ ] **Step 3: Implement chunked bounded reading**

Read fixed-size character blocks, append only through the first newline, strip
one trailing carriage return, and throw immediately once accumulated content
exceeds `maxChars`. Check cancellation on every read.

- [ ] **Step 4: Run bounded-reader tests GREEN**

Run the Task 5 command.

### Task 6: Add retry backoff, deadlines, recovery, and disposal

**Files:**

- Modify: `Editors/Ipc/IpcEditor/AssetEditorIpcServer.cs`
- Modify: `Editors/Ipc/Test.Ipc/AssetEditorIpcServerTests.cs`

**Interfaces:**

- Internal constructor consumes:

```csharp
AssetEditorIpcServerOptions options,
Func<NamedPipeServerStream> pipeFactory,
Func<TimeSpan, CancellationToken, Task> delayAsync
```

- The public constructor keeps the existing dependency-injection signature and
  supplies production defaults.

- [ ] **Step 1: Add bind-conflict RED test**

Use a factory that increments a counter and throws `IOException`. Use a delay
task controlled by a `TaskCompletionSource`. Start the server, wait for the
first delay call, and assert the factory count remains one until the delay is
released.

- [ ] **Step 2: Add real silent-client recovery RED test**

Use short injected deadlines and a unique pipe name or injectable factory.
Connect client one without writing a newline. After the read deadline, connect
client two, send a valid request, and assert it receives a valid response.

- [ ] **Step 3: Add oversize and disposal RED tests**

Assert an oversized client is rejected and a subsequent valid client succeeds.
Dispose while a client is connected but silent and assert disposal completes
within one second under short test options.

- [ ] **Step 4: Run and confirm RED**

Run the Task 5 command. The current loop must hot-retry or hold the first
client.

- [ ] **Step 5: Implement bounded server behavior**

Create pipes with:

```csharp
PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly
```

On bind or loop failure, await the injected cancellation-aware delay. Log the
first failure and then at most once per ten consecutive identical failures;
reset the count after a successful connection.

For request and response phases, create separate linked token sources and call
`CancelAfter` with the configured deadlines. Use `ReadBoundedLineAsync`,
`WriteLineAsync(ReadOnlyMemory<char>, CancellationToken)`, and
`FlushAsync(CancellationToken)`.

- [ ] **Step 6: Run IPC tests GREEN**

```powershell
dotnet test Editors/Ipc/Test.Ipc/Test.Ipc.csproj -c Release --no-restore
```

- [ ] **Step 7: Commit IPC hardening**

```powershell
git add Editors/Ipc/IpcEditor/AssetEditorIpcServer.cs Editors/Ipc/Test.Ipc/AssetEditorIpcServerTests.cs
git commit -m "fix: bound IPC server resource use"
```

### Task 7: Verify updater and IPC hardening

- [ ] **Step 1: Run owning projects**

```powershell
dotnet test Testing/AssetEditorUpdaterTests/AssetEditorUpdaterTests.csproj -c Release --no-restore
dotnet test Editors/Ipc/Test.Ipc/Test.Ipc.csproj -c Release --no-restore
```

- [ ] **Step 2: Run static scope checks**

```powershell
rg -n "LocalApplicationData|CommonApplicationData|Process.Start|CurrentUserOnly|64 \\* 1024|FromSeconds\\(5\\)" AssetEditorUpdater Editors/Ipc
git diff master...HEAD --check
```

- [ ] **Step 3: Perform Windows privilege-boundary verification**

Verify:

- A writable portable install updates without UAC and uses LocalAppData.
- A Program Files install triggers UAC and uses only the protected
  CommonApplicationData transaction root.
- A medium-integrity process cannot modify the protected transaction payload.
- The updated application restarts without administrator privileges.
- Repeated bind conflict does not consume a CPU core or flood logs.
