# RimSearcher Streamable HTTP 传输设计

## 背景

RimSearcher 当前是一个通过 stdio 启动的本地 MCP server，启动形式是可执行文件。每个把该 exe 配置为 MCP server 的 Agent 都会启动自己的进程，也就会各自加载或构建一份 RimWorld C# 与 XML 索引。

目标使用场景里的源码数据基本是静态的：一个反编译后的 RimWorld C# 源码目录，以及一个 RimWorld XML 数据目录，正常使用时不会频繁变化。

本设计的目标是让用户可以手动启动一个共享的本地 MCP server 进程，并让多个支持 URL MCP 配置的客户端连接到它；同时保留当前 stdio 行为，继续兼容仍然直接启动 exe 的客户端。

## 范围

本设计为现有可执行文件增加显式 transport 选择。

- 默认行为仍然是 stdio。
- 新增一个可手动启动的 Streamable HTTP 模式。
- 现有工具和索引行为保持不变。
- 本设计不包含 stdio-to-HTTP proxy、自动后台拉起、服务发现、配置指纹、锁文件或多实例管理。

## 用户可见行为

默认 stdio 模式继续兼容当前 README 中的用法：

```powershell
RimSearcher.Server.exe
```

共享本地 HTTP 模式由用户手动启动：

```powershell
RimSearcher.Server.exe --transport streamable-http --host 127.0.0.1 --port 51234 --mount-path /mcp
```

支持 URL 形式 MCP server 的客户端可以连接到：

```text
http://127.0.0.1:51234/mcp
```

可执行文件继续优先读取 `RIMSEARCHER_CONFIG`。未设置该环境变量时，回退读取 exe 同目录下的 `config.json`。

## CLI 选项

新增以下命令行选项：

- `--transport`：可选值为 `stdio` 或 `streamable-http`；默认 `stdio`。
- `--host`：HTTP 绑定地址；默认 `127.0.0.1`。
- `--port`：HTTP 绑定端口；默认 `51234`。
- `--mount-path`：MCP HTTP endpoint 路径；默认 `/mcp`。

HTTP 相关选项只在 `--transport streamable-http` 时生效。

## 架构

现有启动流程应继续作为唯一的初始化路径：

1. 设置 Console 编码。
2. 加载 `AppConfig`。
3. 初始化 `PathSecurity`。
4. 加载索引缓存，或扫描源码路径并构建索引。
5. 冻结索引。
6. 注册现有六个 MCP 工具。
7. 启动所选 transport。

为了避免复制启动逻辑，可以只在必要处小幅拆分当前程序流程：

- runtime 对象负责工具注册、JSON-RPC 请求处理、并发限制、取消处理和 logging notification 行为。
- stdio transport 从 stdin 读取换行分隔的 JSON-RPC，并向 stdout 写出 JSON-RPC 消息，保持当前行为。
- HTTP transport 暴露本地 endpoint，并复用同一套 JSON-RPC 请求处理路径。

本功能不应重写现有 `Tools/*` 和 `RimSearcher.Core/*` 的行为。

## HTTP 协议行为

初版 Streamable HTTP 实现聚焦 request-response：

- `POST /mcp` 接收一个 JSON-RPC request、notification 或 response body。
- 带 `id` 的 request 返回一个 JSON-RPC response，`Content-Type` 为 `application/json`。
- 不带 `id` 的 notification 返回 `202 Accepted`，无响应体。
- 初版 `GET /mcp` 返回 `405 Method Not Allowed`。
- `initialize`、`notifications/initialized`、`tools/list`、`list_tools`、`tools/call` 和 `call_tool` 在 transport 允许的范围内保持与 stdio 一致。

stdio 下 server-to-client logging notification 很直接，因为 stdout 就是协议流。初版 HTTP request-response 模式不提供独立 SSE stream，因此不主动交付 unsolicited outgoing notification。HTTP 模式下无法作为 JSON-RPC response 返回的诊断信息应写入 stderr 或普通进程日志。

## 安全

默认 HTTP 绑定地址为 `127.0.0.1`。本功能支持的目标使用方式是仅绑定 localhost。

当 HTTP 请求带有 `Origin` header 时，server 应拒绝非 localhost origin，并返回 `403 Forbidden`。这符合 MCP Streamable HTTP 对本地 server 的安全建议，也能降低 DNS rebinding 风险。

`0.0.0.0` 不是默认绑定地址，也不是推荐配置。远程或局域网暴露、认证和授权不属于本设计范围。

## 错误处理

启动阶段错误应尽量保持当前行为：

- 配置缺失或无效时记录日志。
- 源码路径缺失时记录日志。
- 索引缓存加载失败时回退到重建索引。
- 索引缓存保存失败时记录日志，但不阻止 server 运行。

HTTP 特有错误应明确返回：

- 不支持的 HTTP method 返回 `405`。
- JSON 无效时尽可能返回 JSON-RPC parse error。
- 非法 Origin 返回 `403`。
- 端口绑定失败由 host 启动错误暴露，并应能在 stderr 中看到。

## 测试

自动化测试应聚焦 transport 选择和请求处理，不要求真实 RimWorld 源码树：

- CLI 解析覆盖默认 stdio 值和显式 Streamable HTTP 值。
- JSON-RPC 处理覆盖 `initialize` 和 `tools/list`。
- HTTP smoke test 向配置的 mount path POST `initialize`，并验证 `serverInfo`。
- notification 处理验证 HTTP notification 返回 `202 Accepted`。
- `GET /mcp` 返回 `405 Method Not Allowed`。

手动验收应覆盖：

- 现有 stdio 配置仍然能列出全部六个工具。
- 手动启动 HTTP 后可访问 `http://127.0.0.1:51234/mcp`。
- 使用 URL 配置的 MCP 客户端可以调用 `locate`。
- 两个连接到同一 HTTP URL 的客户端共享同一个 server 进程。

## 文档

README 需要更新：

- 保留现有 stdio 用法，并说明它仍是默认模式。
- 新增共享本地 HTTP 服务启动命令。
- 新增 URL 形式 MCP 客户端配置示例。
- 简短说明 stdio 客户端仍会每个客户端启动一个进程。
- 简短提醒 HTTP 模式面向 localhost 使用。
