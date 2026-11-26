// 만화 보관함 — 새 판 확인·자동 업데이트, 설정 창
// GitHub 에 올린 최신 판(Release)을 확인해서, 더 새로우면 물어본 뒤 받아서 바꾸고 다시 켬.
// 보내는 정보는 없음. 받아 오는 것: 최신 판 번호·설명, 새 프로그램 zip.
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace ComicShelf
{
    static class Updater
    {
        public const string AppVersion = "5.4.1";            // 이 프로그램의 판
        public const string Owner = "Jajucity";                 // GitHub 계정 이름 (비어 있으면 업데이트 확인 안 함)
        public const string Repo = "comic-shelf";       // GitHub 저장소 이름
        public const string AssetName = "ComicShelf.zip";

        public static bool Configured { get { return Owner.Length > 0; } }
        public static string SiteUrl { get { return "https://" + Owner.ToLowerInvariant() + ".github.io/" + Repo + "/"; } }
        static string ApiUrl { get { return "https://api.github.com/repos/" + Owner + "/" + Repo + "/releases/latest"; } }
        static string ZipUrl(string tag) { return "https://github.com/" + Owner + "/" + Repo + "/releases/download/" + tag + "/" + AssetName; }

        public static bool AutoCheck
        {
            get { return Edits.Get("업데이트확인", "켬") != "끔"; }
            set { Edits.Set("업데이트확인", value ? "켬" : "끔"); }
        }

        class Latest { public string Tag; public Version Ver; public string Notes; }

        static Version Parse(string s)
        {
            var m = Regex.Match(s ?? "", @"\d+(\.\d+){0,3}");
            Version v;
            if (m.Success && Version.TryParse(m.Value.Contains(".") ? m.Value : m.Value + ".0", out v)) return v;
            return new Version(0, 0);
        }

        static WebClient Client()
        {
            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;   // TLS 1.2
            var wc = new WebClient();
            wc.Encoding = Encoding.UTF8;
            wc.Headers[HttpRequestHeader.UserAgent] = "ComicShelf-Updater";
            return wc;
        }

        static string JsonString(string json, string key)
        {
            var m = Regex.Match(json, "\"" + key + "\"\\s*:\\s*\"((?:\\\\.|[^\"\\\\])*)\"");
            if (!m.Success) return "";
            return Regex.Unescape(m.Groups[1].Value);
        }

        static Latest Fetch()
        {
            using (var wc = Client())
            {
                wc.Headers[HttpRequestHeader.Accept] = "application/vnd.github+json";
                string json = wc.DownloadString(ApiUrl);
                var l = new Latest();
                l.Tag = JsonString(json, "tag_name");
                l.Ver = Parse(l.Tag);
                l.Notes = JsonString(json, "body");
                return l;
            }
        }

        // 켤 때: 하루에 한 번만, 뒤에서 조용히 확인 → 새 판이 있을 때만 물어봄
        public static void CheckOnStart(Form owner)
        {
            CleanOld();
            if (!Configured || !AutoCheck) return;
            DateTime last;
            if (DateTime.TryParse(Edits.Get("업데이트마지막확인", ""), System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out last) && (DateTime.Now - last).TotalHours < 20) return;
            var t = new Thread(delegate()
            {
                Latest l = null;
                try { l = Fetch(); } catch { return; }   // 인터넷이 없으면 그냥 넘어감
                try
                {
                    owner.BeginInvoke((MethodInvoker)delegate
                    {
                        Edits.Set("업데이트마지막확인", DateTime.Now.ToString("s"));
                        if (l.Ver > Parse(AppVersion) && Edits.Get("업데이트건너뜀", "") != l.Tag) Offer(owner, l, true);
                    });
                }
                catch { }
            });
            t.IsBackground = true;
            t.Start();
        }

        // 설정 창의 '지금 확인하기'
        public static void CheckNow(Form owner)
        {
            if (!Configured) { MessageBox.Show(owner, "아직 업데이트를 받을 곳이 정해지지 않았어요.", "새 판 확인"); return; }
            Latest l = null;
            try
            {
                Busy.Run(owner, "새 판 확인하는 중", delegate(Action<int, int> p) { l = Fetch(); });
            }
            catch (Exception ex)
            {
                MessageBox.Show(owner, "새 판을 확인하지 못했어요. 인터넷 연결을 확인해 주세요.\n\n" + ex.Message, "새 판 확인");
                return;
            }
            Edits.Set("업데이트마지막확인", DateTime.Now.ToString("s"));
            if (l.Ver > Parse(AppVersion)) Offer(owner, l, false);
            else MessageBox.Show(owner, "지금 쓰고 있는 판(" + AppVersion + ")이 가장 새로운 판이에요.", "새 판 확인");
        }

        static void Offer(Form owner, Latest l, bool auto)
        {
            string notes = (l.Notes ?? "").Trim();
            if (notes.Length > 700) notes = notes.Substring(0, 700) + "…";
            var r = MessageBox.Show(owner,
                "새 판이 나왔어요!\n\n지금 판: " + AppVersion + "\n새 판: " + l.Tag.TrimStart('v', 'V') +
                (notes.Length > 0 ? "\n\n바뀐 점:\n" + notes : "") +
                "\n\n지금 바꿀까요? (만화는 그대로 남아요. 다 받으면 프로그램이 다시 켜져요)" +
                (auto ? "\n\n'아니요'를 누르면 이 판은 다시 묻지 않아요. (설정에서 언제든 받을 수 있어요)" : ""),
                "새 판", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
            if (r != DialogResult.Yes)
            {
                if (auto) Edits.Set("업데이트건너뜀", l.Tag);
                return;
            }
            Install(owner, l);
        }

        static void Install(Form owner, Latest l)
        {
            string exe = Application.ExecutablePath;
            string dir = Path.GetDirectoryName(exe);
            string tmpZip = Path.Combine(Path.GetTempPath(), "만화보관함_새판_" + Guid.NewGuid().ToString("N") + ".zip");
            string newExe = exe + ".new";
            try
            {
                Busy.Run(owner, "새 판 받는 중", delegate(Action<int, int> progress)
                {
                    using (var wc = Client())
                    {
                        var done = new ManualResetEvent(false);
                        Exception err = null;
                        wc.DownloadProgressChanged += delegate(object s, DownloadProgressChangedEventArgs e)
                        {
                            if (e.TotalBytesToReceive > 0) progress((int)(e.BytesReceived / 1024), (int)(e.TotalBytesToReceive / 1024));
                        };
                        wc.DownloadFileCompleted += delegate(object s, System.ComponentModel.AsyncCompletedEventArgs e) { err = e.Error; done.Set(); };
                        wc.DownloadFileAsync(new Uri(ZipUrl(l.Tag)), tmpZip);
                        done.WaitOne();
                        if (err != null) throw err;
                    }
                    // zip 안의 '만화 보관함.exe' 를 꺼냄
                    using (var zip = ZipFile.OpenRead(tmpZip))
                    {
                        var entry = zip.Entries.FirstOrDefault(e => e.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));
                        if (entry == null) throw new InvalidDataException("받은 파일 안에 프로그램이 없어요.");
                        if (File.Exists(newExe)) File.Delete(newExe);
                        entry.ExtractToFile(newExe);
                    }
                });
                // 켜져 있는 프로그램 파일은 지울 수 없지만 이름은 바꿀 수 있음 → 옛것은 .old 로, 새것을 제자리에
                string old = exe + ".old";
                if (File.Exists(old)) File.Delete(old);
                File.Move(exe, old);
                File.Move(newExe, exe);
                Process.Start(new ProcessStartInfo(exe) { WorkingDirectory = dir, UseShellExecute = true });
                Application.Exit();
            }
            catch (Exception ex)
            {
                // 실패하면 원래대로
                try { if (!File.Exists(exe) && File.Exists(exe + ".old")) File.Move(exe + ".old", exe); } catch { }
                MessageBox.Show(owner, "새 판을 받지 못했어요. 지금 판은 그대로예요.\n\n" + ex.Message, "새 판");
            }
            finally
            {
                try { if (File.Exists(tmpZip)) File.Delete(tmpZip); } catch { }
                try { if (File.Exists(newExe)) File.Delete(newExe); } catch { }
            }
        }

        // 업데이트 뒤 남은 옛 프로그램 파일 정리 (프로그램이 만든 것)
        static void CleanOld()
        {
            try
            {
                string old = Application.ExecutablePath + ".old";
                if (File.Exists(old)) File.Delete(old);
            }
            catch { }
        }
    }
}
