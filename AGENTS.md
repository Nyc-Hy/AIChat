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

## 产品定位（2026-07-30 用户原话）

**AIChat 是 daily driver,要完全替代 ClaudeCode。** 这不是 demo,不是实验场,不是玩票。

含义:
- "AI 味太重 / 活人感" 这类反馈是**核心产品定位**,不是个人偏好。任何 UI 改动都得问:这让一个每天开 8 小时的人用着更舒服,还是更花哨?
- **功能完整度对标 ClaudeCode**:agent loop、工具执行、代码编辑、流式响应、上下文管理、tool approval,这些不是 nice-to-have,是产品本身
- **美学对标 Linear / Notion**:私人工具感、企业级克制,不要 SaaS / AI-startup 调性
- 任何"加新东西"的决定都要先回答:背后有真功能吗?没有就删掉,UI 没功能就是噪音
