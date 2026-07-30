# AIChat Launch Plan

AIChat 的上线目标是成为一个跨平台的 Vibe Coding 编程助手，主产品形态是 **Avalonia 桌面应用**。CLI/TUI 已移除，不再作为产品形态存在。

产品边界见 [PRODUCT_SCOPE.md](PRODUCT_SCOPE.md)。发布前检查见 [RELEASE_CHECKLIST.md](RELEASE_CHECKLIST.md)。主线是让 AIChat 以 Avalonia UI 为主，成为可发布、可验证、可替代 Claude Code 类工作流的桌面编码助手。

## 上线范围

可上线版本聚焦 Avalonia 桌面应用：

- 支持 macOS (Apple Silicon / Intel) / Linux x64 / Windows x64。
- 支持 DeepSeek、MiMo、MiniMAX、OpenAI-compatible、Anthropic-compatible 等已登记 provider。
- 支持项目初始化、Provider 配置、模型列表、Task 一次性 / 连续任务执行。
- 支持 `Fast` / `Standard` / `Deep` 执行模式。
- 支持 DeepSeek / MiMo / MiniMAX 的第一版 `ModelProfile`。
- 支持 Provider readiness 验证（**Test connection** 按钮 → `ProviderConnectionTester`）。
- 支持文件读取、搜索、编辑、补丁、Git、构建、测试和 Shell 工具。
- 默认单 Agent loop，避免 planner、sub-agent、benchmark、memory、plugin/MCP 进入主路径。
- 写入、Shell、构建/测试和 Git mutation 默认需要显式 UI 审批卡（不允许自动批准，除非用户在 advanced 配置中明确改全局默认值）。

## 发布门槛

上线前必须满足：

1. `dotnet build AIChat.sln --no-restore -m:1 -v:minimal` 通过。
2. `dotnet test tests/AIChat.Tests/AIChat.Tests.csproj --no-restore -m:1 -v:minimal` 通过。
3. 桌面端基础冒烟：
   - `dotnet run --project src/AIChat.App.Avalonia/AIChat.App.Avalonia.csproj` 能开窗。
   - 侧边栏添加项目可用，Provider 卡片保存/测试连接可用。
   - 发送任务 → 看到 Activity 流 + Markdown 渲染 → Tool approval 弹窗可用。
4. macOS / Linux / Windows 发布包可通过对应 RID runtime 发布（self-contained single-file）。
5. README 包含桌面端运行说明、平台安装说明、模型配置说明、贡献说明。
6. `scripts/publish-desktop.ps1` 能生成 `aichat-desktop-<rid>.zip` / `tar.gz` 包和 `SHA256SUMS.txt`。
7. GitHub Actions `Release Desktop` 工作流能生成 `osx-arm64` / `osx-x64` / `linux-x64` / `win-x64` archive 和对应 sha256 文件。
8. 推送 `v*` tag 时，GitHub Actions 会自动创建 GitHub Release 并上传四个平台的 archive 和 sha256 文件。

## 1.0 Beta 已覆盖能力

- Avalonia 桌面应用：开窗、侧边栏、Provider 配置、Tool approval、Activity 流、Session metrics、Theme 切换。
- Provider readiness 验证（`Test connection`）。
- macOS Apple Silicon 真机烟测：build 0 错 / 621/621 测试通过 / Avalonia app 启动成功。
- Windows x64 真机烟测：build 0 错 / Avalonia app 启动成功。
- Avalonia 自包含 single-file publish（osx-arm64 / linux-x64 / win-x64 都能产出）。
- 发布包 SHA-256 校验文件流程。
- tag 触发 GitHub Release 自动发布。

## 1.0 Beta 发布验证状态

- Windows `win-x64`：已在本机验证 Avalonia app 启动、侧边栏渲染、Provider 卡片交互。
- macOS `osx-arm64`：已在本机验证 Avalonia app 启动、主题切换、Provider 配置、Activity 流渲染。
- Linux `linux-x64`：发布包已生成；需要在 Linux x64 真机执行同样的 Avalonia app 启动冒烟。

## 1.0 Beta 之后（1.0.0 GA 收尾）

- Avalonia 端到端冒烟：真实 provider（DeepSeek / MiMo / MiniMAX）走完"添加项目 → 配置 Provider → 发送任务 → Tool approval → 完成"全流程。
- Linux x64 真机烟测。
- macOS / Linux 上 Provider API key 加密 at-rest（与 Windows DPAPI 对齐）。
- 真实 coding 任务回归：项目上下文 → 任务 → 工具审批 → 验证 → 总结，确认 agent loop 在桌面 UI 上每平台都成立。

## 之后版本

1.0.0 之后按 [1.0 路线图](ROADMAP_1.0.md) 和 [开发路线图](REMAINING_DEVELOPMENT_PLAN.md) 推进：

1. **跨平台加密存储**：macOS / Linux Provider API key 加密。
2. **上下文工程增强**：增量索引、相关文件智能评分。
3. **MCP Client 集成**：通过 `IExternalToolProvider` 接入 MCP Server 工具。
4. **可观测性**：运行摘要、审计分组、验证输出更清晰。
5. **多 Agent 队列**：扩展单运行队列到排队 / 并发 Agent。
