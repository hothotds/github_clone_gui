using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace GithubDownloadGui
{
    internal sealed class MainForm : Form
    {
        private static readonly Regex StageRe = new Regex(
            @"(Counting objects|Enumerating objects|Compressing objects|" +
            @"Receiving objects|Resolving deltas|Updating files|Checking out files)" +
            @".*?(\d+)%",
            RegexOptions.Compiled);

        private static readonly HttpClient Http = CreateHttpClient();

        private TextBox urlBox, dirBox, nameBox, logBox;
        private Label modeLabel, nameLabel, targetLabel, stageLabel;
        private CheckBox deleteZipCheck;
        private Button startBtn, cancelBtn, openBtn;
        private ProgressBar progress;

        private CancellationTokenSource _cts;
        private bool _busy, _nameEdited, _programmatic;
        private string _openPath = "";

        private readonly string _settingsFile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "GithubDownloadGui", "settings.txt");

        public MainForm()
        {
            BuildUi();
            LoadSettings();

            if (!Util.IsInPath("git.exe"))
                Log("提示: 未找到 git，克隆仓库不可用（文件下载不受影响）。安装地址: https://git-scm.com/downloads");
        }

        private static HttpClient CreateHttpClient()
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = true });
            client.Timeout = Timeout.InfiniteTimeSpan;
            client.DefaultRequestHeaders.UserAgent.ParseAdd("GithubDownloadGui/1.0");
            return client;
        }

        // ================= UI =================
        private void BuildUi()
        {
            SuspendLayout();
            Text = "GitHub 克隆 / 下载工具";
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            ClientSize = new Size(720, 500);
            MinimumSize = new Size(560, 420);
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Microsoft YaHei UI", 9F);

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 5,
                Padding = new Padding(10)
            };
            for (int i = 0; i < 4; i++) root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            // ----- 来源 -----
            var box = new GroupBox
            {
                Text = "来源",
                Dock = DockStyle.Fill,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(8)
            };
            var form = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 6,
                AutoSize = true
            };
            form.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            form.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            form.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            for (int i = 0; i < 6; i++) form.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            urlBox = new TextBox { Anchor = AnchorStyles.Left | AnchorStyles.Right };
            Util.SetCue(urlBox, "仓库地址 / user/repo / 文件下载直链 (如 releases 里的 .zip)");
            urlBox.TextChanged += OnUrlChanged;

            modeLabel = new Label { Text = "-", AutoSize = true, ForeColor = Color.Gray, Anchor = AnchorStyles.Left };

            dirBox = new TextBox { Anchor = AnchorStyles.Left | AnchorStyles.Right };
            dirBox.TextChanged += (s, e) => UpdateTargetLabel();
            var browseBtn = new Button { Text = "浏览…", AutoSize = true };
            browseBtn.Click += BrowseDir;

            nameLabel = new Label { Text = "名称", AutoSize = true, Anchor = AnchorStyles.Left };
            nameBox = new TextBox { Anchor = AnchorStyles.Left | AnchorStyles.Right };
            Util.SetCue(nameBox, "默认使用仓库名 / 文件名");
            nameBox.TextChanged += (s, e) =>
            {
                if (!_programmatic) _nameEdited = true;
                UpdateTargetLabel();
            };

            targetLabel = new Label { Text = "-", AutoSize = true, ForeColor = Color.Gray, Anchor = AnchorStyles.Left };

            deleteZipCheck = new CheckBox
            {
                Text = "zip 解压完成后删除压缩包",
                Checked = true,
                AutoSize = true,
                Anchor = AnchorStyles.Left
            };

            AddRow(form, 0, "地址", urlBox, null);
            AddRow(form, 1, "识别为", modeLabel, null);
            AddRow(form, 2, "保存到", dirBox, browseBtn);
            form.Controls.Add(nameLabel, 0, 3);
            form.Controls.Add(nameBox, 1, 3);
            form.SetColumnSpan(nameBox, 2);
            AddRow(form, 4, "最终路径", targetLabel, null);
            form.Controls.Add(deleteZipCheck, 1, 5);
            form.SetColumnSpan(deleteZipCheck, 2);

            box.Controls.Add(form);
            root.Controls.Add(box, 0, 0);

            // ----- 按钮 -----
            var btnRow = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = false };
            startBtn = new Button { Text = "开始", AutoSize = true, Padding = new Padding(10, 2, 10, 2) };
            startBtn.Click += StartBtn_Click;
            cancelBtn = new Button { Text = "取消", AutoSize = true, Enabled = false, Padding = new Padding(10, 2, 10, 2) };
            cancelBtn.Click += (s, e) => { if (_cts != null) _cts.Cancel(); };
            openBtn = new Button { Text = "打开文件夹", AutoSize = true, Enabled = false, Padding = new Padding(10, 2, 10, 2) };
            openBtn.Click += OpenTarget;
            btnRow.Controls.AddRange(new Control[] { startBtn, cancelBtn, openBtn });
            root.Controls.Add(btnRow, 0, 1);
            AcceptButton = startBtn;

            // ----- 状态 -----
            stageLabel = new Label { Text = "就绪", AutoSize = true, Margin = new Padding(3, 8, 3, 3) };
            progress = new ProgressBar { Dock = DockStyle.Fill, Minimum = 0, Maximum = 100 };
            root.Controls.Add(stageLabel, 0, 2);
            root.Controls.Add(progress, 0, 3);

            // ----- 日志 -----
            logBox = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                Font = new Font("Consolas", 9F),
                BackColor = SystemColors.Window
            };
            root.Controls.Add(logBox, 0, 4);

            Controls.Add(root);
            ResumeLayout(true);

            FormClosing += OnFormClosing;
        }

        private static void AddRow(TableLayoutPanel t, int row, string label, Control field, Control extra)
        {
            t.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left }, 0, row);
            t.Controls.Add(field, 1, row);
            if (extra != null) t.Controls.Add(extra, 2, row);
            else t.SetColumnSpan(field, 2);
        }

        // ================= 设置 =================
        private void LoadSettings()
        {
            string dir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            try
            {
                if (File.Exists(_settingsFile))
                {
                    string[] lines = File.ReadAllLines(_settingsFile, Encoding.UTF8);
                    if (lines.Length > 0 && lines[0].Length > 0) dir = lines[0];
                    if (lines.Length > 1) deleteZipCheck.Checked = lines[1] != "0";
                }
            }
            catch (IOException) { }
            dirBox.Text = dir;
        }

        private void SaveSettings()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_settingsFile));
                File.WriteAllLines(_settingsFile,
                    new[] { dirBox.Text.Trim(), deleteZipCheck.Checked ? "1" : "0" }, Encoding.UTF8);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        // ================= 事件 =================
        private void OnUrlChanged(object sender, EventArgs e)
        {
            string text = urlBox.Text;
            if (nameBox.Text.Length == 0) _nameEdited = false;

            Mode mode = Util.DetectMode(text);
            if (mode == Mode.Download)
            {
                modeLabel.Text = "文件下载（zip 会自动解压到同名文件夹）";
                nameLabel.Text = "文件名";
                if (!_nameEdited) SetName(Util.FileNameFromUrl(text));
            }
            else if (mode == Mode.Git)
            {
                modeLabel.Text = "Git 仓库（克隆）";
                nameLabel.Text = "文件夹名";
                if (!_nameEdited) SetName(Util.RepoName(Util.NormalizeGitUrl(text)));
            }
            else
            {
                modeLabel.Text = "-";
                if (!_nameEdited) SetName("");
            }
            UpdateTargetLabel();
        }

        private void SetName(string name)
        {
            _programmatic = true;
            nameBox.Text = name;
            _programmatic = false;
        }

        private void BrowseDir(object sender, EventArgs e)
        {
            using (var dlg = new FolderBrowserDialog())
            {
                dlg.Description = "选择保存目录";
                if (Directory.Exists(dirBox.Text)) dlg.SelectedPath = dirBox.Text;
                if (dlg.ShowDialog(this) == DialogResult.OK) dirBox.Text = dlg.SelectedPath;
            }
        }

        private static bool IsZip(string name)
        {
            return name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>下载得到的文件路径（git 模式下无意义）。</summary>
        private string DownloadPath()
        {
            string dir = dirBox.Text.Trim(), name = nameBox.Text.Trim();
            if (dir.Length == 0 || name.Length == 0) return "";
            try { return Path.GetFullPath(Path.Combine(dir, name)); }
            catch (ArgumentException) { return ""; }
        }

        /// <summary>最终产物路径：克隆目录 / 解压目录 / 下载文件。</summary>
        private string OutputPath()
        {
            string dir = dirBox.Text.Trim(), name = nameBox.Text.Trim();
            if (dir.Length == 0 || name.Length == 0) return "";
            if (Util.DetectMode(urlBox.Text) == Mode.Download && IsZip(name))
                name = name.Substring(0, name.Length - 4);
            if (name.Length == 0) return "";
            try { return Path.GetFullPath(Path.Combine(dir, name)); }
            catch (ArgumentException) { return ""; }
        }

        private void UpdateTargetLabel()
        {
            string p = OutputPath();
            targetLabel.Text = p.Length == 0 ? "-" : p;
        }

        private void Log(string text)
        {
            logBox.AppendText(text + "\r\n");
        }

        private void SetStage(string text)
        {
            stageLabel.Text = text;
        }

        private void SetProgress(int value)
        {
            if (progress.Style != ProgressBarStyle.Blocks) progress.Style = ProgressBarStyle.Blocks;
            progress.Value = Math.Max(0, Math.Min(100, value));
        }

        private void SetRunning(bool running)
        {
            _busy = running;
            startBtn.Enabled = !running;
            cancelBtn.Enabled = running;
            urlBox.Enabled = dirBox.Enabled = nameBox.Enabled = deleteZipCheck.Enabled = !running;
        }

        private void Ui(Action a)
        {
            if (IsHandleCreated && !IsDisposed) BeginInvoke(a);
        }

        private static void Warn(IWin32Window owner, string msg)
        {
            MessageBox.Show(owner, msg, "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        // ================= 入口 =================
        private async void StartBtn_Click(object sender, EventArgs e)
        {
            string raw = urlBox.Text.Trim();
            Mode mode = Util.DetectMode(raw);
            string name = nameBox.Text.Trim();
            string output = OutputPath();

            if (raw.Length == 0) { Warn(this, "请输入仓库地址或下载链接"); return; }
            if (name.Length == 0 || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || output.Length == 0)
            {
                Warn(this, "请选择保存目录并确认名称（不能含 \\ / : * ? \" < > |）");
                return;
            }

            // ----- 启动前检查 -----
            if (mode == Mode.Git)
            {
                if (!Util.IsInPath("git.exe")) { Warn(this, "未找到 git，请先安装并加入 PATH"); return; }
                if (Directory.Exists(output) && Directory.GetFileSystemEntries(output).Length > 0)
                {
                    Warn(this, "目标文件夹已存在且不为空:\n" + output);
                    return;
                }
            }
            else if (IsZip(name))
            {
                if (Directory.Exists(output) && Directory.GetFileSystemEntries(output).Length > 0 &&
                    MessageBox.Show(this, output + "\n已存在且不为空，解压会合并并覆盖同名文件，是否继续？",
                        "文件夹已存在", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                    return;
            }
            else if (File.Exists(output))
            {
                if (MessageBox.Show(this, output + "\n已存在，是否覆盖？", "文件已存在",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                    return;
            }

            _cts = new CancellationTokenSource();
            CancellationToken ct = _cts.Token;
            _openPath = output;
            SetRunning(true);
            SetProgress(0);
            openBtn.Enabled = false;
            SaveSettings();

            try
            {
                if (mode == Mode.Git)
                {
                    if (await CloneAsync(Util.NormalizeGitUrl(raw), output, ct))
                        openBtn.Enabled = true;
                }
                else
                {
                    await DownloadAndExtractAsync(raw, DownloadPath(), output, ct);
                    openBtn.Enabled = true;
                }
            }
            catch (Exception ex)
            {
                if (ct.IsCancellationRequested)
                {
                    SetProgress(0);
                    SetStage("已取消");
                    Log(mode == Mode.Git
                        ? "已取消。未完成的文件夹可能残留，可手动删除: " + output
                        : "已取消。");
                }
                else
                {
                    SetProgress(0);
                    SetStage("失败");
                    Log("✘ 失败: " + ex.Message);
                }
            }
            finally
            {
                SetRunning(false);
                _cts.Dispose();
                _cts = null;
            }
        }

        // ================= git 克隆 =================
        private async Task<bool> CloneAsync(string url, string target, CancellationToken ct)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(target));
            string args = "clone --progress " + Quote(url) + " " + Quote(target);
            Log("$ git " + args);
            SetStage("正在连接…");

            var psi = new ProcessStartInfo("git", args)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                StandardErrorEncoding = Encoding.UTF8,
                StandardOutputEncoding = Encoding.UTF8
            };
            psi.EnvironmentVariables["GIT_TERMINAL_PROMPT"] = "0"; // 避免卡在账号密码输入

            var p = new Process { StartInfo = psi };
            try
            {
                p.Start();
            }
            catch (Win32Exception)
            {
                Log("✘ 无法启动 git，请确认已安装并在 PATH 中");
                SetStage("无法启动 git");
                return false;
            }

            using (ct.Register(() => KillTree(p)))
            {
                Task errTask = Task.Run(() => PumpGitStderr(p.StandardError));
                Task outTask = Task.Run(() =>
                {
                    string line;
                    while ((line = p.StandardOutput.ReadLine()) != null)
                    {
                        string l = line;
                        Ui(() => Log(l));
                    }
                });
                await Task.WhenAll(errTask, outTask);
                await Task.Run(() => p.WaitForExit());
            }

            int code = p.ExitCode;
            p.Dispose();
            ct.ThrowIfCancellationRequested();

            if (code == 0)
            {
                SetProgress(100);
                SetStage("克隆完成");
                Log("✔ 完成: " + target);
                return true;
            }
            SetStage("克隆失败 (退出码 " + code + ")");
            Log("✘ 失败，退出码 " + code);
            return false;
        }

        private void PumpGitStderr(StreamReader reader)
        {
            var sb = new StringBuilder();
            int c;
            while ((c = reader.Read()) != -1)
            {
                char ch = (char)c;
                if (ch == '\r' || ch == '\n') // git 进度以 \r 刷新
                {
                    Flush(sb);
                }
                else
                {
                    sb.Append(ch);
                }
            }
            Flush(sb);
        }

        private void Flush(StringBuilder sb)
        {
            if (sb.Length == 0) return;
            string line = sb.ToString().Trim();
            sb.Clear();
            if (line.Length == 0) return;

            Ui(() =>
            {
                Match m = StageRe.Match(line);
                if (m.Success)
                {
                    int pct = int.Parse(m.Groups[2].Value);
                    SetStage(m.Groups[1].Value + "… " + pct + "%");
                    SetProgress(pct);
                    if (pct == 100) Log(line);
                }
                else
                {
                    Log(line);
                }
            });
        }

        private static string Quote(string s)
        {
            return "\"" + s.Replace("\"", "\\\"") + "\"";
        }

        private static void KillTree(Process p)
        {
            try
            {
                var psi = new ProcessStartInfo("taskkill", "/PID " + p.Id + " /T /F")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using (Process k = Process.Start(psi)) k.WaitForExit(3000);
            }
            catch (Exception) { }
        }

        // ================= 下载 + 解压 =================
        private async Task DownloadAndExtractAsync(string url, string filePath, string output, CancellationToken ct)
        {
            await DownloadAsync(url, filePath, ct);

            if (!IsZip(filePath))
            {
                SetProgress(100);
                SetStage("下载完成");
                Log("✔ 完成: " + filePath + " (" + Util.FmtSize(new FileInfo(filePath).Length) + ")");
                _openPath = filePath;
                return;
            }

            Log("下载完成，开始解压到: " + output);
            SetStage("正在解压…");
            SetProgress(0);

            var prog = new Progress<Tuple<int, int>>(t =>
            {
                SetStage("解压中… " + t.Item1 + " / " + t.Item2);
                SetProgress(t.Item2 == 0 ? 0 : t.Item1 * 100 / t.Item2);
            });

            int count = await Task.Run(() => ExtractZip(filePath, output, ct, prog));

            if (deleteZipCheck.Checked)
            {
                try
                {
                    File.Delete(filePath);
                    Log("已删除压缩包");
                }
                catch (IOException ex)
                {
                    Log("压缩包删除失败: " + ex.Message);
                }
            }

            SetProgress(100);
            SetStage("解压完成");
            Log("✔ 完成: " + output + " (" + count + " 项)");
            _openPath = output;
        }

        private async Task DownloadAsync(string url, string target, CancellationToken ct)
        {
            string part = target + ".part";
            Directory.CreateDirectory(Path.GetDirectoryName(target));
            SetStage("正在连接…");
            Log("GET " + url);

            var sw = Stopwatch.StartNew();
            try
            {
                using (HttpResponseMessage resp = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct))
                using (ct.Register(() => resp.Dispose())) // 让阻塞中的读取也能被取消
                {
                    resp.EnsureSuccessStatusCode();
                    long? total = resp.Content.Headers.ContentLength;

                    using (Stream src = await resp.Content.ReadAsStreamAsync())
                    using (var dst = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
                    {
                        var buf = new byte[81920];
                        long got = 0, lastTick = 0;
                        int n;
                        while ((n = await src.ReadAsync(buf, 0, buf.Length, ct)) > 0)
                        {
                            await dst.WriteAsync(buf, 0, n, ct);
                            got += n;
                            if (sw.ElapsedMilliseconds - lastTick >= 100)
                            {
                                lastTick = sw.ElapsedMilliseconds;
                                ReportDownload(got, total, sw);
                            }
                        }
                        ReportDownload(got, total, sw);
                    }
                }

                if (File.Exists(target)) File.Delete(target);
                File.Move(part, target);
            }
            catch (Exception)
            {
                try { File.Delete(part); } catch (IOException) { }
                throw;
            }
        }

        private void ReportDownload(long got, long? total, Stopwatch sw)
        {
            double secs = Math.Max(sw.Elapsed.TotalSeconds, 0.001);
            string speed = Util.FmtSize(got / secs) + "/s";
            if (total.HasValue && total.Value > 0)
            {
                SetProgress((int)(got * 100 / total.Value));
                SetStage("下载中… " + Util.FmtSize(got) + " / " + Util.FmtSize(total.Value) + "  (" + speed + ")");
            }
            else
            {
                if (progress.Style != ProgressBarStyle.Marquee) progress.Style = ProgressBarStyle.Marquee; // 未知大小
                SetStage("下载中… " + Util.FmtSize(got) + "  (" + speed + ")");
            }
        }

        /// <summary>
        /// 解压到目标文件夹。若压缩包内所有内容都在同一个顶层文件夹里
        /// （GitHub 源码归档就是这样），则去掉这一层，避免出现 xxx\xxx-main\ 的嵌套。
        /// </summary>
        private static int ExtractZip(string zipPath, string destDir, CancellationToken ct,
            IProgress<Tuple<int, int>> prog)
        {
            Directory.CreateDirectory(destDir);
            string destFull = Path.GetFullPath(destDir).TrimEnd('\\') + "\\";
            int done = 0, written = 0;

            using (ZipArchive zip = ZipFile.OpenRead(zipPath))
            {
                var entries = new List<ZipArchiveEntry>(zip.Entries);
                string strip = CommonRoot(entries);
                int total = entries.Count;
                int step = Math.Max(1, total / 200);

                foreach (ZipArchiveEntry entry in entries)
                {
                    ct.ThrowIfCancellationRequested();
                    done++;

                    string name = entry.FullName;
                    if (strip != null) name = name.Substring(strip.Length);
                    if (name.Length > 0)
                    {
                        string outPath = Path.GetFullPath(Path.Combine(destFull, name.Replace('/', '\\')));
                        // 防止 zip slip：条目不得跳出目标目录
                        if (outPath.StartsWith(destFull, StringComparison.OrdinalIgnoreCase))
                        {
                            if (name.EndsWith("/"))
                            {
                                Directory.CreateDirectory(outPath);
                            }
                            else
                            {
                                Directory.CreateDirectory(Path.GetDirectoryName(outPath));
                                entry.ExtractToFile(outPath, true);
                                written++;
                            }
                        }
                    }

                    if (done % step == 0 || done == total)
                        prog.Report(Tuple.Create(done, total));
                }
            }
            return written;
        }

        private static string CommonRoot(List<ZipArchiveEntry> entries)
        {
            string root = null;
            foreach (ZipArchiveEntry e in entries)
            {
                int idx = e.FullName.IndexOf('/');
                if (idx <= 0) return null; // 根目录下有文件，不能去掉顶层
                string seg = e.FullName.Substring(0, idx + 1);
                if (root == null) root = seg;
                else if (!string.Equals(root, seg, StringComparison.Ordinal)) return null;
            }
            return root;
        }

        // ================= 其他 =================
        private void OpenTarget(object sender, EventArgs e)
        {
            if (_openPath.Length == 0) return;
            try
            {
                if (Directory.Exists(_openPath))
                    Process.Start("explorer.exe", Quote(_openPath));
                else if (File.Exists(_openPath))
                    Process.Start("explorer.exe", "/select," + Quote(_openPath));
            }
            catch (Exception ex)
            {
                Warn(this, "无法打开: " + ex.Message);
            }
        }

        private void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            if (_busy)
            {
                if (MessageBox.Show(this, "任务仍在进行，确定退出吗？", "确认",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                {
                    e.Cancel = true;
                    return;
                }
                if (_cts != null) _cts.Cancel();
            }
            SaveSettings();
        }
    }
}
