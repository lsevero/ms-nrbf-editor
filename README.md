# MS-NRBF Editor

F# tools for reading, editing and writing **MS-NRBF** files — the binary
serialization format used by .NET `BinaryFormatter`.

The project was originally built around a *River City Girls* save file, but the
reader, writer and JSON conversion work against the generic MS-NRBF object
model.

This repository contains:

- **`gui/`** — a small Avalonia desktop editor.
- **`cli/`** — a command-line converter from MS-NRBF to JSON.
- **`core/`** — the shared reader/writer and JSON conversion logic.

The save's root object is typically a `System.Collections.Hashtable`. Its values
can be primitives, strings, arrays, `List<T>` instances, enums and custom
classes. The code implements MS-NRBF directly and does **not** use
`BinaryFormatter` (which is disabled in modern .NET runtimes).

## Build

```bash
dotnet build
```

or:

```bash
make build
```

## GUI editor

```bash
dotnet run --project gui/MSNRBFEditor.Gui.fsproj -- savefile.sav
```

or:

```bash
make gui SAVE=savefile.sav
```

The GUI shows the save keys on the left. Selecting a key displays its value as
pretty-printed JSON on the right. Edit the JSON and use **Apply** to validate it
against the loaded graph, or edit several values and use **Save As…**.

Important behavior:

- **The original file is never overwritten.** The Save As dialog suggests
  `<name>_edited.sav`, and the code refuses to write to the exact path that was
  opened.
- Every save starts from a fresh parse of the original bytes, applies all editor
  drafts, serializes the object graph back to MS-NRBF, validates the generated
  stream by parsing it again, and only then writes the new file.
- List and array lengths must stay the same while editing. Adding/removing save
  keys is not supported. Scalar values, strings, booleans, enum values and
  existing list elements can be changed.

## CLI converter

```bash
dotnet run --project cli/MSNRBFEditor.Cli.fsproj -- savefile.sav savefile.json
```

Compact output to stdout:

```bash
dotnet run --project cli/MSNRBFEditor.Cli.fsproj -- savefile.sav --compact
```

or:

```bash
make cli SAVE=savefile.sav JSON=savefile.json
```

Usage:

```
ms-nrbf-editor <savefile.sav> [output.json] [--compact]
```

Run `make help` to list all Makefile targets.

### Self-contained binaries

Self-contained single-file executables can be cross-published from Linux:

```bash
make publish-win                 # CLI + GUI, win-x64
make publish-win-cli             # CLI only
make publish-win-gui             # GUI only

make publish-win WIN_RID=win-arm64

make publish-linux               # CLI + GUI, linux-x64
make publish-linux-cli           # CLI only
make publish-linux-gui           # GUI only

make publish-linux LINUX_RID=linux-arm64
```

Outputs go to:

```
dist/win-x64-cli/
dist/win-x64-gui/
dist/linux-x64-cli/
dist/linux-x64-gui/
```

These are not statically linked in the Go/Rust sense: the .NET runtime is
bundled, but OS libraries are still loaded dynamically. Validate the Windows
GUI build on Windows or in Windows CI; Wine is not a reliable substitute.

## Conversion rules

| Serialized .NET value | JSON representation |
| --- | --- |
| `System.Collections.Hashtable` | object (`key -> value` pairs from its `Keys`/`Values` arrays) |
| `System.Collections.Generic.List<T>` | array (only the first `_size` elements of `_items`) |
| boxed enum (class with a single `value__` member) | its numeric value |
| arrays | arrays (multi-dimensional arrays become nested arrays) |
| `System.String` | string |
| primitive values | number / boolean / string |
| custom class | object with its fields |

Formatting details:

- `Single` values are written with the shortest round-trippable representation.
- `Decimal` is written as a JSON number.
- `DateTime` and `TimeSpan` are written as ISO-8601 strings.
- Cyclic references, if present, are emitted as `{ "$ref": <object id> }` to
  keep the output finite.

## Format notes

The MS-NRBF records implemented here include:

- `SerializationHeaderRecord`
- `ClassWithId`, `SystemClassWithMembers`, `ClassWithMembers`,
  `SystemClassWithMembersAndTypes`, `ClassWithMembersAndTypes`
- `BinaryObjectString`, `BinaryArray`
- `MemberPrimitiveTyped`, `MemberReference`, `ObjectNull`
- `ObjectNullMultiple256`, `ObjectNullMultiple`
- `ArraySinglePrimitive`, `ArraySingleObject`, `ArraySingleString`
- `BinaryLibrary`, `MessageEnd`

One detail that matters for many real saves: `ObjectNullMultiple256` /
`ObjectNullMultiple` compact runs of nulls inside arrays. A naive
one-record-per-element reader desynchronizes on these records.

## Project layout

```
core/Core.fs                    MS-NRBF reader/writer, JSON model, merge logic
core/MSNRBFEditor.Core.fsproj
cli/Program.fs                  command-line converter
cli/MSNRBFEditor.Cli.fsproj
gui/GuiProgram.fs               Avalonia editor window
gui/MSNRBFEditor.Gui.fsproj
MSNRBFEditor.slnx               solution containing all three projects
```
