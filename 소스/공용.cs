// 만화 보관함 — 여러 화면이 같이 쓰는 것: 고친 그림 파일 위치, 설정, 자막·도형 그리기, 작은 조작 도구
using System;
using System.Collections.Generic;
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

namespace ComicShelf
{
    // ───────────────────────── 고친 그림 파일·설정 ─────────────────────────
    static class Edits
    {
        static readonly Encoding Utf8 = new UTF8Encoding(false);

        // 원본 그림을 지우개로 지운 판 (원본 파일은 그대로 둠)
        public static string BasePath(string itemPath)
        {
            return Path.Combine(Path.GetDirectoryName(itemPath), "편집", Path.GetFileNameWithoutExtension(itemPath) + "_원본.png");
        }

        // 자막·도형 (옮기고 고칠 수 있게 글로 저장)
        public static string ObjsPath(string itemPath)
        {
            return Path.Combine(Path.GetDirectoryName(itemPath), "편집", Path.GetFileNameWithoutExtension(itemPath) + ".txt");
        }

        public static bool HasAny(string itemPath)
        {
            return File.Exists(Store.OverlayPath(itemPath)) || File.Exists(BasePath(itemPath)) || File.Exists(ObjsPath(itemPath));
        }

        // 원본(또는 지운 판) + 브러시 + 자막·도형을 합친 그림
        public static Bitmap Compose(string itemPath)
        {
            string bp = BasePath(itemPath);
            var b = Pics.Load(File.Exists(bp) ? bp : itemPath);
            string ov = Store.OverlayPath(itemPath);
            string op = ObjsPath(itemPath);
            if (!File.Exists(ov) && !File.Exists(op)) return b;
            using (var g = Graphics.FromImage(b))
            {
                if (File.Exists(ov))
                {
                    try { using (var o = Pics.Load(ov)) g.DrawImage(o, 0, 0, b.Width, b.Height); } catch { }
                }
                if (File.Exists(op))
                {
                    try
                    {
                        g.SmoothingMode = SmoothingMode.AntiAlias;
                        foreach (var o in Ink.Load(File.ReadAllText(op, Utf8))) Ink.Draw(g, o);
                    }
                    catch { }
                }
            }
            return b;
        }

        // ── 설정 (자료\설정.txt) ──
        static Dictionary<string, string> settings;
        static string SettingsFile { get { return Path.Combine(Store.Root, "설정.txt"); } }

        static void LoadSettings()
        {
            if (settings != null) return;
            settings = new Dictionary<string, string>();
            try
            {
                if (File.Exists(SettingsFile))
                    foreach (var line in File.ReadAllLines(SettingsFile, Utf8))
                    {
                        int i = line.IndexOf('=');
                        if (i > 0) settings[line.Substring(0, i)] = line.Substring(i + 1);
                    }
            }
            catch { }
        }

        public static string Get(string key, string def)
        {
            LoadSettings();
            string v;
            return settings.TryGetValue(key, out v) ? v : def;
        }

        public static int GetInt(string key, int def)
        {
            int v;
            return int.TryParse(Get(key, ""), out v) ? v : def;
        }

        public static void Set(string key, string value)
        {
            LoadSettings();
            settings[key] = value;
            try
            {
                Directory.CreateDirectory(Store.Root);
                File.WriteAllLines(SettingsFile, settings.Select(kv => kv.Key + "=" + kv.Value), Utf8);
            }
            catch { }
        }

        // 스포이드로 모은 색
        public static List<Color> LoadColors()
        {
            var list = new List<Color>();
            foreach (var p in Get("모은색", "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int argb;
                if (int.TryParse(p, out argb)) list.Add(Color.FromArgb(argb));
            }
            return list;
        }

        public static void SaveColors(List<Color> colors)
        {
            Set("모은색", string.Join(",", colors.Select(c => c.ToArgb().ToString(CultureInfo.InvariantCulture))));
        }
    }

    // ───────────────────────── 자막·도형 ─────────────────────────
    enum ShapeKind { Rect, RoundRect, Ellipse, Triangle, Diamond, Star, Line, Arrow, BubbleOval, BubbleRect }

    class Obj
    {
        public string Type = "text";     // text = 자막, shape = 도형
        public ShapeKind Shape;
        public RectangleF R;             // 자막: 위치(왼쪽 위) / 칸 자막: 칸 / 도형: 범위 (선·화살표는 시작→끝)
        public string Text = "";
        public float FontPx = 32;
        public Color Color = Color.Black;
        public Color Fill = Color.White;
        public bool Filled;              // 도형: 안쪽 채우기 / 칸 자막: 흰 칸으로 덮기
        public bool Boxed;               // 칸 자막(번역 자막)
        public bool Outline;             // 글자 테두리
        public Color OutlineColor = Color.White;
        public float Width = 4;          // 도형 선 굵기

        public bool IsText { get { return Type == "text"; } }
        public bool IsLine { get { return Type == "shape" && (Shape == ShapeKind.Line || Shape == ShapeKind.Arrow); } }

        public Obj Clone() { return (Obj)MemberwiseClone(); }
    }

    static class Ink
    {
        static readonly Graphics measure = Graphics.FromImage(new Bitmap(1, 1));
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static Font MakeFont(float px) { return new Font("맑은 고딕", Math.Max(4f, px), FontStyle.Bold, GraphicsUnit.Pixel); }

        public static RectangleF Norm(RectangleF r)
        {
            return RectangleF.FromLTRB(Math.Min(r.Left, r.Right), Math.Min(r.Top, r.Bottom), Math.Max(r.Left, r.Right), Math.Max(r.Top, r.Bottom));
        }

        // 자막이 칸 안에 들어가도록 글자 크기를 줄임
        static float FitPx(Obj o, StringFormat fmt)
        {
            float px = o.FontPx;
            var r = Norm(o.R);
            while (px > 8)
            {
                using (var f = MakeFont(px))
                {
                    var m = measure.MeasureString(o.Text, f, (int)Math.Max(1, r.Width), fmt);
                    if (m.Height <= r.Height && m.Width <= r.Width) break;
                }
                px = Math.Max(8, px - Math.Max(1f, px * 0.08f));
            }
            return px;
        }

        public static RectangleF Bounds(Obj o)
        {
            if (o.IsText && !o.Boxed)
            {
                using (var f = MakeFont(o.FontPx))
                {
                    var sz = measure.MeasureString(o.Text.Length == 0 ? " " : o.Text, f);
                    return new RectangleF(o.R.X, o.R.Y, sz.Width, sz.Height);
                }
            }
            var r = Norm(o.R);
            if (o.IsLine) r.Inflate(o.Width + 4, o.Width + 4);
            return r;
        }

        public static void Draw(Graphics g, Obj o)
        {
            var oldSmooth = g.SmoothingMode;
            var oldText = g.TextRenderingHint;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAlias;
            try
            {
                if (o.IsText) DrawText(g, o);
                else DrawShape(g, o);
            }
            finally
            {
                g.SmoothingMode = oldSmooth;
                g.TextRenderingHint = oldText;
            }
        }

        static void DrawText(Graphics g, Obj o)
        {
            if (o.Boxed)
            {
                var r = Norm(o.R);
                if (o.Filled) using (var br = new SolidBrush(o.Fill)) g.FillRectangle(br, r);
                if (o.Text.Trim().Length == 0) return;
                using (var fmt = new StringFormat())
                {
                    fmt.Alignment = StringAlignment.Center;
                    fmt.LineAlignment = StringAlignment.Center;
                    float px = FitPx(o, fmt);
                    using (var f = MakeFont(px))
                    using (var path = new GraphicsPath())
                    {
                        path.AddString(o.Text, f.FontFamily, (int)FontStyle.Bold, px, r, fmt);
                        PaintTextPath(g, path, o, px);
                    }
                }
            }
            else
            {
                if (o.Text.Length == 0) return;
                using (var f = MakeFont(o.FontPx))
                using (var path = new GraphicsPath())
                {
                    path.AddString(o.Text, f.FontFamily, (int)FontStyle.Bold, o.FontPx, new PointF(o.R.X, o.R.Y), StringFormat.GenericDefault);
                    PaintTextPath(g, path, o, o.FontPx);
                }
            }
        }

        static void PaintTextPath(Graphics g, GraphicsPath path, Obj o, float px)
        {
            if (o.Outline)
            {
                using (var pen = new Pen(o.OutlineColor, Math.Max(2f, px / 6f)))
                {
                    pen.LineJoin = LineJoin.Round;
                    g.DrawPath(pen, path);
                }
            }
            using (var br = new SolidBrush(o.Color)) g.FillPath(br, path);
        }

        static void AddRound(GraphicsPath p, RectangleF r, float rad)
        {
            float d = Math.Max(1f, Math.Min(rad * 2, Math.Min(r.Width, r.Height)));
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
        }

        public static GraphicsPath ShapePath(ShapeKind k, RectangleF r)
        {
            var p = new GraphicsPath(FillMode.Winding);
            float x = r.X, y = r.Y, w = Math.Max(1, r.Width), h = Math.Max(1, r.Height);
            switch (k)
            {
                case ShapeKind.Rect: p.AddRectangle(new RectangleF(x, y, w, h)); break;
                case ShapeKind.RoundRect: AddRound(p, new RectangleF(x, y, w, h), Math.Min(w, h) * 0.18f); break;
                case ShapeKind.Ellipse: p.AddEllipse(x, y, w, h); break;
                case ShapeKind.Triangle:
                    p.AddPolygon(new[] { new PointF(x + w / 2, y), new PointF(x + w, y + h), new PointF(x, y + h) }); break;
                case ShapeKind.Diamond:
                    p.AddPolygon(new[] { new PointF(x + w / 2, y), new PointF(x + w, y + h / 2), new PointF(x + w / 2, y + h), new PointF(x, y + h / 2) }); break;
                case ShapeKind.Star:
                    {
                        var pts = new PointF[10];
                        for (int i = 0; i < 10; i++)
                        {
                            double a = -Math.PI / 2 + i * Math.PI / 5;
                            float rr = i % 2 == 0 ? 0.5f : 0.2f;
                            pts[i] = new PointF(x + w / 2 + (float)Math.Cos(a) * w * rr, y + h / 2 + (float)Math.Sin(a) * h * rr * 1.05f + h * 0.03f);
                        }
                        p.AddPolygon(pts);
                        break;
                    }
                case ShapeKind.BubbleOval:
                    p.AddEllipse(x, y, w, h * 0.8f);
                    p.AddPolygon(new[] { new PointF(x + w * 0.26f, y + h * 0.62f), new PointF(x + w * 0.16f, y + h), new PointF(x + w * 0.44f, y + h * 0.74f) });
                    break;
                case ShapeKind.BubbleRect:
                    AddRound(p, new RectangleF(x, y, w, h * 0.8f), Math.Min(w, h * 0.8f) * 0.2f);
                    p.AddPolygon(new[] { new PointF(x + w * 0.22f, y + h * 0.7f), new PointF(x + w * 0.16f, y + h), new PointF(x + w * 0.4f, y + h * 0.7f) });
                    break;
                default: p.AddRectangle(new RectangleF(x, y, w, h)); break;
            }
            return p;
        }

        static void DrawShape(Graphics g, Obj o)
        {
            if (o.IsLine)
            {
                using (var pen = new Pen(o.Color, Math.Max(1f, o.Width)))
                {
                    pen.StartCap = LineCap.Round;
                    pen.EndCap = LineCap.Round;
                    if (o.Shape == ShapeKind.Arrow) pen.CustomEndCap = new AdjustableArrowCap(4, 4, true);
                    g.DrawLine(pen, o.R.X, o.R.Y, o.R.Right, o.R.Bottom);
                }
                return;
            }
            var r = Norm(o.R);
            bool bubble = o.Shape == ShapeKind.BubbleOval || o.Shape == ShapeKind.BubbleRect;
            using (var pen = new Pen(o.Color, Math.Max(1f, o.Width)))
            using (var br = new SolidBrush(o.Fill))
            {
                pen.LineJoin = LineJoin.Round;
                if (bubble)
                {
                    // 말풍선: 몸과 꼬리를 따로 그림. 채울 때는 테두리를 두껍게 그린 뒤 안쪽을 채워 몸과 꼬리 사이 선을 없앰
                    float x = r.X, y = r.Y, w = Math.Max(1, r.Width), h = Math.Max(1, r.Height);
                    var body = new GraphicsPath();
                    if (o.Shape == ShapeKind.BubbleOval) body.AddEllipse(x, y, w, h * 0.8f);
                    else AddRound(body, new RectangleF(x, y, w, h * 0.8f), Math.Min(w, h * 0.8f) * 0.2f);
                    var a = new PointF(x + w * 0.24f, y + h * (o.Shape == ShapeKind.BubbleOval ? 0.66f : 0.72f));
                    var tip = new PointF(x + w * 0.16f, y + h);
                    var c = new PointF(x + w * 0.42f, y + h * (o.Shape == ShapeKind.BubbleOval ? 0.74f : 0.72f));
                    using (body)
                    using (var tail = new GraphicsPath())
                    {
                        if (o.Filled)
                        {
                            tail.AddPolygon(new[] { a, tip, c });
                            using (var pen2 = new Pen(o.Color, Math.Max(1f, o.Width) * 2))
                            {
                                pen2.LineJoin = LineJoin.Round;
                                g.DrawPath(pen2, body);
                                g.DrawPath(pen2, tail);
                            }
                            g.FillPath(br, body);
                            g.FillPath(br, tail);
                        }
                        else
                        {
                            tail.AddLines(new[] { a, tip, c });
                            g.DrawPath(pen, body);
                            g.DrawPath(pen, tail);
                        }
                    }
                    return;
                }
                using (var path = ShapePath(o.Shape, r))
                {
                    if (o.Filled) g.FillPath(br, path);
                    if (o.Width > 0) g.DrawPath(pen, path);
                }
            }
        }

        // 도형 단추에 그리는 작은 그림
        public static void DrawIcon(Graphics g, ShapeKind k, Rectangle box, Color c)
        {
            var o = new Obj();
            o.Type = "shape"; o.Shape = k; o.Color = c; o.Width = 2; o.Fill = c;
            if (k == ShapeKind.Line || k == ShapeKind.Arrow) o.R = new RectangleF(box.X + 2, box.Bottom - 3, box.Width - 4, -(box.Height - 6));
            else o.R = new RectangleF(box.X + 1, box.Y + 1, box.Width - 2, box.Height - 2);
            Draw(g, o);
        }

        // ── 글로 저장·읽기 ──
        static string Esc(string s)
        {
            return (s ?? "").Replace("\\", "\\\\").Replace("\t", "\\t").Replace("\r", "").Replace("\n", "\\n");
        }

        static string Unesc(string s)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] == '\\' && i + 1 < s.Length)
                {
                    char c = s[++i];
                    sb.Append(c == 'n' ? '\n' : c == 't' ? '\t' : c);
                }
                else sb.Append(s[i]);
            }
            return sb.ToString().Replace("\n", "\r\n");
        }

        public static string Save(List<Obj> objs)
        {
            var sb = new StringBuilder();
            foreach (var o in objs)
            {
                sb.Append(string.Join("\t", new[]
                {
                    o.Type, o.Shape.ToString(),
                    o.R.X.ToString(Inv), o.R.Y.ToString(Inv), o.R.Width.ToString(Inv), o.R.Height.ToString(Inv),
                    o.FontPx.ToString(Inv), o.Color.ToArgb().ToString(Inv), o.Fill.ToArgb().ToString(Inv),
                    o.Filled ? "1" : "0", o.Boxed ? "1" : "0", o.Outline ? "1" : "0", o.Width.ToString(Inv), Esc(o.Text),
                    o.OutlineColor.ToArgb().ToString(Inv)
                }));
                sb.Append('\n');
            }
            return sb.ToString();
        }

        public static List<Obj> Load(string text)
        {
            var list = new List<Obj>();
            foreach (var line in (text ?? "").Split('\n'))
            {
                var p = line.TrimEnd('\r').Split('\t');
                if (p.Length < 14) continue;
                try
                {
                    var o = new Obj();
                    o.Type = p[0];
                    o.Shape = (ShapeKind)Enum.Parse(typeof(ShapeKind), p[1]);
                    o.R = new RectangleF(float.Parse(p[2], Inv), float.Parse(p[3], Inv), float.Parse(p[4], Inv), float.Parse(p[5], Inv));
                    o.FontPx = float.Parse(p[6], Inv);
                    o.Color = Color.FromArgb(int.Parse(p[7], Inv));
                    o.Fill = Color.FromArgb(int.Parse(p[8], Inv));
                    o.Filled = p[9] == "1"; o.Boxed = p[10] == "1"; o.Outline = p[11] == "1";
                    o.Width = float.Parse(p[12], Inv);
                    o.Text = Unesc(p[13]);
                    if (p.Length > 14) o.OutlineColor = Color.FromArgb(int.Parse(p[14], Inv));
                    list.Add(o);
                }
                catch { }
            }
            return list;
        }
    }

    static class Anim
    {
        // gif 는 움직이도록 그대로(스트림을 계속 둠), 나머지는 보통 그림
        public static Image LoadAny(string path)
        {
            if (Media.KindOf(path) == Kind.Gif)
            {
                var img = Image.FromStream(new MemoryStream(File.ReadAllBytes(path)));
                if (ImageAnimator.CanAnimate(img)) return img;
                var b = new Bitmap(img); img.Dispose(); return b;
            }
            return Pics.Load(path);
        }

        // 가운데 정사각형 부분
        public static RectangleF SquareSrc(Image img)
        {
            float side = Math.Min(img.Width, img.Height);
            return new RectangleF((img.Width - side) / 2f, (img.Height - side) / 2f, side, side);
        }
    }

    // ───────────────────────── 영상 첫 장면 (윈도우 탐색기 썸네일) ─────────────────────────
    static class ShellThumb
    {
        [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IShellItemImageFactory
        {
            [PreserveSig]
            int GetImage(SIZE size, int flags, out IntPtr phbm);
        }

        [StructLayout(LayoutKind.Sequential)]
        struct SIZE { public int cx; public int cy; }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
        static extern void SHCreateItemFromParsingName(string path, IntPtr pbc,
            [MarshalAs(UnmanagedType.LPStruct)] Guid riid, [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory ppv);

        [DllImport("gdi32.dll")]
        static extern bool DeleteObject(IntPtr h);

        public static Bitmap Get(string path, int size)
        {
            try
            {
                IShellItemImageFactory f;
                SHCreateItemFromParsingName(path, IntPtr.Zero, typeof(IShellItemImageFactory).GUID, out f);
                SIZE s; s.cx = size; s.cy = size;
                IntPtr h;
                int hr = f.GetImage(s, 0x8, out h);   // 0x8 = 썸네일만
                Marshal.ReleaseComObject(f);
                if (hr != 0 || h == IntPtr.Zero) return null;
                try { using (var b = Image.FromHbitmap(h)) return new Bitmap(b); }
                finally { DeleteObject(h); }
            }
            catch { return null; }
        }
    }

    // ───────────────────────── 끌어서 조절하는 막대 ─────────────────────────
    class Slider : Control
    {
        double value;
        bool hover;
        public bool Dragging;
        public Color FillColor = Theme.Accent;
        public event Action Changed;     // 끄는 동안 계속
        public event Action Released;    // 손을 뗐을 때

        public Slider()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Height = 22;
            Cursor = Cursors.Hand;
        }

        public double Value { get { return value; } }

        public void SetValue(double v)
        {
            v = Math.Max(0, Math.Min(1, v));
            if (Math.Abs(v - value) < 0.0005) return;
            value = v;
            Invalidate();
        }

        void FromX(int x)
        {
            value = Math.Max(0, Math.Min(1, (x - 7) / (double)Math.Max(1, Width - 14)));
            Invalidate();
            if (Changed != null) Changed();
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            Dragging = true;
            Capture = true;
            FromX(e.X);
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (Dragging) FromX(e.X);
        }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (!Dragging) return;
            Dragging = false;
            Capture = false;
            if (Released != null) Released();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int cy = Height / 2;
            int x0 = 7, x1 = Width - 7;
            int xv = x0 + (int)Math.Round((x1 - x0) * value);
            using (var br = new SolidBrush(Color.FromArgb(90, 255, 255, 255))) g.FillRectangle(br, x0, cy - 2, x1 - x0, 4);
            using (var br = new SolidBrush(FillColor)) g.FillRectangle(br, x0, cy - 2, xv - x0, 4);
            int rad = hover || Dragging ? 7 : 5;
            using (var br = new SolidBrush(Color.White)) g.FillEllipse(br, xv - rad, cy - rad, rad * 2, rad * 2);
        }
    }

    // ───────────────────────── 그림 단추 (재생·멈춤·소리) ─────────────────────────
    enum IconKind { Play, Pause, Speaker, Mute, FullOn, FullOff }

    class IconButton : Control
    {
        public IconKind Kind;
        bool hover;

        public IconButton(IconKind kind)
        {
            Kind = kind;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            Size = new Size(36, 36);
            Cursor = Cursors.Hand;
        }

        public void SetKind(IconKind k) { if (k != Kind) { Kind = k; Invalidate(); } }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            if (hover) using (var br = new SolidBrush(Color.FromArgb(50, 255, 255, 255))) g.FillEllipse(br, 1, 1, Width - 3, Height - 3);
            float cx = Width / 2f, cy = Height / 2f;
            using (var br = new SolidBrush(Color.White))
            using (var pen = new Pen(Color.White, 2f))
            {
                switch (Kind)
                {
                    case IconKind.Play:
                        g.FillPolygon(br, new[] { new PointF(cx - 5, cy - 8), new PointF(cx - 5, cy + 8), new PointF(cx + 8, cy) });
                        break;
                    case IconKind.Pause:
                        g.FillRectangle(br, cx - 7, cy - 8, 5, 16);
                        g.FillRectangle(br, cx + 2, cy - 8, 5, 16);
                        break;
                    case IconKind.FullOn:
                    case IconKind.FullOff:
                        {
                            // 네 모서리 (FullOn = 바깥으로, FullOff = 안으로)
                            float a = 9, b = 4;
                            bool o = Kind == IconKind.FullOn;
                            foreach (var sgn in new[] { new PointF(-1, -1), new PointF(1, -1), new PointF(1, 1), new PointF(-1, 1) })
                            {
                                float x0 = cx + sgn.X * (o ? a : b), y0 = cy + sgn.Y * (o ? a : b);
                                float dx = sgn.X * (o ? -1 : 1) * 5, dy = sgn.Y * (o ? -1 : 1) * 5;
                                g.DrawLine(pen, x0, y0, x0 + dx, y0);
                                g.DrawLine(pen, x0, y0, x0, y0 + dy);
                            }
                            break;
                        }
                    default:
                        g.FillPolygon(br, new[] { new PointF(cx - 10, cy - 4), new PointF(cx - 5, cy - 4), new PointF(cx + 1, cy - 9),
                            new PointF(cx + 1, cy + 9), new PointF(cx - 5, cy + 4), new PointF(cx - 10, cy + 4) });
                        if (Kind == IconKind.Speaker)
                        {
                            g.DrawArc(pen, cx - 2, cy - 5, 8, 10, -60, 120);
                            g.DrawArc(pen, cx - 2, cy - 9, 14, 18, -60, 120);
                        }
                        else
                        {
                            g.DrawLine(pen, cx + 4, cy - 5, cx + 11, cy + 5);
                            g.DrawLine(pen, cx + 11, cy - 5, cx + 4, cy + 5);
                        }
                        break;
                }
            }
        }
    }

    // ───────────────────────── 글 적는 작은 창 ─────────────────────────
    static class TextPrompt
    {
        public static string Ask(IWin32Window owner, string title, string prompt) { return Ask(owner, title, prompt, ""); }

        public static string Ask(IWin32Window owner, string title, string prompt, string initial)
        {
            using (var f = new Form())
            {
                f.Text = title;
                f.FormBorderStyle = FormBorderStyle.FixedDialog;
                f.MaximizeBox = false; f.MinimizeBox = false;
                f.ShowInTaskbar = false;
                f.StartPosition = FormStartPosition.CenterParent;
                f.BackColor = Theme.Bar;
                f.ForeColor = Theme.Text;
                f.Font = Theme.F(10.5f);
                f.ClientSize = new Size(480, 270);
                var l = new Label();
                l.Text = prompt; l.Location = new Point(16, 14); l.Size = new Size(450, 44);
                l.ForeColor = Theme.SubText;
                var tb = new TextBox();
                tb.Multiline = true; tb.AcceptsReturn = true; tb.ScrollBars = ScrollBars.Vertical;
                tb.Location = new Point(16, 62); tb.Size = new Size(448, 140);
                tb.Font = Theme.F(12f);
                tb.AccessibleName = "자막 글";
                tb.Text = initial ?? "";
                var ok = Theme.MakeButton("확인", true);
                ok.DialogResult = DialogResult.OK;
                var cancel = Theme.MakeButton("취소", false);
                cancel.DialogResult = DialogResult.Cancel;
                f.Controls.Add(l); f.Controls.Add(tb); f.Controls.Add(ok); f.Controls.Add(cancel);
                f.CancelButton = cancel;
                f.Shown += delegate
                {
                    cancel.Location = new Point(f.ClientSize.Width - 16 - cancel.Width, 216);
                    ok.Location = new Point(cancel.Left - 10 - ok.Width, 216);
                    tb.Focus();
                    tb.SelectAll();
                };
                return f.ShowDialog(owner) == DialogResult.OK ? tb.Text : null;
            }
        }
    }

    // ───────────────────────── 화면 어디서든 색 고르기 ─────────────────────────
    static class ScreenPicker
    {
        public static Color? Pick(Form editor)
        {
            var hidden = new List<Form>();
            foreach (Form f in Application.OpenForms) if (f.Visible) hidden.Add(f);
            var old = hidden.Select(f => f.Opacity).ToList();
            foreach (var f in hidden) f.Opacity = 0;   // 이 프로그램 창을 잠깐 숨겨서 뒤의 화면도 고를 수 있게
            Application.DoEvents();
            System.Threading.Thread.Sleep(250);
            var vs = SystemInformation.VirtualScreen;
            Color? result = null;
            try
            {
                using (var shot = new Bitmap(vs.Width, vs.Height, PixelFormat.Format32bppArgb))
                {
                    using (var g = Graphics.FromImage(shot)) g.CopyFromScreen(vs.Location, Point.Empty, vs.Size);
                    using (var pf = new PickForm(shot, vs))
                    {
                        pf.ShowDialog();
                        result = pf.Picked;
                    }
                }
            }
            finally
            {
                for (int i = 0; i < hidden.Count; i++) hidden[i].Opacity = old[i];
                editor.Activate();
            }
            return result;
        }

        public static Color PixelAtCursor()
        {
            using (var b = new Bitmap(1, 1))
            {
                using (var g = Graphics.FromImage(b)) g.CopyFromScreen(Cursor.Position, Point.Empty, new Size(1, 1));
                return b.GetPixel(0, 0);
            }
        }

        class PickForm : Form
        {
            readonly Bitmap shot;
            Point mouse;
            public Color? Picked;

            public PickForm(Bitmap shot, Rectangle vs)
            {
                this.shot = shot;
                FormBorderStyle = FormBorderStyle.None;
                StartPosition = FormStartPosition.Manual;
                Bounds = vs;
                TopMost = true;
                ShowInTaskbar = false;
                DoubleBuffered = true;
                Cursor = Cursors.Cross;
                KeyPreview = true;
                mouse = PointToClient(Cursor.Position);
            }

            protected override void OnKeyDown(KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Escape) Close();
                base.OnKeyDown(e);
            }

            protected override void OnMouseMove(MouseEventArgs e) { mouse = e.Location; Invalidate(); base.OnMouseMove(e); }

            protected override void OnMouseClick(MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left && e.X >= 0 && e.Y >= 0 && e.X < shot.Width && e.Y < shot.Height)
                    Picked = shot.GetPixel(e.X, e.Y);
                Close();
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                g.DrawImageUnscaled(shot, 0, 0);
                // 돋보기
                int n = 11, z = 12, size = n * z;
                int bx = mouse.X + 24, by = mouse.Y + 24;
                if (bx + size + 10 > Width) bx = mouse.X - size - 24;
                if (by + size + 60 > Height) by = mouse.Y - size - 84;
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                g.FillRectangle(Brushes.Black, bx - 3, by - 3, size + 6, size + 56);
                g.DrawImage(shot, new Rectangle(bx, by, size, size), new Rectangle(mouse.X - n / 2, mouse.Y - n / 2, n, n), GraphicsUnit.Pixel);
                g.DrawRectangle(Pens.White, bx + (n / 2) * z, by + (n / 2) * z, z, z);
                Color c = (mouse.X >= 0 && mouse.Y >= 0 && mouse.X < shot.Width && mouse.Y < shot.Height) ? shot.GetPixel(mouse.X, mouse.Y) : Color.Black;
                using (var br = new SolidBrush(c)) g.FillRectangle(br, bx, by + size + 6, 40, 40);
                TextRenderer.DrawText(g, "눌러서 이 색 고르기\nEsc: 그만두기", Theme.F(9.5f), new Rectangle(bx + 46, by + size + 4, size - 46, 46), Color.White,
                    TextFormatFlags.Left | TextFormatFlags.WordBreak);
            }
        }
    }
}
