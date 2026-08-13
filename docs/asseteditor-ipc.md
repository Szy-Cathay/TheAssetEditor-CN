# Asset Editor 国区版 IPC

This document describes the current IPC endpoint implemented by `AssetEditor`.

## Status
- Transport: Windows named pipe
- Pipe name: `AssetEditor.CN.Ipc`
- Protocol: JSON line per request (`UTF-8`, newline-terminated)
- Access: current Windows user only
- Current supported actions: `open` only

## Pipe Path (Windows)
- `\\.\pipe\AssetEditor.CN.Ipc`

The pipe uses Windows `CurrentUserOnly` access. Clients must run as the same
Windows user and in a compatible elevation context as Asset Editor.

## Request Format
Send one JSON object followed by a newline.

The request content limit is 65,536 decoded .NET `char` values. This is a
UTF-16 character limit, not a UTF-8 wire-byte limit. LF terminates the request;
an immediately preceding CR is treated as part of CRLF and is not content.
Clients must send a newline, although a bounded partial request at EOF retains
legacy compatibility.

Reading a request and writing its response each have independent five-second
deadlines. A silent, oversized, or timed-out connection is closed and may not
receive a response; the server then accepts the next client.

### Supported action: `open`
```json
{"action":"open","path":"variantmeshes/wh_variantmodels/.../file.rigid_model_v2"}
```

### Request fields
- `action` (required): currently only `"open"`
- `path` (required): pack-internal file path to open
- `bringToFront` (optional, default `true`): bring AssetEditor window to front
- `packPathOnDisk` (optional): disk path to a `.pack`; AssetEditor shows the same Chinese purpose choice used by startup/file-association arguments before opening `path`
- `openInExistingKitbashTab` (optional, default `false`): if `true`, and a Kitbash tab exists, import supported files into that tab instead of opening a new tab

## Open Behavior by File Type
- `.rigid_model_v2`: normal open flow (or import into existing Kitbash tab if `openInExistingKitbashTab=true`)
- `.wsmodel`: forced to open in Kitbash Editor
- `.variantmeshdefinition`: forced to open in Kitbash Editor and imported as a reference on open

## Path Handling
- Forward slashes and backslashes are accepted
- Repeated backslashes are collapsed
- Absolute paths are accepted if they contain a known pack root such as `variantmeshes\`; AssetEditor extracts the pack-relative suffix

## External Pack Choice
- If `packPathOnDisk` is supplied, AssetEditor asks the user to choose `作为参考打开` (read-only reference) or `导入为工程` (folder project). There is no silent editable-Pack mode and no role boolean in the IPC protocol.
- Reference mode adds only a read-only reference and does not replace the current folder-project workspace.
- Import mode reuses the normal folder-project setup, path-safety, progress, rollback, and local-Git initialization flow.
- Canceling either dialog or failing the selected operation stops the request before resource lookup/opening. The current workspace and loaded containers remain unchanged.
- The disk path is normalized using Windows case-insensitive path semantics. Repeating a path in the same role reuses the existing container; requesting another role produces a Chinese conflict message and stops safely.
- Only the single `packPathOnDisk` value is processed, and the named-pipe server retains the bounded request/read/write behavior and `AssetEditor.CN.Ipc` identity described above.

## Response Format
AssetEditor returns one JSON response line and closes the connection.

### Success
```json
{"ok":true}
```

### Failure
```json
{"ok":false,"error":"File not found","normalizedPath":"variantmeshes\\..."}
```

## Examples
Open from already-loaded packs:
```json
{"action":"open","path":"variantmeshes/wh_variantmodels/bi1/cth/cth_great_moon_bird/cth_great_moon_bird_body_01.rigid_model_v2"}
```

Open from a mod pack on disk (the user chooses reference or project import):
```json
{"action":"open","path":"variantmeshes/wh_variantmodels/el1/arb/arb_new_elephants/arb_base_elephant/arb_base_elephant.rigid_model_v2","packPathOnDisk":"k:/SteamLibrary/steamapps/common/Total War WARHAMMER III/data/ovn_araby.pack"}
```

Reuse an existing Kitbash tab:
```json
{"action":"open","path":"variantmeshes/wh_variantmodels/el1/arb/ane/abe/arb_base_elephant_1.wsmodel","packPathOnDisk":"k:/SteamLibrary/steamapps/common/Total War WARHAMMER III/data/ovn_araby.pack","openInExistingKitbashTab":true}
```
