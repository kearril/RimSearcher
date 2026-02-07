# 🧊 RimSearcher

RimSearcher 是一个专为 **RimWorld** 开发者打造的深度源码洞察服务器。基于 Anthropic 的 **Model Context Protocol (MCP)** 协议，它能将 RimWorld 庞大的 C# 源码库与复杂的 XML Def 体系结构化地“喂”给 AI 助手（如 Claude），使其具备超越普通开发者的代码理解与数据检索能力。

## 🌟 核心优势：为什么它如此强大？

与传统的文件搜索不同，RimSearcher 理解 RimWorld 的**领域逻辑**：

- **XML 继承全解析**：RimWorld 的 Def 广泛使用 ParentName 继承。RimSearcher 能够实时递归向上溯源，为你呈现合并后的最终 XML 数据，彻底告别在几十个文件间手动追踪属性的痛苦。
- **C# 与 XML 的深度联动**：当你查看一个 ThingDef 时，RimSearcher 会自动扫描并定位与其关联的 	hingClass、compClass 或 workerClass 源码路径。
- **符号级继承追踪**：支持跨文件计算 C# 类的完整继承链，并能瞬间找出一个基类在全域范围内的所有派生子类。
- **工业级稳定性**：内置 Stdout 劫持保护技术，确保任何意外的日志输出都不会破坏 MCP 通信协议，在高强度 AI 对话中依然坚如磐石。

## 🧰 神兵利器：工具箱说明

| 工具 | 强大之处 | 典型用例 |
| :--- | :--- | :--- |
| **locate** | **全域定位**：不仅搜文件名，还能跨越 C# 类型名、DefName 和 Label 进行模糊匹配。 | "找到所有包含 'Revolver' 的资源" |
| **inspect** | **深度透视**：对 XML 执行继承合并；对 C# 提供 Mermaid 继承图及类结构大纲。 | "解析这个 Def 的最终属性，并展示它的类大纲" |
| **ead_code** | **精准读取**：支持利用 Roslyn 引擎按方法名提取代码，或分页读取超长文件。 | "读取 Pawn 类中 TryGetAttackTarget 方法的实现" |
| **	race** | **关联追踪**：寻找符号的所有引用位置，或列出某个抽象基类的所有具体实现。 | "有哪些类继承自 CompProperties？" |
| **search_regex** | **正则搜索**：在数万个源码文件中进行高性能的正则表达式内容匹配。 | "搜索所有使用了特定字段的 XML 节点" |
| **list_directory** | **结构浏览**：安全地探索授权目录下的文件层级。 | "看看这个 Mod 的文件夹结构" |

## 🛠️ 快速上手

### 1. 构建
确保安装了 [.NET 10.0 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)，在 Sources 目录下运行：
`powershell
dotnet publish -c Release
`

### 2. 配置
在 Sources/Publish/config.json 中配置你的源码路径：
`json
{
  "CsharpSourcePaths": ["D:/Source/RimWorld_Decompiled"],
  "XmlSourcePaths": ["D:/Steam/steamapps/common/RimWorld/Data"]
}
`

### 3. 接入
在 Claude Desktop 配置中指向 mcp.json 即可。

## 🛡️ 安全与限制
- **路径沙箱**：服务器仅允许访问 config.json 中明确授权的目录，保护隐私。
- **性能防护**：内置 2MB 文件解析上限，防御超大型 XML 导致的内存溢出。
- **静态索引**：扫描在启动时完成，若修改了源码请重启服务器以刷新数据。

## 📄 开源协议
基于 **MIT License** 开源。
