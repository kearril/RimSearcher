# RimSearcher

[![Skills Update Time](https://img.shields.io/endpoint?url=https%3A%2F%2Fkearril.github.io%2FRimSearcher%2Fskills-update.json&cacheSeconds=300)](https://github.com/kearril/RimSearcher/commits/master/skills)

English | [简体中文](README.md)

#### RimSearcher V3 is a full rebuild. Starting with this version, the tool abandons the old MCP architecture in favor of a skills + CLI design, which brings better performance, lower overhead, and smarter AI decisions — and it now supports source-code analysis of your mod environment!

## Introduction

RimSearcher specializes in the **Def data layer** — XML definitions, field structures, and type relationships. C# source analysis is delegated to [DecompilerServer](https://github.com/pardeike/DecompilerServer), a decompilation MCP tool built for Unity assemblies. It decompiles loaded .NET assemblies directly, offering type search, member signature browsing, IL-level inspection, call-chain tracing, and cross-version method body comparison — letting the AI see not "maybe-existing APIs" but the code that actually runs. As its design goal states: *"I can inspect the actual code that runs"*.

The Skill file ties both together: CLI locates the Def → extracts C# type names → DecompilerServer reads the source, forming a complete analysis pipeline.

Support for multi-mod environments comes from two layers working together: DecompilerServer can load the vanilla game and any mod's `.dll` assemblies side by side, each with its own context alias, so the AI can inspect source and IL of multiple assemblies in parallel to pinpoint hooks and compatibility boundaries. Meanwhile, RimSearcher's DataMod exports the current mod environment's Def data to a SQLite database in-game, and the CLI provides full-text search over it — one handles C#, the other handles XML data.

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

> **Skills are not published with Releases**: skill files update frequently and are independent of CLI/DataMod functionality. Always fetch the latest version directly from the repository via the "Configure AI Skills" step below (no need to wait for a Release).

You also need the decompilation MCP: [DecompilerServer](https://github.com/pardeike/DecompilerServer) — visit its repo and configure the MCP tool.

### 2. Install the Mod

Extract `RimSearcher_DataMod.zip` into RimWorld's `Mods/` directory. Launch the game and enable **RimSearcherDataMod** in the mod list.

### 3. Export Data

In-game: Options → Mod Settings → RimSearcherDataMod → click **Export Def database**.

When the export finishes, place the generated `defs.db` in the same directory as `rimsearcher.exe`.

### 4. Configure the CLI

Open a terminal in the directory containing `rimsearcher.exe` and run:

```bash
rimsearcher install
```

After this step, don't move the exe file — the system's PATH entry would point to the old location. If you do move it, just run the command again.

### 5. Configure AI Skills

Download [skills.zip](https://raw.githubusercontent.com/kearril/RimSearcher/master/skills.zip) (this link always points to the latest version in the repository, independent of Releases), extract it, and place `skills/rimsearcher/` into your AI assistant's skills directory. Restart the AI client to activate.

### 6. Done

Restart and start testing and using the tool.

---

## Updating

| Component | How to update |
|---|---|
| **rimsearcher CLI** | When a Release is available, run `rimsearcher update` in a terminal — it downloads the latest version from the GitHub Release and replaces the current exe |
| **rimsearcher Skill** | Download [skills.zip](https://raw.githubusercontent.com/kearril/RimSearcher/master/skills.zip), extract and overwrite the skills directory. Skills are **not published with Releases** — always fetch from the repository, so updates land right after each push |
| **RimSearcher.DataMod** | Download the new `RimSearcher_DataMod.zip` from [Releases](https://github.com/kearril/RimSearcher/releases/latest) and extract over the Mods directory |

> Skills are important files that shape AI decisions and may be optimized frequently, and updating them never affects CLI or DataMod functionality — so skills don't get a Release on every update. How to tell whether skills have changed? Check the badge at the top of this page ( ![Skills Update Time](https://img.shields.io/endpoint?url=https%3A%2F%2Fkearril.github.io%2FRimSearcher%2Fskills-update.json) ); it shows the last modification time of `skills.zip` in UTC+8. If it's newer than your local files, there's an update.

## Components

| Component | Description |
|---|---|
| **RimSearcher.DataMod** | In-game reflection export mod. Exports the currently loaded Def data to `defs.db` at runtime; labels and descriptions use the game's current language. The in-game UI supports English, Simplified Chinese and Traditional Chinese; Windows only |
| **rimsearcher CLI** | .NET command-line tool. 10 commands: `search` `list` `get` `find` `fields` `values` `types` `mods` `install` `update` |
| **rimsearcher Skill** | AI assistant skill files. Teach the AI to locate and analyze RimWorld source code using the CLI + decompilation MCP, with anti-hallucination rules |

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
#         RimSearcher_DataMod/Native/ (cross-platform SQLite native libs, generated by the build)
```

## Contributing Skills

Contributions of your RimWorld mod development experience to the Skill repository are welcome. If you have common analysis workflows, frequent hook points, or compatibility experience with specific mods, submit a PR to extend the Skill files and make the AI assistant more knowledgeable about RimWorld.

> The RimSearcher-specific skills need continuous refinement to cover more development scenarios.

## Command Reference

### search — full-text search

```
rimsearcher search <keyword> [--type T] [--mod M] [--limit N] [--count]
```

FTS5 full-text index covers Def names, labels, descriptions and all field values. Mixed Chinese/English queries, prefix wildcards and boolean combinations supported.

### list — paginated browsing

```
rimsearcher list [--type T] [--mod M] [--limit N] [--offset N]
```

Browse Defs by type or mod with pagination. No search overhead; sorted by def_type, def_name.

### get — precise lookup

```
rimsearcher get <defName> [--type T] [--brief]
```

Locate a Def by name. `--brief` extracts all `*Class` bridge field values from the Def (`thingClass`, `compClass`, `workerClass`, `hediffClass`, etc., type-independent) as entry points for the decompilation MCP. When multiple types match, candidates are listed.

### find — reverse lookup

```
rimsearcher find <fieldPath> <value> [--type T] [--mod M] [--limit N]
```

Given a field path and a C# class name, find all Defs referencing that class — ideal for tracing which items use a given Comp or ThingClass.

### fields — field tree

```
rimsearcher fields <defName> --type <T> [--limit N]
```

Show the complete field tree of a single Def for inspecting nested structures.

### values — value enumeration

```
rimsearcher values <fieldPath> [--limit N]
```

Enumerate distinct values for any field path.

### types — type statistics

```
rimsearcher types
```

List all Def types with counts, descending.

### mods — mod statistics

```
rimsearcher mods
```

List all mods with their Def counts, descending.

### install — add to PATH

```
rimsearcher install
```

Adds the rimsearcher directory to the user PATH for global use. Skipped automatically when already present.

### update — self-update

```
rimsearcher update
```

Downloads the latest version from the GitHub Release and replaces the current executable.

### AI integration (Skill)

The Skill files define the standard analysis pipeline; once loaded, the AI follows it automatically to locate source code. Built-in anti-hallucination rules: never guess APIs; read the target method's IL before writing a Harmony patch.

### DataMod — in-game export

RimSearcher.DataMod is an in-game mod that reflects over `DefDatabase<T>` at runtime and exports all Def data of the current mod environment to a SQLite database. The resulting `defs.db` contains serialized Def JSON (max depth 100), a field-value table, and an FTS5 full-text index for the CLI to query.

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
