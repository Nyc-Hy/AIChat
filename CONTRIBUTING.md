# 贡献指南

AIChat 使用 GitHub Issues、Pull Requests 和 CI 管理开发流程。

提交贡献即表示你同意你的贡献使用 Apache License 2.0 授权。

## 工作流

1. 创建或选择一个 GitHub Issue。
2. 从 `master` 创建一个简短、清晰的分支。
3. 每次改动聚焦一个功能、修复或文档更新。
4. 打开 Pull Request 前先完成本地验证。
5. Pull Request 需要关联 Issue，并说明验证结果。

## 分支命名

建议使用简短、可读的名称：

```text
feature/provider-health-check
fix/agent-run-cancellation
docs/github-workflow
chore/update-ci
```

自动化分支可以使用 `codex/` 前缀。

## 提交信息

使用祈使句描述变更：

```text
Harden provider configuration and errors
Improve agent run reliability diagnostics
Document GitHub workflow
```

不要提交本地密钥、日志、安装包、`bin/`、`obj/`、`.vs/`、`.tools/` 或生成产物。

## Pull Request 规范

每个 PR 应包含：

- 改了什么
- 为什么改
- 运行了哪些测试或验证
- UI 变更的截图或说明
- 关联的 Issue

未完成的工作请使用 Draft PR。只有本地构建和测试通过后再标记为 Ready for review。

## 验证

代码变更：

```powershell
dotnet build AIChat.sln --no-restore -m:1 -v:minimal
dotnet test tests\AIChat.Tests\AIChat.Tests.csproj --no-restore -m:1 -v:minimal
```

仅文档变更：

```powershell
git diff --check
```

## 评审标准

评审重点：

- 正确性和回归风险
- 工具、路径、Shell、Git 操作和密钥处理的安全性
- 行为变更是否有测试覆盖
- 是否符合既有架构和代码风格
- 用户可见错误信息是否清晰

除非必要，不要把大规模重构和功能改动混在同一个 PR 中。

## 安全问题

不要在公开 Issue 中披露漏洞细节。请按照 [SECURITY.md](SECURITY.md) 处理。
