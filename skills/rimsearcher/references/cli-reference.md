# CLI Command Reference

Full parameter defaults, SQL schema details, and edge-case behaviors. Load this only when you need internals beyond the [SKILL.md](../SKILL.md) summaries — everyday queries are covered there.

Data-query commands output JSON to stdout; errors and hints go to stderr. The database (`defs.db`) must be in the same directory as `rimsearcher.exe`.

---

## Database Schema (Conceptual)

```
defs:        id, def_name, def_type, label, description, mod_name, package_id, source_file, full_data
field_values: def_id, field_path, field_path_rev, field_value
       field_path_rev is the character-reversed path — it backs the `values` suffix index
       (values queries match case-sensitively on it)
field_paths:  id, path          — path dictionary for null fields (current exports)
null_fields:  def_id, path_id   — Defs whose field exists and is null (current exports)
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
| Numeric format | float/double use G7/G15 significant digits (extreme-precision truncation is a known feature); `NaN`/`±Infinity` are quoted strings; bools are lowercase `true`/`false`; `find` value matching and `values` path matching are case-sensitive (`find` path matching goes through LIKE and is case-insensitive for ASCII) |
| Path matching | `find`/`values` `fieldPath` matches literally as a suffix (`%`/`_` are escaped, not wildcards) |
| Version lock | defs.db carries the exporting DataMod's version in the SQLite `user_version` header field (encoded `major*10000+minor*100+patch`). The CLI accepts only a database exported by the exact same version and fails all data commands otherwise (exit 1 with both versions in the message). Databases without a marker (pre-3.1.2 exports) are rejected too — re-export with the current DataMod. `install`/`update` are exempt (they don't open the database) |
| Null values | Null fields live in a separate table pair (`null_fields` + `field_paths` path dictionary), present only in databases exported by the current DataMod. CLI and DataMod are version-locked: an older database fails these queries with an explicit re-export error. Empty strings and null list/dictionary items are not indexed. `find <path> null` (value must be lowercase) enumerates Defs whose field exists and is null, plus any literal string `"null"` values; a missing field produces no row, so "field absent" cannot be queried |

---

## Exit Codes

| Code | Meaning |
|---|---|
| 0 | Success |
| 1 | Runtime or query failure (message on stderr); **unknown command** (usage on stderr); **database version mismatch** (defs.db must be exported by the same-version DataMod — CLI and DataMod are version-locked; re-export to fix) |
| 2 | Query found nothing or was ambiguous: `get` not-found / multi-type without `--type` / `--field` not found; `find`/`search`/`fields`/`values` 0 hits (stdout stays `[]` or `{"count": 0}`) |

Exception: `list` keeps exit 0 on an empty page (pagination semantics — an out-of-range offset is a normal state, not a miss).

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
| `--name-only` | false | Match only the `def_name` column (FTS column filter) — drops noise from description/full_text hits, e.g. `fish* --name-only` excludes backstories that merely mention fish. All query operators stay in the column (`OR` included); CJK queries return nothing (def_names are ASCII) |

**Output** (default): Array of `{def_name, def_type, label, mod_name, package_id, rank}`, sorted by FTS5 rank ascending (lower rank is more relevant).

**Output** (--count): `{"count": N}`

**Semantics**: FTS5 token matching, not SQL LIKE. The unicode61 tokenizer splits on word boundaries.
`shield` matches the standalone token `shield` but not `ShieldBelt` (one token). Use `shield*` for prefix.

**Indexed content**: `full_text` is built from defName, label, description, and key field values — including nested reference names. Tokenization splits on `_`, so `Apparel_ShieldBelt` contributes both `apparel` and `shieldbelt` tokens: `search ShieldBelt` therefore also hits defs that only *reference* the belt (PawnKinds with it in their default apparel, recipes producing it). Expect reference-related hits, not only literal-name matches.

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
rimsearcher list [--type T] [--mod M] [--limit N] [--offset N] [--total]
```

| Parameter | Default | Description |
|---|---|---|
| `--type` | null | Filter by def_type |
| `--mod` | null | Filter by mod_name |
| `--limit` | 20 | Page size |
| `--offset` | 0 | Skip first N rows |
| `--total` | false | Also return the filtered total (ignoring limit/offset) |

**Output**: Array of `{def_name, def_type, label, mod_name, package_id}`, sorted by `def_type, def_name`.

**Output** (--total): `{"total": N, "results": [...]}` — total is the filtered count regardless of `--limit`/`--offset`, for pagination math.

**Empty page**: exit 0 (pagination semantics — an out-of-range offset is normal, not a miss).

**Examples**:
```bash
rimsearcher list --type ThingDef --offset 40
rimsearcher list --mod Core --limit 10 --total
```

---

## get

Retrieve a single Def by exact def_name match.

```
rimsearcher get <defName> [--type T] [--brief] [--field <path>]
```

| Parameter | Default | Description |
|---|---|---|
| `defName` | required | Exact def_name match |
| `--type` | null | Required when defName matches multiple def_types |
| `--brief` | false | Return only `classes[]` (all `*Class` bridge fields) instead of full JSON |
| `--field` | null | Extract a single field by path (`a.b[0].c`, same format as `fields`) instead of full JSON; mutually exclusive with `--brief` |

**Output** (default): Full `full_data` JSON object — the complete Def serialization.

**Output** (--brief): `{def_name, def_type, label, mod_name, package_id, classes[]}` — every string field whose name ends in `Class` (thingClass, compClass, workerClass, hediffClass, …), the def's C# bridge clues for feeding the decompiler. Type-agnostic and recursion-deep; entries are deduplicated and sorted. When no `*Class` fields exist, stderr prints `Hint: no *Class fields found; try 'fields <defName> --type <T>'` and `classes[]` stays empty.

**Output** (--field): the extracted JSON element as-is (string values include quotes, objects/arrays are raw JSON).

**Examples** (--field) — scalars print bare, arrays/objects print raw JSON, null fields print `null`:
```bash
rimsearcher get Bullet_ChargeRifle --type ThingDef --field projectile.flyOverhead
# false
rimsearcher get Apparel_FlakVest --type ThingDef --field statBases
# [{"stat":"MaxHitPoints","value":200}, ...]
rimsearcher get Apparel_FlakVest --type ThingDef --field tools
# null
```

**Errors** (--field): malformed path (bad format, e.g. `a[`) → exit 1; valid path with no match (missing property / out-of-range index) → exit 2.

**Not found**: exit 2 with a stderr `Did you mean: …` list of similar def_names (same type, up to 5) plus a note that abstract Defs (`Abstract="true"`) are never instantiated and never appear in the database.
**Unknown `--type`**: rejected up front — `unknown def_type 'X'` on stderr, exit 1 (run `types` to list valid types). Applies to every command taking `--type`: `search`, `list`, `get`, `find`, `fields`, `values`.

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
| `fieldPath` | required | Suffix-matched: `LIKE '%fieldPath'` — includes index segments: `pawnGroupMakers[0].kindDef` is queryable, `pawnGroupMakers.kindDef` (no `[i]`) matches nothing |
| `value` | required | Exact match: `field_value = value` |
| `--type` | null | Filter by def_type |
| `--mod` | null | Filter by mod_name |
| `--limit` | 50 | Max results |

**Output**: Array of `{def_name, def_type, label, mod_name, package_id, field_path, field_value}`.

**0 results**: A hint is written to stderr suggesting `rimsearcher search "value"`.

**Null values**: `find <path> null` (lowercase literal) matches Defs whose field exists and is null — the complement of a normal `values` listing — plus any literal string `"null"` values. Requires a current-export database; older databases fail with an explicit re-export error (CLI/DataMod are version-locked). Empty strings and missing fields have no rows and cannot be matched.

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
| `--filter` | null | Path glob filter: `*` matches any character run (crosses segments), everything else is literal — `ingestible.*` or `comps[0].*`; empty filter = no filter |

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
| `fieldPath` | required | Suffix-matched: `LIKE '%fieldPath'` — includes index segments: `pawnGroupMakers[0].kindDef` is queryable, `pawnGroupMakers.kindDef` (no `[i]`) matches nothing |
| `--type` | null | Filter by def_type |
| `--limit` | 200 | Max distinct values |

**Output**: String array of distinct field values. When any Def's field is null, `"null"` appears among the values (current exports only) — e.g. `values armorCategory --type DamageDef` returns `["Blunt","Heat","Sharp","null"]`. On older databases `"null"` appears only if the value was stored literally.

**0 hits**: stdout stays `[]` and the process exits with code 2.

**Performance**: suffix matching runs on the reversed-path index (`field_path_rev`) — ~10 ms on a fresh export; matching is **case-sensitive exact suffix** (`statOffsets[0].value` does not match `equippedStatOffsets[0].value`).

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
- **`values` path matching is case-sensitive** (BINARY on `field_path_rev`), while `find` path matching goes through LIKE and is case-insensitive for ASCII — `values statOffsets[0].value` excludes `equippedStatOffsets[0].value` rows (different case), whereas `find statOffsets[0].value <v>` includes them.
- **`--brief` empty `classes[]` is a legal result**: the def has no `*Class` string fields (stderr prints a hint); use `fields` instead.

---

## See Also

- [DecompilerServer MCP Integration](decompiler-mcp.md) — loading assemblies, searching symbols, call graph, version comparison
