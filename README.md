# RimSearcher

[![Skills Update Time](https://img.shields.io/endpoint?url=https%3A%2F%2Fkearril.github.io%2FRimSearcher%2Fskills-update.json&cacheSeconds=300)](https://github.com/kearril/RimSearcher/commits/master/skills)

[English](README.en.md) | 简体中文

#### RimSearcher V3 全面焕新重置，工具从该版本开始，舍弃了过去的mcp架构，转而使用skills+cli的设计模式，这带来了更好的性能，更低的占用以及更智能的ai决策，并且现在支持模组环境的代码分析了！

## 介绍

RimSearcher 特化为 **Def 数据层**——XML 定义、字段结构、类型关联。C# 源码分析交由
[DecompilerServer](https://github.com/pardeike/DecompilerServer)，一个专门面向 Unity 程序集的
反编译 MCP 工具。它能直接反编译加载的 .NET 程序集，提供类型搜索、成员签名浏览、IL 指令级查看、
调用链追踪，以及跨版本方法体比对——让 AI 看到的不再是"可能存在的 API"，而是真正运行的代码。
正如其设计目标所言：*"I can inspect the actual code that runs"*。

Skill 文件将两者串联：CLI 定位 Def → 提取 C# 类型名 → DecompilerServer 读源码，形成完整的
分析管线。

多模组环境的支持来自两个层面的配合：DecompilerServer 可同时加载原版和任意模组的
`.dll` 程序集，各自分配独立上下文别名，AI 能够并排查看多个程序集的源码和 IL，精确定位 Hook 点
和兼容性边界。而 RimSearcher 的 DataMod 在游戏内将当前模组环境的 Def 数据导出为 SQLite 数据库，
CLI 为其提供全文检索——两者相辅相成，一个负责 C#，一个负责 XML 数据。

## 快速开始 

**不会安装？** 将下面这句话发送给你的 AI 助手，它会一步步引导你完成全部安装：

> Read https://raw.githubusercontent.com/kearril/RimSearcher/master/GUIDED_SETUP.md and guide me through the installation.

---

### 手动安装

如果你已经熟悉工具链，可以按以下步骤自行配置。

### 1. 下载

从 [Releases](https://github.com/kearril/RimSearcher/releases/latest) 下载：

| 文件 | 说明                                                                                                                                                                                                                                                          |
|---|---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| `rimsearcher.exe` | CLI 命令行工具                                                                                                                                                                                                                                                |
| `RimSearcher_DataMod.zip` | 游戏内def数据导出模组                                                                                                                                                                                                                                         |

> **skills 不随 Release 发布**：技能文件更新频繁且独立于 CLI/DataMod 功能，始终通过下方「配置 AI 技能」步骤从仓库直接获取最新版（无需等待 Release）。

还需要反编译 MCP：[DecompilerServer](https://github.com/pardeike/DecompilerServer) — 前往官网下载并配置该mcp工具。

### 2. 安装模组

解压 `RimSearcher_DataMod.zip` 到 RimWorld 的 `Mods/` 目录。启动游戏，在 Mod 列表中启用 **RimSearcherDataMod**。

### 3. 导出数据

进入游戏 → 选项 → Mod 设置 → RimSearcherDataMod → 点击`导出 Def 数据库`。

导出完成后，将生成的 `defs.db` 放到 `rimsearcher.exe` 同目录下。

### 4. 配置 CLI

在 `rimsearcher.exe` 所在目录打开终端，执行：

```bash
rimsearcher install
```

注意，完成这一步后，该exe文件请不要随意移动位置，否则系统会找不到对应的path，或者移动后重复该操作

### 5. 配置 AI 技能

解压 [下载 skills.zip](https://raw.githubusercontent.com/kearril/RimSearcher/master/skills.zip) 获取的文件（该链接始终指向仓库最新版，不依赖 Release），将 `skills/rimsearcher/` 放入你使用的 AI 助手的 skills 目录。
重启 AI 客户端后生效。

### 6.完成
重启后，可以开始进行测试和使用了

---

## 更新说明

| 组件 | 更新方式                                                                                                                                                                            |
|---|-------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| **rimsearcher CLI** | 当Release发行可用更新时，终端执行 `rimsearcher update`，自动从 GitHub Release 下载最新版替换当前 exe                                                                                |
| **rimsearcher Skill** | [下载 skills.zip](https://raw.githubusercontent.com/kearril/RimSearcher/master/skills.zip)，解压后覆盖 skills 目录。技能文件**不随 Release 发布**，始终从仓库获取最新版——每次提交推送后即可更新。 |
| **RimSearcher.DataMod** | 从 [Releases](https://github.com/kearril/RimSearcher/releases/latest) 下载新版 `RimSearcher_DataMod.zip`，解压覆盖 Mods 目录即可                                                    |

> 由于 skills 文件是影响ai决策的重要文件，可能频繁更新优化，而它的更新不会影响 CLI 和 datamod 的功能，
> 因此 skills 不会一有更新就发布 Release。如何判断 skills 是否有更新？看这个标徽或者页面顶部的 ![Skills Update Time](https://img.shields.io/endpoint?url=https%3A%2F%2Fkearril.github.io%2FRimSearcher%2Fskills-update.json) 徽章；它显示 `skills.zip` 最后一次更新的 UTC+8 时间。显示的时间比本地文件新就说明有更新。

## 组件

| 组件 | 说明                                                                                                                             |
|---|----------------------------------------------------------------------------------------------------------------------------------|
| **RimSearcher.DataMod** | 游戏内反射导出模组。运行时将当前加载的 Def 数据导出为 `defs.db`，label 和 description 为游戏当前语言的文本；游戏内 UI 支持英/简中/繁中；理论支持 Windows/macOS/Linux（macOS 与 Linux 未经实测） |
| **rimsearcher CLI** | .NET 命令行工具。10 个命令：`search` `list` `get` `find` `fields` `values` `types` `mods` `install` `update`                     |
| **rimsearcher Skill** | AI 助手技能文件。教 AI 使用 CLI + 反编译 MCP 定位和分析 RimWorld 源码，含反幻觉规则                                              |

## 构建

### 环境

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)

### 编译

```bash
# CLI 工具
dotnet publish Sources/RimSearcher.Cli/ -c Release -o Sources/RimSearcher.Cli/publish/
# 产物: Sources/RimSearcher.Cli/publish/rimsearcher.exe

# DataMod 模组
dotnet build Sources/RimSearcher.DataMod/ -c Release
# 产物: RimSearcher_DataMod/Assemblies/RimSearcher.DataMod.dll（含依赖）
#       RimSearcher_DataMod/Native/（全平台 SQLite 原生库，构建自动生成）
```

## 贡献 Skill


欢迎将你的 RimWorld Mod 开发经验贡献到 Skill 仓库。如果你有常用的分析流程、常见 Hook 点、
或特定模组的兼容性经验，可以提交 PR 扩展 Skill 文件，让 AI 助手变得更懂 RimWorld。

> 当前的rimsearcher 专用skills需要持续优化以覆盖更多的开发场景

## 功能说明

### search — 全文搜索

```
rimsearcher search <keyword> [--type T] [--mod M] [--limit N] [--count]
```

FTS5 全文索引覆盖 Def 名称、标签、描述和所有字段值。中英文混合查询，支持前缀通配和布尔组合。

### list — 分页浏览

```
rimsearcher list [--type T] [--mod M] [--limit N] [--offset N]
```

按类型或所属 Mod 浏览 Def 列表，支持分页。无搜索开销，按 def_type、def_name 排序。

### get — 精确定位

```
rimsearcher get <defName> [--type T] [--brief]
```

按名称定位 Def。`--brief` 提取 Def 中全部 `*Class` 桥接字段值（`thingClass`、`compClass`、`workerClass`、`hediffClass` 等，类型无关），
作为反编译 MCP 的搜索入口。多类型歧义时列出候选项。

### find — 反向查找

```
rimsearcher find <fieldPath> <value> [--type T] [--mod M] [--limit N]
```

给定字段路径和 C# 类名，查找所有引用该类的 Def——适合追踪某个 Comp 或 ThingClass 被哪些物品使用。

### fields — 字段树

```
rimsearcher fields <defName> --type <T> [--limit N]
```

列出单个 Def 的完整字段树，直观查看嵌套结构。

### values — 值枚举

```
rimsearcher values <fieldPath> [--limit N]
```

枚举任意字段路径的去重值集合。

### types — 类型统计

```
rimsearcher types
```

列出所有 Def 类型及其数量，降序排列。

### mods — Mod 统计

```
rimsearcher mods
```

列出所有 Mod 及各自的 Def 数量，降序排列。


### install — 安装到 PATH

```
rimsearcher install
```

将 rimsearcher 所在目录加入用户 PATH，全局可用。重复执行自动跳过。

### update — 自更新

```
rimsearcher update
```

从 GitHub Release 自动下载最新版本并替换当前可执行文件。

### AI 集成（Skill）

Skill 文件定义标准分析管线，AI 加载后自动按流程定位源码。内置反幻觉规则：
禁止猜测 API、Harmony Patch 前必须阅读目标方法的 IL。

### DataMod — 游戏内导出

RimSearcher.DataMod 是一个游戏内模组，运行时通过反射扫描 `DefDatabase<T>`，将当前模组环境
的所有 Def 数据导出为 SQLite 数据库。导出的 `defs.db` 包含 Def 序列化 JSON（深度上限 100）、字段值表
和 FTS5 全文索引，供 CLI 查询使用。

## 运行依赖

- [.NET 10 Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) — CLI 和 DecompilerServer 的运行环境
- [DecompilerServer](https://github.com/pardeike/DecompilerServer) — 反编译 MCP 服务，C# 源码分析必需

## 致谢

- [DecompilerServer](https://github.com/pardeike/DecompilerServer) — 强大的 .NET 反编译 MCP，提供了 C# 源码分析能力
- [RimWorld](https://rimworldgame.com) — 感谢 Ludeon Studios 创造的精彩游戏和开放的 Mod 生态

## 免责声明

RimSearcher 仅读取和分析你本地已安装的游戏数据，不捆绑、不分发任何 RimWorld 游戏文件或第三方模组资产。

使用本工具分析模组时，请注意被分析的模组受其各自的许可协议约束。若基于分析结果创作衍生内容，须遵守对应模组的开源规范。导出数据中可能包含模组作者的创作内容（Def 名称、描述文本等），版权归原作者所有。

本项目与 Ludeon Studios 无关联。RimWorld 为 Ludeon Studios 的商标。

## License

MIT
