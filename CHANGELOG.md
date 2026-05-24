# 变更日志

AIChat 的重要变更会记录在此文件中。

在正式版本化发布开始前，本项目采用简单的日期/阶段式变更日志。

## Unreleased

### 新增

- 跨平台 CLI `aichat`，支持 `models`、`config`、`doctor`、`init`、`ask` 和 `tui`。
- Fast / Standard / Deep 三档 Agent 执行模式，用于平衡速度、准确率和 Token 消耗。
- DeepSeek、MiMo、MiniMAX 模型 profile，用于模型定制化提示和执行策略。
- CLI 三平台发布脚本、GitHub Actions release workflow 和 sha256 校验文件。
- GitHub CI、Issue 模板、Pull Request 模板和贡献流程。
- Provider 配置校验、连接测试和标准化错误分类。
- Agent 运行可靠性诊断，包括取消、工具失败和恢复建议。
- 密钥脱敏和更安全的本地设置序列化。

### 变更

- 默认关闭高消耗自动能力，降低工具轮次、Token 消耗和意外写入风险。
- WPF 启动路径降级为 Windows-only 图形壳层，跨平台入口聚焦 CLI/TUI。
- 构建和测试说明改为使用单节点 .NET 命令，保证本地和 CI 行为更稳定。
- GitHub 仓库管理流程记录在 `docs/GITHUB_WORKFLOW.md`。

### 安全

- API Key 在静态存储中受保护，并在诊断信息中脱敏。
- 工具追踪、审计记录、Provider 事件和 Agent 产物会脱敏敏感值。
- Shell 和路径处理通过 allowlist、blocklist 和项目路径保护进行约束。
