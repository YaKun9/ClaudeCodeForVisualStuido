using ClaudeCodeForVS.Models;
using ClaudeCodeForVS.Services;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using System;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Threading;

namespace ClaudeCodeForVS.ViewModels
{
    public class ChatViewModel : INotifyPropertyChanged
    {
        private string _userInput;
        private bool _isCancelEnabled;
        private bool _isRunning;
        private CancellationTokenSource _cts;

        // UI 更新节流 / UI update throttling
        private static readonly TimeSpan UIUpdateInterval = TimeSpan.FromMilliseconds(50);

        // 使用无锁并发队列代替 StringBuilder + lock，避免调试时死锁 / Use lock-free queue to avoid debug deadlocks
        private readonly ConcurrentQueue<string> _pendingContent = new ConcurrentQueue<string>();
        private DispatcherTimer _updateTimer;
        private ChatMessage _currentAssistantMessage;

        public ChatViewModel()
        {
            Messages = new ObservableCollection<ChatMessage>();
            RunCommand = new RelayCommand(OnRun, CanRun);
            CancelCommand = new RelayCommand(OnCancel, CanCancel);
            UserInput = string.Empty;

            InitializeUpdateTimer();
        }

        private void InitializeUpdateTimer()
        {
            _updateTimer = new DispatcherTimer
            {
                Interval = UIUpdateInterval
            };
            _updateTimer.Tick += OnUpdateTimerTick;
        }

        private void OnUpdateTimerTick(object sender, EventArgs e)
        {
            FlushPendingContent();
        }

        private void FlushPendingContent()
        {
            if (_currentAssistantMessage == null || _pendingContent.IsEmpty)
                return;

            // 无锁方式收集所有待处理内容 / Collect pending content without locks
            var sb = new StringBuilder();
            while (_pendingContent.TryDequeue(out var chunk))
            {
                sb.Append(chunk);
            }

            if (sb.Length > 0)
            {
                _currentAssistantMessage.Content += sb.ToString();
            }
        }

        public ObservableCollection<ChatMessage> Messages { get; }

        public string UserInput
        {
            get => _userInput;
            set
            {
                if (_userInput != value)
                {
                    _userInput = value;
                    OnPropertyChanged(nameof(UserInput));
                    UpdateCommandStates();
                }
            }
        }

        public bool IsCancelEnabled
        {
            get => _isCancelEnabled;
            set
            {
                if (_isCancelEnabled != value)
                {
                    _isCancelEnabled = value;
                    OnPropertyChanged(nameof(IsCancelEnabled));
                    UpdateCommandStates();
                }
            }
        }

        public bool IsRunning
        {
            get => _isRunning;
            set
            {
                if (_isRunning != value)
                {
                    _isRunning = value;
                    OnPropertyChanged(nameof(IsRunning));
                    IsCancelEnabled = _isRunning;
                    UpdateCommandStates();
                }
            }
        }

        public ICommand RunCommand { get; }
        public ICommand CancelCommand { get; }

        private void UpdateCommandStates()
        {
            (RunCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (CancelCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }

        private bool CanRun(object parameter)
        {
            return !IsRunning && !string.IsNullOrWhiteSpace(UserInput);
        }

        private async void OnRun(object parameter)
        {
            if (CanRun(parameter))
            {
                var prompt = UserInput;
                // 添加用户消息 / Add user message
                Messages.Add(new ChatMessage("User", prompt));

                // 清空输入框 / Clear input box
                UserInput = string.Empty;

                // 准备 Assistant 消息 / Prepare assistant message
                var assistantMessage = new ChatMessage("Assistant", "");
                Messages.Add(assistantMessage);
                _currentAssistantMessage = assistantMessage;

                IsRunning = true;
                _cts = new CancellationTokenSource();
                _updateTimer.Start();

                try
                {
                    var workingDir = await GetSolutionDirectoryAsync();

                    await ClaudeCodeCommandService.Instance.RunAsync(prompt, workingDir, (output) =>
                    {
                        // 使用无锁队列累积内容，避免调试时死锁 / Queue content to avoid debug deadlocks
                        _pendingContent.Enqueue(output + "\n");
                    }, _cts.Token);
                }
                catch (OperationCanceledException)
                {
                    FlushPendingContent();
                    assistantMessage.Content += Environment.NewLine + "[Canceled]";
                }
                catch (TimeoutException ex)
                {
                    FlushPendingContent();
                    assistantMessage.Content += Environment.NewLine + "[Timeout]: " + ex.Message;
                    LogService.Warn("Request timed out", ex);
                }
                catch (Exception ex)
                {
                    FlushPendingContent();
                    assistantMessage.Content += Environment.NewLine + "[Error]: " + ex.Message;
                    LogService.Error("OnRun failed", ex);
                }
                finally
                {
                    _updateTimer.Stop();
                    FlushPendingContent();
                    _currentAssistantMessage = null;
                    IsRunning = false;
                    _cts?.Dispose();
                    _cts = null;
                    await ReloadChangedFilesAsync();
                }
            }
        }

        private async Task ReloadChangedFilesAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            var modifiedFiles = ClaudeCodeCommandService.Instance.ModifiedFiles;
            if (modifiedFiles == null || modifiedFiles.Count == 0)
            {
                LogService.Debug("No files to reload");
                return;
            }

            var rdt = ServiceProvider.GlobalProvider.GetService(typeof(SVsRunningDocumentTable)) as IVsRunningDocumentTable;
            if (rdt == null)
                return;

            var reloadedCount = 0;
            foreach (var filePath in modifiedFiles)
            {
                try
                {
                    // 规范化路径 / Normalize path
                    var normalizedPath = System.IO.Path.GetFullPath(filePath);

                    // 通过路径查找文档 / Find document by path
                    if (rdt.FindAndLockDocument((uint)_VSRDTFLAGS.RDT_NoLock, normalizedPath, out _, out _, out var docDataPtr, out var cookie) == VSConstants.S_OK && docDataPtr != IntPtr.Zero)
                    {
                        try
                        {
                            var docData = System.Runtime.InteropServices.Marshal.GetObjectForIUnknown(docDataPtr);
                            if (docData is IVsPersistDocData persistDocData)
                            {
                                persistDocData.ReloadDocData(0);
                                reloadedCount++;
                                LogService.Debug($"Reloaded: {filePath}");
                            }
                        }
                        finally
                        {
                            System.Runtime.InteropServices.Marshal.Release(docDataPtr);
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogService.Warn($"Failed to reload file: {filePath}", ex);
                }
            }

            LogService.Info($"Reloaded {reloadedCount} of {modifiedFiles.Count} modified files");
        }

        private async Task<string> GetSolutionDirectoryAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            if (ServiceProvider.GlobalProvider.GetService(typeof(SVsSolution)) is IVsSolution solution)
            {
                solution.GetSolutionInfo(out var solutionDir, out _, out _);
                return solutionDir;
            }
            return null;
        }

        private bool CanCancel(object parameter)
        {
            return IsRunning;
        }

        private void OnCancel(object parameter)
        {
            if (CanCancel(parameter))
            {
                _cts?.Cancel();
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class RelayCommand : ICommand
    {
        private readonly Action<object> _execute;
        private readonly Func<object, bool> _canExecute;

        public RelayCommand(Action<object> execute, Func<object, bool> canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public event EventHandler CanExecuteChanged;

        public bool CanExecute(object parameter)
        {
            return _canExecute == null || _canExecute(parameter);
        }

        public void Execute(object parameter)
        {
            _execute(parameter);
        }

        public void RaiseCanExecuteChanged()
        {
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
