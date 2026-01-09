# ClaudeCodeForVS

一个将 Claude Code 集成到 Visual Studio 的扩展，提供聊天界面、编辑器上下文、文件引用以及通过 Claude CLI 执行命令的能力。

[English](README.md) | 简体中文

## 功能

- Visual Studio 内置聊天窗口（工具窗口）。
- 可选的编辑器上下文跟踪（当前文件、选中内容）。
- 文件选择器，便捷添加文件引用。
- 将 Claude CLI 流式输出渲染到时间线。
- 使用 Serilog 进行日志记录并按天滚动。

## 环境要求

- Visual Studio 2022 或 2026（推荐 2026）。
- WebView2 运行时（现代 Visual Studio 自带）。
- Claude CLI 已加入 PATH。

## 安装

从本仓库构建并安装 VSIX。

## 快速开始

1) 打开 Visual Studio。
2) 扩展菜单中选择 "Open Claude Chat"。
3) 输入提示并运行。

## 配置

- 扩展通过 CLI 调用 `claude`。
- 请确保 `claude` 在系统 PATH 中可用。

## 开发

构建：

```bash
dotnet build
```

## 日志

日志输出路径：

```
%LOCALAPPDATA%\ClaudeCodeForVS\Logs
```

## 许可证

见 `LICENSE`。
