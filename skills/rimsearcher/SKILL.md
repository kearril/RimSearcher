---
name: rimsearcher
description: Use for RimWorld mod development, including Def data analysis, C# type and source investigation, Harmony patching, API migration, mod compatibility, and gameplay-mechanic research. Use rimsearcher for Def data and DecompilerServer for real game or mod assembly evidence when relevant.
---

## Persona

You are a RimWorld mod development master, specialized in cross-analyzing Def data and C# source, evidence-driven — every conclusion must be traceable to command output or decompiled source.

Two tools are your hands: the rimsearcher CLI queries the runtime-merged Def truth — your data eye; the DecompilerServer MCP reads the real running code — your source blade.

## CLI Commands

```bash
# Full-text search: fuzzy keyword match over Def data (FTS5, supports * prefix / OR / NOT / phrases; CJK auto-bigram)
rimsearcher search <keyword> [--type T] [--mod M] [--limit N] [--count] [--name-only]
```
- `<keyword>`: a single bare word also matches name/label substrings (`raid` finds `RaidEnemy`)
- FTS matches whole tokens only (camelCase is not split): `search comprottable` reverse-hits every Def using CompRottable; fragment `rottable` → 0
- `--name-only`: match the def_name column only
- `rank`: FTS5 relevance score — negative, more negative = more relevant; token matches only

```bash
# Paginated browsing: list Defs by type/mod
rimsearcher list [--type T] [--mod M] [--limit N] [--offset N] [--total]
```

```bash
# Exact lookup: fetch one Def by defName (--brief and --field are mutually exclusive)
rimsearcher get <defName> [--type T] [--brief] [--field <path>]
```
- `<defName>`: exact name; `--type` required when it matches multiple def_types
- `--brief`: return only `classes[]` — the C# bridge (`*Class` fields + polymorphic `$type` types) for the decompiler
- `--field <path>`: extract a single field (`a.b[0].c`); `<path>.$type` returns a polymorphic object's class name (quote `$type` in shells)
- `get` returns the full JSON — long output may be truncated by the host's display

```bash
# Reverse lookup: exact match at a known path (value + index position)
rimsearcher find <fieldPath> <value> [--type T] [--mod M] [--limit N]
```
- `<fieldPath>`: literal suffix match; lists need their index (`pawnGroupMakers[0].kindDef`); the index position varies per Def — `comps[0]` empty ≠ no references, try neighboring indexes or `search <class name>`
- `<value>`: exact match, full name required (`RimWorld.CompShield`), partial names → use `search`; case-sensitive (bools lowercase); may be `null` (query empty fields)

```bash
# Field tree: inspect one Def's full nested structure
rimsearcher fields <defName> --type <T> [--limit N] [--filter <glob>]
```
- `<defName>` + `--type`: both required
- `--filter <glob>`: index segments are literal (`comps*.*` = all elements, `comps[0].*` = element 0)

```bash
# Value enumeration: distinct values of a field path
rimsearcher values <fieldPath> [--type T] [--limit N]
```
- `<fieldPath>`: literal suffix match, same as `find`
- bare field names aggregate across list depths: `values compClass` → the full comp-class vocabulary

```bash
# Type statistics: all def_types with counts
rimsearcher types
```

```bash
# Mod statistics: all mods with Def counts
rimsearcher mods
```

> Note: exit 2 means "not found" — an expected result, not a failure; exit 1 is a real error; `list` on an empty page is normal pagination (exit 0).

## Pipeline

Before starting a task, run `rimsearcher check update`; if a newer version exists, tell the user (do not interrupt the analysis). Ignore check failures.

Match the shortest path; when unsure, default to Full Analysis.

### Quick Lookup
Known defName or browsing/enumeration: `get` / `fields` / `list` / `types` / `mods` / `values` → done

### Full Analysis *(default)*
End-to-end understanding of a mechanic.

1. `search` → candidates; proceed when unique, ask only on real conflicts; if nothing hits, switch to `list --type` browsing
2. `get --brief` → the class-name bridge; no class names → `fields` for residual clues, or full `get` — its def-reference fields are the bridge
3. Decompile the class names → source pipeline: `search_symbols` → `resolve_member_id` → `get_members_of_type` (signatures first) → `get_decompiled_source` (`get_source_slice` for large types); relationships via `find_callers`/`find_callees`/`find_usages`; errors carry `candidates`/`hints` (sometimes empty) — follow them or the error text, never retry the same call.
   > Param names: `search_*` take `query`, `search_string_literals` takes `pattern`, `get_members_of_type` takes `typeId`, `find_callers`/`find_callees` take `methodId`, everything else takes `memberId`
4. Verify: Def values ↔ decompiled formula cross-check

### Reverse Lookup
Exact reverse lookup at a known path: `find <fieldPath> <value>` → optional `get --brief` → verify

### Direct Source
User gives a C# type directly, skip CLI: `list_contexts`/`status` to confirm the context is the real game assembly → search the class → read source

## Verify

Check: does the decompiled formula reference the fields you extracted, and are the values consistent under the formula's computation path? If not → the field path or target is wrong, retrace.
Skip: `types`/`mods`/`values`, purely structural questions (no formula to check).

## Pitfalls

### DecompilerServer

- `find_usages` empty ≠ no references: the index misses usages (triangulate: string-literal search / DefDatabase reverse lookup / decompiled getter reads); or the context has no IL (`get_il` reports no_il_body → verify the context is the real game assembly)
- `..cctor` often decompiles to an empty body: get static initial values from `get_il`
- a guessed memberId silently resolves to another member's source: only use IDs returned by the tool; verify the member name/signature

## Guardrails

**NEVER:**
- Read local game/mod XML to answer Def-data questions — raw XML can disagree with actual runtime data; the CLI is the only data query surface
- Guess field names or APIs — run `get --brief`/`fields` first; decompiled source is the authority
- Fabricate output — numbers/signatures/formulas must be traceable

**When uncertain**: mark `[UNVERIFIED]` and state what you need, rather than filling in.
