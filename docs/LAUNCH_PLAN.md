# AIChat Launch Plan

AIChat 的上线目标是成为跨平台 Vibe Coding 编程助手，优先用 CLI/TUI 替代 Claude Code 的核心代码任务体验，再演进到 Web/Tauri Desktop。

产品边界见 [PRODUCT_SCOPE.md](PRODUCT_SCOPE.md)。发布前检查见 [RELEASE_CHECKLIST.md](RELEASE_CHECKLIST.md)。0.5.0 的主线是让 AIChat 以 CLI/TUI 形式成为可发布、可验证、可替代 Claude Code 类工作流的编码助手。

## 上线范围

`0.5.0` 可上线版本聚焦 CLI：

- 支持 Mac / Linux / Windows。
- 支持 DeepSeek、MiMo、MiniMAX、Anthropic 等已登记 provider。
- 支持项目初始化、模型配置、模型列表、一次性任务执行。
- 支持 `Fast` / `Standard` / `Deep` 执行模式。
- 支持 DeepSeek、MiMo、MiniMAX 的第一版 `ModelProfile`。
- 支持 `doctor`、`projects list`、`config list/use`、`--version`。
- 支持 `tui` 交互式连续会话入口。
- 支持文件读取、搜索、编辑、补丁、Git、构建、测试和 Shell 工具。
- 默认单 Agent loop，避免 planner、sub-agent、benchmark、memory、plugin/MCP 进入主路径。
- 写入、Shell、构建/测试和 Git mutation 默认需要显式 `--yes` 批准。

## 发布门槛

上线前必须满足：

1. `dotnet build AIChat.sln --no-restore -m:1 -v:minimal` 通过。
2. `dotnet test tests/AIChat.Tests/AIChat.Tests.csproj --no-restore -m:1 -v:minimal` 通过。
3. CLI 基础命令可运行：
   - `aichat models`
   - `aichat config show`
   - `aichat config set-provider`
   - `aichat init`
   - `aichat ask`
4. Mac 发布包可通过 `osx-arm64` runtime 发布。
5. README 包含配置、运行和发布说明。
6. `scripts/publish-cli.ps1` 能生成 `aichat-cli-<rid>.zip` 包和 `SHA256SUMS.txt`。
7. GitHub Actions `Release CLI` 工作流能生成 `osx-arm64`、`linux-x64`、`win-x64` zip artifacts 和对应 sha256 文件。
8. 推送 `v*` tag 时，GitHub Actions 会自动创建 GitHub Release 并上传三个平台的 zip 和 sha256 文件。

## 0.5.0 已覆盖能力

- `aichat --version`
- `aichat doctor`
- `aichat models`
- `aichat config show/list/use/set-provider`
- `aichat projects list`
- `aichat init`
- `aichat ask --mode fast|standard|deep`
- `aichat tui --mode fast|standard|deep`
- GitHub Actions CLI release artifacts
- 本地 `scripts/publish-cli.ps1` zip 打包
- 发布包 sha256 校验文件
- tag 触发 GitHub Release 自动发布

## 0.5.0 发布验证状态

- Windows `win-x64`：已在本机验证 `--version`、`models --provider deepseek`、`config set-provider`、`init`、`projects list`、`doctor`、TUI 命令切换。
- macOS `osx-arm64`：已生成 zip 并验证包结构；需要在 Apple Silicon 真机执行 `./aichat --version`、`./aichat doctor`、`./aichat tui`。
- Linux `linux-x64`：已生成 zip 并验证包结构；需要在 Linux x64 真机执行 `./aichat --version`、`./aichat doctor`、`./aichat tui`。

## 之后版本

下一阶段按顺序推进：

1. TUI：连续会话、工具审批、diff 预览、验证状态。
2. ModelProfile 深化：用真实任务基准细化 DeepSeek / MiMo / MiniMAX 参数。
3. Context cache：固定 prompt 前缀、项目摘要、文件索引、工具输出摘要。
4. Web/Tauri Desktop：跨平台图形入口。
5. 高级能力回归：Plugin/MCP、Memory、Benchmark、Sub-agent 作为显式高级模式。
