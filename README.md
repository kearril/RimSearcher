# RimSearcher: RimWorld 源码搜索 MCP 服务器

RimSearcher 是一个基于 Model Context Protocol (MCP) 实现的专用服务器，旨在为 AI 助手（如 Gemini, Claude 等）提供对 RimWorld
游戏源码（C#）和配置文件（XML）的高效检索与深度分析能力。

使用C# 14 和 .NET 10 开发，RimSearcher 利用 Roslyn 编译器平台实现了对 C# 代码的智能解析和方法体提取，同时支持 RimWorld 特有的 XML 定义解析逻辑。

本项目使用了Gemini辅助完成.

---

## 1. MCP 特点

* **高性能索引**：采用内存预扫描机制，支持对数万个 C# 文件和 XML Defs 的秒级检索。
* **低 Token 损耗**：
    * **精准定位**：直接返回方法体或特定的 XML 节点，避免传输冗余代码。
    * **分页机制**：源码读取支持行范围限制，防止上下文溢出。
* **智能 XML 解析**：支持 RimWorld 特有的 XML 继承逻辑（Abstract/ParentName），能够返回解析后的最终属性。
* **协议标准**：遵循 JSON-RPC 2.0 规范，通过标准输入输出（Stdio）与客户端通信，具有良好的兼容性。
* **项目已完整打包**：提供了编译好的单文件可执行程序，用户只需安装 .net 10 CDK即可使用。

---

## 2. 工具能力

RimSearcher 暴露了以下6个强大的核心工具供 AI 调用：

* **locate**: 快速定位。通过名称查找特定的 ThingDef 或 C# 类型定义。
* **inspect**: 深度查看。获取完整的 XML 定义（含继承解析）或 C# 类的成员结构。
* **read_code**: 智能读取。支持按方法名提取函数体，或根据行号进行分页读取。
* **search_regex**: 全局搜索。在全库范围内使用正则表达式匹配内容。
* **trace**: 引用追踪。分析特定 C# 类型或 XML Def 的引用关系。
* **list_directory**: 目录导航。探索源码库的文件层级结构。

---

## 3. 技术栈

* **开发语言**: C# 14
* **运行时**: .NET 10.0
* **核心库**:
    * **Microsoft.CodeAnalysis.CSharp (Roslyn)**: 用于深度的 C# 语法树分析和方法体提取，能够像ide一样优雅得搜索相关引用和处理类的继承。
    * **System.Text.Json**: 高性能 JSON 序列化与协议解析。
    * **Microsoft.Extensions.Logging**: 标准化日志系统（日志输出至 stderr 以避免干扰协议流）。
* **项目架构**:
    * `RimSearcher.Core`: 包含索引引擎、Roslyn 辅助类及 XML 解析逻辑。
    * `RimSearcher.Server`: 负责 MCP 协议实现、工具注册及进程间通信。

---

## 4. 如何使用

* ### 前置要求
* 安装 .NET 10 SDK（这个可以在微软官网安装或者在你的ide里自动安装）

* ### 下载最新的.exe文件
* 访问 **[Releases](https://github.com/kearril/RimSearcher/releases)** 页面，下载最新版本的 RimSearcher.Server.exe 文件。

* ### 配置源码路径
* 新创建一个文件夹，将这个.exe文件放入其中，并在同一目录下创建一个名为 `config.json` 的文件，内容如下：

```json
{
  "CsharpSourcePaths": [
    "C:/Path/To/Your/RimWorld/Source"
  ],
  "XmlSourcePaths": [
    "C:/SteamLibrary/steamapps/common/RimWorld/Data"
  ]
}
```

**CsharpSourcePaths** 指向你反编译后的 RimWorld C# 源码目录，**XmlSourcePaths** 指向 RimWorld 的 Data 目录（包含所有 XML
定义文件）。
上面的源码里也包含了一个示例的 `config.json` 文件，你可以根据自己的环境修改路径。


> 到目前为止，你已经准备好了 MCP 服务器的可执行文件和配置文件，接下来就是将它集成到你的 AI 助手中，让它能够调用这些工具来分析
> RimWorld 的源码了。

### 验证服务器是否正常工作：
我们来到你放置这个.exe文件的目录，像这样

![文件夹1](Image/Snipaste_2026-02-07_23-20-57.png)
一定要确保config.json文件和.exe文件在同一目录下，以及你已经正确配置了路径。然后双击运行这个.exe文件，如果一切正常，你应该会看到类似下面的输出：
![运行图片2](Image/Snipaste_2026-02-07_23-21-56.png)
到现在，就大功告成了！你已经成功启动了 RimSearcher MCP 服务器，并且它已经准备好接受来自 AI 助手的请求了。

---

## 5. 安装至 AI 助手

创建mcp.json文件（上面有一个范例文件），内容如下：

```json
{
  "mcpServers": {
    "rimworld-searcher": {
      "args": [
      ],
      "command": "Folder path to RimSearcher.Server.exe",
      "cwd": "Folder path to RimSearcher"
    }
  }
}
```

> 注意，`command` 需要指向你放置 RimSearcher.Server.exe 的完整路径，`cwd` 则是该可执行文件所在的目录。
> 确保路径正确无误，这样你的 AI 助手才能成功启动 MCP 服务器并与之通信。
> 还有，不用手动去运行这个.exe文件了，AI 助手会根据这个配置自动启动它。

---

#### 到目前为止，你已经成功安装了 RimSearcher MCP 服务器，并将其集成到了你的 AI 助手中。现在，你的助手应该能够通过 MCP 协议调用 RimSearcher 提供的工具来分析和查询 RimWorld 的源码了。

### 如果这个项目对你有帮助，欢迎在 GitHub 上给我点个 Star ⭐，这将是对我最大的支持！如果你有任何问题或者建议，也欢迎在 Issues 中提出，我会尽快回复。

*Powered by .NET 10 & Gemini Cli.*
