// 만화 보관함 — 만화 글씨 읽기 추가 기능 (manga-ocr: 세로 글씨·만화 글씨체에 강한 일본어 글자 읽기)
// 원할 때만 받음. 저장 위치는 C:\ProgramData\ComicShelf\manga-ocr (원드라이브 폴더가 아니라서 올라가지 않음)
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.VisualBasic.FileIO;

namespace ComicShelf
{
    static class MangaOcr
    {
        public const string SizeText = "받기 약 650MB, 설치 뒤 약 1.5GB";
        static readonly string[] PyVersions = { "3.13.12", "3.13.11", "3.13.9", "3.12.10" };

        public static string Dir { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "ComicShelf", "manga-ocr"); } }
        static string PyDir { get { return Path.Combine(Dir, "python"); } }
        static string Py { get { return Path.Combine(PyDir, "python.exe"); } }
        static string Server { get { return Path.Combine(Dir, "읽기.py"); } }
        static string DoneFile { get { return Path.Combine(Dir, "다 됨.txt"); } }
        static string LogFile { get { return Path.Combine(Dir, "설치 기록.txt"); } }

        public static bool Installed { get { return File.Exists(DoneFile) && File.Exists(Py); } }
        // 받아 두었고, 설정에서 끄지 않았으면 일본어 읽기에 씀
        public static bool Enabled
        {
            get { return Installed && Edits.Get("만화글씨읽기", "1") == "1"; }
            set { Edits.Set("만화글씨읽기", value ? "1" : "0"); }
        }

        const string ServerScript = @"import sys, os
sys.stdout.reconfigure(encoding='utf-8')
sys.stdin.reconfigure(encoding='utf-8')
from manga_ocr import MangaOcr
m = MangaOcr()
print('READY', flush=True)
if '--warmup' in sys.argv:
    sys.exit(0)
for line in sys.stdin:
    p = line.strip().lstrip('\ufeff').strip()
    if not p:
        continue
    try:
        t = m(p)
    except Exception as e:
        t = '__ERR__' + str(e)
    print(t.replace('\r', ' ').replace('\n', ' '), flush=True)
";

        // ── 설치 ── (뒤쪽 스레드에서 부름) report(단계, 자세히)
        public static volatile bool CancelRequested;
        static Process current;

        public static void Install(Action<string, string> report)
        {
            CancelRequested = false;
            if (report == null) report = delegate { };
            Directory.CreateDirectory(Dir);
            Log("=== 설치 시작 " + DateTime.Now);
            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;

            // 1. 파이썬 (작은 내장용 판)
            if (!File.Exists(Py))
            {
                string zip = Path.Combine(Dir, "python.zip");
                Exception last = null;
                foreach (var v in PyVersions)
                {
                    try
                    {
                        Download("https://www.python.org/ftp/python/" + v + "/python-" + v + "-embed-amd64.zip", zip, "1/5 파이썬 " + v + " 받는 중", report);
                        last = null;
                        break;
                    }
                    catch (Exception ex) { last = ex; if (CancelRequested) throw; }
                }
                if (last != null) throw new Exception("파이썬을 받지 못했어요. (" + last.Message + ")");
                if (Directory.Exists(PyDir)) Directory.Delete(PyDir, true);
                ZipFile.ExtractToDirectory(zip, PyDir);
                File.Delete(zip);
                // 내장용 파이썬은 site-packages 를 안 씀 → 켜기
                foreach (var pth in Directory.GetFiles(PyDir, "python*._pth"))
                {
                    string t = File.ReadAllText(pth).Replace("#import site", "import site");
                    File.WriteAllText(pth, t);
                }
            }
            Check();

            // 2. pip
            if (!File.Exists(Path.Combine(PyDir, "Scripts", "pip.exe")))
            {
                string gp = Path.Combine(Dir, "get-pip.py");
                Download("https://bootstrap.pypa.io/get-pip.py", gp, "2/5 설치 도구(pip) 받는 중", report);
                Run(Py, "\"" + gp + "\" --no-warn-script-location", "2/5 설치 도구(pip) 설치하는 중", report);
                File.Delete(gp);
            }
            Check();

            // 3. torch (그래픽카드 없이 도는 작은 판)
            Run(Py, "-m pip install --prefer-binary --no-warn-script-location torch --index-url https://download.pytorch.org/whl/cpu",
                "3/5 계산 도구(torch, 약 106MB) 받는 중", report);
            Check();

            // 4. manga-ocr
            Run(Py, "-m pip install --prefer-binary --no-warn-script-location manga-ocr \"transformers<5\"",
                "4/5 만화 글씨 읽기 프로그램 받는 중", report);
            Check();

            // 5. 모델 받기 (처음 한 번, 약 424MB) + 잘 도는지 확인
            File.WriteAllText(Server, ServerScript, new UTF8Encoding(false));
            string outp = Run(Py, "\"" + Server + "\" --warmup", "5/5 만화 글씨 모델(약 424MB) 받는 중", report);
            if (!outp.Contains("READY")) throw new Exception("만화 글씨 읽기를 시작하지 못했어요. 자세한 것은 '" + LogFile + "' 에 있어요.");
            File.WriteAllText(DoneFile, DateTime.Now.ToString("yyyy-MM-dd HH:mm"), Encoding.UTF8);
            Log("=== 설치 끝 " + DateTime.Now);
            report("다 됐어요", "");
        }

        static void Check() { if (CancelRequested) throw new OperationCanceledException("그만뒀어요."); }

        static void Log(string s)
        {
            try { File.AppendAllText(LogFile, s + "\r\n", Encoding.UTF8); } catch { }
        }

        static void Download(string url, string dest, string step, Action<string, string> report)
        {
            Log("받기: " + url);
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.UserAgent = "ComicShelf";
            req.Timeout = 60000;
            using (var res = (HttpWebResponse)req.GetResponse())
            using (var s = res.GetResponseStream())
            using (var f = File.Create(dest))
            {
                long total = res.ContentLength, got = 0;
                var buf = new byte[81920];
                int n; var last = DateTime.MinValue;
                while ((n = s.Read(buf, 0, buf.Length)) > 0)
                {
                    if (CancelRequested) throw new OperationCanceledException("그만뒀어요.");
                    f.Write(buf, 0, n); got += n;
                    if ((DateTime.Now - last).TotalMilliseconds > 300)
                    {
                        last = DateTime.Now;
                        report(step, total > 0 ? (got * 100 / total) + "%  (" + (got >> 20) + " / " + (total >> 20) + "MB)" : (got >> 20) + "MB");
                    }
                }
            }
        }

        // 다른 프로그램을 돌리고 나오는 글을 한 줄씩 알려 줌 (진행 막대는 \r 로 끝나서 글자 단위로 읽음)
        static string Run(string exe, string args, string step, Action<string, string> report)
        {
            Log("실행: " + Path.GetFileName(exe) + " " + args);
            report(step, "");
            var psi = new ProcessStartInfo(exe, args);
            psi.UseShellExecute = false; psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true; psi.RedirectStandardError = true;
            psi.StandardOutputEncoding = Encoding.UTF8; psi.StandardErrorEncoding = Encoding.UTF8;
            psi.WorkingDirectory = Dir;
            SetEnv(psi);
            var all = new StringBuilder();
            using (var p = Process.Start(psi))
            {
                current = p;
                Action<StreamReader> pump = delegate(StreamReader r)
                {
                    var line = new StringBuilder();
                    int c;
                    while ((c = r.Read()) >= 0)
                    {
                        if (c == '\r' || c == '\n')
                        {
                            string l = line.ToString().Trim();
                            line.Length = 0;
                            if (l.Length == 0) continue;
                            lock (all) all.AppendLine(l);
                            report(step, l.Length > 110 ? l.Substring(0, 110) + "…" : l);
                        }
                        else line.Append((char)c);
                    }
                    if (line.Length > 0) lock (all) all.AppendLine(line.ToString());
                };
                var t1 = new Thread(delegate() { pump(p.StandardOutput); });
                var t2 = new Thread(delegate() { pump(p.StandardError); });
                t1.IsBackground = t2.IsBackground = true;
                t1.Start(); t2.Start();
                p.WaitForExit();
                t1.Join(5000); t2.Join(5000);
                current = null;
                string text = all.ToString();
                Log(text.Length > 4000 ? "…" + text.Substring(text.Length - 4000) : text);
                Check();
                if (p.ExitCode != 0)
                    throw new Exception(step.Substring(step.IndexOf(' ') + 1) + " 단계에서 실패했어요. 인터넷 연결을 확인하고 다시 받아 보세요. (자세한 것: " + LogFile + ")");
                return text;
            }
        }

        static void SetEnv(ProcessStartInfo psi)
        {
            psi.EnvironmentVariables["HF_HOME"] = Path.Combine(Dir, "models");
            psi.EnvironmentVariables["PYTHONIOENCODING"] = "utf-8";
            psi.EnvironmentVariables["PYTHONNOUSERSITE"] = "1";
            psi.EnvironmentVariables["PIP_DISABLE_PIP_VERSION_CHECK"] = "1";
            psi.EnvironmentVariables["HF_HUB_DISABLE_TELEMETRY"] = "1";
        }

        public static void CancelInstall()
        {
            CancelRequested = true;
            try { var p = current; if (p != null && !p.HasExited) p.Kill(); } catch { }
        }

        // 지우기: 휴지통으로 (크면 윈도우가 바로 지울지 물어봄)
        public static void Remove()
        {
            Stop();
            if (Directory.Exists(Dir))
                FileSystem.DeleteDirectory(Dir, UIOption.AllDialogs, RecycleOption.SendToRecycleBin);
        }

        // ── 읽기 ── 한 번 켜 두고 계속 씀 (처음 켤 때 모델을 불러오느라 몇 초 걸림)
        static Process server;
        static readonly object gate = new object();

        static void Start()
        {
            if (server != null && !server.HasExited) return;
            File.WriteAllText(Server, ServerScript, new UTF8Encoding(false));
            var psi = new ProcessStartInfo(Py, "\"" + Server + "\"");
            psi.UseShellExecute = false; psi.CreateNoWindow = true;
            psi.RedirectStandardInput = true; psi.RedirectStandardOutput = true; psi.RedirectStandardError = true;
            psi.StandardOutputEncoding = Encoding.UTF8;
            psi.WorkingDirectory = Dir;
            SetEnv(psi);
            psi.EnvironmentVariables["HF_HUB_OFFLINE"] = "1";   // 이미 받은 모델만 씀
            server = Process.Start(psi);
            server.StandardInput.AutoFlush = true;
            // 안내 글(stderr)은 버림
            var err = server.StandardError;
            var t = new Thread(delegate() { try { while (err.ReadLine() != null) { } } catch { } });
            t.IsBackground = true; t.Start();
            string first = ReadLine(server, 120000);
            if (first != "READY")
            {
                Stop();
                throw new Exception("만화 글씨 읽기를 켜지 못했어요. 설정에서 다시 받아 보세요.");
            }
            Application.ApplicationExit += delegate { Stop(); };
        }

        static string ReadLine(Process p, int timeoutMs)
        {
            string line = null;
            var t = new Thread(delegate() { try { line = p.StandardOutput.ReadLine(); } catch { } });
            t.IsBackground = true; t.Start();
            if (!t.Join(timeoutMs)) { try { p.Kill(); } catch { } throw new Exception("만화 글씨 읽기가 너무 오래 걸려서 멈췄어요."); }
            return line;
        }

        public static void Stop()
        {
            lock (gate)
            {
                try { if (server != null && !server.HasExited) { server.StandardInput.Close(); if (!server.WaitForExit(2000)) server.Kill(); } } catch { }
                server = null;
            }
        }

        public static string Read(Image crop)
        {
            string tmp = Path.Combine(Path.GetTempPath(), "ComicShelf_ocr_" + Guid.NewGuid().ToString("N") + ".png");
            try
            {
                crop.Save(tmp, System.Drawing.Imaging.ImageFormat.Png);
                lock (gate)
                {
                    Start();
                    server.StandardInput.WriteLine(tmp);
                    string r = ReadLine(server, 60000);
                    if (r == null) { Stop(); throw new Exception("만화 글씨 읽기가 꺼졌어요. 다시 해 보세요."); }
                    if (r.StartsWith("__ERR__")) throw new Exception("만화 글씨를 읽지 못했어요. (" + r.Substring(7) + ")");
                    return r.Replace("．．．", "…").Replace("...", "…").Trim();
                }
            }
            finally { try { File.Delete(tmp); } catch { } }
        }
    }

    // ───────────────────────── 받기 창 ─────────────────────────
    class MangaOcrInstallForm : Form
    {
        readonly Label step, detail;
        readonly ProgressBar bar;
        readonly Button cancel;
        bool finished;
        public Exception Error;

        public MangaOcrInstallForm()
        {
            Text = "만화 글씨 읽기 받기";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Theme.Bar; ForeColor = Theme.Text; Font = Theme.F(10.5f);
            ClientSize = new Size(560, 190);
            step = new Label(); step.Location = new Point(18, 18); step.Size = new Size(524, 26); step.Font = Theme.F(11f, FontStyle.Bold);
            detail = new Label(); detail.Location = new Point(18, 52); detail.Size = new Size(524, 44); detail.ForeColor = Theme.SubText;
            bar = new ProgressBar(); bar.Location = new Point(18, 102); bar.Size = new Size(524, 20); bar.Style = ProgressBarStyle.Marquee;
            cancel = Theme.MakeButton("그만두기", false);
            cancel.Location = new Point(560 - 18 - 120, 140);
            Controls.Add(step); Controls.Add(detail); Controls.Add(bar); Controls.Add(cancel);
            step.Text = "준비 중…";
            cancel.Click += delegate
            {
                if (MessageBox.Show(this, "받기를 그만둘까요? (받은 것은 남아 있어서 다음에 이어서 받아요)", "만화 글씨 읽기", MessageBoxButtons.YesNo) == DialogResult.Yes)
                {
                    cancel.Enabled = false;
                    step.Text = "그만두는 중…";
                    MangaOcr.CancelInstall();
                }
            };
            FormClosing += delegate(object s, FormClosingEventArgs e) { if (!finished) e.Cancel = true; };
            Shown += delegate
            {
                var t = new Thread(delegate()
                {
                    try
                    {
                        MangaOcr.Install(delegate(string a, string b)
                        {
                            try { BeginInvoke((MethodInvoker)delegate { step.Text = a; detail.Text = b; }); } catch { }
                        });
                    }
                    catch (Exception ex) { Error = ex; }
                    try { BeginInvoke((MethodInvoker)delegate { finished = true; Close(); }); } catch { }
                });
                t.IsBackground = true;
                t.Start();
            };
        }
    }
}
