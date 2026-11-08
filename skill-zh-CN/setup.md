# RimSearcher 项目初始化

文件基准路径约定：
- **Skill 内置资源**：`bin/rimsearcher.exe` 与 `assets/RimSearcher_DataMod.zip`（位于当前 Skill 安装根目录）。
- **项目工作目录**：`.rimsearcher/`（位于当前项目根目录）。

## 1. 建立项目工作区

根据当前工作空间与结构定位项目根目录（存在真实歧义时再询问）：
1. 创建 `.rimsearcher/` 目录（已有目录直接复用）。
2. 若缺少项目 CLI，将 Skill 内置的 `bin/rimsearcher.exe` 复制到 `.rimsearcher/rimsearcher.exe`（不覆盖已存在的 EXE）。
3. 对 Git 项目，将 `.rimsearcher/*` 加入 `.gitignore` 并保留例外 `!.rimsearcher/README.md`（避免将大文件二进制与数据库快照提交入库，同时保留环境说明受控）。
4. 若缺少 `.rimsearcher/README.md`，使用下方模板初始化；若已存在则保留用户已有内容，仅增补缺失项。

```markdown
# RimSearcher 项目环境

- 初始化状态：未完成

## 工具版本

- 项目 CLI：未确认
- 数据库版本：未确认

## 游戏位置

- 游戏根目录：未确认
- 游戏 DLL 存放目录：未确认
- 游戏主要程序集 Assembly-CSharp.dll：未确认
- DataMod 安装目录：未确认

## 依赖模组

- 模组目录：未确认
- 相关程序集 DLL：未确认

## Def 快照

- 项目数据库：与本 README 同目录的 defs.db
- 导出来源路径：未确认
- 导出时间：未确认
- 游戏版本、DLC、启用模组及加载顺序说明：未确认

## 项目备注

无已确认的特殊要求。
```

## 2. 收集与确认环境路径

通过单次交互向用户收集以下关键信息（已提供或已有记录的内容无需重复询问）：

1. **RimWorld 游戏根目录**：用于定位 DataMod 安装目录（`Mods/`）。
   - *常见参考（Steam 游戏根目录）*：`<Steam库目录>/steamapps/common/RimWorld`
2. **依赖模组目录与相关 DLL（可选）**：当前项目所依赖的第三方模组目录或程序集。若无依赖明确记录为“无”，未指定则保持“未确认”。
   - *常见参考（Steam 创意工坊模组目录）*：`<Steam库目录>/steamapps/workshop/content/294100`
**关于游戏核心 DLL（Assembly-CSharp.dll）**：
获得游戏根目录后，**按以下顺序探测**，记录第一个存在的文件，无需向用户追问：
1. RimWorld 1.7 布局：`<游戏根目录>/Assembly-CSharp.dll`
2. RimWorld 1.6 回退布局：`<游戏根目录>/RimWorldWin64_Data/Managed/Assembly-CSharp.dll`

仅当两个文件都不存在或布局非标准时，才向用户询问核心 DLL 存放目录。实地校验目标路径下的 `Assembly-CSharp.dll` 与模组 DLL 真实存在后，再将完整路径记录至 `.rimsearcher/README.md`。不凭空推测本机路径。

## 3. 准备 DecompilerServer MCP

使用客户端提供的 [DecompilerServer](https://github.com/pardeike/DecompilerServer) 加载游戏核心 `Assembly-CSharp.dll` 及依赖模组的实际 DLL（工具名称与参数以当前客户端实际注入的 MCP 定义为准）：
- RimWorld 1.7 须以已记录的**程序集路径**（`assemblyPath`）加载 `Assembly-CSharp.dll`。不依赖游戏目录自动探测（`gameDir`），该探测面向 1.6 的 Unity `Managed` 布局。
- 校验 MCP 加载的上下文与记录的 DLL 路径一致。
- 检索或反编译一个基础类型（如 `Verse.Pawn`），验证 MCP 通信与反编译链路畅通。
- 仅记录持久化的 DLL 文件路径；临时 context ID 不作为长期配置保存，亦不可将游戏 DLL 拷贝进 Skill 目录。
- 若 MCP 尚未安装或配置，引导用户参考官方文档完成当前客户端的配置。

## 4. 导出与验证 Def 数据库快照

1. **安装 DataMod**：在用户确认后，将 Skill 目录下的 `assets/RimSearcher_DataMod.zip` 解压至游戏根目录的 `Mods/` 目录中。确保保留 `RimSearcher_DataMod/` 顶层文件夹及其全部子项（`About/`、`Assemblies/`、`Native/`、`Languages/`）；已有可用安装直接复用。
2. **启动与导出**：
   - 引导用户启动 RimWorld，在模组列表中勾选启用 **RimSearcherDataMod**，并加载分析所需的目标模组环境。
   - 进入游戏内 **选项 > Mod 设置 > RimSearcherDataMod**，点击 **导出 Def 数据库**。
3. **部署快照**：将导出的数据库（默认生成于 `<游戏根目录>/Mods/RimSearcher_DataMod/defs.db`）复制到项目 `.rimsearcher/defs.db`，与 `rimsearcher.exe` 同级。已有数据库先行保留，替换前征得用户同意；不借用其他项目的数据库。
4. **运行验证**：
   - 使用项目 CLI 显式路径执行 `--version` 与 `mods`，确认工具可正常运行且数据库查询成功。
   - 数据库版本须落在 CLI 支持的兼容区间内（运行 `<CLI路径> --help` 查看兼容范围）。
   - CLI 面向 Windows x64，需要 [.NET 10 Runtime](https://dotnet.microsoft.com/download/dotnet/10.0)。系统缺少运行时环境时，引导用户下载安装对应版本。

## 5. 完成判定与日常衔接

满足以下全部条件后，将 `.rimsearcher/README.md` 的初始化状态更新为“已完成”：
- 路径与环境配置已完整记录；
- DecompilerServer MCP 可成功读取目标程序集源码；
- 项目 CLI 成功执行 `mods` 并返回模组列表数据。

若有环节未就绪，保持“未完成”并明确告知用户当前缺失的具体步骤（如缺少 DLL、MCP 待配置或数据库待导出）。
环境就绪后直接开展日常分析，日常使用不重复整套初始化。后续若需升级工具，按 [update.md](update.md) 执行。
