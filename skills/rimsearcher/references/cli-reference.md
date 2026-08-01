# CLI Command Reference

Full parameter defaults, SQL schema details, and edge-case behaviors. Load this only when you need internals beyond the [SKILL.md](../SKILL.md) summaries — everyday queries are covered there.

Data-query commands output JSON to stdout; errors and hints go to stderr. The database (`defs.db`) must be in the same directory as `rimsearcher.exe`.

---

## Database Schema (Conceptual)

```
defs:        id, def_name, def_type, label, description, mod_name, package_id, source_file, full_data
field_values: def_id, field_path, field_value
defs_fts:    FTS5( def_name, label, description, full_text )  — tokenize='unicode61'
```

- `full_data` is the complete JSON serialization of the Def object (depth cap 100; cyclic references output `"$cyclic_ref"`; see Data Limitations below)
- FTS5 with CJK bigram expansion on write: `护盾腰带` is indexed as `护盾腰带 护盾 盾腰 腰带`; the query side expands the same way (space = AND), so any-length CJK phrases work as-is, e.g. `粉碎机械族`
- Output varies with the loaded mod set — never assume fixed counts or a fixed mod list

---

## Data Limitations

Export content reflects runtime state; the following limits are by design:

| Limit | Notes |
|---|---|
| Language | `label`/`description` are in the game language at export time; the original XML text no longer exists in-process (already translated). Switch the game language before exporting |
| Abstract defs invisible | `Abstract="true"` templates are never instantiated — no runtime object exists, so they are not in the database |
| Field policy | Public + private data fields mirror the game deserializer: excludes `[Unsaved(allowLoading:false)]`, compiler-generated (`<` prefix) and delegate fields; runtime fields the game did not mark (caches/back-references) appear as `{}`, raw values, or `"$cyclic_ref"` |
| Depth | `full_data` hard-capped at 100 (`"$truncated"` beyond; real data reaches 29, never triggered); `field_values` retrieval depth 4 — paths like `stages[0].statOffsets[0].value` are reachable; think-tree nodes below level 3: use `get` to read `full_data` |
| Numeric format | float/double use G7/G15 significant digits (extreme-precision truncation is a known feature); `NaN`/`±Infinity` are quoted strings; bools are lowercase `true`/`false`; `find`/`values` matching is case-sensitive |
| Path matching | `find`/`values` `fieldPath` matches literally as a suffix (`%`/`_` are escaped, not wildcards) |

---

## Exit Codes

| Code | Meaning |
|---|---|
| 0 | Success |
| 1 | Runtime or query failure (message on stderr); **unknown command** (usage on stderr) |
| 2 | Query found nothing or was ambiguous: `get` not-found / multi-type without `--type`; `find`/`search`/`fields`/`values` 0 hits (stdout stays `[]` or `{"count": 0}`) |

Data always goes to stdout, diagnostics to stderr.

---

## search

FTS5 full-text search across def_name, label, description, and field values.

```
rimsearcher search <keyword> [--type T] [--mod M] [--limit N] [--count]
```

| Parameter | Default | Description |
|---|---|---|
| `keyword` | required | FTS5 MATCH expression. Supports `*` (prefix), `OR`, `NOT`, `"phrases"` |
| `--type` | null | Filter by def_type (exact match) |
| `--mod` | null | Filter by mod_name (exact match) |
| `--limit` | 20 | Max results |
| `--count` | false | Return `{"count": N}` instead of result array |

**Output** (default): Array of `{def_name, def_type, label, mod_name, package_id, rank}`, sorted by FTS5 rank ascending (lower rank is more relevant).

**Output** (--count): `{"count": N}`

**Semantics**: FTS5 token matching, not SQL LIKE. The unicode61 tokenizer splits on word boundaries.
`shield` matches the standalone token `shield` but not `ShieldBelt` (one token). Use `shield*` for prefix.

**CJK**: CJK runs are expanded into bigrams on the query side (space = AND); any length works as-is:
`护盾` or `粉碎机械族` both hit defs containing the text. Single CJK chars (e.g. `闪`) never match — the index has no single-char tokens.

**0 hits**: stdout stays `[]` (or `{"count": 0}`) and the process exits with code 2; a Latin keyword without `*` also prints a prefix-wildcard hint on stderr.

**FTS syntax errors**: queries with digits or special characters (e.g. `0.1`) trigger an FTS5 syntax error; stderr hints at `find`/`values` for exact matching.

**Examples**:
```bash
rimsearcher search "shield*" --type ThingDef
rimsearcher search "护盾" --count
rimsearcher search "shield OR barrier" --limit 5
```

---

## list

Browse Defs with pagination, no search overhead.

```
rimsearcher list [--type T] [--mod M] [--limit N] [--offset N]
```

| Parameter | Default | Description |
|---|---|---|
| `--type` | null | Filter by def_type |
| `--mod` | null | Filter by mod_name |
| `--limit` | 20 | Page size |
| `--offset` | 0 | Skip first N rows |

**Output**: Array of `{def_name, def_type, label, mod_name, package_id}`, sorted by `def_type, def_name`.

**Examples**:
```bash
rimsearcher list --type ThingDef --offset 40
rimsearcher list --mod Core --limit 10
```

---

## get

Retrieve a single Def by exact def_name match.

```
rimsearcher get <defName> [--type T] [--brief]
```

| Parameter | Default | Description |
|---|---|---|
| `defName` | required | Exact def_name match |
| `--type` | null | Required when defName matches multiple def_types |
| `--brief` | false | Return only `classes[]` (all `*Class` bridge fields) instead of full JSON |

**Output** (default): Full `full_data` JSON object — the complete Def serialization.

**Output** (--brief): `{def_name, def_type, label, mod_name, package_id, classes[]}` — every string field whose name ends in `Class` (thingClass, compClass, workerClass, hediffClass, …), the def's C# bridge clues for feeding the decompiler. Type-agnostic and recursion-deep; entries are deduplicated and sorted. When no `*Class` fields exist, stderr prints `Hint: no *Class fields found; try 'fields <defName> --type <T>'` and `classes[]` stays empty.

**Multi-type behavior**: If `defName` matches multiple types and `--type` is not specified, the command
exits with code 2 and prints candidate types to stderr:

```
Error: 'Human' matches multiple Def types. Specify --type:
  BodyDef
  HediffGiverSetDef
  ThingDef
```

This is informative, not a crash. Add `--type` to resolve.

**Examples**:
```bash
rimsearcher get Apparel_ShieldBelt --type ThingDef           # full Def JSON
rimsearcher get Apparel_ShieldBelt --type ThingDef --brief   # C# types only
rimsearcher get Human                                        # multi-type → error with candidates
```

---

## find

Exact field-value match. Value matching uses `=` equality, not substring.

```
rimsearcher find <fieldPath> <value> [--type T] [--mod M] [--limit N]
```

| Parameter | Default | Description |
|---|---|---|
| `fieldPath` | required | Suffix-matched: `LIKE '%fieldPath'` |
| `value` | required | Exact match: `field_value = value` |
| `--type` | null | Filter by def_type |
| `--mod` | null | Filter by mod_name |
| `--limit` | 50 | Max results |

**Output**: Array of `{def_name, def_type, label, mod_name, package_id, field_path, field_value}`.

**0 results**: A hint is written to stderr suggesting `rimsearcher search "value"`.

**Key distinction**:
- `find` = **exact** field value match. Requires full name: `RimWorld.CompShield`
- `search` = **fuzzy** FTS5 match. Handles partial names, CJK, etc.

**Examples**:
```bash
rimsearcher find compClass RimWorld.CompShield
rimsearcher find thingClass RimWorld.Apparel --type ThingDef
rimsearcher find compClass Shield                          # returns []; value is exact match
```

---

## fields

List all field paths and values for a single Def.

```
rimsearcher fields <defName> --type <T> [--limit N]
```

| Parameter | Default | Description |
|---|---|---|
| `defName` | required | Exact def_name |
| `--type` | required | def_type |
| `--limit` | 1000 | Max results (all rows fetched, then filtered/sorted/natural-ordered) |

**Output**: Array of `{field_path, field_value, def_type?}` — paths in **natural order** (numeric segments by value: `genSteps[2]` before `genSteps[10]`).
`def_type` is an array of all matching `def_types` and appears only when the value is a reference (matches a `def_name` in the `defs` table); cross-type duplicate names are legal in RimWorld, so all hits are listed (e.g. `"def_type": ["GenStepDef", "ThingDef"]`).
Note: annotation is by value match only — generic words that happen to share a `def_name` (`None`, `Normal`, …) may be annotated too; treat `def_type` as a hint and confirm with `get` when the field's semantics are unclear.
**0 hits**: stdout stays `[]` and the process exits with code 2.

**Noise filtering**: The following are excluded:
- Fields matching: `debugRandomId`, `defNameHash`, `generated`, `ignoreConfigErrors`, `ignoreIllegalLabelCharacterConfigError`, `index`, `shortHash`
- Fields with path prefix `modContentPack.`

**Truncation**: when the visible count exceeds `--limit`, stderr prints
`Hint: reached limit N; results may be truncated, use --limit to increase` (exact detection, no false positives).

**Examples**:
```bash
rimsearcher fields Apparel_ShieldBelt --type ThingDef --limit 20
```

---

## values

Enumerate distinct values for a given field path suffix.

```
rimsearcher values <fieldPath> [--limit N]
```

| Parameter | Default | Description |
|---|---|---|
| `fieldPath` | required | Suffix-matched: `LIKE '%fieldPath'` |
| `--limit` | 200 | Max distinct values |

**Output**: String array of distinct field values.

**0 hits**: stdout stays `[]` and the process exits with code 2.

**Examples**:
```bash
rimsearcher values compClass --limit 10
rimsearcher values thingClass
```

---

## types

List all Def types with counts.

```
rimsearcher types
```

No parameters.

**Output**: Array of `{def_type, count}`, sorted by count descending.

**Example output**:
```json
[{"def_type":"ThingDef","count":3415},{"def_type":"SoundDef","count":1231},...]
```

Actual counts depend on the loaded mod set.
---

## mods

List all mods with Def counts.

```
rimsearcher mods
```

No parameters.

**Output**: Array of `{mod_name, package_id?, def_count}`, sorted by def_count descending.

Dynamic/abstract Defs appear as `mod_name: "Unknown"` with `package_id: null`.

---

## Known Limitations

Tool-behavior quirks that look like failures but are by design. If a result seems wrong, check this list first.

- **FTS tokenizes whole words**: `search shield` matches only the exact token `shield`, never `ShieldBelt` — add a prefix wildcard (`shield*`); 0 hits with a Latin keyword and no `*` prints a stderr hint. Single CJK chars (`闪`) never match (no single-char tokens in the index).
- **`fields` `def_type` is a value-match hint**: a value equal to some `def_name` is annotated with all matching `def_types` — generic words that happen to share a def_name (`None`, `Normal`) may be annotated too; it is a hint, not proof. Confirm with `get` when the field semantics are unclear.
- **`fields` order is natural, not lexicographic**: numeric path segments compare by value (`genSteps[2]` before `genSteps[10]`).
- **`find`/`values` `fieldPath` is a literal suffix**: `%`/`_` are escaped, not wildcards; `find` values are exact (`=`), case-sensitive, canonically formatted (bools lowercase).
- **`--brief` empty `classes[]` is a legal result**: the def has no `*Class` string fields (stderr prints a hint); use `fields` instead.

---

## See Also

- [DecompilerServer MCP Integration](decompiler-mcp.md) — loading assemblies, searching symbols, call graph, version comparison
