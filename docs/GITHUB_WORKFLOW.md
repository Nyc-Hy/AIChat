# GitHub 工作流

AIChat 使用 GitHub Issues、Pull Requests 和 CI 管理项目。

## 仓库默认设置

- 默认分支：`master`
- 开源协议：Apache License 2.0
- 代码变更的本地验证命令：
  - `dotnet build AIChat.sln --no-restore -m:1 -v:minimal`
  - `dotnet test tests\AIChat.Tests\AIChat.Tests.csproj --no-restore -m:1 -v:minimal`
- CI 入口：`.github/workflows/ci.yml`
- Release 入口：`.github/workflows/release-desktop.yml`

## Issues 规范

Issues 是规划和问题跟踪的入口。

推荐标签：

- `type:feature`
- `type:bug`
- `type:docs`
- `type:chore`
- `area:agent`
- `area:providers`
- `area:tools`
- `area:ui`
- `area:storage`
- `area:security`
- `good first issue`
- `help wanted`

一个 Issue 应说明问题、期望行为、必要的实现提示和验证方式。

## Pull Request 规范

每个聚焦改动都应通过 PR 合并。PR 应：

- 使用 `Closes #123` 或 `Refs #123` 关联 Issue
- 总结用户可见的行为变化
- 列出验证命令和结果
- 说明刻意留到后续处理的事项

未完成工作使用 Draft PR。Ready PR 应在 CI 通过后再合并。

## CI 策略

CI 在 Pull Request 和 push 到 `master` 时运行。

流程包括：

1. 根据 `global.json` 设置 .NET SDK
2. Restore
3. 单节点 MSBuild 构建
4. 单节点 MSBuild 运行测试项目

单节点构建/测试是有意选择的，用于保证本地和 CI 行为稳定一致。

## 分支保护

`master` 分支应启用保护：

- 合并前需要 Pull Request。
- 合并前需要 CI 构建和测试通过。
- 禁止 force push。
- 直接 push 权限仅限维护者。

## 合并建议

合并前确认：

- CI 通过。
- PR 有清晰摘要和验证说明。
- 安全敏感改动说明了路径、Shell、Provider、审计或密钥处理影响。
- UI 改动说明影响的界面或交互。

建议保持一致的合并策略。若更重视历史可读性，可以使用 squash merge。

## 发布

手动发布桌面端：

```bash
pwsh scripts/publish-desktop.ps1
```

Release notes 应包含用户可见变更、验证结果和已知问题。
