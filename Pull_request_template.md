# Pull Request: 添加 Mod 数据索引和 Patch 查询支持

## 标题
```
feat: 添加 Mod 数据索引和 Patch 查询支持
```

## 描述
本 PR 为 RimSearcher 添加了 Mod 数据索引和 Patch 查询支持，让开发者能够索引 Mod 的源码和 Defs，从而更高效地进行 Mod 开发。

---

## 针对原作者担忧的解决方案

原作者在项目定位中提出"以最少的 token 消耗对本地静态源码进行精确检索与分析"，并对 Mod 支持提出了以下担忧。本 PR 针对这些担忧设计了相应的解决方案：

### 担忧一：XML Patch 处理困难

**原担忧**：游戏本体的 XML Patch 非常少，而 Mod 会大量运用。项目的 XML 解析策略是直接返回继承合并后的最终 Def 以减少 token 消耗，但这导致对 XML Patch 的解析困难。

**解决方案**：采用"知情但不模拟"策略
- 新增 `PatchIndexer` 索引 XML Patch 文件的目标 Def 和操作类型
- 在 `inspect` 工具返回时附加 Patch 数量提示，让 LLM 感知 Patch 存在
- 新增 `list_patches` 工具供 LLM 按需查询 Patch 详情
- **不尝试模拟 Patch 合并结果**，避免静态分析的复杂性

**设计理由**：保持"最少 token 消耗"原则，Patch 信息采用"提示+按需查询"模式，LLM 可自主决定是否深入了解。

### 担忧二：Harmony Patch 处理容易造成 LLM 混乱

**原担忧**：Harmony 补丁较为特殊，是运行时注入的。如果每次 `read_code` 后都自动查询该方法是否被 Patch，会增加 LLM 的混乱和 token 消耗。

**解决方案**：被动提示而非主动查询
- 新增 `HarmonyPatchIndexer` 索引 `[HarmonyPatch]` 特性声明
- 在 `inspect` C# 类型时附加 Harmony Patch 数量提示
- LLM 可通过 `list_patches` 工具按需查询，而非每次 `read_code` 都触发

**设计理由**：避免增加 LLM 混乱，让 LLM 自主决定何时需要了解 Patch 信息。

### 担忧三：Transpiler 解析无能为力

**原担忧**：Transpiler 补丁直接操作 IL 代码，而项目是对本地源码的静态分析，LLM 无法看到源码的 IL Code。曾考虑引入 ICSharpCode.Decompiler 但需要大改架构。

**解决方案**：明确功能边界
- ✅ 索引 Transpiler 的存在和目标方法（让 LLM 知道有这个 Patch）
- ❌ 不分析 Transpiler 的实际效果（保持架构简洁）
- 在文档中明确说明此局限性

**设计理由**：保持项目架构简洁，暂不引入反编译库。后续可考虑作为独立功能扩展。

### 担忧四：Mod 文件结构复杂性

**原担忧**：Mod 可能同时存在多个版本文件夹，Patch 文件位置因作者而异（可能在版本目录也可能在根目录）。

**解决方案**：全面探测策略
- 自动探测所有版本目录（`1.3/`、`1.4/`、`1.5/`）
- 同时索引根目录和版本目录下的 Patch
- 支持显式配置覆盖自动探测
- 在日志中输出探测结果，方便调试

### 担忧五：直接喂给 LLM 违背设计初衷

**原担忧**：直接把 Mod 文件全部喂给 LLM 会违背"最少 token 消耗"的设计初衷。

**解决方案**：明确功能边界

| 能做 | 不能做 |
|------|--------|
| 索引 Patch 文件位置和目标 | 模拟 Patch 合并结果 |
| 索引 Harmony Patch 声明 | 分析 Transpiler 实际效果 |
| 提供 Patch 查询能力 | 反编译 DLL 文件 |
| 让 LLM 感知 Patch 存在 | 预测运行时行为 |

---

## 新增功能

### Mod 配置
- 支持在 `config.json` 中配置多个 Mod 路径
- 可通过 `enabled` 字段控制 Mod 是否参与索引
- 可通过 `csharpPaths` 和 `xmlPaths` 显式指定源码和 Defs 目录

### Mod 目录自动探测
- 自动探测 `Source/` 目录（C# 源码）
- 自动探测 `Defs/` 目录（XML Defs）
- 自动探测 `Patches/` 目录（XML Patch）
- 自动探测版本目录（`1.3/`、`1.4/`、`1.5/` 等）
- 支持版本目录优先级：显式配置 > 版本目录 > 根目录

### Patch 索引
- XML Patch 文件索引（目标 Def、操作类型、来源文件）
- Harmony Patch 声明索引（目标方法、Patch 类型、来源信息）
- 新增 `list_patches` MCP 工具
- `inspect` 工具显示 Patch 数量提示

### 缓存管理
- Mod 配置变化时自动重建索引缓存
- 配置指纹计算包含 Mod 配置信息

---

## 文件变更

### 新增文件
- `Sources/RimSearcher.Core/Core/ModConfig.cs` - Mod 配置记录类型
- `Sources/RimSearcher.Core/Core/ModPathResolver.cs` - Mod 路径解析器
- `Sources/RimSearcher.Core/Core/PatchIndexer.cs` - XML Patch 索引器
- `Sources/RimSearcher.Core/Core/HarmonyPatchIndexer.cs` - Harmony Patch 索引器
- `Sources/RimSearcher.Server/Tools/ListPatchesTool.cs` - Patch 查询工具

### 修改文件
- `Sources/RimSearcher.Core/Core/IndexCacheModels.cs` - 缓存模型结构
- `Sources/RimSearcher.Core/Core/RoslynHelper.cs` - 扩展 Harmony Patch 解析
- `Sources/RimSearcher.Server/AppConfig.cs` - 添加 `Mods` 属性
- `Sources/RimSearcher.Server/Program.cs` - 集成新索引器
- `Sources/RimSearcher.Server/Tools/InspectTool.cs` - 添加 Patch 提示功能
- `README.md` - 更新文档
- `CHANGELOG.md` - 新增变更日志

---

## 配置示例

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

---

## 已知局限

1. **XML Patch 合并**：无法模拟 Patch 的运行时合并效果，LLM 需要自行理解 Patch 逻辑
2. **Transpiler 分析**：只能索引 Transpiler 的存在，无法分析其修改 IL 的具体效果
3. **DLL 反编译**：暂不支持反编译 Mod 的 DLL 文件
4. **运行时行为**：所有分析基于静态源码，无法预测运行时行为

---

## 后续改进方向

- [ ] 考虑引入 IL 反编译库支持 Transpiler 分析
- [ ] 探索 XML Patch 部分合并的可行性
- [ ] 优化 Mod 版本目录的优先级策略
