# RimSearcher

[![Skills Update Time](https://img.shields.io/endpoint?url=https%3A%2F%2Fkearril.github.io%2FRimSearcher%2Fskills-update.json&cacheSeconds=300)](https://github.com/kearril/RimSearcher/commits/master/skills.zip)

English | [简体中文](README.md)

> **Design philosophy**: turn the tool's errors into knowledge inputs — let the model learn from mistakes.
> Errors are documentation, failures are lessons: every limitation and failure path is designed as learning material for the model.

#### RimSearcher V3 is a full rebuild. Starting with this version, the tool abandons the old MCP architecture in favor of a skills + CLI design, which brings better performance, lower overhead, and smarter AI decisions — and it now supports source-code analysis of your mod environment!

## Introduction

RimSearcher is a professional RimWorld source-analysis toolchain built for AI use: it combines query tools — the CLI and the in-game mod — with a skill that teaches the model how to use them. It is not just a tool; it is also a teacher.

RimSearcher specializes in the **Def data layer** (XML definitions, field structures, type relationships): the in-game DataMod exports every Def of the current mod environment to a SQLite database, and the CLI provides full-text search and exact reverse lookup over it. C# source analysis is delegated to [DecompilerServer](https://github.com/pardeike/DecompilerServer) — it decompiles loaded .NET assemblies directly: type search, member signatures, IL-level inspection, call-chain tracing, cross-version comparison — letting the AI see not "maybe-existing APIs" but the code that actually runs. As its design goal states: *"I can inspect the actual code that runs"*.

The Skill file ties both together into an analysis pipeline: CLI locates the Def → extracts C# type names → DecompilerServer reads the source.

Multi-mod environments are supported by two layers working together: DecompilerServer loads the vanilla game and any mod's assemblies side by side (each with its own context alias — inspect source and IL in parallel to pinpoint hooks and compatibility boundaries); DataMod exports the current mod environment's Def data for the CLI to query — one handles code, the other handles data, complementing each other.

## A Light on the Detour — how errors become signposts

A traveler does not ask about the detour — they ask about the light at the end of it.

The tool turns every detour into a signpost: when a query finds nothing, it speaks to point the way — one path, or another; when the syntax loses its voice, it leads you to the door of precision; when versions fall out of step, it tells you how to begin again. Not finding something is not failing — "nothing lies on this path" is a message, not a rebuke.

Yet the most dangerous thing is not thunder, but silence. Hollow constructors, mispointed IDs, references that are absent yet real — what trial and error can never teach, the best of it is charted, like a sailor's map marking the reefs of those who came before, so that those who follow need not run aground.

The tool points the way, the model walks it, and the walker comes to know the way — this is the breathing of the project.

During development, we found that what troubles large models is never errors themselves — it is the silence of not knowing where things went wrong. So we designed this tool from the model's perspective, clearing pitfalls for it, making every error meaningful — every error tells the model what to do next, and every such hint is the result of our optimization through extensive sample analysis:

When a query finds nothing, the hint suggests the next step; when the syntax is invalid, it points to exact-match commands; when a name is misspelled, it offers similar-name candidates... And "not found" is also a result rather than a failure — exit 2 means an expected empty result; the model need not mistake it for an error.

But the tool can only hint at errors it can perceive itself. Problems that are silent even to the tool — ones the model can never discover through trial and error — we distill the high-frequency ones into the skill, so the model can avoid them in advance.

We believe: the tool's errors should become the model's experience, not its cost. Errors are documentation; failures are lessons.

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

Add the directory containing `rimsearcher.exe` to the system PATH. If you are not sure how, ask your AI assistant for help.

Once configured, run in any terminal:

```bash
rimsearcher --version
```

It should print the current version number.

After configuring, if you move `rimsearcher.exe`, you must reconfigure the PATH.

### 5. Configure AI Skills

Download [skills.zip](https://raw.githubusercontent.com/kearril/RimSearcher/master/skills.zip) (this link always points to the latest version in the repository), extract it, and place `skills/rimsearcher/` into your AI assistant's skills directory.

### 6. Done

Restart and start testing and using the tool.

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

## Command Reference

### CLI Commands

```bash
# search — full-text fuzzy search
rimsearcher search <keyword> [--type T] [--mod M] [--limit N] [--count] [--name-only]
# list — paginated browsing
rimsearcher list [--type T] [--mod M] [--limit N] [--offset N] [--total]
# get — precise lookup
rimsearcher get <defName> [--type T] [--brief] [--field <path>]
# find — exact field-value reverse lookup
rimsearcher find <fieldPath> <value> [--type T] [--mod M] [--limit N]
# fields — field tree
rimsearcher fields <defName> --type <T> [--limit N] [--filter <glob>]
# values — distinct values of a field path
rimsearcher values <fieldPath> [--type T] [--limit N]
# types — def type statistics
rimsearcher types
# mods — mod statistics
rimsearcher mods
# check update — check for updates
rimsearcher check update
```

### AI integration (Skill)

The Skill is where the toolchain's soul lives — it teaches the AI how to analyze, not just what tools to use.

Different questions demand different paths: a quick lookup, or a full end-to-end analysis of a mechanic. Once a path is chosen, the conclusion is bound by methodology — every Def value is cross-checked against the decompiled formula, so each conclusion traces back to command output or source code itself. The greatest risk in analysis is not error but fabrication: the skill forbids guessing and inventing, and when information is insufficient, the AI explicitly marks its uncertainty and states what is missing — honesty is the first principle of analysis.

### DataMod — in-game export

RimSearcher.DataMod is an in-game mod that exports all Def data of the current mod environment to a SQLite database for the CLI to query; the database is version-locked to the CLI, so re-export after updating either side.


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

## Runtime Dependencies

- [.NET 10 Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) — runtime for the CLI and DecompilerServer
- [DecompilerServer](https://github.com/pardeike/DecompilerServer) — decompilation MCP service, required for C# source analysis

## Credits

- [DecompilerServer](https://github.com/pardeike/DecompilerServer) — powerful .NET decompilation MCP providing C# source analysis
- [RimWorld](https://rimworldgame.com) — thanks to Ludeon Studios for a wonderful game and an open mod ecosystem

## Disclaimer

- RimSearcher only reads and analyzes game data installed locally on your machine. It bundles and distributes no RimWorld game files or third-party mod assets.
- Analyzed mods are bound by their respective licenses; derivative work based on analysis results must comply with each mod's open-source terms.
- This project is not affiliated with Ludeon Studios. RimWorld is a trademark of Ludeon Studios.

## License

MIT
