# RimSearcher

[![Skills Update Time](https://img.shields.io/endpoint?url=https%3A%2F%2Fkearril.github.io%2FRimSearcher%2Fskills-update.json&cacheSeconds=300)](https://github.com/kearril/RimSearcher/commits/master/skills.zip)

[English](README.en.md) | 简体中文

> **设计哲学**：把工具的错误变成知识的输入——让模型从错误中学习。
> 错误即文档、失败即教学：每个限制与失败路径都设计为模型的学习素材。

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
CLI 为其提供全文检索——两者相辅相成，一个负责 C# 源码，一个负责 def 数据的导出与查询。

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

> **skills 不随 Release 发布**：始终通过下方「配置 AI 技能」步骤从仓库直接获取最新版。

还需要反编译 MCP：[DecompilerServer](https://github.com/pardeike/DecompilerServer) — 前往官网下载并配置该mcp工具。

### 2. 安装模组

解压 `RimSearcher_DataMod.zip` 到 RimWorld 的 `Mods/` 目录。启动游戏，在 Mod 列表中启用 **RimSearcherDataMod**。

### 3. 导出数据

进入游戏 → 选项 → Mod 设置 → RimSearcherDataMod → 点击`导出 Def 数据库`。

导出完成后，将生成的 `defs.db` 放到 `rimsearcher.exe` 同目录下。

### 4. 配置 CLI

将 `rimsearcher.exe` 所在目录加入系统 PATH（命令行伪代码）：

```bash
reg add "HKCU\Environment" /v Path /t REG_EXPAND_SZ /d "<原Path值>;<rimsearcher.exe所在目录>" /f
```

> 必须使用 `REG_EXPAND_SZ` 类型以保留 `%VAR%` 变量展开；写入前可用 `reg query "HKCU\Environment" /v Path` 查看原值。

配置完成后，若移动 `rimsearcher.exe`，需重新配置 PATH。

### 5. 配置 AI 技能

解压 [下载 skills.zip](https://raw.githubusercontent.com/kearril/RimSearcher/master/skills.zip) 获取的文件（该链接始终指向仓库最新版），将 `skills/rimsearcher/` 放入你使用的 AI 助手的 skills 目录。
重启 AI 客户端后生效。

### 6.完成
重启后，可以开始进行测试和使用了

> **平台支持**：Windows 为开发/测试环境；CLI 与 DataMod 代码兼容 macOS/Linux（理论支持），但暂无 Mac/Linux 发布产物，且未经实测。

---

## 更新说明

| 组件 | 更新方式                                                                                                                                       |
|---|------------------------------------------------------------------------------------------------------------------------------------------------|
| **rimsearcher CLI** | 从[Releases](https://github.com/kearril/RimSearcher/releases/latest) 下载新版 `rimsearcher.exe` 替换原文件，并同步更新 DataMod、重新导出数据库 |
| **rimsearcher Skill** | [下载 skills.zip](https://raw.githubusercontent.com/kearril/RimSearcher/master/skills.zip)，解压后覆盖 skills 目录。                           |
| **RimSearcher.DataMod** | 从 [Releases](https://github.com/kearril/RimSearcher/releases/latest) 下载新版 `RimSearcher_DataMod.zip`，解压替换原来的模组，并重新导出数据库 |

> 由于 skills 文件是影响ai决策的重要文件，可能频繁更新优化，
> 因此 skills 不跟随 Release 发布。如何判断 skills 是否有更新？看这个标徽或者页面顶部的 ![Skills Update Time](https://img.shields.io/endpoint?url=https%3A%2F%2Fkearril.github.io%2FRimSearcher%2Fskills-update.json) 徽章；它显示 `skills.zip` 最后一次更新的 UTC+8 时间。显示的时间比本地文件新就说明有更新。

## 组件

| 组件 | 说明                                                                                                               |
|---|--------------------------------------------------------------------------------------------------------------------|
| **RimSearcher.DataMod** | 游戏def数据导出模组。运行时将当前加载的 Def 数据导出为 `defs.db`，label 和 description 为游戏当前语言的文本；      |
| **rimsearcher CLI** | .NET 命令行工具。9 个命令：`search` `list` `get` `find` `fields` `values` `types` `mods` `check update` |
| **rimsearcher Skill** | AI 助手技能文件。教 AI 使用 CLI + 反编译 MCP 定位和分析 RimWorld 源码，含反幻觉规则与数据验证指令                  |

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
#       RimSearcher_DataMod/Native/（SQLite 原生库，构建自动生成）
```

## 贡献 Skill


欢迎将你的 RimWorld Mod 开发经验贡献到 Skill 仓库。如果你有常用的分析流程、常见 Hook 点、
或特定模组的兼容性经验，可以提交 PR 扩展 Skill 文件，让 AI 助手变得更懂 RimWorld，这使每一位rimsearcher的用户受益。

## 功能说明

```bash
# search — 全文搜索：关键词模糊匹配（中英文，支持通配符与布尔组合；单个裸词另匹配defname/lab；--name-only 限定名称列）
rimsearcher search <keyword> [--type T] [--mod M] [--limit N] [--count] [--name-only]
# list — 分页浏览：按类型/Mod 列出 Def
rimsearcher list [--type T] [--mod M] [--limit N] [--offset N] [--total]
# get — 精确定位：按 defName 取单个 Def（--brief 提取 *Class 类名与多态 $type，--field 提取单字段）
rimsearcher get <defName> [--type T] [--brief] [--field <路径>]
# find — 反向查找：字段值精确匹配，追踪某 C# 类被哪些 Def 引用
rimsearcher find <fieldPath> <value> [--type T] [--mod M] [--limit N]
# fields — 字段树：查看单个 Def 的完整嵌套结构（--filter 支持路径 glob）
rimsearcher fields <defName> --type <T> [--limit N] [--filter <glob>]
# values — 值枚举：列出字段路径的全部去重值
rimsearcher values <fieldPath> [--type T] [--limit N]
# types — 类型统计
rimsearcher types
# mods — Mod 统计
rimsearcher mods
# check update — 检查更新：查询 GitHub Release 是否有新版
rimsearcher check update
```

### AI 集成（Skill）

Skill 文件教 AI 使用 CLI 与反编译 MCP 按标准管线分析 RimWorld 机制，内置反幻觉规则防止编造结论与数据可信度验证。

### DataMod — 游戏内导出

RimSearcher.DataMod 是一个游戏内模组：将当前模组环境的全部 Def 数据导出为 SQLite 数据库供 CLI 查询，
库与 CLI 版本锁定，升级任一方后必须重新导出。

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
