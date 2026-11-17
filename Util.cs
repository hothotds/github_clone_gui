using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace GithubDownloadGui
{
    internal enum Mode
    {
        None,
        Git,
        Download
    }

    internal static class Util
    {
        /// <summary>判断输入是 git 仓库还是文件直链。</summary>
        public static Mode DetectMode(string text)
        {
            string t = (text ?? "").Trim();
            if (t.Length == 0) return Mode.None;

            if (Regex.IsMatch(t, "^https?://", RegexOptions.IgnoreCase))
            {
                Uri uri;
                if (Uri.TryCreate(t, UriKind.Absolute, out uri))
                {
                    string path = Uri.UnescapeDataString(uri.AbsolutePath);
                    string host = uri.Host.ToLowerInvariant();
                    string[] segs = path.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);

                    if (path.EndsWith(".git", StringComparison.OrdinalIgnoreCase)) return Mode.Git;
                    if (host == "codeload.github.com") return Mode.Download;
                    if ((host == "github.com" || host == "www.github.com") && segs.Length <= 2) return Mode.Git;
                    if (path.Contains("/releases/download/") || path.Contains("/archive/")) return Mode.Download;

                    string last = segs.Length > 0 ? segs[segs.Length - 1] : "";
                    if (Regex.IsMatch(last, @"\.[A-Za-z0-9]{1,8}$")) return Mode.Download;
                }
            }
            return Mode.Git;
        }

        /// <summary>支持 user/repo 简写，自动补全为 GitHub 地址。</summary>
        public static string NormalizeGitUrl(string text)
        {
            text = (text ?? "").Trim();
            if (Regex.IsMatch(text, @"^[\w.-]+/[\w.-]+$"))
                return "https://github.com/" + text + ".git";
            return text;
        }

        public static string RepoName(string url)
        {
            url = (url ?? "").Trim().TrimEnd('/');
            if (url.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
                url = url.Substring(0, url.Length - 4);
            if (url.Length == 0) return "";
            string[] parts = url.Split('/', ':', '\\');
            return parts[parts.Length - 1];
        }

        /// <summary>从下载链接推断文件名；GitHub 源码归档会补上仓库名，避免出现 main.zip 这种名字。</summary>
        public static string FileNameFromUrl(string url)
        {
            Uri uri;
            if (!Uri.TryCreate((url ?? "").Trim(), UriKind.Absolute, out uri)) return "download.bin";

            string path = Uri.UnescapeDataString(uri.AbsolutePath);
            string[] segs = path.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            string last = segs.Length > 0 ? segs[segs.Length - 1] : "";
            string host = uri.Host.ToLowerInvariant();

            string name;
            if (host == "codeload.github.com" && segs.Length >= 2)
            {
                // /user/repo/zip/refs/heads/main
                name = segs[1] + "-" + last + ".zip";
            }
            else if (host.EndsWith("github.com") && segs.Length >= 4 && segs[2] == "archive")
            {
                // /user/repo/archive/refs/heads/main.zip
                name = segs[1] + "-" + last;
            }
            else
            {
                name = last;
            }
            name = SanitizeFileName(name);
            return name.Length == 0 ? "download.bin" : name;
        }

        public static string SanitizeFileName(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name;
        }

        public static string FmtSize(double n)
        {
            string[] units = { "B", "KB", "MB", "GB" };
            int i = 0;
            while (n >= 1024 && i < units.Length - 1)
            {
                n /= 1024;
                i++;
            }
            return i == 0 ? ((long)n) + " B" : n.ToString("0.0") + " " + units[i];
        }

        public static bool IsInPath(string exe)
        {
            string path = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (string dir in path.Split(Path.PathSeparator))
            {
                try
                {
                    if (dir.Length > 0 && File.Exists(Path.Combine(dir.Trim().Trim('"'), exe)))
                        return true;
                }
                catch (ArgumentException)
                {
                }
            }
            return false;
        }

        // ---------- 输入框灰色提示文字 ----------
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);

        private const int EM_SETCUEBANNER = 0x1501;

        public static void SetCue(TextBox tb, string text)
        {
            if (tb.IsHandleCreated)
                SendMessage(tb.Handle, EM_SETCUEBANNER, (IntPtr)1, text);
            else
                tb.HandleCreated += (s, e) => SendMessage(tb.Handle, EM_SETCUEBANNER, (IntPtr)1, text);
        }
    }
}
