# ClaudeCodeForVS

A Visual Studio extension that integrates Claude Code into the IDE, providing a chat UI, editor context, file references, and command execution through the Claude CLI.

English | [简体中文](README.zh-CN.md)

## Features

- Chat UI inside Visual Studio (tool window).
- Optional editor context tracking (current file, selected content).
- File picker for convenient file references.
- Streams Claude CLI output into a timeline.
- Logging with Serilog and daily rolling files.

## Requirements

- Visual Studio 2022 or 2026 (2026 recommended).
- WebView2 runtime (included with modern Visual Studio).
- Claude CLI available on PATH.

## Installation

Build and install the VSIX from this repository.

## Quick Start

1) Open Visual Studio.
2) In the Extensions menu, choose "Open Claude Chat".
3) Type a prompt and run.

## Configuration

- The extension invokes `claude` via the CLI.
- Make sure `claude` is available in your system PATH.

## Logs

Logs are written to:

```
%LOCALAPPDATA%\ClaudeCodeForVS\Logs
```

## License

See `LICENSE`.
