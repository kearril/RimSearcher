# DecompilerServer MCP Integration

Once you have C# type names from `rimsearcher`, use the DecompilerServer MCP.

## Loading Assemblies

Registered aliases persist across MCP restarts — always check first:

```
list_contexts / status                       ← what's already registered?
select_context(contextAlias="rw16")          ← activate if found
load_assembly(assemblyPath="...", contextAlias="...") ← only for new paths
```

When no aliases are registered, ask the user for each assembly path. Auto-name aliases from file names.
Load once per assembly. `gameDir` auto-discovers Unity layouts; `assemblyPath` is for direct paths.

Reference assemblies (e.g. `krafs.rimworld.ref`) have **no IL**: decompiled source is `public extern` stubs and `get_il` returns `no_il_body`. After selecting a context, confirm it is the real game assembly — `status` shows the path; a known method must decompile to a real body. `find_usages` on a ref context silently returns empty results, which looks like a true negative — do not trust it without the IL check.

## Search + Read

```
search_symbols(query="RimWorld.CompShield")
get_decompiled_source(memberId="<id-from-search>")
```

- Prefer `search_symbols` for fragments, `resolve_member_id` for fully-qualified names like `RimWorld.CompShield.Recharge`.
- Use `list_members(typeId, mode="signatures")` before guessing method names.
- `memberId` carries an MVID — follow-up calls auto-route, no need to repeat `contextAlias`.
- Results paginate: when a response has `hasMore: true`, pass the returned `nextCursor` as `cursor` on the next call to page through (this is normal, not truncation).

## Inheritance + Call Graph

```
find_base_types(typeId="<type-id>")
find_derived_types(baseTypeId="<type-id>")
find_callers(methodId="<method-id>")
find_callees(methodId="<method-id>")
get_il(memberId="<method-id>")       # before writing transpilers
```

## Version Comparison

```
compare_contexts(leftContextAlias="rw15", rightContextAlias="rw16")
compare_symbols(leftContextAlias="rw15", rightContextAlias="rw16", symbol="Verse.Pawn:Kill", symbolKind="method")
compare_symbols(leftContextAlias="rw15", rightContextAlias="rw16", symbol="Verse.Pawn:Kill", symbolKind="method", compareMode="body")  # method body diff
```

## Recovery

If DecompilerServer returns an error with candidates, follow the suggestion rather than retrying:
- `type_not_found` → `search_types` or `search_symbols`
- `member_not_found` → inspect `error.details.candidates`, then `list_members`
- `member_guess_unresolved` → the type resolved but the member did not; inspect returned direct members or call `list_members`.
- `wrong_symbol_kind` → switch to the tool for the actual kind

For the complete tool reference and edge-case handling, consult the official skill:
[DecompilerServer MCP Skill](https://raw.githubusercontent.com/pardeike/DecompilerServer/main/skills/decompiler-mcp/SKILL.md)
