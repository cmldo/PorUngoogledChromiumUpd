using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PorUngoogledChromiumUpd
{
    public class MainForm : Form
    {
        private Button btnDownload;
        private Button btnReinstall;
        private ProgressBar progressBar;
        private Label lblLatestVersion;
        private Label lblInstalled;
        private Label lblStatus;
        private Label lblSpeed;
        private Label lblEta;
        private CheckBox chkLaunchAfterUpdate;
        private CheckBox chkAutoUpdate;
        private CheckBox chkCreateShortcuts;

        private readonly string repoOwner = "ungoogled-software";
        private readonly string repoName = "ungoogled-chromium-windows";
        private readonly string destFolder;
        private readonly string settingsFile;

        private string latestVersion = null!;
        private string latestAssetUrl = null!;
        private string installedVersion = null!;

        public MainForm()
        {
            destFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Ungoogled-Chromium x64");
            settingsFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.ini");

            //-------------------------------------------------------
            // UI
            //-------------------------------------------------------
            btnDownload = new Button() { Text = "Download Ungoogled-Chromium", Width = 180, Top = 20, Left = 20 };
            btnDownload.Click += async (s, e) => await StartDownload(force: false);

            btnReinstall = new Button() { Text = "Reinstall Ungoogled-Chromium", Width = 180, Top = 20, Left = 220 };
            btnReinstall.Click += async (s, e) => await StartDownload(force: true);

            lblLatestVersion = new Label() { Text = "Latest version: ...", Top = 60, Left = 20, Width = 380 };
            lblInstalled = new Label() { Text = "Installed version: ...", Top = 85, Left = 20, Width = 380 };
            lblStatus = new Label() { Text = "Status: Idle", Top = 110, Left = 20, Width = 380 };
            lblSpeed = new Label() { Text = "Speed: 0 MB/s", Top = 135, Left = 20, Width = 380 };
            lblEta = new Label() { Text = "ETA: --", Top = 160, Left = 20, Width = 380 };

            progressBar = new ProgressBar() { Top = 185, Left = 20, Width = 380 };

            chkLaunchAfterUpdate = new CheckBox() { Text = "Launch Chromium after update", Top = 210, Left = 20, Width = 220 };
            chkAutoUpdate = new CheckBox() { Text = "Check for updates on startup", Top = 235, Left = 20, Width = 220 };
            chkCreateShortcuts = new CheckBox() { Text = "Create desktop shortcuts", Top = 260, Left = 20, Width = 220 };

            Button btnRegenerateShortcuts = new Button()
            {
                Text = "Regenerate Shortcuts",
                Top = chkCreateShortcuts.Top,
                Left = chkCreateShortcuts.Left + chkCreateShortcuts.Width + 10,
                Width = 150,
                Height = chkCreateShortcuts.Height
            };
            btnRegenerateShortcuts.Click += (s, e) => CreateDesktopShortcuts();

            Controls.Add(btnDownload);
            Controls.Add(btnReinstall);
            Controls.Add(lblLatestVersion);
            Controls.Add(lblInstalled);
            Controls.Add(lblStatus);
            Controls.Add(lblSpeed);
            Controls.Add(lblEta);
            Controls.Add(progressBar);
            Controls.Add(chkLaunchAfterUpdate);
            Controls.Add(chkAutoUpdate);
            Controls.Add(chkCreateShortcuts);
            Controls.Add(btnRegenerateShortcuts);

            Text = "PorUngoogledChromiumUpd";
            Width = 440;
            Height = 340;

            //-------------------------------------------------------
            // Load settings & check updates
            //-------------------------------------------------------
            this.Load += async (s, e) =>
            {
                LoadSettings();
                await RefreshInstalledVersion();
                await RefreshLatestVersion();

                if (chkAutoUpdate.Checked && latestVersion != installedVersion)
                {
                    var ans = MessageBox.Show(
                        $"New Ungoogled-Chromium version {latestVersion} is available. Update now?",
                        "Update available",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                    if (ans == DialogResult.Yes)
                        await StartDownload(force: false);
                }

                btnReinstall.Enabled = installedVersion != "Not installed";
            };

            this.FormClosing += (s, e) => { SaveSettings(); };
        }

        //-------------------------------------------------------
        // ENTRY POINT
        //-------------------------------------------------------
        [STAThread]
        public static void Main()
        {
            Application.EnableVisualStyles();
            Application.Run(new MainForm());
        }

        //-------------------------------------------------------
        // SETTINGS MANAGEMENT
        //-------------------------------------------------------
        private void LoadSettings()
        {
            chkLaunchAfterUpdate.Checked = true;
            chkAutoUpdate.Checked = true;
            chkCreateShortcuts.Checked = true;

            try
            {
                if (!File.Exists(settingsFile)) return;

                foreach (var line in File.ReadAllLines(settingsFile))
                {
                    var parts = line.Split('=');
                    if (parts.Length != 2) continue;
                    var key = parts[0].Trim();
                    var val = parts[1].Trim().ToLower();

                    if (key == "launchAfterUpdate") chkLaunchAfterUpdate.Checked = val == "true";
                    else if (key == "autoUpdate") chkAutoUpdate.Checked = val == "true";
                    else if (key == "createShortcuts") chkCreateShortcuts.Checked = val == "true";
                }
            }
            catch { }
        }

        private void SaveSettings()
        {
            try
            {
                File.WriteAllLines(settingsFile, new string[]
                {
                    $"launchAfterUpdate={chkLaunchAfterUpdate.Checked}",
                    $"autoUpdate={chkAutoUpdate.Checked}",
                    $"createShortcuts={chkCreateShortcuts.Checked}"
                });
            }
            catch { }
        }

        //-------------------------------------------------------
        // REFRESH INSTALLED VERSION
        //-------------------------------------------------------
        private async Task RefreshInstalledVersion()
        {
            installedVersion = "Not installed";
            string logPath = Path.Combine(destFolder, "revision.log");

            if (File.Exists(logPath))
            {
                try
                {
                    string content = await File.ReadAllTextAsync(logPath);
                    if (!string.IsNullOrWhiteSpace(content))
                    {
                        var parts = content.Split('|');
                        if (parts.Length > 0 && !string.IsNullOrWhiteSpace(parts[0]))
                            installedVersion = parts[0].Trim();
                    }
                }
                catch { }
            }

            Invoke(new Action(() => lblInstalled.Text = "Installed version: " + installedVersion));
        }

        //-------------------------------------------------------
        // GET LATEST VERSION VIA GITHUB API
        //-------------------------------------------------------
        private async Task RefreshLatestVersion()
        {
            try
            {
                using HttpClient client = new HttpClient();
                client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("PorUngoogledChromiumUpd", "1.0"));

                string apiUrl = $"https://api.github.com/repos/{repoOwner}/{repoName}/releases/latest";
                var resp = await client.GetStringAsync(apiUrl);

                using JsonDocument doc = JsonDocument.Parse(resp);
                var root = doc.RootElement;

                if (root.TryGetProperty("tag_name", out var tagNameElement))
                    latestVersion = tagNameElement.GetString()!;
                else
                    latestVersion = "Unknown";

                // Get the first Windows x64 .zip asset
                latestAssetUrl = null!;
                if (root.TryGetProperty("assets", out var assets))
                {
                    foreach (var asset in assets.EnumerateArray())
                    {
                        if (asset.TryGetProperty("name", out var nameEl) &&
                            nameEl.GetString()?.Contains("windows_x64.zip") == true &&
                            asset.TryGetProperty("browser_download_url", out var urlEl))
                        {
                            latestAssetUrl = urlEl.GetString()!;
                            break;
                        }
                    }
                }

                Invoke(new Action(() => lblLatestVersion.Text = "Latest version: " + latestVersion));
            }
            catch (Exception ex)
            {
                latestVersion = null!;
                latestAssetUrl = null!;
                Invoke(new Action(() => lblLatestVersion.Text = "Latest version: (error)"));
                LogError("RefreshLatestVersion failed: " + ex);
            }
        }

        //-------------------------------------------------------
        // MAIN DOWNLOAD LOGIC
        //-------------------------------------------------------
        private async Task StartDownload(bool force)
        {
            try
            {
                SaveSettings();

                if (string.IsNullOrWhiteSpace(latestVersion) || string.IsNullOrWhiteSpace(latestAssetUrl))
                {
                    await RefreshLatestVersion();
                    if (string.IsNullOrWhiteSpace(latestVersion) || string.IsNullOrWhiteSpace(latestAssetUrl))
                    {
                        MessageBox.Show("Couldn't resolve latest version.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }
                }

                if (!force && installedVersion == latestVersion)
                {
                    var ans = MessageBox.Show($"Latest version {latestVersion} is already installed.\nReinstall anyway?",
                        "Already Installed", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                    if (ans == DialogResult.No) return;
                }

                if (IsChromiumRunning())
                {
                    MessageBox.Show("Please close all Chromium instances before updating.", "Chromium Running", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                Directory.CreateDirectory(destFolder);

                lblStatus.Text = "Deleting old version...";
                await DeleteOldVersion();

                lblStatus.Text = "Downloading ZIP...";
                string zipPath = Path.Combine(destFolder, "ungoogled-chromium.zip");
                await DownloadZip(latestAssetUrl, zipPath);

                lblStatus.Text = "Extracting...";
                await ExtractZipWithProgress(zipPath, destFolder);

                try { File.Delete(zipPath); } catch { }

                File.WriteAllText(Path.Combine(destFolder, "revision.log"), $"{latestVersion}|Stable|x64");

                lblStatus.Text = "Done!";

                await RefreshInstalledVersion();

                if (chkCreateShortcuts.Checked)
                    CreateDesktopShortcuts();

                if (chkLaunchAfterUpdate.Checked)
                {
                    string chromeExe = Path.Combine(destFolder, "chrome.exe");
                    if (File.Exists(chromeExe))
                        Process.Start(new ProcessStartInfo { FileName = chromeExe, UseShellExecute = true });
                }

                btnReinstall.Enabled = installedVersion != "Not installed";
            }
            catch (Exception ex)
            {
                LogError(ex.ToString());
                MessageBox.Show("Error:\n" + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                lblSpeed.Text = "Speed: 0 MB/s";
                lblEta.Text = "ETA: --";
            }
        }

        //-------------------------------------------------------
        // DELETE OLD VERSION
        //-------------------------------------------------------
        private Task DeleteOldVersion()
        {
            return Task.Run(() =>
            {
                try
                {
                    if (!Directory.Exists(destFolder)) return;

                    string profilePath = Path.Combine(destFolder, "profile");

                    foreach (var file in Directory.GetFiles(destFolder, "*", SearchOption.TopDirectoryOnly))
                    {
                        try { File.SetAttributes(file, FileAttributes.Normal); File.Delete(file); } catch { }
                    }

                    foreach (var dir in Directory.GetDirectories(destFolder, "*", SearchOption.TopDirectoryOnly))
                    {
                        if (dir.Equals(profilePath, StringComparison.OrdinalIgnoreCase)) continue;
                        try { Directory.Delete(dir, true); } catch { }
                    }
                }
                catch (Exception ex) { LogError("DeleteOldVersion failed: " + ex); }
            });
        }

        //-------------------------------------------------------
        // DOWNLOAD ZIP WITH PROGRESS
        //-------------------------------------------------------
        private async Task DownloadZip(string url, string destination)
        {
            using HttpClient client = new HttpClient();
            using var resp = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            resp.EnsureSuccessStatusCode();

            long? total = resp.Content.Headers.ContentLength;
            using var stream = await resp.Content.ReadAsStreamAsync();
            using var outStream = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None);

            byte[] buffer = new byte[32 * 1024];
            long totalRead = 0;
            int read;
            var sw = Stopwatch.StartNew();

            while ((read = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
            {
                await outStream.WriteAsync(buffer, 0, read);
                totalRead += read;

                if (total.HasValue)
                {
                    int percent = (int)((totalRead * 100) / total.Value);
                    double elapsedSec = Math.Max(0.001, sw.Elapsed.TotalSeconds);
                    double mbRead = totalRead / 1024d / 1024d;
                    double speed = mbRead / elapsedSec;
                    double remainingMb = ((total.Value - totalRead) / 1024d / 1024d);
                    double etaSec = speed > 0 ? remainingMb / speed : double.PositiveInfinity;

                    Invoke(new Action(() =>
                    {
                        progressBar.Value = percent;
                        lblSpeed.Text = $"Speed: {speed:0.0} MB/s";
                        lblEta.Text = double.IsInfinity(etaSec) ? "ETA: --" : $"ETA: {etaSec:0}s";
                    }));
                }
            }

            Invoke(new Action(() => { progressBar.Value = 100; }));
        }

        //-------------------------------------------------------
        // EXTRACT ZIP
        //-------------------------------------------------------
private Task ExtractZipWithProgress(string zipFile, string extractTo)
{
    return Task.Run(() =>
    {
        try
        {
            using ZipArchive archive = ZipFile.OpenRead(zipFile);

            int total = archive.Entries.Count;
            int current = 0;

            foreach (var entry in archive.Entries)
            {
                string relative = entry.FullName;

                // Skip empty entries
                if (string.IsNullOrEmpty(relative)) continue;

                // Flatten top-level folder by removing first segment
                string[] parts = relative.Split(new char[] { '/', '\\' }, 2);
                if (parts.Length == 2)
                    relative = parts[1];
                else
                    relative = parts[0];

                string targetPath = Path.Combine(extractTo, relative);

                if (string.IsNullOrEmpty(entry.Name)) // Directory
                {
                    Directory.CreateDirectory(targetPath);
                }
                else // File
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
                    entry.ExtractToFile(targetPath, true);
                }

                current++;
                int percent = (current * 100) / Math.Max(1, total);
                Invoke(new Action(() => { progressBar.Value = percent; }));
            }
        }
        catch (Exception ex)
        {
            LogError("ExtractZipWithProgress failed: " + ex);
            throw;
        }
    });
}



        //-------------------------------------------------------
        // CREATE DESKTOP SHORTCUTS
        //-------------------------------------------------------
        private void CreateDesktopShortcuts()
        {
            try
            {
                string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                string chromePath = Path.Combine(destFolder, "chrome.exe");

                string profileCommonDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "profile");
                string profileDir = Path.Combine(destFolder, "profile");

                CreateShortcut(desktop, "Ungoogled-Chromium.lnk", chromePath, "");
                CreateShortcut(desktop, "Ungoogled-Chromium - Common Profile.lnk", chromePath, $"--user-data-dir=\"{profileCommonDir}\"");
                CreateShortcut(desktop, "Ungoogled-Chromium - Single Profile.lnk", chromePath, $"--user-data-dir=\"{profileDir}\"");
            }
            catch (Exception ex)
            {
                LogError("CreateDesktopShortcuts failed: " + ex);
            }
        }

        private void CreateShortcut(string folder, string shortcutName, string targetPath, string arguments)
        {
            Type shellType = Type.GetTypeFromProgID("WScript.Shell");
            dynamic shell = Activator.CreateInstance(shellType);
            dynamic shortcut = shell.CreateShortcut(Path.Combine(folder, shortcutName));
            shortcut.TargetPath = targetPath;
            shortcut.Arguments = arguments;
            shortcut.WorkingDirectory = Path.GetDirectoryName(targetPath);
            shortcut.Save();
        }

        //-------------------------------------------------------
        // CHECK IF CHROMIUM IS RUNNING
        //-------------------------------------------------------
        private bool IsChromiumRunning()
        {
            try { return Process.GetProcessesByName("chrome").Length > 0; }
            catch { return false; }
        }

        //-------------------------------------------------------
        // LOGGING
        //-------------------------------------------------------
        private void LogError(string msg)
        {
            try
            {
                File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "PorUngoogledChromiumUpd.log"),
                    DateTime.Now.ToString("s") + " --- " + msg + Environment.NewLine);
            }
            catch { }
        }
    }
}
