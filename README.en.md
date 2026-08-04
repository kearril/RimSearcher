# RimSearcher

[![Skills Update Time](https://img.shields.io/endpoint?url=https%3A%2F%2Fkearril.github.io%2FRimSearcher%2Fskills-update.json&cacheSeconds=300)](https://github.com/kearril/RimSearcher/commits/master/skills.zip)

English | [简体中文](README.md)

> **Design philosophy**: turn the tool's errors into knowledge inputs — let the model learn from mistakes.
> Errors are documentation, failures are lessons: every limitation and failure path is designed as learning material for the model.

#### RimSearcher V3 is a full rebuild. Starting with this version, the tool abandons the old MCP architecture in favor of a skills + CLI design, which brings better performance, lower overhead, and smarter AI decisions — and it now supports source-code analysis of your mod environment!

## Introduction

RimSearcher specializes in the **Def data layer** — XML definitions, field structures, and type relationships. C# source analysis is delegated to [DecompilerServer](https://github.com/pardeike/DecompilerServer), a decompilation MCP tool built for Unity assemblies. It decompiles loaded .NET assemblies directly, offering type search, member signature browsing, IL-level inspection, call-chain tracing, and cross-version method body comparison — letting the AI see not "maybe-existing APIs" but the code that actually runs. As its design goal states: *"I can inspect the actual code that runs"*.

The Skill file ties both together: CLI locates the Def → extracts C# type names → DecompilerServer reads the source, forming a complete analysis pipeline.

Support for multi-mod environments comes from two layers working together: DecompilerServer can load the vanilla game and any mod's `.dll` assemblies side by side, each with its own context alias, so the AI can inspect source and IL of multiple assemblies in parallel to pinpoint hooks and compatibility boundaries. Meanwhile, RimSearcher's DataMod exports the current mod environment's Def data to a SQLite database in-game, and the CLI provides full-text search over it — one handles C# source, the other handles Def-data export and querying.

## Quick Start

**Not comfortable installing it yourself?** Send the following line to your AI assistant and it will guide you through the whole installation, step by step:

> Read https://raw.githubusercontent.com/kearril/RimSearcher/master/GUIDED_SETUP.md and guide me through the installation.

---

### Manual Installation

If you're already familiar with the toolchain, follow these steps to configure it yourself.

### 1. Download

Download from [Releases](https://github.com/kearril/RimSearcher/releases/latest):

| File | Description |
|---|---|
| `rimsearcher.exe` | CLI command-line tool |
| `RimSearcher_DataMod.zip` | In-game Def data export mod |

> **Skills are not published with Releases**: always fetch the latest version directly from the repository via the "Configure AI Skills" step below.

You also need the decompilation MCP: [DecompilerServer](https://github.com/pardeike/DecompilerServer) — visit its repo and configure the MCP tool.

### 2. Install the Mod

Extract `RimSearcher_DataMod.zip` into RimWorld's `Mods/` directory. Launch the game and enable **RimSearcherDataMod** in the mod list.

### 3. Export Data

In-game: Options → Mod Settings → RimSearcherDataMod → click **Export Def database**.

When the export finishes, place the generated `defs.db` in the same directory as `rimsearcher.exe`.

### 4. Configure the CLI

Add the directory containing `rimsearcher.exe` to the system PATH (command-line pseudocode):

```bash
reg add "HKCU\Environment" /v Path /t REG_EXPAND_SZ /d "<current-Path-value>;<rimsearcher.exe-directory>" /f
```

> Must use the `REG_EXPAND_SZ` type to preserve `%VAR%` variable expansion; check the current value first with `reg query "HKCU\Environment" /v Path` if needed.

After configuring, if you move `rimsearcher.exe`, you must reconfigure the PATH.

### 5. Configure AI Skills

Download [skills.zip](https://raw.githubusercontent.com/kearril/RimSearcher/master/skills.zip) (this link always points to the latest version in the repository), extract it, and place `skills/rimsearcher/` into your AI assistant's skills directory. Restart the AI client to activate.

### 6. Done

Restart and start testing and using the tool.

> **Platform support**: Windows is the development/testing environment; the CLI and DataMod code are compatible with macOS/Linux (theoretically supported), but no Mac/Linux release artifacts are provided and they have not been tested there.

---

## Updating

| Component | How to update |
|---|---|
| **rimsearcher CLI** | Download the new `rimsearcher.exe` from [Releases](https://github.com/kearril/RimSearcher/releases/latest) and replace the old exe, then update DataMod and re-export the database |
| **rimsearcher Skill** | Download [skills.zip](https://raw.githubusercontent.com/kearril/RimSearcher/master/skills.zip), extract and overwrite the skills directory |
| **RimSearcher.DataMod** | Download the new `RimSearcher_DataMod.zip` from [Releases](https://github.com/kearril/RimSearcher/releases/latest), extract and replace the old mod, then re-export the database |

> Skills are important files that shape AI decisions and may be optimized frequently, so skills are not published with Releases. How to tell whether skills have changed? Check the badge at the top of this page ( ![Skills Update Time](https://img.shields.io/endpoint?url=https%3A%2F%2Fkearril.github.io%2FRimSearcher%2Fskills-update.json) ); it shows the last modification time of `skills.zip` in UTC+8. If it's newer than your local files, there's an update.

## Components

| Component | Description |
|---|---|
| **RimSearcher.DataMod** | In-game Def data export mod. Exports the currently loaded Def data to `defs.db`; labels and descriptions use the game's current language |
| **rimsearcher CLI** | .NET command-line tool. 9 commands: `search` `list` `get` `find` `fields` `values` `types` `mods` `check update` |
| **rimsearcher Skill** | AI assistant skill files. Teach the AI to locate and analyze RimWorld source code using the CLI + decompilation MCP, with anti-hallucination rules and data-verification instructions |

## Building

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)

### Compile

```bash
# CLI tool
dotnet publish Sources/RimSearcher.Cli/ -c Release -o Sources/RimSearcher.Cli/publish/
# Output: Sources/RimSearcher.Cli/publish/rimsearcher.exe

# DataMod mod
dotnet build Sources/RimSearcher.DataMod/ -c Release
# Output: RimSearcher_DataMod/Assemblies/RimSearcher.DataMod.dll (with dependencies)
#         RimSearcher_DataMod/Native/ (SQLite native libs, generated by the build)
```

## Contributing Skills

Contributions of your RimWorld mod development experience to the Skill repository are welcome. If you have common analysis workflows, frequent hook points, or compatibility experience with specific mods, submit a PR to extend the Skill files and make the AI assistant more knowledgeable about RimWorld — which benefits every RimSearcher user.

## Command Reference

```bash
# search — full-text search: fuzzy keyword matching (mixed CN/EN, wildcards & boolean ops; a single bare word also matches defname/label substrings; --name-only restricts to the name column)
rimsearcher search <keyword> [--type T] [--mod M] [--limit N] [--count] [--name-only]
# list — paginated browsing: list Defs by type/mod
rimsearcher list [--type T] [--mod M] [--limit N] [--offset N] [--total]
# get — precise lookup: fetch one Def by defName (--brief extracts *Class names and polymorphic $type, --field extracts one field)
rimsearcher get <defName> [--type T] [--brief] [--field <path>]
# find — reverse lookup: exact field-value match (which Defs reference a C# class)
rimsearcher find <fieldPath> <value> [--type T] [--mod M] [--limit N]
# fields — field tree: inspect one Def's full nested structure (--filter supports path glob)
rimsearcher fields <defName> --type <T> [--limit N] [--filter <glob>]
# values — value enumeration: distinct values of a field path
rimsearcher values <fieldPath> [--type T] [--limit N]
# types — def type statistics
rimsearcher types
# mods — mod statistics
rimsearcher mods
# check update — check for a newer GitHub Release
rimsearcher check update
```

### AI integration (Skill)

The Skill files teach the AI to analyze RimWorld mechanics with the CLI and the decompilation MCP along a standard pipeline, with built-in anti-hallucination rules and data-trust verification.

### DataMod — in-game export

RimSearcher.DataMod is an in-game mod that exports all Def data of the current mod environment to a SQLite database for the CLI to query; the database is version-locked to the CLI, so re-export after updating either side.

## Runtime Dependencies

- [.NET 10 Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) — runtime for the CLI and DecompilerServer
- [DecompilerServer](https://github.com/pardeike/DecompilerServer) — decompilation MCP service, required for C# source analysis

## Credits

- [DecompilerServer](https://github.com/pardeike/DecompilerServer) — powerful .NET decompilation MCP providing C# source analysis
- [RimWorld](https://rimworldgame.com) — thanks to Ludeon Studios for a wonderful game and an open mod ecosystem

## Disclaimer

RimSearcher only reads and analyzes game data installed locally on your machine. It bundles and distributes no RimWorld game files or third-party mod assets.

When analyzing mods with this tool, note that the analyzed mods are bound by their respective licenses. Derivative work based on analysis results must comply with each mod's open-source terms. Exported data may contain mod authors' creative content (Def names, description text, etc.), and the copyright belongs to the original authors.

This project is not affiliated with Ludeon Studios. RimWorld is a trademark of Ludeon Studios.

## License

MIT
