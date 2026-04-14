# DFS Tool

DFS Tool is a command-line interface (CLI) utility for working with Area 51's DFS (Data File System) archive format. The DFS format is used to store game assets.

This C# implementation is based on the original C++ implementation by Inevitable Entertainment Inc, and attempts to 100% match the original implementation's behaviour, and complete features that were not implemented in the original implementation.

## Features

- List the contents of a DFS archive
- Extract files from a DFS archive
- Create a new DFS archive from a directory of files
- Verify the integrity of a DFS archive

## Requirements

- .NET 9 or later

## Installation

1. Clone the repository:
```bash
git clone https://github.com/ProjectDreamland/dfs-tool.git
```
2. Build the project:
```bash
cd dfs-tool
dotnet build
```


## Usage

```bash
Usage: dfs <command> [options]
Commands:
  create <inputDir> <outputFileName> [--crc] [--base-path <path>]    Creates a DFS file from the specified directory with optional CRC and base path.
  extract <inputFile> <extractPath>             Extracts files from the specified DFS file to the specified path.
  verify <inputFile>                            Verifies the integrity of the specified DFS file.
  list <inputFile>                              Lists the contents of the specified DFS file.
  help                                          Displays this help text.
```

### Remastering Area 51 assets

The original Area 51 DFS archives store an absolute Windows path (e.g. `C:\GAMEDATA\A51\RELEASE\PC\`) for every file. The game uses this path to locate assets inside the archive, so the packed archive must use the same path.

To repack a modified archive while preserving the original path:

1. Find the base path stored in the original archive:
   ```bash
   dfs list <original.dfs>
   ```
   The path shown before each filename is the base path (e.g. `C:\GAMEDATA\A51\RELEASE\PC\`).

2. Extract the original archive:
   ```bash
   dfs extract <original.dfs> <outputDir>
   ```

3. Replace or modify the files in `<outputDir>`.

4. Repack with the original base path:
   ```bash
   dfs create <outputDir> <repacked.dfs> --base-path "C:\GAMEDATA\A51\RELEASE\PC\"
   ```