# Changelog

All notable changes to this project will be documented in this file.

## [Unreleased]

### Fixed

#### MCP 协议兼容性修复
- 修复 `tools/list` 响应中 `annotations` 字段为 `null` 导致 MCP 客户端验证失败的问题
- 当工具没有图标时，不再包含 `annotations` 字段（而非设置为 `null`）
- 符合 MCP 协议规范：`annotations` 必须是 `object` 类型或不存在

#### RimWorld 1.6 版本支持
- `ModPathResolver.VersionDirNames` 新增 `1.6` 版本目录支持
- 修复使用 `1.6/` 版本目录的 Mod 无法被正确索引的问题

### Added - Mod 数据索引支持

#### Mod 配置
- 新增 `mods` 配置项，支持配置多个 Mod 路径
- 支持通过 `enabled` 字段控制 Mod 是否参与索引
- 支持通过 `csharpPaths` 和 `xmlPaths` 显式指定 Mod 的源码和 Defs 路径

#### Mod 目录自动探测
- 自动探测 Mod 的 `Source/` 目录（C# 源码）
- 自动探测 Mod 的 `Defs/` 目录（XML Defs）
- 自动探测 Mod 的 `Patches/` 目录（XML Patch）
- 自动探测版本目录（`1.3/`、`1.4/`、`1.5/` 等），优先索引版本目录内容
- 显式配置路径优先于自动探测

#### 缓存兼容
- Mod 配置变化时自动重建索引缓存
- 配置指纹计算包含 Mod 配置信息

#### 日志增强
- 启动时输出每个 Mod 的加载状态
- 输出 Mod 版本目录探测结果
- 输出 Mod 索引路径统计

### Added - Patch 索引支持

#### XML Patch 索引
- 新增 `PatchIndexer` 索引 XML Patch 文件
- 提取 Patch 目标 Def 名称、操作类型（Add/Remove/Replace/Insert）
- 从 XPath 中提取目标 Def 信息
- 支持快照导出/导入

#### Harmony Patch 索引
- 新增 `HarmonyPatchIndexer` 索引 Harmony Patch 声明
- 解析 `[HarmonyPatch]` 特性，提取目标类型和方法
- 识别 Patch 类型（Prefix/Postfix/Transpiler/Finalizer）
- 支持快照导出/导入

#### 新增 MCP 工具
- `list_patches`：查询指定 Def 或方法的相关 Patch
  - 支持查询 XML Patch
  - 支持查询 Harmony Patch
  - 返回 Patch 来源文件、Mod 名称、操作类型等信息

#### Inspect 工具增强
- 在 inspect Def 时显示 XML Patch 数量提示
- 在 inspect C# 类型时显示 Harmony Patch 数量提示

### Changed

- `AppConfig` 新增 `Mods` 属性（向后兼容）
- `IndexCacheSnapshot` 新增 `Patch` 和 `HarmonyPatch` 字段
- `InspectTool` 构造函数新增可选参数 `patchIndexer` 和 `harmonyPatchIndexer`

### Configuration Example

```json
{
  "csharpSourcePaths": ["D:/RimWorld/Source"],
  "xmlSourcePaths": ["D:/RimWorld/Data"],
  "mods": [
    {
      "name": "HugsLib",
      "path": "D:/RimWorld/Mods/HugsLib"
    },
    {
      "name": "JecsTools",
      "path": "D:/RimWorld/Mods/JecsTools",
      "enabled": false
    },
    {
      "name": "CustomMod",
      "path": "D:/RimWorld/Mods/CustomMod",
      "csharpPaths": ["Source", "AdditionalSource"],
      "xmlPaths": ["Defs", "MyCustomDefs"]
    }
  ],
  "skipPathSecurity": false,
  "checkUpdates": true
}
```

### Design Principles

#### Token 消耗控制
- Patch 信息采用"提示+按需查询"模式，不主动返回完整内容
- 索引仅存储元数据，不存储完整 Patch 内容
- LLM 可通过 `list_patches` 工具按需获取详情

#### 功能边界
- ✅ 索引 Patch 文件位置和目标
- ✅ 索引 Harmony Patch 声明
- ✅ 提供 Patch 查询能力
- ❌ 不模拟 Patch 合并结果
- ❌ 不分析 Transpiler 的实际效果
- ❌ 不反编译 DLL 文件
