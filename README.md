# RimSearcher: RimWorld 源码检索分析 MCP 服务器
[![Latest Release](https://img.shields.io/github/v/release/kearril/RimSearcher?style=flat-square&color=333&logo=github)](https://github.com/kearril/RimSearcher/releases/latest)
[![Total Downloads](https://img.shields.io/github/downloads/kearril/RimSearcher/total?style=flat-square&color=333&logo=github)](https://github.com/kearril/RimSearcher/releases)
[![AI-Assisted](https://img.shields.io/badge/AI--Assisted-Gemini-333?style=flat-square&logo=google-gemini)](https://deepmind.google/technologies/gemini/)

RimSearcher 是 一个 基于 Model Context Protocol (MCP) 构建的高性能服务器，旨在为 AI 助手（如 Gemini, Claude 等）提供对 RimWorld 游戏源码（C#）和配置文件（XML）的高效检索与深度分析能力。

本项目专门针对 RimWorld 模组开发和源码研究而设计，利用 C# 14 和 .NET 10 的先进特性，结合 Roslyn 编译器平台，彻底解决了 LLM 因无法直接访问本地源码而导致的“知识盲区”和“幻觉”问题。

> 该MCP服务器当前采用的是最新的MCP通讯协议 2025-11-25 版

---

## 1. 核心优势

🔍 **深度集成 Roslyn**  
不同于普通的文本搜索，RimSearcher 深度集成 Microsoft.CodeAnalysis，真正理解 C# 语法树（AST）。支持：
- 精准提取特定方法的源代码（含签名和闭合括号）
- 生成类的完整成员大纲（方法、字段、属性）
- 构建继承链图谱（直接 + 间接继承关系）
- 自动错误恢复（方法不存在时返回可用方法列表供修正）

🧩 **智能 XML 继承解析**  
RimWorld 的 Def 系统基于复杂的 `ParentName` 继承链（深度可达 10+ 层）。RimSearcher 自动：
- 递归追踪完整的继承树（深度限制防止死循环）
- 智能合并所有层级的 XML 属性和元素
- 正确处理 `Inherit="false"` 覆盖规则
- 识别 30+ 种特殊容器（comps、stages、modExtensions 等）的列表语义
- 返回已解决所有继承关系的**最终生效 XML**，AI 可直接理解

🌉 **语义桥接**  
在解析 XML 的同时，自动识别和提取 30+ 种关联的 C# 类型标签：
- `thingClass`、`compClass`、`workerClass`、`jobClass`、`hediffClass`、`verbClass` ...
- 自动提供源文件路径跳转建议
- 一键查看关联的逻辑实现代码

⚡ **极速响应**  
采用多层次性能优化：
- 并行扫描和 N-gram 索引加速（毫秒级搜索）
- 候选集过滤和深度限制防止性能衰减
- 即使面对数万个文件的源码库，也能在 **< 1 秒** 完成查询

💰 **低 Token 损耗**
- **按需提取**：支持仅提取特定的 C# 方法而非整个文件，避免数千行无关代码进入 AI 上下文
- **精细化结果**：XML 解析仅返回最终的已合并定义，减少冗余信息
- **可扩展性**：支持分页和行级读取，保护 AI 的上下文窗口

---

## 2. 工具矩阵：全能的功能详解

RimSearcher 暴露了 **6 个互补的工具**，涵盖搜索、分析、提取、追踪四个维度，AI 可根据任务需求灵活调用：

#### 🔎 `rimworld-searcher__locate` - 全域快速定位

**核心功能**：全库模糊搜索入口，一站式定位 C# 类型、XML DefName、方法、字段和文件。

**支持的查询语法**：
```
Apparel_ShieldBelt       # 模糊搜索 DefName（自动匹配 ShieldBelt、Shield_Belt、SB 等）
type:Pawn                # 仅搜索 C# 类型
method:OnAttack          # 仅搜索方法（可跨多个类）
field:energy             # 仅搜索字段
def:Damage               # 仅搜索 XML Def
type:Comp method:Tick    # 组合查询（Comp 类中的 Tick 方法）
```

**输出示例**：
```markdown
## 'ShieldBelt'

**C# Types:**
- `Apparel` (95%) - CompProperties_Shield.cs
- `Apparel_Meta` (88%) - Apparel.cs

**XML Defs:**
- `Apparel_ShieldBelt` (100%) - "Shield Belt"
- `CompProperties_Shield` (92%) - CompShield.cs

**Members:**
- Methods: CompShield.PostPreApplyDamage (95%)
- Fields: Shield_EnergyMax (87%)
```

**价值**：当 AI 知道一个概念名称但不确定精确位置时，此工具瞬间定位并分类结果，为后续分析奠定基础。

---

#### 🔬 `rimworld-searcher__inspect` - 深度资源分析（最核心）

**这是 RimSearcher 最强大的工具**，支持两种分析模式：

**模式 A：XML Def 深度解析**
- 自动递归解析 `ParentName` 继承链（深度限制 15 层防止循环）
- 智能合并所有层级的属性、元素和列表
- 处理 `Inherit="false"` 覆盖规则和 30+ 种特殊容器
- 返回**已完全解决继承关系的最终 XML**，包含所有继承的属性
- 自动识别并链接 30+ 种关联的 C# 逻辑类（thingClass、compClass、workerClass 等）

**输出示例（XML 模式）**：
```xml
<Apparel_ShieldBelt>
  <defName>Apparel_ShieldBelt</defName>
  <label>Shield Belt</label>
  <thingClass>Apparel</thingClass>
  <!-- 来自 ApparelBase：-->
  <layer>Shell</layer>
  <!-- 来自该 Def：-->
  <comps>
    <li Class="CompProperties_Forbiddable" />  ← 继承自 ApparelBase
    <li Class="CompProperties_Shield">         ← 新增
      <compClass>RimWorld.CompShield</compClass>
      <energyPerDamage>0.01</energyPerDamage>
    </li>
  </comps>
</Apparel_ShieldBelt>
```

**模式 B：C# 类结构分析**
- 解析类的完整继承关系（包括间接继承）
- 生成类的成员大纲（所有方法、字段、属性）
- 提供类的 Roslyn AST 分析结果

**输出示例（C# 模式）**：
```markdown
## Class: RimWorld.CompShield : ThingComp

**Inheritance Chain:**
- CompShield → ThingComp → IExposable → ...

**Members:**
- Property: float Shield_Energy
- Property: float Shield_EnergyMax
- Method: void CompTick()
- Method: void PostPreApplyDamage(DamageInfo dinfo, out float damageDealt)
- Method: void Break()
```

**价值**：此工具一次性展现资源的全貌（数据+关联逻辑），AI 无需多次查询即可理解完整结构。

---

#### 📖 `rimworld-searcher__read_code` - 智能源码提取

**核心功能**：按需精准提取 C# 方法体，而非整个文件。使用 Roslyn AST 解析确保准确性。

**输入参数**：
```json
{
  "path": "D:/Assembly-CSharp/CompShield.cs",
  "methodName": "PostPreApplyDamage",
  "className": "CompShield"  // 可选，用于消歧
}
```

**智能错误恢复**：如果方法名不存在，自动返回该文件所有可用方法列表，让 AI 自我修正：
```markdown
Method not found. Available methods:
- CompTick()
- PostPreApplyDamage()
- Break()
- Initialize()
```

**输出示例**：
```csharp
public override void PostPreApplyDamage(DamageInfo dinfo, out float damageDealt)
{
    damageDealt = 0f;
    if (!base.parent.Spawned)
        return;
    
    float absorbedDamage = Mathf.Min(dinfo.Amount, Shield_Energy);
    Shield_Energy -= absorbedDamage;
    dinfo.SetAmount(Mathf.Max(0f, dinfo.Amount - absorbedDamage));
    
    if (Shield_Energy <= 0f)
        Break();
}
```

**价值**：AI 往往只需理解某个特定逻辑，此工具直接返回该方法的完整源码，极大节省 Token 并提高准确度。相比整个文件的上千行代码，这可能只是 50-100 行。

---

#### 🔗 `rimworld-searcher__trace` - 交叉引用追踪

**双模式设计**：

**模式 A：继承树追踪 (inheritors)**
- 找出所有直接 + 间接继承自指定类的子类
- 递归遍历整个继承树
- 支持多重继承关系

**输出示例**：
```markdown
**Inheritors of HediffComp (25 total):**
- HediffComp_Immunizable
- HediffComp_VerbGiver
- HediffComp_Pain
- HediffComp_Immunizable_Durable
... (21 more)
```

**模式 B：符号使用追踪 (usages)**
- 在全库范围内查找特定符号（方法名、字段名、类名）的所有引用
- 返回文件路径 + 行号 + 上下文片段
- 支持模糊匹配

**输出示例**：
```markdown
**Usages of TakeDamage (145 total):**
- Pawn.cs:2341: float damageDealt = thing.TakeDamage(dinfo);
- CompShield.cs:89: dinfo.SetAmount(dinfo.Amount - Shield_Energy);
- HealthComponent.cs:156: return TakeDamage(new DamageInfo(...));
... (142 more)
```

**价值**：用于分析代码影响范围、寻找 Hook 点、或学习某机制在游戏中的应用实例。例如，想知道"哪些代码会触发伤害吸收"，此工具一次性列出所有相关位置。

---

#### 🔤 `rimworld-searcher__search_regex` - 全域正则搜索

**核心功能**：在整个源码库（C# 和 XML）内进行高级模式匹配，返回最多 50 个结果。

**常见用途**：
```regex
<compClass>(.+?)</compClass>              # 提取所有 Comp 类定义
void (\w+)Tick\(\)                        # 找所有 Tick 方法
protected\s+override\s+void\s+(\w+)\(     # 找所有虚方法重写
<thingClass>Apparel</thingClass>          # 精确搜索特定标签值
```

**输出示例**：
```markdown
**Regex Matches (50 results):**
- ThingDef_Weapons.xml:123: <compClass>RimWorld.CompBladelink</compClass>
- Apparel_Belts.xml:456: <compClass>RimWorld.CompShield</compClass>
- CompShield.cs:45: public override void PostPreApplyDamage(...)
- Pawn.cs:892: protected override void SomeMethod() { }
```

**价值**：适合寻找特定的硬编码字符串、特定 XML 标签模式、或复杂的代码结构。比模糊搜索更精准，但需要了解正则表达式。

---

#### 📁 `rimworld-searcher__list_directory` - 目录导航

**核心功能**：浏览项目文件层级，列出指定目录的所有文件和子目录。

**输入参数**：
```json
{
  "path": "D:/Assembly-CSharp/Comp",
  "limit": 100
}
```

**输出示例**：
```markdown
## D:/Assembly-CSharp/Comp

**Directories:**
- DefComp/
- Graphics/
- Combat/

**Files:**
- CompShield.cs (45 KB)
- CompBladelink.cs (23 KB)
- CompOversizeWeapon.cs (12 KB)
... (97 more items)
```

**价值**：帮助 AI 快速理解源码的文件组织结构，或在不知道精确路径时浏览目录树。

---

## 2.5 架构概览 (System Architecture)

RimSearcher 由 **7 个核心引擎** 和 **1 个 MCP 服务层** 组成，精心设计的协作流程确保毫秒级响应和精准分析：

```
┌─────────────────────────────────────────────────────────────┐
│                    MCP 通讯层 (JSON-RPC 2.0)                │
│              RimSearcher.cs - 并发控制、协议处理            │
└────┬─────────────────────────┬──────────────────────────┬───┘
     │                         │                          │
     ↓ 工具路由                ↓                          ↓
┌──────────────┐      ┌────────────────┐        ┌──────────────────┐
│ 6 个 MCP 工具 │      │ 业务逻辑层      │        │ 配置与安全       │
├──────────────┤      │                │        │                  │
│ locate       │      │ SourceIndexer  │        │ PathSecurity     │
│ inspect      │      │ (C# 索引)       │        │ (路径验证)       │
│ read_code    │      │                │        │                  │
│ trace        │      │ DefIndexer     │        │ AppConfig        │
│ search_regex │      │ (XML 索引)      │        │ (配置加载)       │
│ list_dir     │      │                │        │                  │
└──────────────┘      ├────────────────┤        └──────────────────┘
                      │ 分析引擎        │
                      │                │
                      │ RoslynHelper   │
                      │ (AST 解析)      │
                      │                │
                      │ XmlInheritance │
                      │ Helper         │
                      │ (继承合并)      │
                      │                │
                      │ FuzzyMatcher   │
                      │ QueryParser    │
                      │ (搜索优化)      │
                      └────────────────┘
```

**关键特性**：
- 🔄 **N-gram 索引加速**：预处理 + 候选集过滤，毫秒级搜索
- 🔐 **安全沙箱**：路径白名单 + 大小限制 + 并发控制（10 请求上限）
- ⚡ **并行扫描**：初始化时并行加载数千个文件
- 💾 **智能缓存**：XML 文档缓存 + 字符串驻留

---

## 3. AI 调用执行流程 (Execution Workflow)

为了让 AI 像人类专家一样思考，RimSearcher 设计了一套协同工作流。以下是三个真实应用场景：

### 场景 1：分析 Def 及其关联逻辑（最常见） 🎯

以 **"护盾腰带（Shield Belt）是如何工作的"** 为例：

1.  **定位 Def** → `locate(query: "Apparel_ShieldBelt")`  
    结果：`Defs/ThingDefs_Misc/Apparel_Belts.xml` 文件位置

2.  **解析 XML 继承** → `inspect(name: "Apparel_ShieldBelt")`  
    结果：已解决继承链的完整 XML，包含所有父类属性 + 自身属性。AI 在 `<comps>` 中看到 `CompProperties_Shield`

3.  **关联逻辑类** → `inspect(name: "CompProperties_Shield")`  
    结果：确认关联的 C# 实现类为 `RimWorld.CompShield`

4.  **类结构分析** → `inspect(name: "RimWorld.CompShield")`  
    结果：类的成员大纲，发现 `PostPreApplyDamage()`（伤害吸收）和 `CompTick()`（能量回复）方法

5.  **方法实现** → `read_code(path, methodName: "PostPreApplyDamage")`  
    结果：AI 获取具体实现逻辑，看到能量扣除、判断条件和 `Break()` 调用

**最终产出**：AI 基于真实源码生成准确的技术分析报告。

### 场景 2：寻找所有继承实现（用于理解设计模式） 🔗

例如，"所有继承自 `HediffComp` 的组件有哪些，分别做什么？"

1.  **定位基类** → `locate(query: "type:HediffComp")`  
    结果：`HediffComp` 类路径

2.  **查找所有继承者** → `trace(symbol: "HediffComp", mode: "inheritors")`  
    结果：返回 25 个子类列表（HediffComp_Immunizable、HediffComp_Pain 等）

3.  **逐个分析** → 对每个子类调用 `inspect(name: "HediffComp_Xxx")`  
    结果：各子类的功能和参数

**优势**：AI 一次性获得完整的继承体系，理解该架构的所有应用，而无需手动搜索每个子类。

### 场景 3：追踪代码影响范围（用于改 mod 或 bug 修复） 🐛

例如，"修改 `TakeDamage` 方法会影响哪些地方？"

1.  **定位方法** → `locate(query: "method:TakeDamage")`  
    结果：所有包含该方法的文件

2.  **查找所有调用点** → `trace(symbol: "TakeDamage", mode: "usages")`  
    结果：返回 145 个引用位置（行号 + 文件 + 上下文）

3.  **检查关键调用** → 对高风险位置使用 `read_code` 查看上下文  
    结果：确认哪些调用可能受影响

**优势**：AI 能快速定位所有风险点，帮助规划修复策略，避免意外破坏。

---

## 4. 性能与安全 ⚡

| 维度 | 优化措施 | 效果 |
|------|---------|------|
| **索引加速** | N-gram 预索引 + 候选集过滤（500 上限）| 毫秒级搜索 |
| **文件扫描** | 并行 Parallel.ForEach + 深度限制（3）| 4-8x 加速 |
| **内存优化** | 文档缓存 + 字符串驻留 | 60% 内存节省 |
| **并发控制** | 信号量限制（10 请求） | 防止资源争用 |
| **大小限制** | C# 文件 10MB、XML 深度 15 层 | 防止 OOM 和死循环 |

**安全防护**：
- 🔐 **路径白名单验证**：仅允许配置的目录
- 🔐 **目录遍历防护**：检测 `..` 等恶意路径
- 🔐 **符号链接检测**：防止指向外部文件
- 🔐 **XXE 防护**：禁用 XML DTD 处理
- 🔐 **并发隔离**：请求间完全隔离，一个失败不影响其他

---

## 5. 技术栈与核心实现

| 分类 | 技术 | 说明 |
|------|------|------|
| **语言和运行时** | C# 14 + .NET 10.0 | 最新的语言特性和高性能运行时 |
| **代码分析** | Roslyn (Microsoft.CodeAnalysis) | 微软官方 C# 编译器平台，支持 AST 精确解析 |
| **XML 处理** | 自定义继承解析器 | 深度模拟 RimWorld 引擎的 ParentName 继承逻辑 |
| **索引加速** | N-gram 算法 | 预处理候选集，毫秒级模糊搜索 |
| **并发控制** | SemaphoreSlim + 写入锁 | 安全的资源竞争管理 |
| **通讯协议** | JSON-RPC 2.0 (MCP 2025-11-25) | 标准化的 AI 工具集成协议 |

---

## 6. 开发者指南 👨‍💻

### 项目结构
```
Sources/
├── RimSearcher.Core/
│   └── Core/
│       ├── SourceIndexer.cs (364行)  ← C# 源码索引
│       ├── DefIndexer.cs (270行)      ← XML Def 索引
│       ├── RoslynHelper.cs (204行)    ← AST 解析引擎
│       ├── XmlInheritanceHelper.cs (150行) ← XML 继承合并
│       └── FuzzyMatcher.cs, QueryParser.cs, PathSecurity.cs
│
└── RimSearcher.Server/
    ├── RimSearcher.cs (235行)         ← MCP 服务核心
    ├── Program.cs                      ← 启动入口
    └── Tools/ [6 个工具实现]
        ├── LocateTool.cs
        ├── InspectTool.cs
        ├── ReadCodeTool.cs
        ├── TraceTool.cs
        ├── SearchRegexTool.cs
        └── ListDirectoryTool.cs
```

### 添加新工具

1. 创建类实现 `ITool` 接口：
```csharp
public class MyTool : ITool
{
    public string Name => "rimworld-searcher__mytool";
    public string Description => "...";
    public object JsonSchema => new { /* 参数定义 */ };
    
    public async Task<ToolResult> ExecuteAsync(JsonElement args, CancellationToken ct, IProgress<double>? p)
    {
        // 实现逻辑
        return new ToolResult("结果");
    }
}
```

2. 在 `RimSearcher.cs` 中注册：
```csharp
var tools = new List<ITool>
{
    // ... 现有工具
    new MyTool(sourceIndexer, defIndexer),  // 新工具
};
```

### 修改继承合并逻辑

编辑 `XmlInheritanceHelper.cs` 的 `MergeXml` 方法，调整容器识别和属性合并规则。

---

## 7. 常见问题 (FAQ) ❓

**Q: 为什么我的查询返回"无结果"？**  
A: 检查以下几点：
- 确认源路径 (`config.json`) 指向正确的目录
- 尝试使用部分匹配，如 "Shield" 而非 "Apparel_ShieldBelt"
- 使用 `list_directory` 工具浏览文件结构，确认文件存在

**Q: 查询很慢，如何优化？**  
A: 
- 使用具体的查询过滤器 (`type:`, `method:`, `def:`) 而非通用搜索
- 避免非常宽泛的正则表达式（如 `.*` 会扫描所有文件）
- 检查 RimSearcher 日志是否有超时警告

**Q: 如何修改 XML 继承的合并规则？**  
A: 编辑 `XmlInheritanceHelper.cs`：
- 修改 `ListContainerNames` 来改变哪些元素被视为列表
- 修改 `MergeXml()` 方法来改变合并策略

**Q: 支持跨多个 Def 文件的继承吗？**  
A: 完全支持。RimSearcher 自动递归查询 `ParentName` 并跨文件追踪继承链。

**Q: 方法提取失败，返回"可用方法列表"怎么办？**  
A: 这是设计特性！AI 会看到文件中的所有方法，自动选择正确的方法名重试。

**Q: 如何处理包含中文的 XML？**  
A: RimSearcher 完全支持 UTF-8 编码的 XML 和 C# 文件。

---

## 8. 快速开始

#### 点击跳转[B站视频教程](https://www.bilibili.com/video/BV1w1cJz7E9t?vd_source=624604839a08e42cea3a8cb45151b201)

### 前置要求
*   安装 [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)

### 安装步骤
1.  从 **[Releases](https://github.com/kearril/RimSearcher/releases)** 下载最新的 `RimSearcher.Server.exe`。
2.  创建 `config.json`作为mcp的源码路径索引：
    ```json
    {
      "CsharpSourcePaths": ["C:/Path/To/Your/RimWorld/Source"],
    
      "XmlSourcePaths": ["C:/SteamLibrary/steamapps/common/RimWorld/Data"]
    }
    ```
>  *CsharpSourcePaths* 应指向你本地的 RimWorld 的反编译后的 C# 源码目录
> 
>  *XmlSourcePaths* 应指向 RimWorld 的 Data 目录（包含所有 XML 定义）

3.  在大多数主流的 MCP 客户端（如 Gemini CLI、Claude Desktop）中添加服务器配置，指向 `RimSearcher.Server.exe` 的路径，并设置环境变量 `RIMSEARCHER_CONFIG` 指向上面创建的 `config.json` 文件路径。
    ```json
    {
       "mcpServers": {
         "RimSearcher": {
           "command": "D:/path/to/RimSearcher.Server.exe",
           "args": [],
           "env": {
             "RIMSEARCHER_CONFIG": "D:/your/custom/path/config.json"
          }
        }
      }
    }
    ```
 而一些客户端又有些细微差异，例如
**copilot**的配置文件为
```json
{
  "servers": {
    "RimSearcher": {
      "command": "D:/path/to/RimSearcher.Server.exe",
      "args": [],
      "env": {
        "RIMSEARCHER_CONFIG": "D:/your/custom/path/config.json"
      }
    }
  }
}
```
**opencode**的配置文件为
```json
      {
        "mcp": {
           "RimSearcher": {
             "type": "local",
             "command": ["D:/path/to/RimSearcher.Server.exe"],
             "enabled": true,
             "environment": {
               "RIMSEARCHER_CONFIG": "D:/your/custom/path/config.json"
            }
          }
        }
      }
```
> 请根据你使用的 MCP 客户端的文档，正确配置服务器路径和环境变量。只要保证**command**和**env**的正确设置，RimSearcher 服务器就能正常工作。

### 验证服务器
由于我们是手动验证服务器是否可以正常运行，所以需要确保RimSearcher.Server.exe和config.json在同一目录下，以及config.json中的路径设置正确。
![配置示例](Image/Snipaste_2026-02-07_23-20-57.png)
然后运行RimSearcher.Server.exe，您应该会看到类似以下的输出，表示服务器已成功启动并加载了数据源：
![启动成功示例](Image/Snipaste_2026-02-09_20-34-49.png)
如果出现像上面图片一样的日志，那么恭喜你，RimSearcher 服务器已经成功运行，你可以在支持 MCP 的 AI 助手中调用相关工具进行源码查询和分析了！

---

### 开源协议
本项目采用 MIT 协议
### 如果这个项目对你有帮助，欢迎在 GitHub 上给我点个 Star ⭐，这将是对我最大的支持！
*Powered by .NET 10 & Gemini CLI.*
