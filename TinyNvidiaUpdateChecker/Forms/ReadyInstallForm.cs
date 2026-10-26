using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;
using TinyNvidiaUpdateChecker.Handlers;

namespace TinyNvidiaUpdateChecker.Forms
{
    public partial class ReadyInstallForm : Form
    {
        string driverPath;
        string folderPath;
        List<string> tempFiles;
        bool hasDeletedTempFiles = false;
        bool hasRunInstaller = false;
        bool isInstallerRunning = false;

        public int InstallerExitCode { get; private set; }

        public ReadyInstallForm(string driverPath, List<string> tempFiles)
        {
            this.driverPath = driverPath;
            this.folderPath = Path.GetDirectoryName(driverPath);
            this.tempFiles = tempFiles;

            InitializeComponent();
        }

        public static int handleInstall(string driverPath, List<string> tempFiles, bool keepDriver = false)
        {
            int exitCode;

            if (!MainConsole.confirmDL)
            {
                using ReadyInstallForm form = new(driverPath, tempFiles);
                form.ShowDialog();
                exitCode = form.InstallerExitCode;
            }
            else
            {
                // confirmDL-mode does not show UI, just force-installs driver

                try
                {
                    ConsoleHelper.WriteLine();
                    ConsoleHelper.Write("Executing driver installer . . . ");

                    ProcessStartInfo startInfo = new(driverPath)
                    {
                        UseShellExecute = true,
                        Arguments = "/s /noreboot"
                    };

                    using Process installer = Process.Start(startInfo)
                        ?? throw new InvalidOperationException("The installer could not be started");

                    installer.WaitForExit();
                    if (installer.ExitCode == 3010)
                    {
                        ConsoleHelper.WriteLine($"Note: installer returned {installer.ExitCode} (a reboot is required).");
                    }
                    else if (installer.ExitCode != 0)
                    {
                        throw new InvalidOperationException($"The installer exited with code {installer.ExitCode}.");
                    }

                    exitCode = installer.ExitCode;
                    ConsoleHelper.Write("OK!");
                }
                catch (Exception ex)
                {
                    ConsoleHelper.WriteLine($"Installation failed: {ex.Message}");
                    ConsoleHelper.WriteLine();
                    return 1;
                }

                ConsoleHelper.WriteLine();

                string folderPath = Path.GetDirectoryName(driverPath);

                if (!keepDriver)
                {
                    if (FileSystemHandler.DeleteTemporaryFiles(folderPath, tempFiles))
                    {
                        ConsoleHelper.WriteLine($"Cleaned up: {folderPath}");
                    }
                    else
                    {
                        ConsoleHelper.WriteLine($"Could not cleanup: {folderPath}");
                    }
                }
            }

            return exitCode;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            base.OnFormClosing(e);

            if (isInstallerRunning)
            {
                e.Cancel = true;
                MessageBox.Show("Please wait for the installer to finish.", "TinyNvidiaUpdateChecker", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (!hasDeletedTempFiles && tempFiles.Count > 0 && Directory.Exists(folderPath))
            {
                string message = "Temporary driver files have not been deleted. Do you want to delete them before closing?";

                if (!hasRunInstaller)
                {
                    message += "\n\nImportant note: It does not appear that the installer has been run yet. Deleting temporary files will require you to download the driver again.";
                }

                DialogResult result = MessageBox.Show(message, "TinyNvidiaUpdateChecker", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);

                if (result == DialogResult.Yes)
                {
                    bool success = deleteTempFiles();

                    if (success)
                    {
                        hasDeletedTempFiles = true;
                    }
                    else
                    {
                        MessageBox.Show("Could not delete temporary files!", "TinyNvidiaUpdateChecker", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
                else if (result == DialogResult.Cancel)
                {
                    e.Cancel = true;
                }
            }
        }

        private async void runBtn_Click(object sender, EventArgs e)
        {
            Enabled = false;
            deleteBtn.Enabled = false;
            isInstallerRunning = true;

            try
            {
                ConsoleHelper.WriteLine();
                ConsoleHelper.Write("Executing driver installer . . . ");

                ProcessStartInfo startInfo = new(driverPath)
                {
                    UseShellExecute = true
                };

                using Process installer = Process.Start(startInfo)
                    ?? throw new InvalidOperationException("The installer could not be started.");
                await installer.WaitForExitAsync();
                int exitCode = installer.ExitCode;
                if (exitCode == 3010)
                {
                    ConsoleHelper.WriteLine($"Note: installer returned {exitCode} (a reboot is required).");
                }
                else if (exitCode != 0)
                {
                    throw new InvalidOperationException($"The installer exited with code {exitCode}.");
                }

                InstallerExitCode = exitCode;
                hasRunInstaller = true;
                ConsoleHelper.Write("OK!");
                ConsoleHelper.WriteLine();
                runBtn.Enabled = false;
                Focus();
            }
            catch (Exception ex)
            {
                InstallerExitCode = 1;
                MessageBox.Show($"Installation failed: {ex.Message}", "TinyNvidiaUpdateChecker", MessageBoxButtons.OK, MessageBoxIcon.Error);
                ConsoleHelper.Write("ERROR!");
                ConsoleHelper.WriteLine();
                ConsoleHelper.WriteLine($"Installation failed: {ex.Message}");
            }
            finally
            {
                isInstallerRunning = false;
                Enabled = true;
                deleteBtn.Enabled = tempFiles.Count > 0 && !hasDeletedTempFiles;
            }
        }

        private void deleteBtn_Click(object sender, EventArgs e)
        {
            bool success = deleteTempFiles();

            if (success)
            {

                hasDeletedTempFiles = true;
                deleteBtn.Enabled = false;
                runBtn.Enabled = false;
                folderBtn.Enabled = false;
                MessageBox.Show("Deleted temporary files", "TinyNvidiaUpdateChecker", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else
            {
                MessageBox.Show("Could not delete temporary files!", "TinyNvidiaUpdateChecker", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void folderBtn_Click(object sender, EventArgs e)
        {
            if (Directory.Exists(folderPath))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = folderPath,
                    UseShellExecute = true
                });
            }
        }

        // Delete temporary files, and the parent folder if it is empty
        private bool deleteTempFiles()
        {
            return FileSystemHandler.DeleteTemporaryFiles(folderPath, tempFiles);
        }

        private void ReadyInstallForm_Load(object sender, EventArgs e)
        {
            // Set exeLabel
            string exeName = Path.GetFileName(this.driverPath);
            exeLabel.Text = exeName;
        }

        private void ReadyInstallForm_Shown(object sender, EventArgs e)
        {
            // Flash and play sound
            this.Flash(true);
        }
    }
}
