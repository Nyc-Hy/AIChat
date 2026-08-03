# 变更日志

AIChat 的重要变更会记录在此文件中。

> **1.0 起版本化发布**。格式参考 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.1.0/)。
> 之前的 0.x 时代按"日期/阶段"列出；1.0+ 按 [SemVer](https://semver.org/lang/zh-CN/)。
> 本文件与 `docs/RELEASE_NOTES_<version>.md` 的分工：CHANGELOG 是公开发布日志
> （GitHub Release 自动 dump），RELEASE_NOTES 是单 release 的详细说明（breaking
> changes + 迁移指引 + screenshots）。

## 1.0.0-beta.1 — 2026-08-03

首次面向公开用户的 beta release。基于 codex/desktop-rebuild 分支 12-wave Codex
Desktop parity first-slice ship。provider 收敛到 MiniMax（M3 latest），CLI/TUI
完全删除，只剩 Avalonia desktop（macOS / Windows / Linux）。

### Added
- 5 first-level nav（拉取请求 / 站点 / 已安排 / 插件 / 设置）+ 5 Environment sections
  （变更 / 本地 / 子智能体 / 来源 / Background），全部 first-slice ship。
- Standalone 与 Project 二元 session 分类 + 跨项目聚合最近会话 + 多 folder
  WorkspaceProject + `⌘O` 添加项目。
- 12 first-class agent tools：list_files / read_file / write_file / edit_file /
  apply_patch / search_text / git_status / git_diff / git_restore_file / git_commit
  / run_build / run_test / run_shell / read_input_artifact / update_plan。
- Sprint 0.5 2-toggle 权限模型（Default Access + Full Access）+ Environment panel
  显隐状态持久化。
- 命令面板（⌘K）+ 18+ 键盘快捷键 + Slash 命令（/help / /status / /memory /
  /git / /clear / /copy）。
- Settings 4 大分类（Personal / Integrations / Coding / Archived）+ 搜索。
- Plugin registry / Scheduled tasks / Sites 三套 persistence + modal。
- BackgroundProcessSupervisor（进程组 kill + 重启恢复 + log tail ring buffer）。
- CrashReporter：全局 AppDomain / Dispatcher / TaskScheduler hook + append-only
  `crash.log` + 启动时检测上轮崩溃并 toast 提示。
- `AICHAT_API_KEY` env var override：daily driver 启动 0 次 keychain access，
  0 弹窗，settings.json 原 keychain 引用保留（unset 即切回，不丢 secret）。
- ProviderSettingsService 升级：检测到已删 provider host（Anthropic / DeepSeek /
  Xiaomi MIMO）→ 强制重写为 MiniMax 默认 endpoint，避免 0.5 升级用户首次 send
  撞 401/404。
- 跨平台 Git 真实 Stage / Unstage / Restore / Commit + GitStatusView diff viewer。
- Tool approval modal：Esc = 拒绝 / Enter = 允许一次（"本会话内允许" 故意不绑
  快捷键）。
- AI bubble 失败 / 停止视觉：红边 / 琥珀边 + status chip 同步。
- Conversation list inline rename（右键 → 重命名）。
- EmptyStateView 首屏 hero + 4 quick-action card。
- Local-preview Sites：`python3 -m http.server` 走 BackgroundProcessSupervisor
  进程组，关 app 自动杀。

### Changed
- **Provider 收敛**：5 provider（DeepSeek / MiMo / MiniMax / OpenAI / Anthropic）
  → **1 provider**（MiniMax M3），OpenAI-compatible 协议。自定义 base URL
  可以指向其他 OpenAI-compatible 端点，但 catalog 只有 1 行。
- 默认 model：`MiniMax-M3`（`modelContextLimit = 200_000`，vision = false）。
- 工具权限默认：所有写操作 `ConfirmEachTime`（按 Codex parity 约定）。
- Avalonia 12 主题 tokens 切到 `DynamicResource`，theme 切换立即生效。
- 测试基线：**817/817 pass**（DI lock 37/37 + 迁移 19/19 + registry 30/30 +
  supervisor 11/11 + 6 crash reporter + 8 env override + 6 legacy host rewrite
  + ...），0 警告 0 错误。

### Removed
- **CLI / TUI 全部删除**：`aichat` / `aichat tui` / `aichat ask` / `aichat context`
  / `aichat doctor` / `aichat models` / `aichat config` / `aichat projects` /
  `aichat init`。1.0 是 desktop-only。
- **WPF 启动路径删除**：1.0 是 Avalonia-only（macOS / Windows / Linux 同一 shell）。
- **Anthropic provider 整项目删除**：`AIChat.Providers.Anthropic/`。
- **DeepSeek / MiMo / Anthropic 模板删除**：`ChatProviderCatalog.All = [MiniMax]`。
- **3 个被 user 删除的子系统**：FileTreeView / FilePreviewView / FileTreeBuilder
  及其 view-models（见 AGENTS.md "3 个被 user 删除的 test file"段；恢复前先 grep
  确认无人读）。

### Security
- API Key 静态存储：macOS Keychain / Linux Secret Service / Windows DPAPI
  current user + `AICHAT_API_KEY` env override。
- Tool approval 强制 modal，⌘K / command palette 触发。
- ProjectPathGuard 工具调用层强制。
- 密钥脱敏在所有 log / diagnostic / 错误信息（`SensitiveDataRedactor`）。
- 审计 log：5MB rotation + 30 天 retention。
- 无遥测：详见 `docs/TELEMETRY.md`。
- 数据处理：详见 `docs/PRIVACY.md`。

### Fixed
- Settings modal 没有显示新增的 `DefaultAccess` / `FullAccessEnabled` /
  `EnvironmentPanelOpen` 字段（schema 写了 UI 没接）—— 已 ship 但 1.0 Beta
  仍只通过 titlebar badge 切；UI 入口 follow-up 1.0.1。
- macOS 启动时每个 provider key 弹 1 次 keychain 授权对话框——`AICHAT_API_KEY`
  env override 解决（commit `35ef756`）。
- 0.5 升级用户的 BaseUrl 静默保留指向已删 provider（`api.anthropic.com` 等）→
  首次 send 必 401/404——`LegacyProviderHosts` 重写解决（commit `2bf9c04`）。
- 关 app 不杀 BackgroundProcessSupervisor 持有的子进程组——Exit handler 先
  `StopAllAsync` 再 Dispose（commit `<待 ship>`）。
- fire-and-forget 抛异常 → app 静默退出——CrashReporter 兜底（commit `<待 ship>`）。
- (历史) 9 个 commit 清理 wave 见 SHIP_REPORT_2026-08-02.md §6。

## 0.x 历史（已废止，仅供回溯）

0.5.0 之前的 CHANGELOG 在 git log / docs/RELEASE_NOTES_0.5.0.md。0.5.0 是 CLI/TUI
主导的最后版本，1.0.0 切换到 desktop-only，迁移指引见
`docs/RELEASE_NOTES_1.0.0.md` §"Migration from 0.5.0"。
