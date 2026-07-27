# P1 Pack Reliability Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make Pack path lookup identity-safe, commit serialized sources only after file replacement succeeds, and preserve unsupported animation payloads byte for byte.

**Architecture:** Keep path ownership in `PackFileService`, make `PackFileSerializerWriter` return an explicit pending commit, and keep unknown-format fallbacks inside their current parsers. No binary format changes or repository-wide transaction abstraction are introduced.

**Tech Stack:** C#, .NET 10, NUnit, Moq, existing Pack and animation serializers

## Global Constraints

- Preserve all Pack, metadata, and AnimPack binary formats.
- Never resolve a detached `PackFile` by basename.
- A failed Pack save must not replace any active `DataSource`, Pack path, or saved-size metadata.
- Unknown child payloads must round trip byte for byte.
- Add and observe a focused RED test before each production change.
- Do not modify unrelated warnings, nullability, formatting, or editor code.

---

## File Structure

- Modify `Shared/SharedCore/PackFiles/PackFileService.cs`: strict identity lookup and post-replacement commit.
- Modify `Shared/SharedCore/PackFiles/Serialization/PackFileSerializerWriter.cs`: build pending source assignments without mutating the model.
- Create `Testing/Shared.Core.Test/PackFiles/PackFileService_GetFullPath.cs`: duplicate-basename path tests.
- Modify `Testing/Shared.Core.Test/PackFiles/Serialization/PackFileSerializerWriterTests.cs`: serialization failure and explicit commit tests.
- Modify `Shared/GameFiles/AnimationMeta/Parsing/MetaDataFileParser.cs`: raw unknown metadata serialization.
- Create `Editors/AnimationMeta/Test.AnimationMeta/MetaDataFileParserTests.cs`: byte-exact metadata round trip.
- Modify `Shared/GameFiles/AnimationPack/AnimationPackSerializer.cs`: child-range fallback.
- Create `Testing/FileTypesTests/AnimationPackSerializerTests.cs`: failed known-child parser fallback.

### Task 1: Resolve Pack paths by object identity

**Files:**

- Create: `Testing/Shared.Core.Test/PackFiles/PackFileService_GetFullPath.cs`
- Modify: `Shared/SharedCore/PackFiles/PackFileService.cs`

**Interfaces:**

- Consumes: `PackFileContainer.FileList : Dictionary<string, PackFile>`
- Produces: unchanged `string IPackFileService.GetFullPath(PackFile file, PackFileContainer? container = null)`

- [ ] **Step 1: Add failing duplicate-basename tests**

Create tests with two distinct objects named `shared.bin`:

```csharp
[Test]
public void GetFullPath_WhenEarlierSiblingHasSameBasename_ReturnsRequestedInstancePath()
{
    var earlier = PackFile.CreateFromBytes("shared.bin", [1]);
    var requested = PackFile.CreateFromBytes("shared.bin", [2]);
    var container = new PackFileContainer("test")
    {
        FileList =
        {
            ["a\\shared.bin"] = earlier,
            ["z\\shared.bin"] = requested
        }
    };
    var service = CreateService(container);

    Assert.That(service.GetFullPath(requested), Is.EqualTo("z\\shared.bin"));
}
```

Add the explicit-container, earlier-container, and detached-object cases. The
detached case must assert an exception even when exactly one stored file has
the same basename.

- [ ] **Step 2: Run the tests and confirm RED**

Run:

```powershell
dotnet test Testing/Shared.Core.Test/Test.Shared.Core.csproj -c Release --no-restore --filter "FullyQualifiedName~PackFileService_GetFullPath"
```

Expected: the sibling and earlier-container tests return the first basename
match, and the detached-object test incorrectly returns a path.

- [ ] **Step 3: Remove basename fallback**

Use exact identity in both branches:

```csharp
var result = container.FileList
    .FirstOrDefault(item => ReferenceEquals(item.Value, file))
    .Key;
```

For a global search, continue through all containers until the exact object is
found. Preserve the existing exception when no exact object exists.

- [ ] **Step 4: Run the tests and confirm GREEN**

Run the Task 1 command again. All path tests must pass.

- [ ] **Step 5: Commit the isolated path fix**

```powershell
git add Shared/SharedCore/PackFiles/PackFileService.cs Testing/Shared.Core.Test/PackFiles/PackFileService_GetFullPath.cs
git commit -m "fix: resolve Pack paths by identity"
```

### Task 2: Defer Pack `DataSource` mutation until commit

**Files:**

- Modify: `Shared/SharedCore/PackFiles/Serialization/PackFileSerializerWriter.cs`
- Modify: `Shared/SharedCore/PackFiles/PackFileService.cs`
- Modify: `Testing/Shared.Core.Test/PackFiles/Serialization/PackFileSerializerWriterTests.cs`
- Modify: `Testing/Shared.Core.Test/PackFiles/PackConcurrencyTests.cs`

**Interfaces:**

- Produces:

```csharp
internal sealed record PendingDataSourceUpdate(
    PackFile PackFile,
    PackedFileSource DataSource);

internal sealed class PackFileSerializationResult
{
    internal IReadOnlyList<PendingDataSourceUpdate> Updates { get; }
    internal void Commit();
}

internal static PackFileSerializationResult SaveToByteArray(
    string outputFileName,
    PackFileContainer container,
    BinaryWriter writer,
    GameInformation currentGameInformation,
    bool enableCompression = true);
```

- [ ] **Step 1: Add a deterministic failing serialization test**

Create a sorted two-file container. The first file uses `MemorySource`; the
second uses a `PackedFileSource` whose parent points to a missing file:

```csharp
var firstSource = new MemorySource([1, 2, 3]);
var missingSource = new PackedFileSource(
    new PackedFileSourceParent { FilePath = missingPath },
    0,
    4,
    false,
    false,
    CompressionFormat.None,
    0);
var first = new PackFile("first.bin", firstSource);
var second = new PackFile("second.bin", missingSource);
container.FileList["a\\first.bin"] = first;
container.FileList["z\\second.bin"] = second;

Assert.Throws<IOException>(() =>
    PackFileSerializerWriter.SaveToByteArray(
        outputPath, container, writer, gameInfo, false));
Assert.That(first.DataSource, Is.SameAs(firstSource));
Assert.That(second.DataSource, Is.SameAs(missingSource));
```

Assert the concrete `FileNotFoundException` produced by the missing parent
path.

- [ ] **Step 2: Run the failure test and confirm RED**

Run:

```powershell
dotnet test Testing/Shared.Core.Test/Test.Shared.Core.csproj -c Release --no-restore --filter "FullyQualifiedName~PackFileSerializerWriterTests"
```

Expected: the first file's `DataSource` has already been replaced before the
second file throws.

- [ ] **Step 3: Return pending source assignments**

Create one `PackedFileSourceParent` for the output file. In
`SerializeFileBlob`, append `PendingDataSourceUpdate` values rather than
assigning `packFile.DataSource`:

```csharp
pendingUpdates.Add(new PendingDataSourceUpdate(
    packFile,
    new PackedFileSource(
        outputParent,
        offset,
        data.Length,
        false,
        shouldCompress,
        fileMetaData.CompressionInfo.IntendedCompressionFormat,
        uncompressedSize)));
```

`PackFileSerializationResult.Commit()` must contain only non-throwing property
assignments:

```csharp
internal void Commit()
{
    foreach (var update in Updates)
        update.PackFile.DataSource = update.DataSource;
}
```

- [ ] **Step 4: Add and run explicit commit tests**

Add a successful serialization test that asserts sources remain unchanged
before `result.Commit()` and become `PackedFileSource` instances after it.
Run the Task 2 test command and confirm GREEN.

- [ ] **Step 5: Move commit ownership into `PackFileService`**

Capture the result while the temporary stream is open:

```csharp
PackFileSerializationResult serializationResult;
using (var stream = new FileStream(tempPath, FileMode.CreateNew))
{
    using var writer = new BinaryWriter(stream);
    serializationResult = PackFileSerializerWriter.SaveToByteArray(
        path, pf, writer, gameInformation, useCompression);
}
```

Keep original sources installed while closing their shared parent streams and
performing `File.Move(tempPath, path, true)`. Immediately after a successful
move:

```csharp
serializationResult.Commit();
pf.SystemFilePath = path;
pf.OriginalLoadByteSize = new FileInfo(path).Length;
```

- [ ] **Step 6: Add final-replacement failure coverage**

Create a directory at the requested destination file path. Serialization to
the sibling GUID temporary path will succeed, while
`File.Move(tempPath, destinationDirectoryPath, true)` deterministically throws
`IOException`. Assert every original `DataSource`, `SystemFilePath`, and
`OriginalLoadByteSize` remains unchanged. Do not add a production filesystem
abstraction for this test.

- [ ] **Step 7: Run Pack tests and confirm GREEN**

Run:

```powershell
dotnet test Testing/Shared.Core.Test/Test.Shared.Core.csproj -c Release --no-restore --filter "FullyQualifiedName~PackFileSerializerWriterTests|FullyQualifiedName~PackConcurrencyTests"
```

- [ ] **Step 8: Commit the transactional save fix**

```powershell
git add Shared/SharedCore/PackFiles/Serialization/PackFileSerializerWriter.cs Shared/SharedCore/PackFiles/PackFileService.cs Testing/Shared.Core.Test/PackFiles
git commit -m "fix: commit Pack sources after file replacement"
```

### Task 3: Preserve unknown metadata bytes

**Files:**

- Create: `Editors/AnimationMeta/Test.AnimationMeta/MetaDataFileParserTests.cs`
- Modify: `Shared/GameFiles/AnimationMeta/Parsing/MetaDataFileParser.cs`

**Interfaces:**

- Consumes: `ParsedUnknownMetadataAttribute.Data`
- Produces: unchanged `byte[]? MetaDataFileParser.Serialize(ParsedMetadataAttribute entry, out string? errorMessage)`

- [ ] **Step 1: Add a byte-exact unknown metadata round-trip test**

Build a version-2 metadata file containing one unknown uppercase CA tag, its
attribute version, and opaque payload:

```csharp
var original = BuildMetadataFile(
    "CODEX_UNKNOWN_TAG",
    attributeVersion: 77,
    payload: [0x10, 0x20, 0x30, 0x40]);
var parsed = parser.ParseFile(original);
var written = parser.GenerateBytes(parsed.Version, parsed);

Assert.That(written, Is.EqualTo(original));
```

The helper must use `ByteParsers.String.WriteCaString` so the fixture follows
the real format.

- [ ] **Step 2: Run the test and confirm RED**

Run:

```powershell
dotnet test Editors/AnimationMeta/Test.AnimationMeta/Test.AnimationMeta.csproj -c Release --no-restore --filter "FullyQualifiedName~MetaDataFileParserTests"
```

Expected: the written attribute contains its version but loses the opaque
payload.

- [ ] **Step 3: Serialize raw unknown data directly**

Place the special case at the public serializer boundary:

```csharp
if (entry is ParsedUnknownMetadataAttribute unknown)
{
    if (unknown.Data == null)
        throw new InvalidDataException("Unknown metadata is missing its raw payload.");

    errorMessage = null;
    return unknown.Data.ToArray();
}
```

Known derived metadata must continue through the existing reflected property
serializer.

- [ ] **Step 4: Run the test and confirm GREEN**

Run the Task 3 command again.

### Task 4: Preserve only the failed AnimPack child's bytes

**Files:**

- Create: `Testing/FileTypesTests/AnimationPackSerializerTests.cs`
- Modify: `Shared/GameFiles/AnimationPack/AnimationPackSerializer.cs`

**Interfaces:**

- Consumes: `AnimationEntryMetaData.StartOffset` and `.Size`
- Produces: unchanged `AnimationPackFileDatabase AnimationPackSerializer.Load(...)`

- [ ] **Step 1: Add a failing known-child fallback test**

Create an AnimPack with:

- A child name that selects a known serializer.
- Deliberately invalid bytes for that serializer.
- A second sentinel child.

After loading, assert the first child is `UnknownAnimFile` and:

```csharp
Assert.That(fallback.ToByteArray(), Is.EqualTo(invalidChildPayload));
Assert.That(fallback.ToByteArray(), Is.Not.EqualTo(parentPackBytes));
Assert.That(AnimationPackSerializer.ConvertToBytes(loaded), Is.EqualTo(parentPackBytes));
```

- [ ] **Step 2: Run the test and confirm RED**

Run:

```powershell
dotnet test Testing/FileTypesTests/FileTypesTests.csproj -c Release --no-restore --filter "FullyQualifiedName~AnimationPackSerializerTests"
```

Expected: the fallback contains the full parent buffer and repacking expands
or changes the parent.

- [ ] **Step 3: Slice the current child**

Replace the catch fallback with:

```csharp
return new UnknownAnimFile(
    animationInfoDataFile.Name,
    data.GetBytesFromBuffer(
        animationInfoDataFile.StartOffset,
        animationInfoDataFile.Size));
```

- [ ] **Step 4: Run metadata and AnimPack tests**

Run the Task 3 and Task 4 commands. Both must pass.

- [ ] **Step 5: Commit unknown-format preservation**

```powershell
git add Shared/GameFiles/AnimationMeta/Parsing/MetaDataFileParser.cs Shared/GameFiles/AnimationPack/AnimationPackSerializer.cs Editors/AnimationMeta/Test.AnimationMeta/MetaDataFileParserTests.cs Testing/FileTypesTests/AnimationPackSerializerTests.cs
git commit -m "fix: preserve unknown animation payloads"
```

### Task 5: Verify the Pack reliability batch

- [ ] **Step 1: Run all owning projects**

```powershell
dotnet test Testing/Shared.Core.Test/Test.Shared.Core.csproj -c Release --no-restore
dotnet test Editors/AnimationMeta/Test.AnimationMeta/Test.AnimationMeta.csproj -c Release --no-restore
dotnet test Testing/FileTypesTests/FileTypesTests.csproj -c Release --no-restore
```

- [ ] **Step 2: Run formatting and scope checks**

```powershell
git diff master...HEAD --check
git status --short
```

- [ ] **Step 3: Review invariants**

Confirm from tests and diff:

- No basename fallback remains in `GetFullPath`.
- No serializer loop assigns an active `DataSource`.
- Only the post-`File.Move` path calls `PackFileSerializationResult.Commit`.
- Unknown format tests compare full byte arrays, not only lengths.
