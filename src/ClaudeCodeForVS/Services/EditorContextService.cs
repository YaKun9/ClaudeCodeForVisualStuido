using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;
using System;
using System.Collections.Generic;
using System.IO;

namespace ClaudeCodeForVS.Services
{
    /// <summary>
    /// 服务用于跟踪编辑器上下文（当前文件、选中的文本等） / Service for tracking editor context (active file, selection, etc.).
    /// </summary>
    public class EditorContextService
    {
        private static EditorContextService _instance;
        private DTE2 _dte;

        public static EditorContextService Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new EditorContextService();
                }
                return _instance;
            }
        }

        public event EventHandler<EditorContextChangedEventArgs> ContextChanged;

        private EditorContextService() { }

        /// <summary>
        /// 初始化服务 / Initialize the service.
        /// </summary>
        public void Initialize(DTE2 dte)
        {
            _dte = dte;
        }

        /// <summary>
        /// 获取当前活动文档的信息 / Get info about the active document.
        /// </summary>
        public EditorContext GetCurrentContext()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (_dte?.ActiveDocument == null)
                return null;

            var context = new EditorContext
            {
                FilePath = _dte.ActiveDocument.FullName,
                FileName = Path.GetFileName(_dte.ActiveDocument.FullName),
                Language = _dte.ActiveDocument.Language
            };

            // 计算相对于解决方案目录的相对路径 / Compute path relative to solution directory
            try
            {
                if (_dte.Solution != null && !string.IsNullOrEmpty(_dte.Solution.FullName))
                {
                    string solutionDir = Path.GetDirectoryName(_dte.Solution.FullName);
                    if (!string.IsNullOrEmpty(solutionDir) && context.FilePath.StartsWith(solutionDir, StringComparison.OrdinalIgnoreCase))
                    {
                        context.RelativePath = context.FilePath.Substring(solutionDir.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    }
                    else
                    {
                        context.RelativePath = context.FilePath;
                    }
                }
                else
                {
                    context.RelativePath = context.FilePath;
                }
            }
            catch
            {
                context.RelativePath = context.FilePath;
            }

            // 获取选中的文本 / Get selected text
            if (_dte.ActiveDocument.Object("TextDocument") is TextDocument textDoc)
            {
                var selection = textDoc.Selection;
                if (selection != null && !selection.IsEmpty)
                {
                    context.SelectedText = selection.Text;
                    context.SelectionStartLine = selection.TopLine;
                    context.SelectionEndLine = selection.BottomLine;
                }

                // 获取当前光标位置 / Get current caret position
                context.CurrentLine = selection?.CurrentLine ?? 0;

                // 获取完整文档内容（可选） / Get full document text (optional)
                var startPoint = textDoc.StartPoint.CreateEditPoint();
                context.FullText = startPoint.GetText(textDoc.EndPoint);
            }

            return context;
        }

        /// <summary>
        /// 触发上下文变化事件 / Raise the context changed event.
        /// </summary>
        public void NotifyContextChanged()
        {
            var context = GetCurrentContext();
            ContextChanged?.Invoke(this, new EditorContextChangedEventArgs { Context = context });
        }

        /// <summary>
        /// 获取解决方案中的所有项目文件 / Get all project files in the solution.
        /// </summary>
        public List<ProjectFileInfo> GetProjectFiles()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var files = new List<ProjectFileInfo>();

            if (_dte?.Solution == null)
            {
                LogService.Debug("[GetProjectFiles] Solution is null");
                return files;
            }

            try
            {
                var solutionDir = Path.GetDirectoryName(_dte.Solution.FullName);
                LogService.Debug($"[GetProjectFiles] Solution dir: {solutionDir}");
                
                if (string.IsNullOrEmpty(solutionDir))
                {
                    LogService.Debug("[GetProjectFiles] Solution dir is empty");
                    return files;
                }

                var projectCount = _dte.Solution.Projects.Count;
                LogService.Debug($"[GetProjectFiles] Found {projectCount} projects");

                int projectIndex = 0;
                foreach (Project project in _dte.Solution.Projects)
                {
                    projectIndex++;
                    try
                    {
                        var projectName = project?.Name ?? "Unknown";
                        LogService.Debug($"[GetProjectFiles] Processing project {projectIndex}/{projectCount}: {projectName}");
                        
                        if (project?.ProjectItems != null)
                        {
                            GetProjectItems(project.ProjectItems, solutionDir, files);
                        }
                        else
                        {
                            LogService.Debug($"[GetProjectFiles] Project {projectName} ProjectItems is null");
                        }
                    }
                    catch (Exception ex)
                    {
                        LogService.Debug($"[GetProjectFiles] Failed to process project {projectIndex}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                LogService.Debug($"[GetProjectFiles] Failed: {ex.Message}");
            }

            return files;
        }

        /// <summary>
        /// 递归获取项目项 / Recursively collect project items.
        /// </summary>
        private void GetProjectItems(ProjectItems projectItems, string solutionDir, List<ProjectFileInfo> files)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (projectItems == null)
            {
                LogService.Debug("[GetProjectItems] projectItems is null");
                return;
            }

            try
            {
                var itemCount = projectItems.Count;
                LogService.Debug($"[GetProjectItems] Processing {itemCount} items");
                
                foreach (ProjectItem item in projectItems)
                {
                    try
                    {
                        ThreadHelper.ThrowIfNotOnUIThread();

                        var itemName = item?.Name ?? "Unknown";
                        var itemKind = item?.Kind ?? "Unknown";
                        LogService.Debug($"[GetProjectItems] Item: {itemName}, Kind: {itemKind}");

                        // 尝试获取文件路径（不仅限于物理文件类型） / Try to read file paths (not only physical files)
                        try
                        {
                            var fileCount = item.FileCount;
                            if (fileCount > 0)
                            {
                                LogService.Debug($"[GetProjectItems] Item has {fileCount} file(s)");
                                
                                for (short i = 1; i <= fileCount; i++)
                                {
                                    try
                                    {
                                        string filePath = item.FileNames[i];
                                        LogService.Debug($"[GetProjectItems] File path: {filePath}");
                                        
                                        if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath))
                                        {
                                            string fileName = Path.GetFileName(filePath);
                                            string relativePath = filePath.StartsWith(solutionDir, StringComparison.OrdinalIgnoreCase)
                                                ? filePath.Substring(solutionDir.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                                                : filePath;

                                            string directory = Path.GetDirectoryName(relativePath) ?? "";
                                            directory = directory.Replace(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

                                            LogService.Debug($"[GetProjectItems] Adding: Name={fileName}, Path={relativePath}");
                                            
                                            files.Add(new ProjectFileInfo
                                            {
                                                Name = fileName,
                                                Path = relativePath.Replace(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                                                Directory = directory
                                            });
                                        }
                                        else
                                        {
                                            LogService.Debug($"[GetProjectItems] File does not exist or path is empty: {filePath}");
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        LogService.Debug($"[GetProjectItems] Failed to get file info: {ex.Message}");
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            LogService.Debug($"[GetProjectItems] Failed to access FileCount for {itemName}: {ex.Message}");
                        }

                        if (item.ProjectItems != null && item.ProjectItems.Count > 0)
                        {
                            LogService.Debug($"[GetProjectItems] Recursing: {itemName}, Count: {item.ProjectItems.Count}");
                            GetProjectItems(item.ProjectItems, solutionDir, files);
                        }

                        if (item.SubProject != null)
                        {
                            LogService.Debug($"[GetProjectItems] Subproject: {itemName}");
                            GetProjectItems(item.SubProject.ProjectItems, solutionDir, files);
                        }
                    }
                    catch (Exception ex)
                    {
                        LogService.Debug($"[GetProjectItems] Failed to process item: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                LogService.Debug($"[GetProjectItems] Failed to iterate: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// 编辑器上下文信息 / Editor context info.
    /// </summary>
    public class EditorContext
    {
        public string FilePath { get; set; }
        public string FileName { get; set; }
        public string RelativePath { get; set; }
        public string Language { get; set; }
        public string SelectedText { get; set; }
        public int SelectionStartLine { get; set; }
        public int SelectionEndLine { get; set; }
        public int CurrentLine { get; set; }
        public string FullText { get; set; }

        public bool HasSelection => !string.IsNullOrEmpty(SelectedText);
    }

    public class EditorContextChangedEventArgs : EventArgs
    {
        public EditorContext Context { get; set; }
    }

    /// <summary>
    /// 项目文件信息 / Project file info.
    /// </summary>
    public class ProjectFileInfo
    {
        public string Name { get; set; }
        public string Path { get; set; }
        public string Directory { get; set; }
    }
}
