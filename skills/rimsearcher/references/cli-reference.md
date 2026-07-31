# CLI Command Reference

Full parameter defaults, SQL schema details, and edge-case behaviors. Load this only when you need internals beyond the [SKILL.md](../SKILL.md) summaries — everyday queries are covered there.

Data-query commands output JSON to stdout; errors and hints go to stderr. The database (`defs.db`) must be in the same directory as `rimsearcher.exe`.

---

## 数据库 Schema（概念）

```
defs:        id, def_name, def_type, label, description, mod_name, package_id, source_file, full_data
field_values: def_id, field_path, field_value
defs_fts:    FTS5( def_name, label, description, full_text )  — tokenize='unicode61'
```

- `full_data` 是 Def 对象的完整 JSON 序列化（深度上限 100，循环引用输出 `"$cyclic_ref"`；详见「数据限制」）
- FTS5 写侧 CJK bigram 展开：`"护盾腰带"` 被索引为 `"护盾腰带 护盾 盾腰 腰带"`；查询侧同样展开为二元组（空格 = AND），因此任意长度的连续中文按原样使用即可，如 `粉碎机械族`
- 输出随加载的 mod 集合变化——不要假设固定数量或 mod 列表

---

## 数据限制（Data Limitations）

导出内容与游戏运行时状态相关，以下限制是设计使然：

| 限制 | 说明 |
|---|---|
| 语言依赖 | `label`/`description` 是导出时游戏当前语言的文本，原始 XML 文本在进程内不存在（已翻译）；导出前请切换目标语言 |
| 抽象 def 不可见 | `Abstract="true"` 的模板从未被实例化，运行时无对象，不进入数据库 |
| 字段策略 | 收录 public + private 数据字段，镜像游戏反序列化器：排除 `[Unsaved(allowLoading:false)]` 字段、编译器生成字段（`<` 前缀）与委托字段；未被游戏标记的运行时字段（临时缓存/回链）会以 `{}`、原始值或 `"$cyclic_ref"` 形式出现 |
| 深度 | `full_data` 深度 100 硬防护，超出输出 `"$truncated"`（真实数据最深 29 层，不会误触）；`field_values` 检索层深度 4——`stages[0].statOffsets[0].value` 等路径可达，行为树第 3 层以下的节点请用 `get` 查看 `full_data` |
| 数值格式 | float/double 用 G7/G15 有效数字（极端精度截断为已知特性）；`NaN`/`±Infinity` 输出为带引号字符串；bool 输出小写 `true`/`false`；`find`/`values` 值匹配大小写敏感 |
| 路径匹配 | `find`/`values` 的 `fieldPath` 按字面后缀匹配（`%`/`_` 已转义，不会当通配符） |

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

**CJK**: 连续中文在查询侧展开为相邻二元组（空格 = AND），任意长度按原样使用：
`护盾`、`粉碎机械族` 都能命中包含对应文本的 Def；单字中文（如 `闪`）因索引无单字 token 而不可命中。

**FTS 语法错误**：数值或含特殊字符的查询（如 `0.1`）会触发 FTS5 语法错误，stderr 会提示改用 `find`/`values` 精确匹配。

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
| `--brief` | false | Return only `thing_class` + `comp_classes` instead of full JSON |

**Output** (default): Full `full_data` JSON object — the complete Def serialization.

**Output** (--brief): `{def_name, def_type, label, mod_name, package_id, thing_class, comp_classes[]}`

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

**Truncation**: 达到 `--limit` 或内部取行窗口（`limit*2`，上限 40000）时，stderr 输出提示
`Hint: 已达 limit N，结果可能截断，可用 --limit 增大`；检测基于精确 COUNT，不会误报。

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
| `--limit` | 1000 | Max results (fetches 2x internally to compensate for noise filter) |

**Output**: Array of `{field_path, field_value}`.

**Noise filtering**: The following are excluded:
- Fields matching: `debugRandomId`, `defNameHash`, `generated`, `ignoreConfigErrors`, `ignoreIllegalLabelCharacterConfigError`, `index`, `shortHash`
- Fields with path prefix `modContentPack.`

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


## See Also

- [DecompilerServer MCP Integration](decompiler-mcp.md) — loading assemblies, searching symbols, call graph, version comparison
