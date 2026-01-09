using Microsoft.VisualStudio.Shell;
using System;
using System.Runtime.InteropServices;

namespace ClaudeCodeForVS
{
    [Guid("c5b8e8d2-4a7f-4e2d-9b3c-1d8e5f6a7b8c")]
    public class ClaudeChatToolWindow : ToolWindowPane
    {
        public ClaudeChatToolWindow() : base(null)
        {
            try
            {
                this.Caption = "Claude Code";
                this.BitmapResourceID = 301;
                this.BitmapIndex = 1;

                Services.LogService.Debug("Creating ClaudeChatControl...");
                var control = new ClaudeChatControl();
                base.Content = control;
                Services.LogService.Debug("ClaudeChatToolWindow initialized successfully");
            }
            catch (Exception ex)
            {
                Services.LogService.Error("Failed to initialize ClaudeChatToolWindow", ex);
                throw;
            }
        }
    }
}
