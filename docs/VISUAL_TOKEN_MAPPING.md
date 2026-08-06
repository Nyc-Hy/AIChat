# AIChat Visual / Shortcut / Copy Mapping

> **Wave 0 first slice (AIChat 侧)。** Codex Desktop 侧等 `docs/competitor-evidence/` 收齐后补全。
> 本文档 3 张映射表都是 Wave 0 退出条件要求的"版本化映射表"：
> 1. 设计 token（surface / text / accent / status / spacing / radius / motion）
> 2. 键盘快捷键（已有 vs 计划新增）
> 3. 文案（i18n 占位 + Codex 平行术语）
>
> 任何 XAML 改动必须**先**回这里登记 token 来源，**再**写 `Tokens.axaml` / `Tokens.Dark.axaml`。

---

## 1. 设计 token 映射

### 1.1 Surface ladder

| Token key (AIChat) | Light | Dark | 用途 | Codex 侧对照 |
|---|---|---|---|---|
| `BgColor` | `#fafaf7` | `<待补>` | 应用底色 | 待 `competitor-evidence` |
| `SurfaceSunkenColor` | `#f1ede4` | `<待补>` | 凹陷表面（输入框 / sidebar 选中底） | 待补 |
| `SurfaceColor` | `#ffffff` | `<待补>` | 卡片底 | 待补 |
| `SurfaceRaisedColor` | `#ffffff` | `<待补>` | 浮起卡片 | 待补 |
| `SurfaceOverlayColor` | `#ffffff` | `<待补>` | 模态底 | 待补 |
| `LineColor` | `#e9e3d6` | `<待补>` | 主分隔线 | 待补 |
| `LineSoftColor` | `#ebe5d3` | `<待补>` | 弱分隔线 | 待补 |
| `LineStrongColor` | `#d8d2c2` | `<待补>` | 强分隔线 | 待补 |

### 1.2 Text ladder

| Token key | Light | Dark | 用途 |
|---|---|---|---|
| `TextColor` | `#18181b` | `<待补>` | 主文字 |
| `Text2Color` | `#3a4256` | `<待补>` | 次级文字 |
| `MutedColor` | `#6b6b75` | `<待补>` | 弱化文字（placeholder / hint） |

### 1.3 Accent ramp (teal)

| Token key | Light | 用途 |
|---|---|---|
| `Accent50Color`..`Accent900Color` | teal 50→900 | 主交互色（按钮 / 选中 / 链接） |
| `AccentSoftColor` | `#1F2f6f5e` | 软背景（hover / 选中态底） |
| `AccentHoverColor` | `#255e4f` | 按钮 hover |
| `AccentActiveColor` | `#1d4a3e` | 按钮 active |
| `AccentFgColor` | `#ffffff` | accent 背景上的前景色 |

**Wave 0 退出条件**：用 Computer Use / 截图核验 Codex Desktop 的 accent 色温，跟 AIChat 当前的 teal 对齐。差距大时记录到 `inferred.md` 备查。

### 1.4 Status

| Token key | Light | 用途 |
|---|---|---|
| `InfoBg/Fg/BorderColor` | `#eaf2ff` / `#1e3a8a` / `#c7d7fe` | 蓝色信息 |
| `SuccessBg/Fg/BorderColor` | `#e7f7ef` / `#0e3b21` / `#bfe5cc` | 绿色成功 |
| `WarningBg/Fg/BorderColor` | `#fff3d6` / `#7c2d12` / `#f4cf86` | 琥珀警告 |
| `ErrorBg/Fg/BorderColor` | `#fbeaeb` / `#8a1f23` / `#e9b4b6` | 红色错误 |

### 1.5 Type ramp

| Token | 数值 | 用途 |
|---|---|---|
| `FontDisplay` | 40 | 启动页 hero |
| `FontH1` | 30 | 一级标题 |
| `FontH2` | 22 | 二级标题 |
| `FontH3` | 18 | 三级标题 / 卡片标题 |
| `FontBodyL` | 16 | 输入框 / 大段正文 |
| `FontBodyM` | 14 | 列表 / 卡片正文 |
| `FontBodyS` | 13 | 次级正文 |
| `FontCaption` | 12 | 标签 / 状态行 |
| `FontOverline` | 11 | 分类小标题（间距大） |
| `FontMicro` | 10 | 工具提示 / 角标 |

### 1.6 Spacing & Radius

| Token | 数值 | 用途 |
|---|---|---|
| `Space0`..`Space11` | 0/4/8/12/16/20/24/32/40/48/64/80 | 8 倍数栅格 |
| `Padding1`..`Padding7` | 4/8/12/16/20/24/32 | padding shortcut |
| `RadiusXs`..`Radius2xl` | 4/6/10/14/20/28 | 卡片圆角 |
| `RadiusPill` / `RadiusFull` | 999 | 胶囊 / 圆形 |

### 1.7 Elevation & Motion

| Token | 数值 | 用途 |
|---|---|---|
| `Elevation0..5Shadow` | 0–24px Y + alpha 渐强 | 阴影阶梯 |
| `ElevationToastShadow` | 4px Y + 0.18 alpha | Toast |
| `MotionFast/Base/Slow` | 100/180/240ms | 通用过渡 |
| `MotionBubble` | 220ms | AI bubble 出现 |
| `MotionDot` | 1200ms | "正在输入" 动画 |
| `MotionToast` | 220ms | Toast 出现 / 消失 |

### 1.8 字体

| Token | 字体栈 |
|---|---|
| `FontSans` | Outfit, -apple-system, "PingFang SC", "Microsoft YaHei", sans-serif |
| `FontMono` | JetBrains Mono, ui-monospace, "Sarasa Mono SC", monospace |

**Wave 0 退出条件**：用 Computer Use 截 Codex Desktop 设置中心"Appearance"，确认 Dark / Light 模式下的 accent 色温、surface 梯度和字重，是否与 AIChat 当前 teal 调性一致。差距大时在 `VISUAL_TOKEN_MAPPING.md` §1 顶部加"视觉对齐偏差"段。

---

## 2. 键盘快捷键映射

来源：`src/AIChat.App.Avalonia/Views/MainWindow.axaml.cs:50-208`（19 个 `KeyBinding`）+ `Views/Controls/ToolApprovalView.axaml.cs`（Esc/Enter）+ `Views/Controls/MemoryEditorView.axaml.cs`（如有）。

### 2.1 AIChat 已绑定（19 个 + ToolApproval 2 个）

| 快捷键 | 命令 / 行为 | 绑定位置 | 关联 Plan §8 旅程 |
|---|---|---|---|
| ⌘K | 打开命令面板 | `MainWindow.axaml.cs:50` | 全局入口 |
| ⌘, | 打开设置 | `MainWindow.axaml.cs:56` | 打开设置 |
| ⌘N | 新建对话 | `MainWindow.axaml.cs:62` | 新建普通聊天 (`UJ-NEW-01`) / 新建项目会话 |
| ⌘⇧T | 切换主题 | `MainWindow.axaml.cs:68` | Light / Dark |
| ⌘. | 停止当前任务 | `MainWindow.axaml.cs:76` | 停止任务或进程 |
| ⌘R | 重试上一次任务 | `MainWindow.axaml.cs:84` | 重试 |
| ⌘O | 添加项目（folder picker） | `MainWindow.axaml.cs:92` | 添加项目 (`UJ-PROJ-04`) |
| ⌘T | 测试当前模型 | `MainWindow.axaml.cs:101` | Provider 调试 |
| ⌘⇧C | `/copy` 复制最后 AI 回复 | `MainWindow.axaml.cs:111` | — |
| ⌘L | 聚焦 prompt（SelectAll） | `MainWindow.axaml.cs:122` | 全局入口 |
| ⌘⇧R | 切换只读 / no-write 模式 | `MainWindow.axaml.cs:132` | 切权限 (`UJ-COMP-03`) |
| ⌘⇧V | 切换自动验证 | `MainWindow.axaml.cs:142` | — |
| ⌘⇧M | 打开 memory editor modal | `MainWindow.axaml.cs:151` | Memory |
| ⌘G | `/git` 当前分支 + 变更列表（bubble） | `MainWindow.axaml.cs:160` | 查看 git |
| ⌘⇧G | 打开 git status / diff viewer modal | `MainWindow.axaml.cs:169` | 打开 Diff (`UJ-GIT-02`) |
| ⌘⇧K | 清空对话（运行中禁用） | `MainWindow.axaml.cs:178` | — |
| ⌘/ | 打开键盘快捷键 cheat sheet | `MainWindow.axaml.cs:197` | 帮助 |
| F5 | 刷新状态 | `MainWindow.axaml.cs:206` | — |
| Esc | 关闭 modal（按优先级） | `MainWindow.axaml.cs:211` | 关闭 modal |
| Esc | Tool approval 拒绝 | `ToolApprovalView.axaml.cs` | 切权限 |
| Enter | Tool approval 允许一次 | `ToolApprovalView.axaml.cs` | 切权限 |

### 2.2 Plan §8 操作预算 vs 现状

| 旅程 (Plan §8) | AIChat 现状 | 缺口 |
|---|---|---|
| 新建普通聊天 | ⌘N (1 键) | Wave 3 前确认行为：⌘N 现有 `NewConversationCommand` 是否对应"无项目"还是"复用当前项目"？ |
| 添加项目 | ⌘O (1 键) | Wave 3 前确认 folder picker 流程 |
| 项目内新建会话 | 缺专属快捷键 | Wave 3 加：例如 ⌘⇧N |
| 搜索历史 | 缺 | Wave 3 加：⌘P / ⌘F 模糊搜索 |
| 归档或恢复 | 缺 | Wave 3 加：右键菜单 → 归档 |
| 切权限 | ⌘⇧R（no-write） | Wave 4 加：Composer 内 3 档 profile 切换 |
| 打开 Diff | ⌘⇧G（modal） | Wave 6 改：Environment 内 |
| 停止任务或进程 | ⌘. | 已实现 |
| 打开设置 | ⌘, | 已实现；Wave 10 改 full page route |
| 搜索设置 | 缺 | Wave 10 加 |
| 打开插件目录 | 缺 | Wave 8 加 |
| 安装并授权插件 | 缺 | Wave 8 加 |
| 新建 Scheduled Task | 缺 | Wave 9 加 |
| 添加 Source | 缺 | Wave 7 加 |
| 查看或重试 Subagent | 缺 | Wave 7 加 |
| 切换 Branch / Worktree | 缺 | Wave 6 加 |
| 创建 PR | 缺 | Wave 6 加 |
| 创建 Sites 项目 | 缺 | Wave 9 加 |
| 部署 Sites | 缺 | Wave 9 加 |

---

## 3. 文案映射（i18n 占位 + Codex 平行术语）

### 3.1 现状

- 现有 `MainWindowViewModel.Greeting` / `SubGreeting` 走 `EmptyStateView`，中英混排
- 现有 Slash 命令 body 走 `Resources/HelpText.md`（`EmbeddedResource`）
- 现有 modal title / button label 在 XAML hard-code（`Text="关闭"` 等）

### 3.2 Wave 0 待办

- [ ] 确认是否需要建立 `Resources/Copy.zh-CN.md` / `Resources/Copy.en-US.md`（按语言分文件而不是单文件）
- [ ] Codex Desktop 平行术语收集（待 Computer Use）：
  - "Conversation" ↔ "Chat" / "对话"
  - "Project" ↔ "项目"（一致）
  - "Workspace" ↔ "工作区" / "项目内的工作目录"
  - "Environment" ↔ "环境"（一致）
  - "Plugin" ↔ "插件"（一致）
  - "Scheduled" ↔ "已安排" / "定时任务"
  - "Sites" ↔ "站点" / "应用"
  - "Approval" ↔ "审批"（一致）

### 3.3 截屏与图标的差异化

`CODEX_DESKTOP_PARITY_PLAN.md` §2 明确要求：
- 使用 AIChat 名称、图标、文案、设计资产
- **不复制** Codex 商标、Logo、专有图标、不可获得的私有云基础设施

落到 XAML 时，所有 first-level 入口（5 个全局入口 + 4 个一级设置分类）的 icon 必须使用自有设计资产；不得从 Codex 截屏抠图。

---

## 4. Revision Changelog

- `r0.1`（2026-08-01, Wave 0 first slice, AIChat 侧）：建表 §1 AIChat token / §2 AIChat 已有快捷键 / §3 文案占位。
- `r0.2`（待 Wave 0 退出前）：补 Codex 侧 token 对照、§2.2 缺口与 Wave 计划、§3.2 平行术语收齐。
