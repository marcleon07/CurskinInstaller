using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace CurskinInstaller
{
    class VariantItem
    {
        public CurskinVariant Variant;

        public override string ToString()
        {
            string name = string.IsNullOrWhiteSpace(Variant.Name) ? "Variant" : Variant.Name;
            return name + "  (" + Variant.Cursors.Count + " cursors)";
        }
    }

    class InstallRequest
    {
        public string File;
        public CurskinSkin Skin;
        public List<CurskinVariant> Variants;
        public bool Mixed;
        public bool Apply;
    }

    class MainForm : Form
    {
        private static readonly Color DropColor = Color.FromArgb(235, 242, 252);
        private static readonly Color DropHotColor = Color.FromArgb(212, 231, 252);

        private string currentFile;
        private CurskinSkin skin;
        private bool busy;

        private Panel dropPanel;
        private Label dropLabel;
        private Button browseButton;
        private Label nameValue;
        private Label authorValue;
        private Label descriptionValue;
        private CheckedListBox variantList;
        private CheckBox mixedCheck;
        private CheckBox applyCheck;
        private Button installButton;
        private Button restoreButton;
        private Button mouseButton;
        private Button closeButton;
        private Label statusLabel;
        private Label warningLabel;
        private ProgressBar progressBar;
        private ToolTip toolTip;
        private BackgroundWorker worker;

        public MainForm(string initialFile)
        {
            currentFile = initialFile;
            BuildUi();
            InitializeWorker();

            if (!string.IsNullOrEmpty(initialFile))
            {
                Shown += delegate { LoadSkin(initialFile); };
            }
        }

        private void InitializeWorker()
        {
            worker = new BackgroundWorker();
            worker.WorkerReportsProgress = true;
            worker.DoWork += Worker_DoWork;
            worker.ProgressChanged += Worker_ProgressChanged;
            worker.RunWorkerCompleted += Worker_RunWorkerCompleted;
        }

        private void BuildUi()
        {
            SuspendLayout();

            Text = "Curskin Installer";
            ClientSize = new Size(780, 620);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            ShowIcon = false;
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 9F);
            BackColor = Color.FromArgb(248, 249, 251);
            AutoScaleMode = AutoScaleMode.Dpi;
            DoubleBuffered = true;
            AllowDrop = true;

            Label title = new Label();
            title.Text = "Curskin Installer";
            title.Font = new Font("Segoe UI", 20F, FontStyle.Bold);
            title.ForeColor = Color.FromArgb(20, 30, 45);
            title.AutoSize = true;
            title.Location = new Point(20, 14);
            Controls.Add(title);

            Label subtitle = new Label();
            subtitle.Text = "Drop a Mouse X .curskin file to install it as native Windows cursors.";
            subtitle.Font = new Font("Segoe UI", 10F);
            subtitle.ForeColor = Color.FromArgb(90, 100, 115);
            subtitle.AutoSize = true;
            subtitle.Location = new Point(22, 52);
            Controls.Add(subtitle);

            dropPanel = new Panel();
            dropPanel.Location = new Point(20, 82);
            dropPanel.Size = new Size(740, 100);
            dropPanel.BackColor = DropColor;
            dropPanel.BorderStyle = BorderStyle.FixedSingle;
            dropPanel.AllowDrop = true;
            dropPanel.DragEnter += DragEnterHandler;
            dropPanel.DragLeave += DragLeaveHandler;
            dropPanel.DragDrop += DragDropHandler;
            Controls.Add(dropPanel);

            dropLabel = new Label();
            dropLabel.Text = "Drop a .curskin file here\r\nor click Browse";
            dropLabel.Font = new Font("Segoe UI", 12F, FontStyle.Bold);
            dropLabel.ForeColor = Color.FromArgb(45, 95, 160);
            dropLabel.BackColor = Color.Transparent;
            dropLabel.TextAlign = ContentAlignment.MiddleCenter;
            dropLabel.Dock = DockStyle.Fill;
            dropLabel.AllowDrop = true;
            dropLabel.DragEnter += DragEnterHandler;
            dropLabel.DragLeave += DragLeaveHandler;
            dropLabel.DragDrop += DragDropHandler;
            dropPanel.Controls.Add(dropLabel);

            browseButton = new Button();
            browseButton.Text = "Browse...";
            browseButton.Size = new Size(100, 32);
            browseButton.Location = new Point(620, 34);
            browseButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            browseButton.Click += Browse_Click;
            dropPanel.Controls.Add(browseButton);
            browseButton.BringToFront();

            GroupBox info = new GroupBox();
            info.Text = "Skin";
            info.Location = new Point(20, 196);
            info.Size = new Size(740, 120);
            info.ForeColor = Color.FromArgb(50, 60, 75);
            Controls.Add(info);

            nameValue = AddInfoRow(info, "Name", 22);
            nameValue.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
            authorValue = AddInfoRow(info, "Author", 46);
            descriptionValue = AddInfoRow(info, "Description", 70);
            descriptionValue.ForeColor = Color.FromArgb(80, 90, 105);

            GroupBox variants = new GroupBox();
            variants.Text = "Variants";
            variants.Location = new Point(20, 328);
            variants.Size = new Size(740, 150);
            variants.ForeColor = Color.FromArgb(50, 60, 75);
            Controls.Add(variants);

            variantList = new CheckedListBox();
            variantList.Location = new Point(14, 24);
            variantList.Size = new Size(480, 110);
            variantList.CheckOnClick = true;
            variantList.IntegralHeight = false;
            variantList.BorderStyle = BorderStyle.FixedSingle;
            variantList.ItemCheck += delegate
            {
                if (IsHandleCreated)
                    BeginInvoke(new MethodInvoker(UpdateControls));
            };
            variants.Controls.Add(variantList);

            mixedCheck = new CheckBox();
            mixedCheck.Text = "Create a Mixed scheme";
            mixedCheck.Location = new Point(510, 24);
            mixedCheck.Size = new Size(215, 24);
            mixedCheck.Enabled = false;
            variants.Controls.Add(mixedCheck);

            Label mixedHelp = new Label();
            mixedHelp.Text = "One extra scheme that mixes the selected variants.";
            mixedHelp.Location = new Point(510, 50);
            mixedHelp.Size = new Size(215, 42);
            mixedHelp.ForeColor = Color.FromArgb(120, 130, 145);
            variants.Controls.Add(mixedHelp);

            applyCheck = new CheckBox();
            applyCheck.Text = "Apply after install";
            applyCheck.Location = new Point(510, 100);
            applyCheck.Size = new Size(215, 24);
            applyCheck.Checked = true;
            variants.Controls.Add(applyCheck);

            warningLabel = new Label();
            warningLabel.Location = new Point(20, 488);
            warningLabel.Size = new Size(740, 20);
            warningLabel.ForeColor = Color.FromArgb(180, 95, 0);
            warningLabel.Visible = false;
            Controls.Add(warningLabel);

            statusLabel = new Label();
            statusLabel.Location = new Point(20, 510);
            statusLabel.Size = new Size(740, 20);
            statusLabel.ForeColor = Color.FromArgb(80, 90, 105);
            statusLabel.AutoEllipsis = true;
            statusLabel.Text = "Drop or choose a .curskin file to begin.";
            Controls.Add(statusLabel);

            progressBar = new ProgressBar();
            progressBar.Location = new Point(20, 534);
            progressBar.Size = new Size(740, 14);
            progressBar.Style = ProgressBarStyle.Continuous;
            Controls.Add(progressBar);

            installButton = new Button();
            installButton.Text = "Install selected";
            installButton.Location = new Point(20, 558);
            installButton.Size = new Size(150, 38);
            installButton.Enabled = false;
            installButton.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            installButton.FlatStyle = FlatStyle.Flat;
            installButton.FlatAppearance.BorderSize = 0;
            installButton.UseVisualStyleBackColor = false;
            installButton.EnabledChanged += InstallButton_EnabledChanged;
            installButton.Click += Install_Click;
            Controls.Add(installButton);
            InstallButton_EnabledChanged(null, EventArgs.Empty);

            restoreButton = new Button();
            restoreButton.Text = "Restore original cursors";
            restoreButton.Location = new Point(180, 558);
            restoreButton.Size = new Size(180, 38);
            restoreButton.Enabled = Installer.HasBackup;
            restoreButton.Click += Restore_Click;
            Controls.Add(restoreButton);

            mouseButton = new Button();
            mouseButton.Text = "Mouse settings";
            mouseButton.Location = new Point(370, 558);
            mouseButton.Size = new Size(150, 38);
            mouseButton.Click += delegate { Installer.OpenMouseProperties(); };
            Controls.Add(mouseButton);

            closeButton = new Button();
            closeButton.Text = "Close";
            closeButton.Location = new Point(660, 558);
            closeButton.Size = new Size(100, 38);
            closeButton.Click += delegate { Close(); };
            Controls.Add(closeButton);

            toolTip = new ToolTip();
            toolTip.AutoPopDelay = 15000;
            toolTip.InitialDelay = 400;
            toolTip.ReshowDelay = 100;

            DragEnter += DragEnterHandler;
            DragLeave += DragLeaveHandler;
            DragDrop += DragDropHandler;
            FormClosing += FormClosingHandler;

            AcceptButton = installButton;
            CancelButton = closeButton;

            ResumeLayout(false);
            PerformLayout();
        }

        private static Label AddInfoRow(GroupBox box, string caption, int y)
        {
            Label label = new Label();
            label.Text = caption;
            label.Location = new Point(16, y);
            label.Size = new Size(80, 20);
            label.ForeColor = Color.FromArgb(110, 120, 135);
            box.Controls.Add(label);

            Label value = new Label();
            value.Text = "-";
            value.Location = new Point(100, y);
            value.Size = new Size(620, 20);
            value.ForeColor = Color.FromArgb(30, 40, 55);
            value.AutoEllipsis = true;
            box.Controls.Add(value);
            return value;
        }

        private void InstallButton_EnabledChanged(object sender, EventArgs e)
        {
            if (installButton.Enabled)
            {
                installButton.BackColor = Color.FromArgb(45, 110, 190);
                installButton.ForeColor = Color.White;
            }
            else
            {
                installButton.BackColor = Color.FromArgb(205, 211, 220);
                installButton.ForeColor = Color.FromArgb(120, 128, 140);
            }
        }

        private void Browse_Click(object sender, EventArgs e)
        {
            if (busy)
                return;

            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Title = "Choose a .curskin file";
                dialog.Filter = "Mouse X cursor skins (*.curskin)|*.curskin|All files (*.*)|*.*";
                dialog.CheckFileExists = true;
                dialog.Multiselect = false;
                if (dialog.ShowDialog(this) == DialogResult.OK)
                    LoadSkin(dialog.FileName);
            }
        }

        private static string CleanDescription(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "";

            value = value.Replace("\r", " ").Replace("\n", " ");
            value = Regex.Replace(value,
                @"\[/?(?:url|img|b|i|u|color|size|font|quote|list|table|tr|td|code|spoiler)(?:=[^\]]*)?\]",
                "",
                RegexOptions.IgnoreCase);
            value = Regex.Replace(value, @"\s+", " ").Trim();
            return value;
        }

        private void LoadSkin(string path)
        {
            if (busy)
                return;
            if (!File.Exists(path))
            {
                MessageBox.Show(this, "The file no longer exists:\r\n" + path, "File not found",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            try
            {
                Cursor.Current = Cursors.WaitCursor;

                CurskinSkin parsed = CurskinParser.ParseFile(path);
                List<CurskinVariant> usable = new List<CurskinVariant>();
                int totalCursors = 0;
                foreach (CurskinVariant variant in parsed.Variants)
                {
                    if (variant.Cursors.Count > 0)
                    {
                        usable.Add(variant);
                        totalCursors += variant.Cursors.Count;
                    }
                }

                if (usable.Count == 0)
                    throw new InvalidDataException("No cursors were found in this .curskin file.");

                skin = parsed;
                currentFile = path;

                string description = CleanDescription(parsed.Description);

                nameValue.Text = string.IsNullOrWhiteSpace(parsed.Name) ? "-" : parsed.Name;
                authorValue.Text = string.IsNullOrWhiteSpace(parsed.Author) ? "-" : parsed.Author;
                descriptionValue.Text = description.Length == 0 ? "-" : description;
                toolTip.SetToolTip(nameValue, nameValue.Text);
                toolTip.SetToolTip(authorValue, authorValue.Text);
                toolTip.SetToolTip(descriptionValue, description);

                variantList.Items.Clear();
                foreach (CurskinVariant variant in usable)
                    variantList.Items.Add(new VariantItem { Variant = variant }, true);

                mixedCheck.Enabled = usable.Count > 1;
                mixedCheck.Checked = usable.Count > 1;
                applyCheck.Enabled = true;
                applyCheck.Checked = true;

                dropLabel.Text = Path.GetFileName(path) + "\r\nDrop another .curskin to replace";
                dropLabel.ForeColor = Color.FromArgb(30, 100, 70);
                statusLabel.Text = string.Format("Loaded {0} variant(s) and {1} cursors.", usable.Count, totalCursors);
                Text = "Curskin Installer - " + nameValue.Text;

                warningLabel.Visible = Installer.IsMouseXRunning();
                warningLabel.Text = warningLabel.Visible
                    ? "Mouse X is running - quit it from the tray to see the new Windows cursors."
                    : "";

                UpdateControls();
            }
            catch (Exception ex)
            {
                statusLabel.Text = "Could not load the file.";
                MessageBox.Show(this, ex.Message, "Could not load .curskin",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                Cursor.Current = Cursors.Default;
            }
        }

        private void Install_Click(object sender, EventArgs e)
        {
            if (busy || skin == null || string.IsNullOrEmpty(currentFile))
                return;

            List<CurskinVariant> selected = new List<CurskinVariant>();
            foreach (object item in variantList.CheckedItems)
            {
                VariantItem variantItem = item as VariantItem;
                if (variantItem != null)
                    selected.Add(variantItem.Variant);
            }

            if (selected.Count == 0)
            {
                MessageBox.Show(this, "Select at least one variant to install.", "No variants selected",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            InstallRequest request = new InstallRequest();
            request.File = currentFile;
            request.Skin = skin;
            request.Variants = selected;
            request.Mixed = mixedCheck.Checked && selected.Count > 1;
            request.Apply = applyCheck.Checked;

            SetBusy(true);
            statusLabel.Text = "Installing...";
            worker.RunWorkerAsync(request);
        }

        private void Restore_Click(object sender, EventArgs e)
        {
            if (busy)
                return;

            DialogResult answer = MessageBox.Show(this,
                "Restore the cursor settings that were saved before Curskin Installer applied its first scheme?",
                "Restore original cursors", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (answer != DialogResult.Yes)
                return;

            try
            {
                string message = Installer.RestorePrevious();
                statusLabel.Text = "Original cursors restored.";
                MessageBox.Show(this, message, "Restored", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Restore failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void Worker_DoWork(object sender, DoWorkEventArgs e)
        {
            InstallRequest request = (InstallRequest)e.Argument;
            try
            {
                e.Result = Installer.Install(request.File, request.Skin, request.Variants,
                    request.Mixed, request.Apply, delegate(string message)
                    {
                        worker.ReportProgress(0, message);
                    });
            }
            catch (Exception ex)
            {
                e.Result = ex;
            }
        }

        private void Worker_ProgressChanged(object sender, ProgressChangedEventArgs e)
        {
            statusLabel.Text = e.UserState == null ? "" : e.UserState.ToString();
        }

        private void Worker_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            busy = false;
            UpdateControls();

            if (e.Result is Exception)
            {
                Exception ex = (Exception)e.Result;
                statusLabel.Text = "Install did not finish.";
                MessageBox.Show(this, ex.Message, "Install problem",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            InstallResult result = (InstallResult)e.Result;
            statusLabel.Text = string.Format("Installed {0} scheme(s) into {1}", result.SchemeNames.Count, result.InstallDir);

            string message = "Installed schemes:\r\n\r\n" + string.Join("\r\n", result.SchemeNames.ToArray());
            if (!string.IsNullOrEmpty(result.AppliedScheme))
                message += "\r\n\r\nApplied: " + result.AppliedScheme;
            else
                message += "\r\n\r\nOpen Mouse Properties to choose a scheme.";
            message += "\r\n\r\nFiles: " + result.InstallDir;
            if (result.MouseXRunning)
                message += "\r\n\r\nMouse X is running. Quit it from the tray to see the new Windows cursors.";

            MessageBox.Show(this, message, "Cursors installed", MessageBoxButtons.OK, MessageBoxIcon.Information);

            warningLabel.Visible = result.MouseXRunning;
            warningLabel.Text = result.MouseXRunning
                ? "Mouse X is running - quit it from the tray to see the new Windows cursors."
                : "";
        }

        private void SetBusy(bool value)
        {
            busy = value;
            if (busy)
            {
                progressBar.Style = ProgressBarStyle.Marquee;
                progressBar.MarqueeAnimationSpeed = 30;
            }
            UpdateControls();
        }

        private void UpdateControls()
        {
            if (IsDisposed || Disposing)
                return;

            int checkedCount = variantList.CheckedItems.Count;
            installButton.Enabled = !busy && skin != null && checkedCount > 0;
            browseButton.Enabled = !busy;
            variantList.Enabled = !busy && variantList.Items.Count > 0;
            mixedCheck.Enabled = !busy && checkedCount > 1;
            if (checkedCount < 2)
                mixedCheck.Checked = false;
            applyCheck.Enabled = !busy && skin != null;
            restoreButton.Enabled = !busy && Installer.HasBackup;
            closeButton.Enabled = !busy;
            progressBar.Style = busy ? ProgressBarStyle.Marquee : ProgressBarStyle.Continuous;
            if (!busy)
                progressBar.Value = 0;
        }

        private void FormClosingHandler(object sender, FormClosingEventArgs e)
        {
            if (!busy)
                return;
            e.Cancel = true;
            MessageBox.Show(this, "Please wait for the current install to finish.", "Still installing",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void DragEnterHandler(object sender, DragEventArgs e)
        {
            if (FindCurskin(e.Data) != null)
            {
                e.Effect = DragDropEffects.Copy;
                dropPanel.BackColor = DropHotColor;
            }
            else
            {
                e.Effect = DragDropEffects.None;
            }
        }

        private void DragLeaveHandler(object sender, EventArgs e)
        {
            dropPanel.BackColor = DropColor;
        }

        private void DragDropHandler(object sender, DragEventArgs e)
        {
            dropPanel.BackColor = DropColor;
            string file = FindCurskin(e.Data);
            if (file != null)
                LoadSkin(file);
        }

        private static string FindCurskin(IDataObject data)
        {
            if (data == null || !data.GetDataPresent(DataFormats.FileDrop))
                return null;

            string[] files = data.GetData(DataFormats.FileDrop) as string[];
            if (files == null)
                return null;

            foreach (string file in files)
            {
                if (file != null && file.EndsWith(".curskin", StringComparison.OrdinalIgnoreCase))
                    return file;
            }
            return null;
        }
    }
}
