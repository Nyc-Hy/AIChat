# AIChat

本文件由 AIChat 自动生成，用于帮助 AI Agent 理解本项目。

## 技术栈

- C# / .NET

## 目录结构

```text
.claude/
artifacts/
docs/
src/
tests/
```

## 构建

```bash
dotnet build AIChat.sln --no-restore -m:1 -v:minimal
```

## 测试

```bash
dotnet test tests/AIChat.Tests/AIChat.Tests.csproj --no-restore -m:1 -v:minimal
```

## Git

本项目使用 GitHub Issues、Pull Requests 和 CI 管理开发流程。
请使用聚焦的分支，并保持 Pull Request 足够小，便于评审。

## 约定

- 遵循项目现有代码风格和模式。
- 提交前运行构建和测试。
- 使用能清楚描述变更的提交信息。
- 不要提交本地密钥、日志、安装包、`.tools/`、`.vs/`、`bin/` 或 `obj/`。
- GitHub 工作流详见 `CONTRIBUTING.md` 和 `docs/GITHUB_WORKFLOW.md`。
