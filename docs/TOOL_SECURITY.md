# 工具安全模型

## 权限模式

每个工具都有权限模式，用于控制执行方式：

| 模式 | 行为 |
|---|---|
| `Disabled` | 不暴露给模型 |
| `AutoReadOnly` | 只读工具无需确认；写入和 Shell 工具仍需审批 |
| `ConfirmEachTime` | 每次调用都需要用户审批 |
| `AllowForSession` | 首次审批后，本次 Agent 运行内自动允许 |

### 默认权限

`AgentToolRegistry` 按风险分配默认权限：

- **只读工具**（list_files、read_file、search_text、git_status、git_diff）：`AutoReadOnly`
- **写入工具**（write_file、edit_file、apply_patch、git_restore_file、git_commit）：`ConfirmEachTime`
- **Shell 工具**（run_build、run_test、run_shell）：`ConfirmEachTime`

### 项目级覆盖

项目可以覆盖全局工具权限。合并配置时，项目级覆盖优先。

## 路径保护

所有文件操作都通过 `ProjectPathGuard` 限制在项目目录内：

- `ResolveInsideProject()` 将相对路径解析到项目根目录。
- 拒绝通过 `..` 或绝对路径逃逸项目目录。
- 适用于 `ReadFileTool`、`WriteFileTool`、`EditFileTool`、`ApplyPatchTool`、`ShellCommandTool`。

## Shell 沙箱

`ShellCommandTool` 使用多层防护。

### Blocklist（拒绝列表）

包含以下模式的命令会被直接拒绝：

- 递归删除：`rm -rf`、`Remove-Item -Recurse`、`rmdir /s`
- 强制重置：`git reset --hard`、`git clean -fdx`、`git push --force`
- 磁盘操作：`dd if=`、`mkfs.`、`format `
- 系统命令：`shutdown`、`reboot`、`Stop-Computer`
- 权限提升：`chmod 777`、`chown -R`、`Set-ExecutionPolicy`

### Allowlist（允许列表）

以下前缀开头的命令被视为相对安全：

- 构建/测试：`dotnet build`、`dotnet test`、`dotnet restore`、`dotnet run`
- Git 只读：`git status`、`git diff`、`git log`、`git branch`、`git show`
- 搜索：`rg`、`grep`、`find`
- 文件列表：`ls`、`dir`、`cat`、`head`、`tail`
- 信息查询：`echo`、`pwd`、`which`、`file`、`stat`

### 超时

Shell 命令有可配置超时，默认 30 秒，最大 120 秒。超时后进程会被终止。

## 写入工具安全

### 快照 + 哈希

修改文件前，工具会记录：

- `ContentSnapshot`：修改前内容。
- `PostChangeHash`：修改后内容的 SHA256。

### 冲突检测

回滚变更时，系统会比较当前文件哈希和 `PostChangeHash`。如果不同，说明文件被外部修改过，会报告冲突。

## 审计日志

每个重要动作都会记录到审计日志：

- `ToolCallRequested`：模型请求工具调用。
- `ToolCallApproved`：用户批准。
- `ToolCallRejected`：用户拒绝。
- `FileWritten`：文件被修改。
- `ShellExecuted`：Shell 命令已执行。
- `VerificationRun`：验证命令已执行。
- `AgentRunStarted/Completed/Failed/Cancelled`：运行生命周期。

审计事件以 JSONL 形式保存在 `%APPDATA%\AIChat\audit\` 下，每个项目一份。

## 审批界面

工具需要审批时，用户会看到：

- 工具名称和风险标识
- 工具将执行内容的摘要
- 完整参数 JSON
- 文件操作预览
- 编辑操作的 diff

可选动作包括：本次允许、本会话允许、拒绝。
