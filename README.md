# RimSearcher
[![Latest Release](https://img.shields.io/github/v/release/kearril/RimSearcher?style=flat-square&color=333&logo=github)](https://github.com/kearril/RimSearcher/releases/latest)

一个基于 MCP 的 RimWorld 源码检索与分析服务。它把本地 RimWorld C# / XML 数据建立为可查询索引，让 AI 助手能在真实源码上定位、追踪、阅读和解释逻辑，减少“幻觉式回答”。

采用 Roslyn + XML 继承解析，支持高并发只读查询。
> MCP 协议版本: `2025-11-25`

---

## 1. 核心特性

### 精准 C# 解析（Roslyn）
- 单次解析提取类型继承和成员索引（方法/属性/字段/事件）
- 支持类大纲、成员体提取、继承链追踪
- 支持方法、属性、构造器、索引器、运算符级别读取

### XML Def 继承合并
- 递归解析 `ParentName` 链路
- 合并父子节点并处理列表容器/覆盖逻辑
- 输出可直接阅读的“最终 Def 结果”

### C# 与 XML 语义桥接
- 从 Def 自动提取关联 C# 类型（如 thingClass / compClass / workerClass）
- 在 `inspect` 中同时展示 Def 信息与关联代码路径

### 面向查询性能优化
- 预建索引 + N-gram 候选筛选
- 启动后冻结索引（`FrozenDictionary`）优化只读查询吞吐
- 搜索结果带上限控制，避免超长输出拖慢上下文

### 运行模型与边界
- 本地运行，核心检索不依赖网络
- 网络请求仅用于版本更新提示（可关闭）

---

## 2. 六大工具

以下为实际注册的 MCP 工具名与能力说明。

### 🔎 `rimworld-searcher__locate`
全局模糊定位入口。

**支持内容**
- C# 类型、成员（方法/属性/字段）、XML Def、文件名
- 过滤语法：`type:` `method:` `field:` `def:`
- CamelCase 缩写与拼写容错（如 `JDW`）

**示例查询**
```text
def:Apparel_ShieldBelt
type:CompShield
method:CompTick
field:energy
```

---

### 🔬 `rimworld-searcher__inspect`
深度分析单个 Def 或 C# 类型。

**Def 模式**
- 展示 Def 类型、来源文件
- 返回继承合并后的 XML
- 提取关联 C# 类型并尝试映射到索引文件

**C# 模式**
- 返回继承关系图
- 返回类成员大纲（字段/属性/方法签名）

**示例**
```text
Apparel_ShieldBelt
RimWorld.CompShield
```

---

### 🔗 `rimworld-searcher__trace`
交叉引用追踪工具。

**模式**
- `inheritors`：列出某基类/接口的子类
- `usages`：查找符号文本引用（C# + XML），带行号预览

**示例**
```text
symbol: ThingComp, mode: inheritors
symbol: CompShield, mode: usages
```

---

### 📖 `rimworld-searcher__read_code`
精确读取 C# 代码片段。

**支持读取方式**
- 指定成员：`methodName`（支持方法/属性/构造器/索引器/运算符）
- 指定类型：`extractClass`
- 指定行区间：`startLine` + `lineCount`

**路径支持**
- 绝对路径
- 已索引文件名（如 `CompShield.cs`）
- 文件基名（如 `CompShield`）

**示例**
```text
path: CompShield.cs, methodName: CompTick
```

---

### 🔤 `rimworld-searcher__search_regex`
全局正则检索（C# + XML）。

**特性**
- 可选 `fileFilter`（如 `.cs` / `.xml`）
- 结果按文件分组，显示行号预览
- 内置输出截断提示，避免超大响应

**示例**
```text
pattern: class.*:.*ThingComp
fileFilter: .cs
```

---

### 📁 `rimworld-searcher__list_directory`
目录浏览工具。

**特性**
- 列出目录下文件与子目录（子目录以 `/` 结尾）
- 支持 `limit` 分页提示
- 受 `PathSecurity` 白名单约束（除非显式关闭）

---

## 2.5 系统架构

```text
                                           RimSearcher Architecture (Wide)

┌───────────────────────────┐
│ MCP Client                │
│ Claude / Cursor / VSCode  │
└──────────────┬────────────┘
               │ JSON-RPC (MCP)
               v
┌───────────────────────────────────────────────────────────────────────────────────────────────────────────────┐
│ MCP Runtime: RimSearcher.cs                                                                                  │
│ - request routing  - concurrency limit  - cancel request  - progress notify  - logging bridge (ServerLogger)│
└───────────────────────────────┬───────────────────────────────────────────────────────────────────────────────┘
                                │
                                v
┌───────────────────────────────────────────────────────────────────────────────────────────────────────────────┐
│ Tool Layer                                                                                                    │
│ locate | inspect | trace | read_code | search_regex | list_directory                                         │
└───────────────────────┬───────────────────────────────────────────────────────────────────────┬───────────────┘
                        │                                                                       │
                        v                                                                       v
┌───────────────────────────────────────────────┐                               ┌───────────────────────────────────────────────┐
│ SourceIndexer                                 │                               │ DefIndexer                                    │
│ - C# type/member/file index                   │                               │ - XML def/content index                       │
│ - inheritance lookup                          │                               │ - defName/parent/label/content search         │
│ - regex scan                                  │                               │ - parent-chain query support                  │
│ - FrozenDictionary read path                  │                               │ - FrozenDictionary read path                  │
└───────────────────────┬───────────────────────┘                               └───────────────────────┬───────────────────────┘
                        │                                                                       │
                        v                                                                       v
┌───────────────────────────────────────────────┐                               ┌───────────────────────────────────────────────┐
│ RoslynHelper / FuzzyMatcher / QueryParser     │                               │ XmlInheritanceHelper / FuzzyMatcher           │
│ PathSecurity (path validation when enabled)   │                               │ QueryParser / PathSecurity                    │
└───────────────────────┬───────────────────────┘                               └───────────────────────┬───────────────────────┘
                        │                                                                       │
                        v                                                                       v
┌───────────────────────────────────────────────┐                               ┌───────────────────────────────────────────────┐
│ Local C# Source (Assembly-CSharp)             │                               │ Local RimWorld XML Data (Data/Defs...)        │
└───────────────────────────────────────────────┘                               └───────────────────────────────────────────────┘
```

**启动流程**
1. 读取配置（优先 `RIMSEARCHER_CONFIG`，未设置时回退到同目录 `config.json`）
2. 初始化路径安全策略
3. 扫描 C# / XML 并建索引
4. 冻结索引（读优化）
5. 注册工具并启动 MCP 服务

---

## 3. 典型工作流

### 场景：分析护盾腰带如何生效
1. `locate(def:Apparel_ShieldBelt)`：定位 Def
2. `inspect(Apparel_ShieldBelt)`：看合并后 XML 与关联 C# 类型
3. `inspect(RimWorld.CompShield)`：看继承链和类大纲
4. `read_code(path=CompShield.cs, methodName=CompTick)`：读取核心逻辑
5. `trace(symbol=CompShield, mode=usages)`：追踪相关引用

---

## 4. 性能与安全

| 维度 | 当前实现 |
|------|----------|
| 索引策略 | 启动扫描后冻结索引（`FrozenDictionary`） |
| 模糊匹配 | N-gram 候选过滤 + 评分排序 |
| 并发控制 | MCP 请求并发上限 10 |
| 正则搜索保护 | 全局/单文件命中上限 + 行数上限 + regex 超时 |
| 路径安全 | 白名单根目录校验（`SkipPathSecurity=false` 时生效） |
| XML 安全 | 禁用 DTD，防 XXE |

---

## 5. 快速开始

#### 视频教程
- [B站视频教程（点击跳转）](https://www.bilibili.com/video/BV1w1cJz7E9t?vd_source=624604839a08e42cea3a8cb45151b201)

### 前置要求
- 运行发布包：.NET 10 Runtime（`--self-contained false` 发布时需要）
- 源码构建：.NET 10 SDK

### 安装步骤
1. 从 [Releases](https://github.com/kearril/RimSearcher/releases) 下载 `RimSearcher.Server.exe`。
2. 创建 `config.json`

配置示例：
```json
{
  "CsharpSourcePaths": [
    "C:/Path/To/Your/RimWorld/Source"
  ],
  "XmlSourcePaths": [
    "C:/SteamLibrary/steamapps/common/RimWorld/Data"
  ],
  "SkipPathSecurity": false,
  "CheckUpdates": true
}
```

字段说明：
- `CsharpSourcePaths`: C# 源码目录（反编译源码目录）
- `XmlSourcePaths`: RimWorld `Data` 目录
- `SkipPathSecurity`: `true` 时关闭路径白名单检查（仅建议本地可信环境）
- `CheckUpdates`: 是否启用版本更新提示

3. 在 MCP 客户端中把 `RimSearcher.Server.exe` 注册为 **stdio MCP Server**，并设置环境变量 `RIMSEARCHER_CONFIG` 指向上一步的 `config.json`。

> 兼容模式说明：
> - 若设置了 `RIMSEARCHER_CONFIG`，优先读取该路径。
> - 若未设置，则回退到 `RimSearcher.Server.exe` 同目录下的 `config.json`。

### 安装到 AI 助手（不同客户端配置差异）

#### 通用 MCP 客户端（Claude Desktop / Gemini CLI / Cursor 等）
```json
{
  "mcpServers": {
    "RimSearcher": {
      "command": "D:/Tools/RimSearcher/RimSearcher.Server.exe",
      "args": [],
      "env": {
        "RIMSEARCHER_CONFIG": "D:/your/custom/path/config.json"
      }
    }
  }
}
```

#### GitHub Copilot（`servers` 结构）
```json
{
  "servers": {
    "RimSearcher": {
      "command": "D:/Tools/RimSearcher/RimSearcher.Server.exe",
      "args": [],
      "env": {
        "RIMSEARCHER_CONFIG": "D:/your/custom/path/config.json"
      }
    }
  }
}
```

#### OpenCode（`mcp` 结构）
```json
{
  "mcp": {
    "RimSearcher": {
      "type": "local",
      "command": ["D:/Tools/RimSearcher/RimSearcher.Server.exe"],
      "enabled": true,
      "environment": {
        "RIMSEARCHER_CONFIG": "D:/your/custom/path/config.json"
      }
    }
  }
}
```

常见注意事项：
- `command` 使用 `RimSearcher.Server.exe` 的绝对路径。
- 推荐始终配置 `RIMSEARCHER_CONFIG` 指向明确路径，避免多环境切换时误读配置。
- 若不设置 `RIMSEARCHER_CONFIG`，才要求 `config.json` 与 exe 在同一目录。
- 修改客户端 MCP 配置后，重启客户端或重载 MCP 服务。
- 若客户端有工具白名单/权限开关，确保已允许 `RimSearcher`。

### 本地验证
手动验证时：
- 方式 A：设置环境变量 `RIMSEARCHER_CONFIG` 指向目标 `config.json`。
- 方式 B：不设置环境变量，把 `config.json` 放到 `RimSearcher.Server.exe` 同目录。

![配置示例](Image/Snipaste_2026-02-07_23-20-57.png)

然后运行 `RimSearcher.Server.exe`，若看到类似 `Program: Index build completed ...` 与 `Program: RimSearcher MCP server started` 日志，表示启动成功。

![启动成功示例](Image/Snipaste_2026-02-09_20-34-49.png)

快速检查是否接入成功：
- 客户端工具列表中能看到 `rimworld-searcher__locate`、`rimworld-searcher__inspect` 等 6 个工具。
- 执行一次 `locate`（例如 `def:Apparel_ShieldBelt`）能返回结果。

---

## 6. 更新提示说明

- 更新检查为非阻塞后台任务，不影响核心检索服务。
- 仅在 `CheckUpdates=true` 时启用。
- 若遇到 GitHub 匿名限流，更新检查会静默失败，不影响工具功能。

---

## License
MIT

> 如果这个项目对你有帮助，欢迎点个 Star⭐。

