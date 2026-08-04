# RimSearcher

[![Skills Update Time](https://img.shields.io/endpoint?url=https%3A%2F%2Fkearril.github.io%2FRimSearcher%2Fskills-update.json&cacheSeconds=300)](https://github.com/kearril/RimSearcher/commits/master/skills.zip)

[English](README.en.md) | 简体中文

> **设计哲学**：把工具的错误变成知识的输入——让模型从错误中学习。
> 错误即文档、失败即教学：每个限制与失败路径都设计为模型的学习素材。

#### RimSearcher V3 全面焕新重置，工具从该版本开始，舍弃了过去的mcp架构，转而使用skills+cli的设计模式，这带来了更好的性能，更低的占用以及更智能的 AI 决策，并且现在支持模组环境的代码分析了！

## 介绍

RimSearcher 是一套供 AI 使用的专业 RimWorld 源码分析工具链：既有 CLI 与游戏内模组构成的查询工具，也有教模型如何使用它们的技能——它不只是工具，也是老师。

RimSearcher 特化 Def 数据层（XML 定义、字段结构、类型关联）：游戏内的 DataMod 将当前模组环境的全部 Def 导出为 SQLite 数据库，CLI 提供全文检索与精确反查。C# 源码分析交由 [DecompilerServer](https://github.com/pardeike/DecompilerServer)——直接反编译加载的 .NET 程序集，类型搜索、成员签名、IL 指令、调用链追踪、跨版本比对，让 AI 看到的不再是"可能存在的 API"，而是真正运行的代码。正如其设计目标所言：*"I can inspect the actual code that runs"*。

Skill 文件将两者串联成一条分析管线：CLI 定位 Def → 提取 C# 类型名 → DecompilerServer 读源码。

多模组环境由两层配合支撑：DecompilerServer 同时加载原版与任意模组的程序集（各自独立上下文别名，并排查看源码与 IL，精确定位 Hook 点与兼容性边界）；DataMod 导出当前模组环境的 Def 数据供 CLI 查询——一个管代码，一个管数据，相辅相成。

## 歧途有灯——错误如何成为路标

行路者不问歧途，问的是歧途尽处的灯火。

工具把每一次折返都化作路标：凡查询无果，必有言示路——或指他途，或导别径；语法失语，则引之精确之门；版本相违，则告之以重来。纵使未获，亦非败绩——"此路无物"之讯，非责难，乃信息。

然最险者非风雷，乃无声之渊。空壳之器、错位之名、虚引之实——试错不可察者，择其要者录之，如航者之海图，标前人暗礁，使后来者免于重蹈。

工具指路，模型行路，行路者终识途——此项目之呼吸也。

在该项目开发的时候，我们发现，困扰大模型的从来不是错误，可怕的是不知道错在哪里的静默无声，因此我们在设计该工具时，站在模型的视角为其踩坑排障，让每一次错误都有意义——每个错误都会提示模型下一步该怎么做，每一次提示的背后，都是我们通过大量样本分析优化的结果：

查询无果时，hint 会给出下一步建议；语法出错时，提示改用精确匹配；拼写有误时，给出相似名候选……而"未找到"也是一种结果而非失败——exit 2 是预期空结果，模型无需误判重试。

但工具能提示的，只有它自己能察觉的错误。那些连工具本身都静默的问题，模型靠试错无法发现——我们把这些高频陷阱择要写进 skill，让模型提前避开。

我们相信：工具的错误应该成为模型的经验，而不是代价。错误即文档，失败即教学。

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
| `RimSearcher_DataMod.zip` | 游戏内 Def 数据导出模组                                                                                                                                                                                                                                         |

> **skills 不随 Release 发布**：始终通过下方「配置 AI 技能」步骤从仓库直接获取最新版。

还需要反编译 MCP：[DecompilerServer](https://github.com/pardeike/DecompilerServer) — 前往官网下载并配置该 MCP 工具。

### 2. 安装模组

解压 `RimSearcher_DataMod.zip` 到 RimWorld 的 `Mods/` 目录。启动游戏，在 Mod 列表中启用 **RimSearcherDataMod**。

### 3. 导出数据

进入游戏 → 选项 → Mod 设置 → RimSearcherDataMod → 点击`导出 Def 数据库`。

导出完成后，将生成的 `defs.db` 放到 `rimsearcher.exe` 同目录下。

### 4. 配置 CLI

将 `rimsearcher.exe` 所在目录加入系统 PATH。若不清楚如何操作，请教你的 AI 助手。

配置成功后，在任意终端执行：

```bash
rimsearcher --version
```

应显示当前版本号。

配置完成后，若移动 `rimsearcher.exe`，需重新配置 PATH。

### 5. 配置 AI 技能

下载并解压 [skills.zip](https://raw.githubusercontent.com/kearril/RimSearcher/master/skills.zip)（该链接始终指向仓库最新版），将 `skills/rimsearcher/` 放入你使用的 AI 助手的 skills 目录。
### 6. 完成
重启 AI 客户端后，可以开始进行测试和使用了

---

## 更新说明

| 组件 | 更新方式                                                                                                                                       |
|---|------------------------------------------------------------------------------------------------------------------------------------------------|
| **rimsearcher CLI** | 从[Releases](https://github.com/kearril/RimSearcher/releases/latest) 下载新版 `rimsearcher.exe` 替换原文件，并同步更新 DataMod、重新导出数据库 |
| **rimsearcher Skill** | [下载 skills.zip](https://raw.githubusercontent.com/kearril/RimSearcher/master/skills.zip)，解压后覆盖 skills 目录。                           |
| **RimSearcher.DataMod** | 从 [Releases](https://github.com/kearril/RimSearcher/releases/latest) 下载新版 `RimSearcher_DataMod.zip`，解压替换原来的模组，并重新导出数据库 |

> 由于 skills 文件是影响 AI 决策的重要文件，可能频繁更新优化，
> 因此 skills 不跟随 Release 发布。如何判断 skills 是否有更新？看这个徽章或者页面顶部的 ![Skills Update Time](https://img.shields.io/endpoint?url=https%3A%2F%2Fkearril.github.io%2FRimSearcher%2Fskills-update.json) 徽章；它显示 `skills.zip` 最后一次更新的 UTC+8 时间。显示的时间比本地文件新就说明有更新。

## 组件

| 组件 | 说明                                                                                                               |
|---|--------------------------------------------------------------------------------------------------------------------|
| **RimSearcher.DataMod** | 游戏 Def 数据导出模组。运行时将当前加载的 Def 数据导出为 `defs.db`，label 和 description 为游戏当前语言的文本；      |
| **rimsearcher CLI** | .NET 命令行工具。9 个命令：`search` `list` `get` `find` `fields` `values` `types` `mods` `check update` |
| **rimsearcher Skill** | AI 助手技能文件。教 AI 使用 CLI + 反编译 MCP 定位和分析 RimWorld 源码，含反幻觉规则与数据验证指令                  |


## 功能说明

### CLI 命令介绍

```bash
# search — 全文模糊搜索
rimsearcher search <keyword> [--type T] [--mod M] [--limit N] [--count] [--name-only]
# list — 分页浏览
rimsearcher list [--type T] [--mod M] [--limit N] [--offset N] [--total]
# get — 精确定位
rimsearcher get <defName> [--type T] [--brief] [--field <路径>]
# find — 字段值精确反查
rimsearcher find <fieldPath> <value> [--type T] [--mod M] [--limit N]
# fields — 字段树
rimsearcher fields <defName> --type <T> [--limit N] [--filter <glob>]
# values — 字段路径去重值枚举
rimsearcher values <fieldPath> [--type T] [--limit N]
# types — 类型统计
rimsearcher types
# mods — Mod 统计
rimsearcher mods
# check update — 检查更新
rimsearcher check update
```

### AI 集成（Skill）

Skill 是工具链的灵魂所在——它教 AI 如何分析，而不只是能用什么工具。

面对不同的问题，AI 需要选择不同的路径：是快速定位，还是深入机制的全链路分析。路径一旦选定，结论就有了方法论的约束——每一项 Def 数值都要与反编译公式交叉核对，让每个结论都能追溯到命令输出或源码本身。分析中最大的风险不是出错，而是编造：skill 明确禁止猜测与虚构，当信息不足时，AI 会显式标注不确定之处，并说明缺少什么——诚实是分析的第一原则。

### DataMod — 游戏内导出

RimSearcher.DataMod 是一个游戏内模组：将当前模组环境的全部 Def 数据导出为 SQLite 数据库供 CLI 查询，
库与 CLI 版本锁定，升级任一方后必须重新导出。

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
或特定模组的兼容性经验，可以提交 PR 扩展 Skill 文件，让 AI 助手变得更懂 RimWorld，这使每一位 RimSearcher 的用户受益。

## 运行依赖

- [.NET 10 Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) — CLI 和 DecompilerServer 的运行环境
- [DecompilerServer](https://github.com/pardeike/DecompilerServer) — 反编译 MCP 服务，C# 源码分析必需

## 致谢

- [DecompilerServer](https://github.com/pardeike/DecompilerServer) — 强大的 .NET 反编译 MCP，提供了 C# 源码分析能力
- [RimWorld](https://rimworldgame.com) — 感谢 Ludeon Studios 创造的精彩游戏和开放的 Mod 生态

## 免责声明

- RimSearcher 仅读取和分析你本地已安装的游戏数据，不捆绑、不分发任何 RimWorld 游戏文件或第三方模组资产。
- 被分析的模组受其各自许可协议约束，基于分析结果创作衍生内容须遵守对应模组的开源规范。
- 本项目与 Ludeon Studios 无关联，RimWorld 为 Ludeon Studios 的商标。

## License

MIT
