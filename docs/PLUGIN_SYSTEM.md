# 插件系统

AIChat 支持第一版本地插件系统。插件通过 `plugin.json` 声明工具，应用启动时会从 `%APPDATA%\AIChat\plugins` 发现并加载启用的插件工具。

当前版本只支持 **命令型插件工具**：插件声明一个可执行文件和参数模板，AIChat 将它包装成标准 `IAgentTool`。插件工具仍会进入正常工具权限、审批、审计和结果摘要流程。

## 插件目录

每个插件放在独立目录中：

```text
%APPDATA%\AIChat\plugins\
  my-plugin\
    plugin.json
```

## Manifest 示例

```json
{
  "id": "dotnet_tools",
  "name": "Dotnet Tools",
  "version": "0.1.0",
  "enabled": true,
  "description": "Local dotnet helper tools.",
  "tools": [
    {
      "id": "dotnet_version",
      "description": "Read installed dotnet SDK version.",
      "risk": "read_only",
      "category": "插件",
      "groupLabel": "本地插件",
      "parametersJson": {
        "type": "object",
        "properties": {}
      },
      "command": {
        "executable": "dotnet",
        "arguments": ["--version"],
        "timeoutSeconds": 30,
        "maxOutputChars": 12000
      }
    }
  ]
}
```

> 注意：`parametersJson` 在文件中可以写成 JSON 对象或转义后的 JSON 字符串；建议保持简单对象结构。

## 参数模板

命令参数支持从工具调用参数中展开占位符：

```json
{
  "id": "search_docs",
  "description": "Search docs with rg.",
  "risk": "read_only",
  "parametersJson": {
    "type": "object",
    "required": ["query"],
    "properties": {
      "query": { "type": "string" }
    }
  },
  "command": {
    "executable": "rg",
    "arguments": ["{query}", "docs"],
    "workingDirectory": "{project_path}"
  }
}
```

内置占位符：

- `{project_path}`：当前项目根目录。
- `{plugin_id}`：插件 id。
- `{tool_id}`：工具 id。
- 其他 `{name}`：来自模型工具调用参数。

## 风险等级

`risk` 支持：

- `read_only`：只读工具，默认可自动执行。
- `write`：写入工具，默认每次确认。
- `shell`：Shell/外部命令工具，默认每次确认。

插件作者应保守设置风险等级。任何可能修改文件、访问网络、调用外部系统或产生副作用的工具都应使用 `shell` 或 `write`。

## 当前限制

- 插件不会加载任意 .NET 程序集，也不会直接执行插件代码。
- 插件命令通过 `ProcessStartInfo.ArgumentList` 启动，不经过 Shell 展开。
- 插件工具的权限、审批和审计沿用内置工具链路。
- 当前没有插件 UI 管理页，启用/禁用通过 `plugin.json` 的 `enabled` 字段控制。
