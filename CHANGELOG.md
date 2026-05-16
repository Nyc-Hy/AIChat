# 变更日志

AIChat 的重要变更会记录在此文件中。

在正式版本化发布开始前，本项目采用简单的日期/阶段式变更日志。

## Unreleased

### 新增

- GitHub CI、Issue 模板、Pull Request 模板和贡献流程。
- Provider 配置校验、连接测试和标准化错误分类。
- Agent 运行可靠性诊断，包括取消、工具失败和恢复建议。
- 密钥脱敏和更安全的本地设置序列化。

### 变更

- 构建和测试说明改为使用单节点 .NET 命令，保证本地和 CI 行为更稳定。
- GitHub 仓库管理流程记录在 `docs/GITHUB_WORKFLOW.md`。

### 安全

- API Key 在静态存储中受保护，并在诊断信息中脱敏。
- 工具追踪、审计记录、Provider 事件和 Agent 产物会脱敏敏感值。
- Shell 和路径处理通过 allowlist、blocklist 和项目路径保护进行约束。
