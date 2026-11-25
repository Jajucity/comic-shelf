// 만화 보관함 — 만화를 모아 두고 웹툰처럼 내려 보는 프로그램
// 빌드: 소스\만들기.ps1 (윈도우에 들어 있는 .NET Framework 4 컴파일러 사용, C# 5 문법)
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using Microsoft.VisualBasic.FileIO;

namespace ComicShelf
{
    static class Program
    {
        [DllImport("user32.dll")]
        static extern bool SetProcessDPIAware();

        [STAThread]
        static void Main()
        {
            try { SetProcessDPIAware(); } catch { }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.ThreadException += delegate(object s, System.Threading.ThreadExceptionEventArgs e)
            {
                MessageBox.Show("문제가 생겼어요.\n\n" + e.Exception.Message, "만화 보관함", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            };
            Application.Run(new MainForm());
        }
    }

    // ───────────────────────── 색·글꼴 ─────────────────────────
    static class Theme
    {
        public static readonly Color Back = Color.FromArgb(28, 29, 34);
        public static readonly Color Bar = Color.FromArgb(36, 38, 45);
        public static readonly Color Card = Color.FromArgb(46, 48, 57);
        public static readonly Color CardHover = Color.FromArgb(62, 65, 77);
        public static readonly Color Text = Color.FromArgb(236, 237, 241);
        public static readonly Color SubText = Color.FromArgb(160, 164, 176);
        public static readonly Color Accent = Color.FromArgb(255, 145, 70);
        public static readonly Color ViewBack = Color.FromArgb(18, 18, 20);
        public static readonly CultureInfo Ko = new CultureInfo("ko-KR");

        static readonly Dictionary<string, Font> fonts = new Dictionary<string, Font>();
        public static Font F(float size) { return F(size, FontStyle.Regular); }
        public static Font F(float size, FontStyle style)
        {
            string key = size + "|" + style;
            Font f;
            if (!fonts.TryGetValue(key, out f))
            {
                f = new Font("맑은 고딕", size, style);
                fonts[key] = f;
            }
            return f;
        }

        public static GraphicsPath Round(Rectangle r, int radius)
        {
            var p = new GraphicsPath();
            if (radius < 1) radius = 1;
            int d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
            if (d < 2) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static Button MakeButton(string text, bool accent)
        {
            var b = new Button();
            b.Text = text;
            b.AccessibleName = text;
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderSize = 0;
            b.BackColor = accent ? Accent : Card;
            b.ForeColor = accent ? Color.FromArgb(30, 20, 10) : Text;
            b.FlatAppearance.MouseOverBackColor = accent ? Color.FromArgb(255, 170, 110) : CardHover;
            b.Font = F(11f, FontStyle.Bold);
            b.Cursor = Cursors.Hand;
            b.AutoSize = true;
            b.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            b.Padding = new Padding(14, 6, 14, 6);
            b.UseVisualStyleBackColor = false;
            return b;
        }

        public static string Ago(DateTime t)
        {
            var d = DateTime.Now - t;
            if (d.TotalMinutes < 1) return "방금";
            if (d.TotalHours < 1) return (int)d.TotalMinutes + "분 전";
            if (t.Date == DateTime.Today) return (int)d.TotalHours + "시간 전";
            if (t.Date == DateTime.Today.AddDays(-1)) return "어제";
            if (d.TotalDays < 30) return (int)Math.Ceiling(d.TotalDays) + "일 전";
            return t.ToString("yyyy년 M월 d일", Ko);
        }

        public static string When(DateTime t)
        {
            if (t.Year != DateTime.Now.Year) return t.ToString("yyyy년 M월 d일 tt h:mm", Ko);
            return t.ToString("M월 d일 tt h:mm", Ko);
        }
    }

    static class Native
    {
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);

        // 글상자가 비어 있을 때 흐리게 보이는 안내 글
        public static void Cue(TextBox tb, string text)
        {
            EventHandler set = delegate { SendMessage(tb.Handle, 0x1501, (IntPtr)1, text); };
            if (tb.IsHandleCreated) set(tb, EventArgs.Empty);
            else tb.HandleCreated += set;
        }
    }

    // ───────────────────────── 파일 종류 ─────────────────────────
    enum Kind { None, Image, Gif, Video, Music }

    static class Media
    {
        public static readonly string[] ImageExts = { ".jpg", ".jpeg", ".png", ".bmp" };
        public static readonly string[] GifExts = { ".gif" };
        public static readonly string[] VideoExts = { ".mp4", ".m4v", ".mkv", ".avi", ".wmv", ".mov" };
        public static readonly string[] MusicExts = { ".mp3", ".wav", ".m4a", ".wma", ".flac", ".aac" };

        public static Kind KindOf(string path)
        {
            string e = Path.GetExtension(path).ToLowerInvariant();
            if (ImageExts.Contains(e)) return Kind.Image;
            if (GifExts.Contains(e)) return Kind.Gif;
            if (VideoExts.Contains(e)) return Kind.Video;
            if (MusicExts.Contains(e)) return Kind.Music;
            return Kind.None;
        }

        static string Pat(IEnumerable<string> exts) { return string.Join(";", exts.Select(x => "*" + x)); }

        public static string PictureFilter
        {
            get { return "그림 파일|" + Pat(ImageExts.Concat(GifExts)) + "|모든 파일|*.*"; }
        }

        public static string AddFilter
        {
            get
            {
                var all = ImageExts.Concat(GifExts).Concat(VideoExts).Concat(MusicExts);
                return "그림·영상·음악|" + Pat(all) + "|그림|" + Pat(ImageExts) + "|움직이는 그림 (gif)|" + Pat(GifExts)
                    + "|영상|" + Pat(VideoExts) + "|음악|" + Pat(MusicExts);
            }
        }

        // 영상의 가로·세로 크기 (윈도우 탐색기가 아는 값). 모르면 16:9
        public static Size VideoSize(string path)
        {
            try
            {
                dynamic sh = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application"));
                dynamic folder = sh.NameSpace(Path.GetDirectoryName(path));
                dynamic item = folder.ParseName(Path.GetFileName(path));
                object w = item.ExtendedProperty("System.Video.FrameWidth");
                object h = item.ExtendedProperty("System.Video.FrameHeight");
                if (w != null && h != null)
                {
                    int wi = Convert.ToInt32(w), hi = Convert.ToInt32(h);
                    if (wi > 0 && hi > 0) return new Size(wi, hi);
                }
            }
            catch { }
            return new Size(16, 9);
        }
    }

    // ───────────────────────── 그림 다루기 ─────────────────────────
    static class Pics
    {
        // 파일을 잠그지 않도록 메모리로 읽어서 엶
        public static Bitmap Load(string path)
        {
            byte[] bytes = File.ReadAllBytes(path);
            using (var ms = new MemoryStream(bytes))
            using (var img = Image.FromStream(ms))
            {
                var b = new Bitmap(img.Width, img.Height, PixelFormat.Format32bppArgb);
                using (var g = Graphics.FromImage(b)) g.DrawImage(img, 0, 0, img.Width, img.Height);
                return b;
            }
        }

        public static Size ReadSize(string path)
        {
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var img = Image.FromStream(fs, false, false))
            {
                return img.Size;
            }
        }

        public static Bitmap Scale(Image src, int w, int h)
        {
            if (w < 1) w = 1;
            if (h < 1) h = 1;
            var bmp = new Bitmap(w, h, PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.CompositingQuality = CompositingQuality.HighQuality;
                using (var ia = new ImageAttributes())
                {
                    ia.SetWrapMode(WrapMode.TileFlipXY); // 가장자리 번짐 막기
                    g.DrawImage(src, new Rectangle(0, 0, w, h), 0, 0, src.Width, src.Height, GraphicsUnit.Pixel, ia);
                }
            }
            return bmp;
        }

        // 가운데를 정사각형으로 잘라 size×size 로 만듦
        public static Bitmap Square(Image src, int size)
        {
            int side = Math.Min(src.Width, src.Height);
            int sx = (src.Width - side) / 2;
            int sy = (src.Height - side) / 2;
            var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                using (var ia = new ImageAttributes())
                {
                    ia.SetWrapMode(WrapMode.TileFlipXY);
                    g.DrawImage(src, new Rectangle(0, 0, size, size), sx, sy, side, side, GraphicsUnit.Pixel, ia);
                }
            }
            return bmp;
        }
    }

    // ───────────────────────── 저장소 (자료 폴더) ─────────────────────────
    class Comic
    {
        public string Dir;
        public string Title;
        public string Description;
        public string ThumbPath { get { return Path.Combine(Dir, "썸네일.png"); } }
        public string ThumbGif { get { return Path.Combine(Dir, "썸네일.gif"); } }
        // 실제로 보여 줄 썸네일 파일 (움직이는 gif 가 있으면 그것)
        public string ThumbFile { get { return File.Exists(ThumbGif) ? ThumbGif : File.Exists(ThumbPath) ? ThumbPath : null; } }
        public string EpisodesDir { get { return Path.Combine(Dir, "회차"); } }
    }

    static class Store
    {
        public static readonly string Root = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "자료");
        static string ComicsDir { get { return Path.Combine(Root, "만화"); } }
        static readonly Encoding Utf8 = new UTF8Encoding(false);

        public static List<Comic> LoadComics()
        {
            var list = new List<Comic>();
            if (!Directory.Exists(ComicsDir)) return list;
            foreach (var d in Directory.GetDirectories(ComicsDir).OrderBy(x => Path.GetFileName(x), StringComparer.Ordinal))
            {
                string info = Path.Combine(d, "정보.txt");
                if (!File.Exists(info)) continue;
                var lines = File.ReadAllText(info, Utf8).Replace("\r\n", "\n").Split('\n');
                var c = new Comic();
                c.Dir = d;
                c.Title = lines.Length > 0 ? lines[0] : "";
                c.Description = lines.Length > 1 ? string.Join("\r\n", lines.Skip(1)).Trim() : "";
                list.Add(c);
            }
            return list;
        }

        static void WriteInfo(Comic c)
        {
            File.WriteAllText(Path.Combine(c.Dir, "정보.txt"), c.Title + "\n" + c.Description, Utf8);
        }

        // 새 만화 폴더 (만든 시각 이름)
        public static string NewComicDir()
        {
            Directory.CreateDirectory(ComicsDir);
            string id = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
            string dir = Path.Combine(ComicsDir, id);
            int n = 1;
            while (Directory.Exists(dir)) { dir = Path.Combine(ComicsDir, id + "_" + n); n++; }
            Directory.CreateDirectory(dir);
            return dir;
        }

        public static Comic CreateComic(string title, string desc, Image thumb, string thumbGif)
        {
            string dir = NewComicDir();
            Directory.CreateDirectory(Path.Combine(dir, "회차"));
            var c = new Comic();
            c.Dir = dir;
            c.Title = title.Replace("\r", " ").Replace("\n", " ").Trim();
            c.Description = (desc ?? "").Trim();
            WriteInfo(c);
            if (thumbGif != null) File.Copy(thumbGif, c.ThumbGif, true);
            else if (thumb != null) thumb.Save(c.ThumbPath, ImageFormat.Png);
            return c;
        }

        // 제목·설명·썸네일 고치기 (newThumb 가 null 이면 썸네일은 그대로)
        public static void UpdateComic(Comic c, string title, string desc, Image newThumb, string newGif)
        {
            c.Title = title.Replace("\r", " ").Replace("\n", " ").Trim();
            c.Description = (desc ?? "").Trim();
            WriteInfo(c);
            if (newGif != null)
            {
                File.Copy(newGif, c.ThumbGif + ".새것", true);
                if (File.Exists(c.ThumbGif)) File.Delete(c.ThumbGif);
                File.Move(c.ThumbGif + ".새것", c.ThumbGif);
                if (File.Exists(c.ThumbPath)) File.Delete(c.ThumbPath);
            }
            else if (newThumb != null)
            {
                if (File.Exists(c.ThumbGif)) File.Delete(c.ThumbGif);
                string tmp = c.ThumbPath + ".새것";
                newThumb.Save(tmp, ImageFormat.Png);
                if (File.Exists(c.ThumbPath)) File.Delete(c.ThumbPath); // 프로그램이 만든 작은 썸네일이라 바로 바꿈
                File.Move(tmp, c.ThumbPath);
            }
        }

        public static List<int> Episodes(Comic c)
        {
            var list = new List<int>();
            if (!Directory.Exists(c.EpisodesDir)) return list;
            foreach (var d in Directory.GetDirectories(c.EpisodesDir))
            {
                int k;
                if (int.TryParse(Path.GetFileName(d), out k) && k > 0) list.Add(k);
            }
            list.Sort();
            return list;
        }

        public static string EpisodeDir(Comic c, int n) { return Path.Combine(c.EpisodesDir, n.ToString()); }

        public static int AddEpisode(Comic c)
        {
            var eps = Episodes(c);
            int next = eps.Count == 0 ? 1 : eps[eps.Count - 1] + 1;
            Directory.CreateDirectory(EpisodeDir(c, next));
            return next;
        }

        // 화 안의 그림·움직이는 그림·영상 (넣은 순서)
        public static List<string> EpisodeItems(Comic c, int n)
        {
            string d = EpisodeDir(c, n);
            if (!Directory.Exists(d)) return new List<string>();
            return Directory.GetFiles(d)
                .Where(f =>
                {
                    var k = Media.KindOf(f);
                    return (k == Kind.Image || k == Kind.Gif || k == Kind.Video) && !Path.GetFileName(f).StartsWith("음악");
                })
                .OrderBy(f => Path.GetFileName(f), StringComparer.Ordinal)
                .ToList();
        }

        // 원본을 자료 폴더로 복사 (원본을 지워도 남도록)
        public static string AddItem(Comic c, int n, string src)
        {
            string d = EpisodeDir(c, n);
            Directory.CreateDirectory(d);
            int max = 0;
            foreach (var f in EpisodeItems(c, n))
            {
                int k;
                if (int.TryParse(Path.GetFileNameWithoutExtension(f), out k) && k > max) max = k;
            }
            string ext = Path.GetExtension(src).ToLowerInvariant();
            if (ext == ".jpeg") ext = ".jpg";
            string dest = Path.Combine(d, (max + 1).ToString("0000") + ext);
            File.Copy(src, dest, false);
            return dest;
        }

        public static string MusicPath(Comic c, int n)
        {
            string d = EpisodeDir(c, n);
            if (!Directory.Exists(d)) return null;
            return Directory.GetFiles(d, "음악.*").FirstOrDefault(f => Media.KindOf(f) == Kind.Music);
        }

        public static void SetMusic(Comic c, int n, string src)
        {
            string old = MusicPath(c, n);
            if (old != null) Recycle(old);
            string dest = Path.Combine(EpisodeDir(c, n), "음악" + Path.GetExtension(src).ToLowerInvariant());
            File.Copy(src, dest, true);
        }

        // 그림 고치기로 그린 것(브러시·자막)은 원본과 따로 '편집' 폴더에 둠
        public static string OverlayPath(string itemPath)
        {
            return Path.Combine(Path.GetDirectoryName(itemPath), "편집", Path.GetFileNameWithoutExtension(itemPath) + ".png");
        }

        // ── 언제 봤는지 ──
        static string ViewsFile(Comic c) { return Path.Combine(c.Dir, "기록.txt"); }

        public static Dictionary<int, DateTime> LoadViews(Comic c)
        {
            var d = new Dictionary<int, DateTime>();
            string f = ViewsFile(c);
            if (!File.Exists(f)) return d;
            foreach (var line in File.ReadAllLines(f, Utf8))
            {
                var p = line.Split('\t');
                int n; DateTime t;
                if (p.Length == 2 && int.TryParse(p[0], out n) &&
                    DateTime.TryParseExact(p[1], "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out t))
                    d[n] = t;
            }
            return d;
        }

        static void SaveViews(Comic c, Dictionary<int, DateTime> d)
        {
            File.WriteAllLines(ViewsFile(c),
                d.OrderBy(k => k.Key).Select(k => k.Key + "\t" + k.Value.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)), Utf8);
        }

        public static void MarkViewed(Comic c, int n)
        {
            var d = LoadViews(c);
            d[n] = DateTime.Now;
            SaveViews(c, d);
        }

        // ── 지우기는 모두 휴지통으로 ──
        public static void Recycle(string file)
        {
            FileSystem.DeleteFile(file, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
        }

        public static void DeleteComic(Comic c)
        {
            FileSystem.DeleteDirectory(c.Dir, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
        }

        public static void DeleteEpisode(Comic c, int n)
        {
            FileSystem.DeleteDirectory(EpisodeDir(c, n), UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
            // 뒤의 화 번호를 하나씩 당김 (3화를 지우면 4화가 3화로)
            foreach (int k in Episodes(c))
            {
                if (k > n) Directory.Move(EpisodeDir(c, k), EpisodeDir(c, k - 1));
            }
            var views = LoadViews(c);
            var moved = new Dictionary<int, DateTime>();
            foreach (var kv in views)
            {
                if (kv.Key < n) moved[kv.Key] = kv.Value;
                else if (kv.Key > n) moved[kv.Key - 1] = kv.Value;
            }
            SaveViews(c, moved);
        }

        public static void DeleteItem(string path)
        {
            Recycle(path);
            foreach (var f in new[] { OverlayPath(path), Edits.BasePath(path), Edits.ObjsPath(path) })
                if (File.Exists(f)) Recycle(f);
        }
    }

    // ───────────────────────── 주 창 ─────────────────────────
    class MainForm : Form
    {
        Control current;
        bool full;
        FormWindowState prevState;
        Rectangle prevBounds;

        public MainForm()
        {
            Text = "만화 보관함";
            BackColor = Theme.Back;
            ForeColor = Theme.Text;
            Font = Theme.F(10f);
            Size = new Size(1180, 820);
            MinimumSize = new Size(760, 560);
            StartPosition = FormStartPosition.CenterScreen;
            KeyPreview = true;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            ShowLibrary();
            Shown += delegate { BeginInvoke((MethodInvoker)delegate { GuideForm.ShowFirstTime(this); Updater.CheckOnStart(this); }); };
        }

        // F11 또는 Alt+Enter = 전체 화면, Esc = 전체 화면 끝
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.F1)
            {
                GuideForm.Open(this);
                return true;
            }
            if (keyData == Keys.F11 || keyData == (Keys.Alt | Keys.Enter))
            {
                ToggleFullScreen();
                return true;
            }
            if (keyData == Keys.Escape && full)
            {
                ToggleFullScreen();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        public bool IsFullScreen { get { return full; } }

        public void ToggleFullScreen()
        {
            SuspendLayout();
            if (!full)
            {
                prevState = WindowState;
                prevBounds = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
                FormBorderStyle = FormBorderStyle.None;
                WindowState = FormWindowState.Normal;
                WindowState = FormWindowState.Maximized;
                full = true;
            }
            else
            {
                FormBorderStyle = FormBorderStyle.Sizable;
                WindowState = FormWindowState.Normal;
                Bounds = prevBounds;
                if (prevState == FormWindowState.Maximized) WindowState = FormWindowState.Maximized;
                full = false;
            }
            ResumeLayout();
        }

        void ShowPage(Control page)
        {
            SuspendLayout();
            var old = current;
            current = page;
            page.Dock = DockStyle.Fill;
            Controls.Add(page);
            if (old != null) { Controls.Remove(old); old.Dispose(); }
            ResumeLayout();
            var vp = page as ViewerPage;
            if (vp != null) vp.FocusView(); else page.Focus();
        }

        public void ShowLibrary() { ShowPage(new LibraryPage(this)); }
        public void ShowEpisodes(Comic c) { ShowPage(new EpisodesPage(this, c)); }
        public void ShowViewer(Comic c, int ep) { ShowPage(new ViewerPage(this, c, ep)); }
    }

    // 위쪽 막대 (뒤로 가기·제목·오른쪽 버튼)
    class TopBar : Panel
    {
        public readonly FlowLayoutPanel LeftArea;
        public readonly FlowLayoutPanel RightArea;
        public readonly Panel Center;
        public TopBar()
        {
            Dock = DockStyle.Top;
            Height = 60;
            BackColor = Theme.Bar;
            Padding = new Padding(12, 10, 12, 10);
            RightArea = new FlowLayoutPanel();
            RightArea.Dock = DockStyle.Right;
            RightArea.AutoSize = true;
            RightArea.WrapContents = false;
            RightArea.BackColor = Theme.Bar;
            LeftArea = new FlowLayoutPanel();
            LeftArea.Dock = DockStyle.Left;
            LeftArea.AutoSize = true;
            LeftArea.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            LeftArea.WrapContents = false;
            LeftArea.BackColor = Theme.Bar;
            Center = new Panel();
            Center.BackColor = Theme.Bar;
            Center.Padding = new Padding(12, 0, 12, 0);
            Controls.Add(Center);
            Controls.Add(LeftArea);
            Controls.Add(RightArea);
            LeftArea.SizeChanged += delegate { PlaceCenter(); };
            RightArea.SizeChanged += delegate { PlaceCenter(); };
        }

        // 가운데 칸은 왼쪽·오른쪽 칸 사이에 정확히 맞춤 (겹치지 않게)
        void PlaceCenter()
        {
            if (LeftArea == null || RightArea == null || Center == null) return;   // 만드는 중
            int l = LeftArea.Right, r = RightArea.Left;
            Center.Bounds = new Rectangle(l, Padding.Top, Math.Max(0, r - l), Math.Max(0, Height - Padding.Vertical));
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            PlaceCenter();
        }

        public Label AddTitle(string text)
        {
            var l = new Label();
            l.Text = text;
            l.AutoSize = true;
            l.Font = Theme.F(15f, FontStyle.Bold);
            l.ForeColor = Theme.Text;
            l.Margin = new Padding(10, 4, 0, 0);
            l.UseMnemonic = false;
            LeftArea.Controls.Add(l);
            return l;
        }
    }

    // ───────────────────────── 정사각형 카드 ─────────────────────────
    class Tile : Control
    {
        public Image Picture;
        string caption = "";
        public string Caption { get { return caption; } set { caption = value ?? ""; Text = caption; Invalidate(); } }
        public bool IsAdd;
        public string Placeholder;
        bool hover;
        const int Box = 180;

        bool animating;
        void Frame(object s, EventArgs e) { if (IsHandleCreated && !IsDisposed) { try { BeginInvoke((MethodInvoker)Invalidate); } catch { } } }

        public void SetPicture(Image img)
        {
            StopAnim();
            Picture = img;
            if (img != null && ImageAnimator.CanAnimate(img)) { ImageAnimator.Animate(img, Frame); animating = true; }
            Invalidate();
        }

        void StopAnim()
        {
            if (animating && Picture != null) ImageAnimator.StopAnimate(Picture, Frame);
            animating = false;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) StopAnim();
            base.Dispose(disposing);
        }

        public Tile()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Size = new Size(Box + 20, Box + 76);
            Margin = new Padding(8);
            BackColor = Theme.Back;
            Cursor = Cursors.Hand;
            AccessibleRole = AccessibleRole.PushButton;
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            var box = new Rectangle(10, 10, Box, Box);
            using (var path = Theme.Round(box, 14))
            {
                using (var br = new SolidBrush(hover ? Theme.CardHover : Theme.Card)) g.FillPath(br, path);
                if (IsAdd)
                {
                    using (var pen = new Pen(Theme.Accent, 8f))
                    {
                        pen.StartCap = LineCap.Round; pen.EndCap = LineCap.Round;
                        int cx = box.X + box.Width / 2, cy = box.Y + box.Height / 2;
                        g.DrawLine(pen, cx - 30, cy, cx + 30, cy);
                        g.DrawLine(pen, cx, cy - 30, cx, cy + 30);
                    }
                }
                else if (Picture != null)
                {
                    if (animating) ImageAnimator.UpdateFrames(Picture);
                    g.SetClip(path);
                    g.DrawImage(Picture, box, Anim.SquareSrc(Picture), GraphicsUnit.Pixel);
                    g.ResetClip();
                }
                else if (Placeholder != null)
                {
                    TextRenderer.DrawText(g, Placeholder, Theme.F(11f), box, Theme.SubText,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
                }
                else
                {
                    string first = Caption.Length > 0 ? Caption.Substring(0, 1) : "?";
                    TextRenderer.DrawText(g, first, Theme.F(48f, FontStyle.Bold), box, Theme.SubText,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                }
                if (hover)
                {
                    using (var pen = new Pen(Theme.Accent, 3f)) g.DrawPath(pen, path);
                }
            }
            var tr = new Rectangle(6, Box + 18, Width - 12, Height - Box - 20);
            TextRenderer.DrawText(g, IsAdd ? "만화 추가" : Caption, Theme.F(11f, FontStyle.Bold), tr,
                IsAdd ? Theme.Accent : Theme.Text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }
    }

    // ───────────────────────── 1. 만화 목록 ─────────────────────────
    class LibraryPage : Panel
    {
        readonly MainForm main;
        readonly FlowLayoutPanel flow;
        readonly TextBox search;
        readonly Label empty;
        readonly List<KeyValuePair<Tile, Comic>> tiles = new List<KeyValuePair<Tile, Comic>>();

        public LibraryPage(MainForm main)
        {
            this.main = main;
            BackColor = Theme.Back;
            flow = new FlowLayoutPanel();
            flow.Dock = DockStyle.Fill;
            flow.AutoScroll = true;
            flow.Padding = new Padding(18);
            flow.BackColor = Theme.Back;
            // 처음 만들 때(작은 크기) 잡힌 스크롤 범위가 남지 않도록 크기가 바뀌면 다시 잡음
            flow.SizeChanged += delegate
            {
                flow.PerformLayout();
                if (flow.IsHandleCreated) flow.BeginInvoke((MethodInvoker)delegate { if (!flow.IsDisposed) flow.PerformLayout(); });
            };
            Controls.Add(flow);

            empty = new Label();
            empty.AutoSize = true;
            empty.ForeColor = Theme.SubText;
            empty.Font = Theme.F(12f);
            empty.Margin = new Padding(20, 90, 0, 0);
            empty.Visible = false;
            empty.UseMnemonic = false;

            var bar = new TopBar();
            bar.AddTitle("만화 보관함");
            search = new TextBox();
            search.Width = 280;
            search.Font = Theme.F(12f);
            search.BackColor = Theme.Card;
            search.ForeColor = Theme.Text;
            search.BorderStyle = BorderStyle.FixedSingle;
            search.Margin = new Padding(0, 4, 4, 0);
            search.AccessibleName = "만화 검색";
            Native.Cue(search, "만화 검색 (제목·설명)");
            search.TextChanged += delegate { ApplyFilter(); };
            var importBtn = Theme.MakeButton("꾸러미로 추가", false);
            importBtn.Margin = new Padding(0, 0, 12, 0);
            new ToolTip().SetToolTip(importBtn, "다른 사람이 보내 준 만화 꾸러미 파일(.comicpack)로 만화를 추가해요");
            importBtn.Click += delegate { ImportPack(); };
            var guideBtn = Theme.MakeButton("사용법", false);
            guideBtn.Margin = new Padding(0, 0, 8, 0);
            new ToolTip().SetToolTip(guideBtn, "어떻게 쓰는지, 어떤 기능이 있는지 알려 줘요 (F1)");
            guideBtn.Click += delegate { GuideForm.Open(FindForm()); };
            var setBtn = Theme.MakeButton("설정", false);
            setBtn.Margin = new Padding(0, 0, 8, 0);
            setBtn.Click += delegate { using (var sf = new SettingsForm()) sf.ShowDialog(FindForm()); };
            bar.RightArea.Controls.Add(setBtn);
            bar.RightArea.Controls.Add(guideBtn);
            bar.RightArea.Controls.Add(importBtn);
            bar.RightArea.Controls.Add(search);
            Controls.Add(bar);
            Reload();
        }

        void Reload()
        {
            flow.SuspendLayout();
            foreach (Control c in flow.Controls.Cast<Control>().ToList())
            {
                if (c == empty) continue;
                var t = c as Tile;
                var pic = t != null ? t.Picture : null;
                c.Dispose();
                if (pic != null) pic.Dispose();
            }
            flow.Controls.Clear();
            tiles.Clear();

            var add = new Tile();
            add.IsAdd = true;
            add.Text = "만화 추가";
            add.AccessibleName = "만화 추가";
            add.MouseClick += delegate(object s, MouseEventArgs e) { if (e.Button == MouseButtons.Left) AddComic(); };
            flow.Controls.Add(add);

            foreach (var comic in Store.LoadComics())
            {
                var t = new Tile();
                t.Caption = comic.Title;
                t.AccessibleName = comic.Title;
                if (comic.ThumbFile != null)
                {
                    try { t.SetPicture(Anim.LoadAny(comic.ThumbFile)); } catch { }
                }
                var c = comic;
                t.MouseClick += delegate(object s, MouseEventArgs e)
                {
                    if (e.Button == MouseButtons.Left) main.ShowEpisodes(c);
                };
                var menu = new ContextMenuStrip();
                menu.Font = Theme.F(10.5f);
                menu.Items.Add("정보 고치기 (제목·썸네일·설명)", null, delegate { EditComic(c); });
                menu.Items.Add(new ToolStripSeparator());
                menu.Items.Add("꾸러미 파일로 내보내기 (나눠 주기)", null, delegate { ExportPack(c); });
                menu.Items.Add(new ToolStripSeparator());
                menu.Items.Add("이 만화 지우기 (휴지통으로)", null, delegate { DeleteComic(c); });
                t.ContextMenuStrip = menu;
                flow.Controls.Add(t);
                tiles.Add(new KeyValuePair<Tile, Comic>(t, c));
            }
            flow.Controls.Add(empty);
            ApplyFilter();
            flow.ResumeLayout();
        }

        static string Norm(string s)
        {
            return new string((s ?? "").Where(ch => !char.IsWhiteSpace(ch)).ToArray()).ToLowerInvariant();
        }

        // 띄어쓰기는 무시하고 제목이나 설명에 들어 있으면 보여 줌
        void ApplyFilter()
        {
            string q = Norm(search.Text);
            int shown = 0;
            flow.SuspendLayout();
            foreach (var pair in tiles)
            {
                bool ok = q.Length == 0 || Norm(pair.Value.Title).Contains(q) || Norm(pair.Value.Description).Contains(q);
                pair.Key.Visible = ok;
                if (ok) shown++;
            }
            empty.Text = "'" + search.Text.Trim() + "'(으)로 찾은 만화가 없어요.";
            empty.Visible = q.Length > 0 && shown == 0;
            flow.ResumeLayout();
        }

        void AddComic()
        {
            using (var dlg = new ComicDialog(null))
            {
                if (dlg.ShowDialog(FindForm()) == DialogResult.OK)
                {
                    Store.CreateComic(dlg.ComicTitle, dlg.ComicDescription, dlg.Thumbnail, dlg.ThumbGifPath);
                    Reload();
                }
            }
        }

        void EditComic(Comic c)
        {
            using (var dlg = new ComicDialog(c))
            {
                if (dlg.ShowDialog(FindForm()) == DialogResult.OK)
                {
                    try { Store.UpdateComic(c, dlg.ComicTitle, dlg.ComicDescription, dlg.ThumbChanged ? dlg.Thumbnail : null, dlg.ThumbChanged ? dlg.ThumbGifPath : null); }
                    catch (Exception ex) { MessageBox.Show(FindForm(), "고치지 못했어요.\n" + ex.Message, "정보 고치기"); }
                    Reload();
                }
            }
        }

        void ExportPack(Comic c)
        {
            using (var sfd = new SaveFileDialog())
            {
                sfd.Title = "만화 꾸러미 파일로 내보내기";
                sfd.Filter = Pack.Filter;
                sfd.FileName = Pack.SafeName(c.Title) + Pack.Ext;
                sfd.InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                sfd.OverwritePrompt = true;
                if (sfd.ShowDialog(FindForm()) != DialogResult.OK) return;
                try
                {
                    Busy.Run(FindForm(), "꾸러미 만드는 중", delegate(Action<int, int> progress) { Pack.Export(c, sfd.FileName, progress); });
                    long mb = new FileInfo(sfd.FileName).Length / (1024 * 1024);
                    MessageBox.Show(FindForm(), "꾸러미 파일을 만들었어요. (" + Math.Max(1, mb) + "MB)\n\n" + sfd.FileName +
                        "\n\n이 파일을 메신저나 USB 로 건네면, 받은 사람이 만화 보관함 첫 화면의 '꾸러미로 추가'로 넣을 수 있어요.\n(언제 봤는지 기록은 빼고 담았어요.)", "꾸러미 내보내기");
                }
                catch (Exception ex) { MessageBox.Show(FindForm(), "꾸러미를 만들지 못했어요.\n" + ex.Message, "꾸러미 내보내기"); }
            }
        }

        void ImportPack()
        {
            using (var ofd = new OpenFileDialog())
            {
                ofd.Title = "받은 만화 꾸러미 파일 고르기";
                ofd.Filter = Pack.Filter;
                if (ofd.ShowDialog(FindForm()) != DialogResult.OK) return;
                Comic added = null;
                try
                {
                    Busy.Run(FindForm(), "꾸러미에서 만화 꺼내는 중", delegate(Action<int, int> progress) { added = Pack.Import(ofd.FileName, progress); });
                }
                catch (Exception ex) { MessageBox.Show(FindForm(), "만화를 추가하지 못했어요.\n" + ex.Message, "꾸러미로 추가"); return; }
                search.Text = "";
                Reload();
                if (added != null) MessageBox.Show(FindForm(), "'" + added.Title + "' 만화를 추가했어요.", "꾸러미로 추가");
            }
        }

        void DeleteComic(Comic c)
        {
            var r = MessageBox.Show(FindForm(),
                "'" + c.Title + "' 만화를 지울까요?\n\n모든 회차와 그림이 함께 휴지통으로 가요.\n(휴지통에서 되살릴 수 있어요.)",
                "만화 지우기", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
            if (r != DialogResult.Yes) return;
            try { Store.DeleteComic(c); }
            catch (Exception ex) { MessageBox.Show(FindForm(), "지우지 못했어요.\n" + ex.Message, "만화 지우기"); }
            Reload();
        }
    }

    // ───────────────────────── 만화 추가·정보 고치기 창 ─────────────────────────
    class ComicDialog : Form
    {
        public string ComicTitle { get { return titleBox.Text.Trim(); } }
        public string ComicDescription { get { return descBox.Text.Trim(); } }
        public Bitmap Thumbnail;
        public string ThumbGifPath;     // 움직이는 gif 를 고르면 그 파일
        Image animThumb;
        public bool ThumbChanged;

        readonly Tile thumbTile;
        readonly TextBox titleBox;
        readonly TextBox descBox;

        public ComicDialog(Comic existing)
        {
            Text = existing == null ? "만화 추가" : "만화 정보 고치기";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Theme.Bar;
            ForeColor = Theme.Text;
            Font = Theme.F(10.5f);
            ClientSize = new Size(640, 360);

            thumbTile = new Tile();
            thumbTile.Caption = "썸네일 (정사각형)";
            thumbTile.Placeholder = "여기를 눌러\n그림 고르기";
            thumbTile.BackColor = Theme.Bar;
            thumbTile.Location = new Point(14, 14);
            thumbTile.AccessibleName = "썸네일 고르기";
            thumbTile.Text = "썸네일 고르기";
            thumbTile.MouseClick += delegate(object s, MouseEventArgs e) { if (e.Button == MouseButtons.Left) PickThumb(); };
            Controls.Add(thumbTile);

            var l1 = MakeLabel("만화 제목", new Point(240, 22));
            titleBox = new TextBox();
            titleBox.Location = new Point(240, 50);
            titleBox.Width = 380;
            titleBox.Font = Theme.F(12f);
            titleBox.AccessibleName = "만화 제목";
            Controls.Add(titleBox);

            var l2 = MakeLabel("설명 (적고 싶으면)", new Point(240, 96));
            descBox = new TextBox();
            descBox.Location = new Point(240, 124);
            descBox.Size = new Size(380, 150);
            descBox.Multiline = true;
            descBox.ScrollBars = ScrollBars.Vertical;
            descBox.AccessibleName = "설명";
            Controls.Add(descBox);
            Controls.Add(l1); Controls.Add(l2);

            if (existing != null)
            {
                titleBox.Text = existing.Title;
                descBox.Text = existing.Description;
                if (existing.ThumbFile != null)
                {
                    try { animThumb = Anim.LoadAny(existing.ThumbFile); thumbTile.SetPicture(animThumb); } catch { }
                }
            }

            var save = Theme.MakeButton("저장", true);
            save.Location = new Point(436, 300);
            save.Click += delegate { Save(); };
            Controls.Add(save);
            var cancel = Theme.MakeButton("취소", false);
            cancel.Location = new Point(536, 300);
            cancel.DialogResult = DialogResult.Cancel;
            Controls.Add(cancel);
            CancelButton = cancel;
            Shown += delegate
            {
                cancel.Left = ClientSize.Width - 20 - cancel.Width;
                save.Left = cancel.Left - 10 - save.Width;
                titleBox.Focus();
                titleBox.SelectionStart = titleBox.Text.Length;
            };
        }

        Label MakeLabel(string text, Point p)
        {
            var l = new Label();
            l.Text = text; l.AutoSize = true; l.Location = p;
            l.ForeColor = Theme.SubText;
            return l;
        }

        void PickThumb()
        {
            using (var ofd = new OpenFileDialog())
            {
                ofd.Title = "썸네일로 쓸 그림 고르기";
                ofd.Filter = Media.PictureFilter;
                if (ofd.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    var img = Anim.LoadAny(ofd.FileName);
                    thumbTile.SetPicture(null);
                    if (animThumb != null) { animThumb.Dispose(); animThumb = null; }
                    if (Thumbnail != null) { Thumbnail.Dispose(); Thumbnail = null; }
                    ThumbGifPath = null;
                    if (ImageAnimator.CanAnimate(img))
                    {
                        animThumb = img;            // 움직이는 gif 는 그대로 (보일 때 가운데를 정사각형으로)
                        ThumbGifPath = ofd.FileName;
                        thumbTile.SetPicture(img);
                    }
                    else
                    {
                        Thumbnail = Pics.Square(img, 360);
                        img.Dispose();
                        thumbTile.SetPicture(Thumbnail);
                    }
                    ThumbChanged = true;
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "이 그림은 열 수 없어요.\n(jpg, png, gif, bmp 그림을 골라 주세요.)\n\n" + ex.Message, "썸네일");
                }
            }
        }

        void Save()
        {
            if (ComicTitle.Length == 0)
            {
                MessageBox.Show(this, "만화 제목을 적어 주세요.", Text);
                titleBox.Focus();
                return;
            }
            DialogResult = DialogResult.OK;
            Close();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                thumbTile.SetPicture(null);
                if (Thumbnail != null) { Thumbnail.Dispose(); Thumbnail = null; }
                if (animThumb != null) { animThumb.Dispose(); animThumb = null; }
            }
            base.Dispose(disposing);
        }
    }

    // ───────────────────────── 2. 회차 목록 ─────────────────────────
    class EpisodeRow : Control
    {
        public string Main = "";
        public string Time = "";
        public bool Recent;
        public string Sub = "";
        bool hover;
        public EpisodeRow()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Height = 58;
            Cursor = Cursors.Hand;
            BackColor = Theme.Back;
            Margin = new Padding(0, 0, 0, 8);
            AccessibleRole = AccessibleRole.PushButton;
        }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new Rectangle(0, 0, Width - 1, Height - 1);
            using (var path = Theme.Round(r, 10))
            using (var br = new SolidBrush(hover ? Theme.CardHover : Theme.Card))
            {
                g.FillPath(br, path);
                if (hover || Recent)
                    using (var pen = new Pen(Recent && !hover ? Color.FromArgb(150, Theme.Accent) : Theme.Accent, 2f)) g.DrawPath(pen, path);
            }
            TextRenderer.DrawText(g, Main, Theme.F(13f, FontStyle.Bold), new Rectangle(20, 0, 90, Height), Theme.Text,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
            if (Time.Length > 0)
                TextRenderer.DrawText(g, Time, Theme.F(10f, Recent ? FontStyle.Bold : FontStyle.Regular), new Rectangle(110, 0, Math.Max(10, Width - 300), Height),
                    Recent ? Theme.Accent : Theme.SubText, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
            TextRenderer.DrawText(g, Sub, Theme.F(10f), new Rectangle(0, 0, Width - 20, Height), Theme.SubText,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Right);
        }
    }

    class EpisodesPage : Panel
    {
        readonly MainForm main;
        readonly Comic comic;
        readonly FlowLayoutPanel list;
        readonly TextBox search;
        readonly Button sortBtn;
        readonly Label none;
        bool newestFirst;
        Image thumb;
        bool thumbAnim;
        Panel infoPanel;

        public EpisodesPage(MainForm main, Comic comic)
        {
            this.main = main;
            this.comic = comic;
            BackColor = Theme.Back;

            list = new FlowLayoutPanel();
            list.Dock = DockStyle.Fill;
            list.FlowDirection = FlowDirection.TopDown;
            list.WrapContents = false;
            list.AutoScroll = true;
            list.Padding = new Padding(24, 8, 24, 24);
            list.BackColor = Theme.Back;
            list.Resize += delegate { FitRows(); };
            Controls.Add(list);

            var info = new Panel();
            info.Dock = DockStyle.Top;
            info.Height = 196;
            info.BackColor = Theme.Back;
            infoPanel = info;
            if (comic.ThumbFile != null) { try { thumb = Anim.LoadAny(comic.ThumbFile); } catch { } }
            if (thumb != null && ImageAnimator.CanAnimate(thumb)) { ImageAnimator.Animate(thumb, ThumbFrame); thumbAnim = true; }
            info.Paint += delegate(object s, PaintEventArgs e) { PaintInfo(e.Graphics, info); };
            info.Resize += delegate { info.Invalidate(); };

            var add = Theme.MakeButton("+  새 화 만들기", true);
            add.Location = new Point(196, 140);
            add.Click += delegate
            {
                Store.AddEpisode(comic);
                Reload();
                if (list.Controls.Count > 0) list.ScrollControlIntoView(list.Controls[list.Controls.Count - 1]);
            };
            info.Controls.Add(add);

            // 화 검색·정렬
            newestFirst = Edits.Get("회차정렬", "오래된") == "최근";
            search = new TextBox();
            search.Font = Theme.F(11.5f);
            search.Width = 180;
            search.BackColor = Theme.Card;
            search.ForeColor = Theme.Text;
            search.BorderStyle = BorderStyle.FixedSingle;
            search.AccessibleName = "화 검색";
            Native.Cue(search, "화 검색 (예: 3)");
            search.TextChanged += delegate { Reload(); };
            sortBtn = Theme.MakeButton("", false);
            sortBtn.Click += delegate
            {
                newestFirst = !newestFirst;
                Edits.Set("회차정렬", newestFirst ? "최근" : "오래된");
                Reload();
            };
            info.Controls.Add(search);
            info.Controls.Add(sortBtn);
            info.Resize += delegate { PlaceTools(add); };
            add.SizeChanged += delegate { PlaceTools(add); };
            Controls.Add(info);

            none = new Label();
            none.AutoSize = true;
            none.ForeColor = Theme.SubText;
            none.Font = Theme.F(11.5f);
            none.Margin = new Padding(4, 20, 0, 0);
            none.UseMnemonic = false;

            var bar = new TopBar();
            var back = Theme.MakeButton("←  만화 목록", false);
            back.Click += delegate { main.ShowLibrary(); };
            bar.LeftArea.Controls.Add(back);
            Controls.Add(bar);

            Disposed += delegate
            {
                if (thumbAnim) ImageAnimator.StopAnimate(thumb, ThumbFrame);
                if (thumb != null) thumb.Dispose();
            };
            Reload();
        }

        void ThumbFrame(object s, EventArgs e)
        {
            if (infoPanel != null && infoPanel.IsHandleCreated && !infoPanel.IsDisposed)
            {
                try { infoPanel.BeginInvoke((MethodInvoker)delegate { infoPanel.Invalidate(new Rectangle(24, 18, 156, 156)); }); } catch { }
            }
        }

        void PlaceTools(Control add)
        {
            int y = add.Top + (add.Height - search.Height) / 2;
            search.Location = new Point(add.Right + 16, y);
            sortBtn.Location = new Point(search.Right + 10, add.Top);
        }

        void PaintInfo(Graphics g, Control info)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            var box = new Rectangle(24, 18, 156, 156);
            using (var path = Theme.Round(box, 12))
            {
                using (var br = new SolidBrush(Theme.Card)) g.FillPath(br, path);
                if (thumb != null)
                {
                    if (thumbAnim) ImageAnimator.UpdateFrames(thumb);
                    g.SetClip(path);
                    g.DrawImage(thumb, box, Anim.SquareSrc(thumb), GraphicsUnit.Pixel);
                    g.ResetClip();
                }
                else
                {
                    string first = comic.Title.Length > 0 ? comic.Title.Substring(0, 1) : "?";
                    TextRenderer.DrawText(g, first, Theme.F(40f, FontStyle.Bold), box, Theme.SubText,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                }
            }
            int x = 196, w = Math.Max(100, info.Width - x - 24);
            TextRenderer.DrawText(g, comic.Title, Theme.F(18f, FontStyle.Bold), new Rectangle(x, 18, w, 40), Theme.Text,
                TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            if (comic.Description.Length > 0)
                TextRenderer.DrawText(g, comic.Description, Theme.F(10.5f), new Rectangle(x, 62, w, 72), Theme.SubText,
                    TextFormatFlags.Left | TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }

        static string Contents(Comic comic, int ep)
        {
            var items = Store.EpisodeItems(comic, ep);
            int pics = items.Count(f => Media.KindOf(f) != Kind.Video);
            int vids = items.Count - pics;
            var parts = new List<string>();
            if (pics > 0) parts.Add("그림 " + pics + "장");
            if (vids > 0) parts.Add("영상 " + vids + "개");
            if (Store.MusicPath(comic, ep) != null) parts.Add("음악");
            return parts.Count == 0 ? "비어 있음" : string.Join(" · ", parts);
        }

        void Reload()
        {
            list.SuspendLayout();
            foreach (Control c in list.Controls.Cast<Control>().ToList()) if (c != none) c.Dispose();
            list.Controls.Clear();
            sortBtn.Text = newestFirst ? "정렬: 최근 화부터" : "정렬: 1화부터";
            string q = new string(search.Text.Where(char.IsDigit).ToArray());
            var views = Store.LoadViews(comic);
            int latest = -1;
            DateTime latestT = DateTime.MinValue;
            foreach (var kv in views) if (kv.Value > latestT) { latestT = kv.Value; latest = kv.Key; }

            var all = Store.Episodes(comic);
            var shown = all.Where(n => q.Length == 0 || n.ToString().Contains(q)).ToList();
            if (newestFirst) shown.Reverse();
            foreach (int ep in shown)
            {
                var row = new EpisodeRow();
                row.Main = ep + "화";
                row.AccessibleName = ep + "화";
                row.Text = ep + "화";
                row.Sub = Contents(comic, ep);
                DateTime seen;
                if (views.TryGetValue(ep, out seen))
                {
                    row.Recent = ep == latest;
                    row.Time = (row.Recent ? "● 최근에 본 화 · " : "본 날 · ") + Theme.Ago(seen) + "  (" + Theme.When(seen) + ")";
                }
                int n = ep;
                row.MouseClick += delegate(object s, MouseEventArgs e)
                {
                    if (e.Button == MouseButtons.Left) main.ShowViewer(comic, n);
                };
                var menu = new ContextMenuStrip();
                menu.Font = Theme.F(10.5f);
                menu.Items.Add("이 화 지우기 (휴지통으로)", null, delegate { DeleteEpisode(n); });
                row.ContextMenuStrip = menu;
                list.Controls.Add(row);
            }
            if (search.Text.Trim().Length > 0 && shown.Count == 0)
            {
                none.Text = "\u0027" + search.Text.Trim() + "\u0027(으)로 찾은 화가 없어요.";
                list.Controls.Add(none);
            }
            FitRows();
            list.ResumeLayout();
        }

        void FitRows()
        {
            int w = list.ClientSize.Width - list.Padding.Horizontal - 2;
            if (w < 200) w = 200;
            foreach (Control c in list.Controls) if (c is EpisodeRow) c.Width = w;
        }

        void DeleteEpisode(int n)
        {
            var r = MessageBox.Show(FindForm(),
                n + "화를 지울까요?\n\n이 화의 그림이 휴지통으로 가고, 뒤의 화 번호는 하나씩 당겨져요.\n(휴지통에서 되살릴 수 있어요.)",
                "화 지우기", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
            if (r != DialogResult.Yes) return;
            try { Store.DeleteEpisode(comic, n); }
            catch (Exception ex) { MessageBox.Show(FindForm(), "지우지 못했어요.\n" + ex.Message, "화 지우기"); }
            Reload();
        }
    }
}
