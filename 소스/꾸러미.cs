// 만화 보관함 — 만화 꾸러미 파일 (.comicpack) 로 다른 사람과 주고받기
// 꾸러미 = 만화 폴더를 통째로 담은 zip. 언제 봤는지 기록(기록.txt)은 빼고 담음.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.VisualBasic.FileIO;

namespace ComicShelf
{
    static class Pack
    {
        public const string Ext = ".comicpack";
        public const string Filter = "만화 꾸러미 (*.comicpack)|*.comicpack";
        const string Marker = "꾸러미.txt";
        static readonly string[] Skip = { "기록.txt", Marker };

        public static string SafeName(string title)
        {
            var bad = Path.GetInvalidFileNameChars();
            var s = new string((title ?? "").Select(ch => bad.Contains(ch) ? '_' : ch).ToArray()).Trim();
            return s.Length == 0 ? "만화" : s;
        }

        public static void Export(Comic c, string dest, Action<int, int> progress)
        {
            var files = Directory.GetFiles(c.Dir, "*", System.IO.SearchOption.AllDirectories)
                .Where(f => !Skip.Contains(Path.GetFileName(f)))
                .ToList();
            string tmp = dest + ".만드는중";
            try
            {
                using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write))
                using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
                {
                    var m = zip.CreateEntry(Marker);
                    using (var w = new StreamWriter(m.Open(), new UTF8Encoding(false))) w.Write("만화 보관함 꾸러미 1\n" + c.Title);
                    int i = 0;
                    foreach (var f in files)
                    {
                        string rel = f.Substring(c.Dir.Length).TrimStart('\\', '/').Replace('\\', '/');
                        // 그림·영상·음악은 이미 눌러 담겨 있어서 그대로 (빠름)
                        var k = Media.KindOf(f);
                        var level = (k == Kind.None) ? CompressionLevel.Optimal : CompressionLevel.NoCompression;
                        zip.CreateEntryFromFile(f, rel, level);
                        progress(++i, files.Count);
                    }
                }
                if (File.Exists(dest)) File.Delete(dest);   // 덮어쓰기는 저장 창에서 이미 물어봄
                File.Move(tmp, dest);
            }
            finally
            {
                if (File.Exists(tmp)) { try { File.Delete(tmp); } catch { } }   // 만들다 만 임시 파일
            }
        }

        public static Comic Import(string pack, Action<int, int> progress)
        {
            ZipArchive opened;
            try { opened = ZipFile.OpenRead(pack); }
            catch (InvalidDataException) { throw new InvalidDataException("만화 꾸러미 파일이 아니거나 망가진 파일이에요."); }
            using (var zip = opened)
            {
                if (zip.GetEntry("정보.txt") == null)
                    throw new InvalidDataException("만화 꾸러미 파일이 아니에요. (만화 정보가 없어요)");
                var entries = zip.Entries.Where(e => !e.FullName.EndsWith("/") && !Skip.Contains(Path.GetFileName(e.FullName))).ToList();
                string dir = Store.NewComicDir();
                string root = Path.GetFullPath(dir).TrimEnd('\\') + "\\";
                try
                {
                    int i = 0;
                    foreach (var e in entries)
                    {
                        string target = Path.GetFullPath(Path.Combine(dir, e.FullName.Replace('/', '\\')));
                        // 폴더 밖으로 나가는 이상한 경로는 넣지 않음
                        if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase)) { progress(++i, entries.Count); continue; }
                        Directory.CreateDirectory(Path.GetDirectoryName(target));
                        e.ExtractToFile(target, false);
                        progress(++i, entries.Count);
                    }
                    Directory.CreateDirectory(Path.Combine(dir, "회차"));
                }
                catch
                {
                    // 꺼내다 실패하면 만들던 폴더는 휴지통으로
                    try { FileSystem.DeleteDirectory(dir, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin); } catch { }
                    throw;
                }
                return Store.LoadComics().FirstOrDefault(c => string.Equals(Path.GetFullPath(c.Dir), Path.GetFullPath(dir), StringComparison.OrdinalIgnoreCase));
            }
        }
    }

    // 오래 걸리는 일을 하는 동안 보여 주는 작은 창 (진행 막대)
    static class Busy
    {
        public static void Run(IWin32Window owner, string title, Action<Action<int, int>> work)
        {
            Exception err = null;
            bool done = false;
            using (var f = new Form())
            {
                f.Text = title;
                f.FormBorderStyle = FormBorderStyle.FixedDialog;
                f.ControlBox = false;
                f.ShowInTaskbar = false;
                f.StartPosition = FormStartPosition.CenterParent;
                f.BackColor = Theme.Bar; f.ForeColor = Theme.Text; f.Font = Theme.F(10.5f);
                f.ClientSize = new Size(420, 110);
                var l = new Label();
                l.Text = title + "…";
                l.Location = new Point(16, 16); l.Size = new Size(388, 24);
                var bar = new ProgressBar();
                bar.Location = new Point(16, 50); bar.Size = new Size(388, 24);
                bar.Maximum = 1000;
                f.Controls.Add(l); f.Controls.Add(bar);
                f.FormClosing += delegate(object s, FormClosingEventArgs e) { if (!done) e.Cancel = true; };
                Action<int, int> report = delegate(int i, int n)
                {
                    try
                    {
                        f.BeginInvoke((MethodInvoker)delegate
                        {
                            bar.Value = n <= 0 ? 0 : Math.Min(1000, i * 1000 / n);
                            l.Text = title + "… (" + i + " / " + n + ")";
                        });
                    }
                    catch { }
                };
                f.Shown += delegate
                {
                    var t = new Thread(delegate()
                    {
                        try { work(report); }
                        catch (Exception ex) { err = ex; }
                        try { f.BeginInvoke((MethodInvoker)delegate { done = true; f.Close(); }); } catch { }
                    });
                    t.IsBackground = true;
                    t.Start();
                };
                f.ShowDialog(owner);
            }
            if (err != null) throw err;
        }
    }
}
