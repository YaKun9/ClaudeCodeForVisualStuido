using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ClaudeCodeForVS.Services
{
    /// <summary>
    /// 管理与 Claude CLI 进程的通信服务 / Service for managing communication with the Claude CLI process.
    /// </summary>
    public class ClaudeCodeCommandService : IDisposable
    {
        public static readonly Guid ClaudeCodePaneGuid = new Guid("A8F925EA-4515-4BBA-92E3-BB69C995AEEC");

        // 单例实例 / Singleton instance
        private static readonly Lazy<ClaudeCodeCommandService> _instance = new Lazy<ClaudeCodeCommandService>(() => new ClaudeCodeCommandService());

        public static ClaudeCodeCommandService Instance => _instance.Value;

        // 配置常量 / Configuration constants
        private static readonly TimeSpan DefaultRequestTimeout = TimeSpan.FromMinutes(10);

        private IVsOutputWindowPane _outputPane;
        private readonly SemaphoreSlim _runLock = new SemaphoreSlim(1, 1);
        private Process _process;
        private StreamWriter _stdin;
        private StreamReader _stdout;
        private StreamReader _stderr;
        private Task _stderrPumpTask;
        private string _workingDirectory;
        private bool _disposed;

        // 跟踪被修改的文件 / Track modified files
        private readonly HashSet<string> _modifiedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// 获取本次运行中被修改的文件列表 / Get modified files from the current run.
        /// </summary>
        public IReadOnlyCollection<string> ModifiedFiles => _modifiedFiles;

        /// <summary>
        /// 清除已修改文件列表 / Clear the modified file list.
        /// </summary>
        public void ClearModifiedFiles() => _modifiedFiles.Clear();

        private static async Task PumpStreamAsync(Stream stream, Encoding encoding, Action<string> onChunk, CancellationToken ct, string prefix = null)
        {
            var buffer = new byte[4096];
            var decoder = encoding.GetDecoder();
            var charBuffer = new char[encoding.GetMaxCharCount(buffer.Length)];

            while (!ct.IsCancellationRequested)
            {
                int bytesRead;
                try
                {
                    bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (IOException)
                {
                    // 流已关闭 / Stream closed
                    return;
                }

                if (bytesRead <= 0)
                {
                    return;
                }

                var charsDecoded = decoder.GetChars(buffer, 0, bytesRead, charBuffer, 0, flush: false);
                if (charsDecoded <= 0)
                {
                    continue;
                }

                var text = new string(charBuffer, 0, charsDecoded);
                if (!string.IsNullOrEmpty(prefix))
                {
                    text = prefix + text;
                }

                try
                {
                    onChunk?.Invoke(text);
                }
                catch (Exception ex)
                {
                    LogService.Error("[PumpStreamAsync] UI callback error", ex);
                }
            }
        }

        /// <summary>
        /// 解析 JSON 行并提取类型信息 / Parse a JSON line and extract the type.
        /// </summary>
        private static (JObject parsed, string type) TryParseJsonLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return (null, null);

            try
            {
                var root = JObject.Parse(line);
                var type = root.Value<string>("type");
                return (root, type);
            }
            catch (Exception ex)
            {
                LogService.Error($"[TryParseJsonLine] Failed to parse: {line?.Substring(0, Math.Min(100, line?.Length ?? 0))}", ex);
                return (null, null);
            }
        }

        /// <summary>
        /// 从 tool_use 事件中提取被修改的文件路径 / Extract modified file paths from tool_use events.
        /// </summary>
        private void TrackModifiedFile(JObject root)
        {
            try
            {
                // 检查是否是写入文件的工具调用 / Only track write/edit tools
                var eventType = root.SelectToken("event.type")?.Value<string>();
                if (eventType != "content_block_start")
                    return;

                var toolName = root.SelectToken("event.content_block.name")?.Value<string>();
                if (toolName != "Write" && toolName != "Edit" && toolName != "MultiEdit")
                    return;

                // 尝试从 input 中提取文件路径 / Extract file path from input
                var input = root.SelectToken("event.content_block.input");
                if (input == null)
                    return;

                var filePath = input.Value<string>("file_path") ?? input.Value<string>("path");
                if (!string.IsNullOrEmpty(filePath))
                {
                    _modifiedFiles.Add(filePath);
                    Log($"[FileTrack] Modified: {filePath}");
                }
            }
            catch (Exception ex)
            {
                LogService.Error("[TrackModifiedFile] Failed to track file", ex);
            }
        }

        public async Task RunAsync(string prompt, string workingDirectory, Action<string> onOutputReceived, CancellationToken ct)
        {
            ThrowIfDisposed();
            await EnsureOutputPaneAsync();
            await _runLock.WaitAsync(ct).ConfigureAwait(false);

            // 清除上次运行的文件列表 / Clear files from the previous run
            _modifiedFiles.Clear();

            try
            {
                var resolvedWorkingDirectory = string.IsNullOrWhiteSpace(workingDirectory) ? Environment.CurrentDirectory : workingDirectory;

                EnsureProcessStarted(resolvedWorkingDirectory, onOutputReceived, ct);

                var input = new
                {
                    type = "user",
                    message = new
                    {
                        role = "user",
                        content = new[]
                        {
                            new { type = "text", text = prompt }
                        }
                    }
                };

                var json = JsonConvert.SerializeObject(input);
                Log($"[Input] {json}");
                await _stdin.WriteLineAsync(json).ConfigureAwait(false);
                await _stdin.FlushAsync().ConfigureAwait(false);

                // 使用超时机制 / Apply timeout
                using (var timeoutCts = new CancellationTokenSource(DefaultRequestTimeout))
                using (var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token))
                {
                    await ProcessResponseAsync(onOutputReceived, linkedCts.Token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                Log("[Info] Operation canceled by user.");
                StopProcess();
                throw;
            }
            catch (OperationCanceledException)
            {
                Log("[Error] Request timed out.");
                StopProcess();
                throw new TimeoutException($"Claude CLI request timed out after {DefaultRequestTimeout.TotalMinutes} minutes.");
            }
            catch (Exception ex)
            {
                Log($"[Error] {ex.Message}");
                LogService.Error("RunAsync failed", ex);
                throw;
            }
            finally
            {
                _runLock.Release();
            }
        }

        /// <summary>
        /// 处理 Claude CLI 的响应流 / Process the Claude CLI response stream.
        /// </summary>
        private async Task ProcessResponseAsync(Action<string> onOutputReceived, CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                // 健康检查：确保进程仍在运行 / Health check: ensure the process is running
                if (_process == null || _process.HasExited)
                {
                    throw new InvalidOperationException("Claude CLI process has unexpectedly terminated.");
                }

                var line = await ReadLineWithCancellationAsync(_stdout, ct).ConfigureAwait(false);
                if (line == null)
                {
                    throw new InvalidOperationException("Claude CLI output stream closed unexpectedly.");
                }

                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                // 解析 JSON 一次，提取类型 / Parse JSON once and extract the type
                var (parsed, type) = TryParseJsonLine(line);

                // 只记录关键事件到日志，跳过流式增量更新（减少日志体积） / Only log key events, skip streaming deltas
                if (ShouldLogEvent(parsed, type))
                {
                    Log($"[Output] {line}");
                }

                // 跟踪被修改的文件 / Track modified files
                if (parsed != null && type == "stream_event")
                {
                    TrackModifiedFile(parsed);
                }

                // 回调通知 UI / Notify UI
                onOutputReceived?.Invoke(line);

                // 检查是否完成 / Check completion
                if (string.Equals(type, "result", StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }
            }
        }

        /// <summary>
        /// 判断是否应该记录该事件到日志 / Decide whether to log the event.
        /// 跳过流式增量更新（text_delta, input_json_delta）以减少日志体积 / Skip streaming deltas to reduce log size.
        /// </summary>
        private static bool ShouldLogEvent(JObject parsed, string type)
        {
            if (parsed == null)
                return true; // 无法解析的行记录下来以便调试 / Log unparsed lines for debugging

            // 始终记录这些重要事件 / Always log these important events
            if (type == "result" || type == "error" || type == "system")
                return true;

            // 对于 stream_event，只记录开始和结束，跳过增量更新 / For stream_event, log start/stop only
            if (type == "stream_event")
            {
                var eventType = parsed.SelectToken("event.type")?.Value<string>();
                // 跳过增量更新事件 / Skip delta updates
                if (eventType == "content_block_delta")
                    return false;
                // 记录其他事件（content_block_start, content_block_stop, message_start, message_stop 等） / Log other events
                return true;
            }

            return true;
        }

        private async Task EnsureOutputPaneAsync()
        {
            if (_outputPane != null)
                return;

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            if (ServiceProvider.GlobalProvider.GetService(typeof(SVsOutputWindow)) is IVsOutputWindow outputWindow)
            {
                var guid = ClaudeCodePaneGuid;
                outputWindow.GetPane(ref guid, out _outputPane);
                if (_outputPane == null)
                {
                    outputWindow.CreatePane(ref guid, "Claude Code", 1, 1);
                    outputWindow.GetPane(ref guid, out _outputPane);
                }
            }
        }

        private void Log(string message)
        {
            // Also write to Debug output for fallback / 额外写入 Debug 输出以便回退
            Debug.WriteLine($"[ClaudeCode] {message}");

            // Write to file log / 写入文件日志
            LogService.Info(message);

            // 异步写入 Output 窗口，不阻塞调用者 / Write to Output window async
            var _ = Task.Run(async () =>
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                _outputPane?.OutputString(message + Environment.NewLine);
            });
        }

        private void EnsureProcessStarted(string workingDirectory, Action<string> onOutputReceived, CancellationToken ct)
        {
            if (_process != null && !_process.HasExited)
            {
                if (!string.Equals(_workingDirectory, workingDirectory, StringComparison.OrdinalIgnoreCase))
                {
                    StopProcess();
                }
                else
                {
                    return;
                }
            }

            _workingDirectory = workingDirectory;

            var startInfo = new ProcessStartInfo
            {
                FileName = WindowsCommandResolver.Resolve("claude"),
                Arguments = "-p --output-format stream-json --input-format stream-json --include-partial-messages --verbose --permission-mode bypassPermissions",
                WorkingDirectory = _workingDirectory,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            _process = new Process { StartInfo = startInfo };

            try
            {
                Log($"[Info] Starting process: {startInfo.FileName} {startInfo.Arguments}");
                _process.Start();
            }
            catch (System.ComponentModel.Win32Exception ex)
            {
                var errorMsg = "Claude CLI not found. Ensure 'claude' is on PATH. Details: " + ex.Message;
                Log($"[Error] {errorMsg}");
                throw new Exception(errorMsg);
            }

            _stdin = new StreamWriter(_process.StandardInput.BaseStream, new UTF8Encoding(false)) { AutoFlush = true };
            _stdout = _process.StandardOutput;
            _stderr = _process.StandardError;

            // 保存 stderr 泵任务以便后续等待 / Save stderr pump task for later wait
            _stderrPumpTask = PumpStreamAsync(
                _stderr.BaseStream,
                _process.StartInfo.StandardErrorEncoding ?? Encoding.UTF8,
                (text) =>
                {
                    onOutputReceived?.Invoke(text);
                    LogService.Warn($"[Stderr] {text}");
                },
                ct,
                prefix: "[error] ");
        }

        private static async Task<string> ReadLineWithCancellationAsync(StreamReader reader, CancellationToken ct)
        {
            var readTask = reader.ReadLineAsync();
            var completed = await Task.WhenAny(readTask, Task.Delay(Timeout.Infinite, ct)).ConfigureAwait(false);
            if (completed != readTask)
            {
                throw new OperationCanceledException(ct);
            }
            return await readTask.ConfigureAwait(false);
        }

        private void StopProcess()
        {
            try
            {
                // 等待 stderr 泵任务完成（最多 1 秒） / Wait for stderr pump (max 1s)
                // 在 Dispose 上下文中必须同步等待 / Must wait synchronously in Dispose
                if (_stderrPumpTask != null && !_stderrPumpTask.IsCompleted)
                {
                    Task.WaitAny(new[] { _stderrPumpTask }, TimeSpan.FromSeconds(1));
                }
            }
            catch (Exception ex)
            {
                LogService.Error("[StopProcess] Failed to wait for stderr pump", ex);
            }

            try
            {
                if (_process != null && !_process.HasExited)
                {
                    _process.Kill();
                    _process.WaitForExit(1000);
                }
            }
            catch (Exception ex)
            {
                LogService.Error("[StopProcess] Failed to kill process", ex);
            }
            finally
            {
                _stdin?.Dispose();
                _stdout?.Dispose();
                _stderr?.Dispose();
                _process?.Dispose();
                _stdin = null;
                _stdout = null;
                _stderr = null;
                _process = null;
                _stderrPumpTask = null;
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(ClaudeCodeCommandService));
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed)
                return;

            if (disposing)
            {
                StopProcess();
                _runLock?.Dispose();
            }

            _disposed = true;
        }

        ~ClaudeCodeCommandService()
        {
            Dispose(false);
        }
    }

    public static class WindowsCommandResolver
    {
        private static readonly string[] Extensions =
        {
            ".exe",
            ".cmd"
        };

        public static string Resolve(string commandName)
        {
            var paths = new List<string>();

            // 1) 当前进程 PATH / Current process PATH
            var envPath = Environment.GetEnvironmentVariable("PATH");
            if (!string.IsNullOrWhiteSpace(envPath))
            {
                paths.AddRange(envPath.Split(';'));
            }

            // 2) npm 全局 bin / Global npm bin
            var npmBin = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "npm");

            if (Directory.Exists(npmBin))
            {
                paths.Add(npmBin);
            }

            paths = paths.Distinct().ToList();

            foreach (var dir in paths)
            {
                foreach (var ext in Extensions)
                {
                    var full = Path.Combine(dir, commandName + ext);
                    if (File.Exists(full))
                    {

                        LogService.Info($"Found executable: {full}");
                        return full;
                    }
                }
            }

            LogService.Error($"Command not found on Windows: {commandName} (tried exe/cmd)");
            return $"{commandName}";
        }
    }
}
