# Codex Desktop 竞品证据目录

> **Wave 0 用途**：收集 Codex Desktop 的官方资料、用户截图、Computer Use 实操笔记，
> 作为 `PARITY_TRACKING.md` 中"证据等级"列的物理来源。
> 每个证据文件必须能被 `Feature ID` 引用；引用格式：
> `<evidence-level>: <file/url/line>`。

## 目录约定

```
competitor-evidence/
├── README.md                       # 本文件
├── official-docs/                  # official-confirmed 证据
│   ├── projects.md
│   ├── plugins.md
│   ├── automations.md              # scheduled tasks
│   ├── subagents.md
│   ├── sandboxing.md
│   └── sites.md
├── screenshots/                    # screenshot-confirmed 证据
│   ├── YYYY-MM-DD-<topic>-<index>.png
│   └── YYYY-MM-DD-<topic>-<index>.md   # 配套说明：用户提供的截图 + 注释
├── computer-use/                   # observed 证据
│   ├── YYYY-MM-DD-<topic>-<index>.md   # 笔记本
│   └── YYYY-MM-DD-<topic>-<index>.json # 自动化录制的 step log
└── inferred.md                     # inferred 证据汇总：列出"靠名字/布局猜"的项
```

## 证据等级（与 `CODEX_DESKTOP_PARITY_PLAN.md` §3 一致）

- `screenshot-confirmed`：用户提供的截图直接可见。`screenshots/` 下文件。
- `official-confirmed`：当前官方文档明确说明。`official-docs/` 下文件 + URL。
- `observed`：Computer Use 实际操作确认。`computer-use/` 下笔记本。
- `inferred`：仅从名称或布局推断。`inferred.md` 中集中登记。
- `deferred`：已有证据但明确不进入当前 Wave。登记在 `PARITY_TRACKING.md` 的"延后原因"列。

## Wave 0 必须落地的内容

- [ ] `official-docs/` 下 6 个 md 文件（每个官方链接一份笔记）
- [ ] `computer-use/` 下至少 6 份笔记本（覆盖：新对话、项目、PR、Plugins、Scheduled、Sites、Diff、审批、Environment、设置）
- [ ] `inferred.md` 中集中登记所有 `inferred` 项及理由

**未完成前不要宣称达到 Wave 0 退出条件。**
