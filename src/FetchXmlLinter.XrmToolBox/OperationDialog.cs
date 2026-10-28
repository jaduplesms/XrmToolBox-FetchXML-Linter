using System;
using System.Drawing;
using System.Windows.Forms;

namespace FetchXmlLinter.XrmToolBox
{
    internal sealed class OperationDialog : Form
    {
        private readonly Label message;
        private readonly Button cancel;
        private readonly Timer timer = new Timer { Interval = 100 };
        private readonly ToolTip tooltips = new ToolTip();

        public OperationDialog(string title, Func<bool> completed, Action requestCancellation)
        {
            Text = title;
            AccessibleName = "Operation progress";
            ClientSize = new Size(460, 180);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            Font = new Font("Segoe UI", 9.5f);
            message = new Label { Text = title + "...", Dock = DockStyle.Fill, Padding = new Padding(16), AccessibleName = "Operation detail" };
            var footer = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 50, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8) };
            cancel = new Button { Text = "Cancel", AutoSize = true, AccessibleName = "Cancel operation", AccessibleDescription = "Request cancellation; an in-flight Dataverse read may need to finish." };
            tooltips.SetToolTip(cancel, cancel.AccessibleDescription);
            cancel.Click += (_, __) =>
            {
                requestCancellation();
                message.Text = "Cancellation requested. An in-flight Dataverse read may need to finish.";
                cancel.Enabled = false;
            };
            CancelButton = cancel;
            footer.Controls.Add(cancel);
            Controls.Add(message);
            Controls.Add(new ProgressBar { Dock = DockStyle.Bottom, Height = 8, Style = ProgressBarStyle.Marquee, MarqueeAnimationSpeed = 30 });
            Controls.Add(footer);
            timer.Tick += (_, __) => { if (completed()) Close(); };
            Shown += (_, __) => timer.Start();
            FormClosing += (_, args) =>
            {
                if (completed() || Disposing) return;
                args.Cancel = true;
                cancel.PerformClick();
            };
        }

        public void Report(string text)
        {
            if (!IsDisposed && cancel.Enabled) message.Text = text;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                timer.Dispose();
                tooltips.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
