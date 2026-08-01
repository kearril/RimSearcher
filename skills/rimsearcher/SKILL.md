---
name: rimsearcher
description: Use for RimWorld mod development, including Def and XML analysis, C# type and source investigation, Harmony patching, API migration, mod compatibility, and gameplay-mechanic research. Use rimsearcher for Def data and DecompilerServer for real game or mod assembly evidence when relevant.
---

# RimSearcher

You are a RimWorld mod development master. The rimsearcher CLI queries game data.
The DecompilerServer MCP reads C# source. Never guess an API — look it up.

## Choosing a Command

```
search "keyword"          ← partial / fuzzy match
get <name> --type <T> [--field <path>]  ← exact defName  (!) multi-type -> must add --type
find <path> <fullValue>   ← C# class → all Defs using it (empty → exit 2)
list --type T --limit N --offset N  ← browsing / paginating
fields <name> --type <T>  ← inspect one Def's field tree
values <path>             ← distinct field values
types                     ← def_type stats
mods                      ← mod stats
```

## Rules

These are the CLI behaviors that guessing wrong wastes turns.

### search — use a prefix wildcard for Latin terms
FTS5 token matching, **not** SQL LIKE. `shield` matches only the standalone token `shield` — it will not match `ShieldBelt` (one token). For Latin/alphanumeric prefix searches, add `*`: `shield*`.
CJK is auto-bigram (query-side expansion): `护盾` matches `护盾腰带`, and multi-char phrases work as-is too (`粉碎机械族`). Single CJK chars (`闪`) cannot match — the index has no single-char tokens.
0 hits with a Latin keyword and no `*` prints a stderr hint explaining the prefix-wildcard mechanism (e.g. `shield` → hint suggests `shield*`).

### find — value is exact match
`find <path> <value>` uses `=` equality. `find compClass Shield` matches nothing; you need the full name: `find compClass RimWorld.CompShield`. For partial names, use `search`.
Values are case-sensitive and formatted canonically: booleans are lowercase — `find showOnPawns true`, not `True`.
0 hits → stdout `[]` + stderr hint, **exit code 2** (scripts can distinguish "not found" from failure).

### get — multi-type and `--brief`
A defName can exist in multiple def_types (e.g. `Human` is in BodyDef, ThingDef, HediffGiverSetDef). Without `--type`, the command exits with code 2 and prints candidates — this is NOT a crash, just add `--type` and retry.
`--brief` returns `{classes[]}` — every `*Class`-suffixed string field in the def (thingClass, compClass, workerClass, hediffClass, …), i.e. the C# bridge to the decompiler. Decompile the entries relevant to your question; if none fits, use `fields` and scan `field_path`/`field_value` pairs yourself.
`--field <path>` extracts a single field (same path format as `fields`: `a.b[0].c`) instead of the full JSON — use it when only one value is needed (saves tokens and parsing); mutually exclusive with `--brief`.

### output format
Data-query commands write JSON to stdout; errors and hints go to stderr.

## Pipeline

Match the shortest path. Unsure? Default to **Full Analysis**.

### Quick Lookup
User knows the defName or wants to browse/enumerate. No search needed.
  `get` / `fields` / `list` / `types` / `mods` / `values` → done

### Full Analysis *(default)*
User wants to understand a game mechanic end-to-end.

1. `search "<query>" --type T`          ← Latin/alphanumeric prefix: append `*`; CJK: use as-is
   If several Defs match, disambiguate automatically first: a mechanic search usually has one canonical def_type (map generation → GenStepDef/MapGeneratorDef, apparel stats → StatDef, …). When `def_type` + `label` + `mod_name` single out the intended Def, continue without asking; only present candidates and ask when they genuinely conflict.
2. `get <name> --type T --brief`          ← extracts `classes[]` (all `*Class` bridge fields)
   Decompile the entries relevant to the question.
   If none yields the behavior, use `fields <name> --type <T>` and inspect every `field_path` / `field_value` pair for fully-qualified C# type names — paths ending in `Class` are common clues (workerClass, hediffClass, driverClass, …), not an exhaustive list.
3. Decompiler:
   `list_contexts` → `select_context` or ask user for paths
   `load_assembly(assemblyPath="<path>", contextAlias="<alias>")` — only for a new assembly path
   Confirm the context has IL bodies (decompile a known method: real body, not `public extern` stubs — reference assemblies have none).
   For each selected C# type, use `resolve_member_id` when its name is fully qualified; otherwise use `search_symbols`. Then call `get_decompiled_source` with the resolved `memberId`.
4. Verify — cross-check the Def values against the decompiled formula (see Verify section).

### Reverse Lookup
User asks "which Defs use this C# class?"

1. `find <fieldPath> <fullClassName>`    ← value is exact match
2. Optional: `get --brief` on key results → decompiler
3. Verify

### Direct Source
User names a C# type directly. Skip CLI.

1. Decompiler:
   `list_contexts` → `select_context` or ask user for paths
   `load_assembly(assemblyPath="<path>", contextAlias="<alias>")` — only for a new assembly path
   For a fully-qualified type or member name, use `resolve_member_id`; otherwise use `search_symbols(query="<ClassName>")`.

## Verify

`types`, `mods`, `values`: skip this step.

Verification means: **cross-check the Def numbers you found against the decompiled source** — does the located method/class actually reference the fields you extracted (baseValue, curve points, stage offsets)? Def values and formula constants must agree. If they do not, the field path or the decompiled target is wrong — retrace.

CLI behavior questions — parameters, output fields, FTS syntax, pagination/filtering, database schema, exit codes, or any unexpected result — are answered by **reading `references/cli-reference.md` first**. The reference file is the contract.

Before touching any game/mod XML for a Def-data question, **read `references/cli-reference.md` first** — its Data Limitations section explains what the database contains (runtime-merged defs, export-time language, depth caps); if the CLI already answers the question, the XML is unnecessary.
Read `references/decompiler-mcp.md` only for context loading, recovery, inheritance/call-graph analysis, IL/transpiler work, or version comparison.

## Guardrails

**Grounding first** — decompiler results are only as good as the memberId that produced them:
- A guessed or stale `memberId` can silently resolve to an *unrelated* member (no error is raised). Before reading further, confirm the returned `fullName`/signature matches the target; if not, re-resolve.

**NEVER:**
- Guess field names — run `get --brief` or `fields` first.
- Read local game/mod XML files (`RimWorld*/Mods/**/Defs/*.xml`, the game install dir, Steam workshop folders) to answer Def-data questions — `defs.db` is the **runtime-merged** export (post-inheritance, post-patch, plus runtime state); XML is raw source text and can disagree. The CLI is the only data query surface.
- Invent method signatures — read decompiled source before patching.
- Assume 1.5 APIs work in 1.6 — for method behavior, compare the two context aliases with `compare_symbols(..., compareMode:"body")` as described in `references/decompiler-mcp.md`.
- Write a Harmony patch without reading IL — run `get_il` first.
- Fabricate XML — inspect full `get` output, not `--brief`.
- Fall back to shell tools while the DecompilerServer MCP is connected.

When uncertain about an API you cannot verify, mark it `[UNVERIFIED]` and state what you need.

**Recovery:**
- CLI symptom → action pairs live in the per-command Rules above (`get` multi-type, `find` empty, `search` empty).
- DecompilerServer errors → follow the `candidates` hint in the structured error.
- `no_il_body` / decompiled source shows only `extern` stubs → the context is a reference assembly; select a different alias (see `references/decompiler-mcp.md`).
- DecompilerServer unresponsive → run `list_contexts`; registered aliases persist across restarts.

