using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace DeviceScanner.Utils
{
    /// <summary>
    /// Windows dialogs for everything the operator answers (language, name, profile, yes/no), so nothing is typed into the console.
    /// Every dialog is top-most, and centred on the scan window (or the screen, before the window opens).
    /// Needs an STA thread ([STAThread] on Main).
    /// </summary>
    internal static class Dialogs
    {
        public const string Caption = "Device Scanner";

        static Dialogs()
        {
            try { SetProcessDPIAware(); } catch { }    // otherwise Windows bitmap-scales the dialogs and they look blurry
            Application.EnableVisualStyles();
        }

        /// <summary>Yes / No question. No has the focus unless <paramref name="yesByDefault"/>, like the old "(y/N)", so Enter alone never starts anything.</summary>
        public static bool Confirm(string question, bool caution = false, bool yesByDefault = false, IWin32Window owner = null)
        {
            using (var form = new PromptForm(question, caution ? SystemIcons.Warning : SystemIcons.Question,
                                             L.T("btn.yes"), L.T("btn.no"), null, focusOk: yesByDefault))
                return Show(form, owner) == DialogResult.OK;
        }

        /// <summary>One line of text. OK only works once something is typed; null = window closed / cancelled.</summary>
        public static string AskText(string question)
        {
            var box = new TextBox { Width = 320 };
            using (var form = new PromptForm(question, SystemIcons.Question, L.T("btn.ok"), L.T("btn.cancel"), box, focusOk: true))
            {
                form.OkButton.Enabled = false;
                box.TextChanged += (s, e) => form.OkButton.Enabled = box.Text.Trim().Length > 0;
                form.ActiveControl = box;
                return form.ShowDialog() == DialogResult.OK ? box.Text.Trim() : null;
            }
        }

        /// <summary>Pick one item from a list (double-click or OK). Returns its index, or -1 if cancelled.</summary>
        public static int Choose(string question, IList<string> options, int selected = 0,
                                 string ok = null, string cancel = null, string caption = Caption)
        {
            var list = new ListBox { Width = 320, IntegralHeight = true };
            foreach (string o in options) list.Items.Add(o);
            if (options.Count > 0) list.SelectedIndex = Math.Min(Math.Max(selected, 0), options.Count - 1);

            using (var form = new PromptForm(question, SystemIcons.Question, ok ?? L.T("btn.ok"), cancel ?? L.T("btn.cancel"),
                                             list, focusOk: true, caption: caption))
            {
                // sized once the dialog font is applied, so every option shows (up to 12, then it scrolls)
                int rows = Math.Min(Math.Max(options.Count, 2), 12);
                form.Load += (s, e) => list.Height = list.GetItemHeight(0) * rows + (list.Height - list.ClientSize.Height);
                list.DoubleClick += (s, e) => { if (list.SelectedIndex >= 0) { form.DialogResult = DialogResult.OK; form.Close(); } };
                form.ActiveControl = list;
                return form.ShowDialog() == DialogResult.OK ? list.SelectedIndex : -1;
            }
        }

        public static void Inform(string message, IWin32Window owner = null) => Message(message, SystemIcons.Information, owner);

        public static void Error(string message, IWin32Window owner = null) => Message(message, SystemIcons.Error, owner);

        private static void Message(string message, Icon icon, IWin32Window owner)
        {
            using (var form = new PromptForm(message, icon, L.T("btn.ok"), null, null, focusOk: true))
                Show(form, owner);
        }

        /// <summary>Centred on the scan window when there is one, otherwise on the screen.</summary>
        private static DialogResult Show(Form form, IWin32Window owner)
        {
            if (owner == null) return form.ShowDialog();
            form.StartPosition = FormStartPosition.CenterParent;
            return form.ShowDialog(owner);
        }

        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        /// <summary>Icon + message, an optional input control under it, and OK / Cancel buttons at the bottom right.</summary>
        private sealed class PromptForm : Form
        {
            public Button OkButton { get; }

            public PromptForm(string message, Icon icon, string ok, string cancel, Control input, bool focusOk, string caption = Caption)
            {
                Text = caption;
                Font = SystemFonts.MessageBoxFont;
                AutoScaleMode = AutoScaleMode.Font;
                FormBorderStyle = FormBorderStyle.FixedDialog;
                MaximizeBox = false;
                MinimizeBox = false;
                StartPosition = FormStartPosition.CenterScreen;
                TopMost = true;
                ShowInTaskbar = true;          // before the scan window opens, this is the only window of the app
                AutoSize = true;
                AutoSizeMode = AutoSizeMode.GrowAndShrink;
                Padding = new Padding(16);

                var grid = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, RowCount = 3 };
                grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
                grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

                if (icon != null)
                    grid.Controls.Add(new PictureBox { Image = icon.ToBitmap(), SizeMode = PictureBoxSizeMode.AutoSize,
                                                       Margin = new Padding(0, 0, 14, 0) }, 0, 0);
                grid.Controls.Add(new Label { Text = message, AutoSize = true, MaximumSize = new Size(460, 0),
                                              Margin = new Padding(0, 6, 0, 14) }, 1, 0);
                if (input != null)
                {
                    input.Margin = new Padding(0, 0, 0, 14);
                    grid.Controls.Add(input, 1, 1);
                }

                // right-to-left flow: the first button added is the right-most one
                var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true,
                                                    Dock = DockStyle.Fill, Margin = Padding.Empty };
                Button cancelButton = null;
                if (cancel != null)
                {
                    cancelButton = NewButton(cancel, DialogResult.Cancel);
                    buttons.Controls.Add(cancelButton);
                }
                OkButton = NewButton(ok, DialogResult.OK);
                buttons.Controls.Add(OkButton);
                grid.Controls.Add(buttons, 0, 2);
                grid.SetColumnSpan(buttons, 2);

                Controls.Add(grid);
                AcceptButton = OkButton;
                CancelButton = cancelButton ?? OkButton;
                ActiveControl = focusOk || cancelButton == null ? OkButton : cancelButton;

                Shown += (s, e) => Activate();    // bring it to the front even after a long scan
            }

            private static Button NewButton(string text, DialogResult result) => new Button
            {
                Text = text, DialogResult = result, AutoSize = true, MinimumSize = new Size(90, 0), Margin = new Padding(8, 0, 0, 0),
            };
        }
    }
}
