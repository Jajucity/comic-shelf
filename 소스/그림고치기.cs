// 만화 보관함 — 그림 고치기 창
// 원본 파일은 절대 바꾸지 않음. 고친 것은 '편집' 폴더에 따로 저장:
//   0001.png = 브러시로 그린 것, 0001_원본.png = 지우개로 지운 원본, 0001.txt = 자막·도형 (옮기고 고칠 수 있음)
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace ComicShelf
{
    class EditorForm : Form
    {
        enum Tool { Select, Brush, Eraser, Text, Cover, Shape, Picker }

        class Snap { public byte[] Brush; public byte[] Base; public bool BaseEdited; public string Objs; }

        class Canvas : Control
        {
            public Canvas()
            {
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
                Cursor = Cursors.Cross;
                Text = "그림";
            }
        }

        class ShapeButton : Control
        {
            public readonly ShapeKind Kind;
            public bool Selected;
            bool hover;
            public ShapeButton(ShapeKind k, string name)
            {
                Kind = k;
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
                Size = new Size(38, 34);
                Margin = new Padding(2);
                Cursor = Cursors.Hand;
                Text = name;
                AccessibleName = name;
            }
            protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
            protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                g.Clear(Selected ? Color.FromArgb(120, 70, 35) : hover ? Theme.CardHover : Theme.Card);
                if (Selected) using (var pen = new Pen(Theme.Accent, 2)) g.DrawRectangle(pen, 1, 1, Width - 3, Height - 3);
                Ink.DrawIcon(g, Kind, new Rectangle(9, 7, Width - 18, Height - 14), Color.White);
            }
        }

        class Swatch : Control
        {
            public readonly Color C;
            public Swatch(Color c)
            {
                C = c;
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
                Size = new Size(24, 24);
                Margin = new Padding(2);
                Cursor = Cursors.Hand;
            }
            protected override void OnPaint(PaintEventArgs e)
            {
                using (var br = new SolidBrush(C)) e.Graphics.FillRectangle(br, 0, 0, Width, Height);
                e.Graphics.DrawRectangle(Pens.Gray, 0, 0, Width - 1, Height - 1);
            }
        }

        static readonly Color[] Basic =
        {
            Color.Black, Color.White, Color.FromArgb(128, 128, 128), Color.FromArgb(230, 30, 30), Color.FromArgb(255, 140, 0),
            Color.FromArgb(255, 220, 0), Color.FromArgb(0, 170, 60), Color.FromArgb(0, 190, 230), Color.FromArgb(30, 90, 255),
            Color.FromArgb(150, 60, 200), Color.FromArgb(255, 120, 180), Color.FromArgb(140, 80, 40)
        };

        static readonly Encoding Utf8 = new UTF8Encoding(false);

        readonly string ovPath, basePath, objsPath;
        readonly Bitmap original;
        Bitmap baseImg, brush;
        List<Obj> objs;
        bool baseEdited, changed, saved;
        readonly List<Snap> undo = new List<Snap>();
        Tool tool = Tool.Brush;
        ShapeKind shape = ShapeKind.Rect;
        Color color = Color.Black, fillColor = Color.White, outlineColor = Color.White;
        int slot;
        bool eraseText, eraseShape, eraseBrush = true, eraseBase;
        float scale = 1f;
        Obj selected, temp;
        bool drawing, dragging, moving, moved;
        PointF last, dragA, dragB, moveStart;
        RectangleF moveOrig;
        readonly List<Color> collected;

        readonly Panel scroller;
        readonly Canvas canvas;
        readonly FlowLayoutPanel shapePanel, palette;
        readonly List<ShapeButton> shapeButtons = new List<ShapeButton>();
        readonly TrackBar sizeBar;
        readonly NumericUpDown fontBox;
        readonly CheckBox outlineCheck, coverCheck, fillCheck;
        readonly Button colorBtn, fillBtn, outlineBtn, undoBtn;
        readonly Label help, zoomLabel;
        readonly ContextMenuStrip objMenu;

        public EditorForm(string path)
        {
            ovPath = Store.OverlayPath(path);
            basePath = Edits.BasePath(path);
            objsPath = Edits.ObjsPath(path);
            original = Pics.Load(path);
            if (File.Exists(basePath))
            {
                try
                {
                    var b = Pics.Load(basePath);
                    if (b.Size == original.Size) { baseImg = b; baseEdited = true; } else b.Dispose();
                }
                catch { }
            }
            if (baseImg == null) baseImg = new Bitmap(original);
            if (File.Exists(ovPath))
            {
                try
                {
                    var o = Pics.Load(ovPath);
                    if (o.Size == original.Size) brush = o; else o.Dispose();
                }
                catch { }
            }
            if (brush == null) brush = new Bitmap(original.Width, original.Height, PixelFormat.Format32bppArgb);
            objs = new List<Obj>();
            if (File.Exists(objsPath)) { try { objs = Ink.Load(File.ReadAllText(objsPath, Utf8)); } catch { } }
            collected = Edits.LoadColors();

            Text = "그림 고치기";
            Size = new Size(1280, 900);
            MinimumSize = new Size(860, 620);
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            BackColor = Theme.Back;
            ForeColor = Theme.Text;
            Font = Theme.F(10.5f);
            KeyPreview = true;

            var tools = new FlowLayoutPanel();
            tools.Dock = DockStyle.Left;
            tools.Width = 256;
            tools.FlowDirection = FlowDirection.TopDown;
            tools.WrapContents = false;
            tools.AutoScroll = true;
            tools.Padding = new Padding(14, 6, 6, 10);
            tools.BackColor = Theme.Bar;

            tools.Controls.Add(Head("도구"));
            AddTool(tools, "옮기기·고치기 (자막·도형)", Tool.Select);
            AddTool(tools, "브러시", Tool.Brush);
            AddTool(tools, "지우개 (지울 것 고르기)", Tool.Eraser);
            AddTool(tools, "자막 넣기 (글자만)", Tool.Text);
            AddTool(tools, "번역 자막 (칸에 맞춤)", Tool.Cover);
            AddTool(tools, "도형", Tool.Shape);
            AddTool(tools, "스포이드 (그림에서 색)", Tool.Picker);

            shapePanel = new FlowLayoutPanel();
            shapePanel.Width = 214;
            shapePanel.AutoSize = true;
            shapePanel.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            shapePanel.MaximumSize = new Size(214, 0);
            shapePanel.BackColor = Theme.Bar;
            shapePanel.Margin = new Padding(3, 2, 3, 4);
            var names = new Dictionary<ShapeKind, string>
            {
                { ShapeKind.Rect, "사각형" }, { ShapeKind.RoundRect, "둥근 사각형" }, { ShapeKind.Ellipse, "원" },
                { ShapeKind.Triangle, "삼각형" }, { ShapeKind.Diamond, "마름모" }, { ShapeKind.Star, "별" },
                { ShapeKind.Line, "선" }, { ShapeKind.Arrow, "화살표" }, { ShapeKind.BubbleOval, "말풍선 (둥근)" }, { ShapeKind.BubbleRect, "말풍선 (네모)" }
            };
            var tip = new ToolTip();
            foreach (var kv in names)
            {
                var sb = new ShapeButton(kv.Key, kv.Value);
                sb.Selected = kv.Key == shape;
                tip.SetToolTip(sb, kv.Value);
                var k = kv.Key;
                sb.Click += delegate { shape = k; foreach (var b in shapeButtons) { b.Selected = b.Kind == k; b.Invalidate(); } };
                shapeButtons.Add(sb);
                shapePanel.Controls.Add(sb);
            }
            fillCheck = MakeCheck("안쪽 채우기 (채우기 색으로)", false);
            shapePanel.SetFlowBreak(shapeButtons[shapeButtons.Count - 1], true);
            shapePanel.Controls.Add(fillCheck);
            shapePanel.Visible = false;
            tools.Controls.Add(shapePanel);

            tools.Controls.Add(Head("색"));
            var colorRow = new FlowLayoutPanel();
            colorRow.AutoSize = true;
            colorRow.WrapContents = false;
            colorRow.Margin = new Padding(0);
            colorRow.BackColor = Theme.Bar;
            colorBtn = ColorButton("글자·선");
            fillBtn = ColorButton("채우기");
            outlineBtn = ColorButton("테두리");
            var slotBtns = new[] { colorBtn, fillBtn, outlineBtn };
            for (int i = 0; i < 3; i++)
            {
                int k = i;
                slotBtns[i].Click += delegate { slot = k; UpdateColorButtons(); };
                colorRow.Controls.Add(slotBtns[i]);
            }
            tools.Controls.Add(colorRow);
            tools.Controls.Add(Small("먼저 바꿀 칸(글자·선 / 채우기 / 테두리)을 누르고, 아래 색이나 스포이드로 골라요."));
            var moreBtn = SideButton("색 고르는 창 열기");
            moreBtn.Click += delegate
            {
                var c = AskColor(slot == 0 ? color : slot == 1 ? fillColor : outlineColor);
                if (c.HasValue) { SetActive(c.Value); AddCollected(c.Value); }
            };
            tools.Controls.Add(moreBtn);
            palette = new FlowLayoutPanel();
            palette.Width = 216;
            palette.AutoSize = true;
            palette.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            palette.MaximumSize = new Size(216, 0);
            palette.BackColor = Theme.Bar;
            palette.Margin = new Padding(1, 4, 1, 0);
            tools.Controls.Add(palette);
            var screenBtn = SideButton("화면에서 색 고르기");
            screenBtn.Click += delegate
            {
                var c = ScreenPicker.Pick(this);
                if (c.HasValue) { SetActive(c.Value); AddCollected(c.Value); }
            };
            tools.Controls.Add(screenBtn);
            UpdateColorButtons();
            RebuildPalette();

            tools.Controls.Add(Head("굵기 (브러시·지우개·도형 선)"));
            sizeBar = new TrackBar();
            sizeBar.Minimum = 1; sizeBar.Maximum = 80; sizeBar.Value = 8;
            sizeBar.TickStyle = TickStyle.None;
            sizeBar.Width = 214;
            sizeBar.BackColor = Theme.Bar;
            tools.Controls.Add(sizeBar);

            tools.Controls.Add(Head("자막 글자 크기"));
            fontBox = new NumericUpDown();
            fontBox.Minimum = 8; fontBox.Maximum = 300; fontBox.Value = 32;
            fontBox.Width = 214;
            fontBox.Font = Theme.F(11f);
            tools.Controls.Add(fontBox);
            outlineCheck = MakeCheck("자막 글자에 테두리 (테두리 색)", false);
            tools.Controls.Add(outlineCheck);
            coverCheck = MakeCheck("번역 자막을 흰 칸으로 덮기", true);
            tools.Controls.Add(coverCheck);

            tools.Controls.Add(Head(""));
            undoBtn = SideButton("되돌리기 (Ctrl+Z)");
            undoBtn.Click += delegate { Undo(); };
            tools.Controls.Add(undoBtn);
            var resetBtn = SideButton("원래대로 (처음 그림)");
            resetBtn.Click += delegate { Restore(); };
            tools.Controls.Add(resetBtn);

            help = new Label();
            help.AutoSize = false;
            help.Size = new Size(214, 150);
            help.ForeColor = Theme.SubText;
            help.Margin = new Padding(3, 12, 3, 3);
            tools.Controls.Add(help);

            var bottom = new FlowLayoutPanel();
            bottom.Dock = DockStyle.Bottom;
            bottom.Height = 60;
            bottom.FlowDirection = FlowDirection.RightToLeft;
            bottom.Padding = new Padding(10);
            bottom.BackColor = Theme.Bar;
            var cancel = Theme.MakeButton("취소", false);
            cancel.Click += delegate { Close(); };
            var save = Theme.MakeButton("저장", true);
            save.Click += delegate { Save(); };
            save.Margin = new Padding(3, 3, 10, 3);
            zoomLabel = new Label();
            zoomLabel.AutoSize = true;
            zoomLabel.ForeColor = Theme.SubText;
            zoomLabel.Margin = new Padding(3, 12, 20, 3);
            bottom.Controls.Add(cancel);
            bottom.Controls.Add(save);
            bottom.Controls.Add(zoomLabel);

            scroller = new Panel();
            scroller.Dock = DockStyle.Fill;
            scroller.AutoScroll = true;
            scroller.BackColor = Theme.ViewBack;
            scroller.Resize += delegate { PlaceCanvas(); };
            canvas = new Canvas();
            canvas.Paint += CanvasPaint;
            canvas.MouseDown += CanvasDown;
            canvas.MouseMove += CanvasMove;
            canvas.MouseUp += CanvasUp;
            canvas.MouseDoubleClick += CanvasDoubleClick;
            canvas.MouseWheel += CanvasWheel;
            scroller.Controls.Add(canvas);

            objMenu = new ContextMenuStrip();
            objMenu.Font = Theme.F(10.5f);

            Controls.Add(scroller);
            Controls.Add(tools);
            Controls.Add(bottom);

            Shown += delegate { FitWidth(); };
            FormClosing += AskClose;
            UpdateHelp();
            UpdateUndo();
        }

        // ── 왼쪽 도구 칸 만들기 ──
        Label Head(string text)
        {
            var l = new Label();
            l.Text = text;
            l.AutoSize = true;
            l.Font = Theme.F(10f, FontStyle.Bold);
            l.ForeColor = Theme.SubText;
            l.Margin = new Padding(3, 12, 3, 4);
            return l;
        }

        Label Small(string text)
        {
            var l = new Label();
            l.Text = text;
            l.AutoSize = false;
            l.Size = new Size(214, 50);
            l.Font = Theme.F(9f);
            l.ForeColor = Theme.SubText;
            l.Margin = new Padding(3, 2, 3, 2);
            return l;
        }

        CheckBox MakeCheck(string text, bool on)
        {
            var c = new CheckBox();
            c.Text = text;
            c.Checked = on;
            c.AutoSize = true;
            c.ForeColor = Theme.Text;
            c.Margin = new Padding(3, 6, 3, 2);
            return c;
        }

        Button SideButton(string text)
        {
            var b = Theme.MakeButton(text, false);
            b.AutoSize = false;
            b.Size = new Size(214, 36);
            b.Margin = new Padding(3, 3, 3, 5);
            return b;
        }

        Button ColorButton(string text)
        {
            var b = Theme.MakeButton(text, false);
            b.AutoSize = false;
            b.Size = new Size(68, 36);
            b.Margin = new Padding(2, 0, 2, 0);
            b.Padding = new Padding(0);
            b.Font = Theme.F(9.5f, FontStyle.Bold);
            b.FlatAppearance.BorderSize = 1;
            b.FlatAppearance.BorderColor = Color.Gray;
            return b;
        }

        void AddTool(Control parent, string text, Tool t)
        {
            var rb = new RadioButton();
            rb.Appearance = Appearance.Button;
            rb.Text = text;
            rb.AccessibleName = text;
            rb.FlatStyle = FlatStyle.Flat;
            rb.FlatAppearance.BorderColor = Theme.Accent;
            rb.FlatAppearance.CheckedBackColor = Color.FromArgb(120, 70, 35);
            rb.FlatAppearance.MouseOverBackColor = Theme.CardHover;
            rb.BackColor = Theme.Card;
            rb.ForeColor = Theme.Text;
            rb.Font = Theme.F(10f, FontStyle.Bold);
            rb.TextAlign = ContentAlignment.MiddleLeft;
            rb.Padding = new Padding(8, 0, 0, 0);
            rb.Size = new Size(214, 34);
            rb.Margin = new Padding(3, 2, 3, 3);
            rb.Cursor = Cursors.Hand;
            bool on = t == tool;
            rb.Checked = on;
            rb.FlatAppearance.BorderSize = on ? 2 : 0;
            rb.CheckedChanged += delegate
            {
                rb.FlatAppearance.BorderSize = rb.Checked ? 2 : 0;
                if (rb.Checked) SetTool(t);
            };
            // 지우개는 누를 때마다 무엇을 지울지 고르는 창
            if (t == Tool.Eraser) rb.Click += delegate { AskEraser(); };
            parent.Controls.Add(rb);
        }

        void SetTool(Tool t)
        {
            tool = t;
            shapePanel.Visible = t == Tool.Shape;
            if (t != Tool.Select) selected = null;
            canvas.Cursor = t == Tool.Select ? Cursors.Default : t == Tool.Text ? Cursors.IBeam : Cursors.Cross;
            UpdateHelp();
            canvas.Invalidate();
        }

        void AskEraser()
        {
            using (var f = new Form())
            {
                f.Text = "지우개 — 무엇을 지울까요?";
                f.FormBorderStyle = FormBorderStyle.FixedDialog;
                f.MaximizeBox = false; f.MinimizeBox = false; f.ShowInTaskbar = false;
                f.StartPosition = FormStartPosition.CenterParent;
                f.BackColor = Theme.Bar; f.ForeColor = Theme.Text; f.Font = Theme.F(11f);
                f.ClientSize = new Size(380, 290);
                var l = new Label();
                l.Text = "지우개로 지울 것을 골라 주세요. (여러 개 골라도 돼요)";
                l.Location = new Point(16, 14); l.Size = new Size(350, 40); l.ForeColor = Theme.SubText;
                f.Controls.Add(l);
                var c1 = new CheckBox(); c1.Text = "자막 (닿으면 통째로 지워짐)"; c1.Checked = eraseText;
                var c2 = new CheckBox(); c2.Text = "도형 (닿으면 통째로 지워짐)"; c2.Checked = eraseShape;
                var c3 = new CheckBox(); c3.Text = "브러시로 그린 것"; c3.Checked = eraseBrush;
                var c4 = new CheckBox(); c4.Text = "원본 그림 (흰색으로 지워짐)"; c4.Checked = eraseBase;
                int y = 58;
                foreach (var c in new[] { c1, c2, c3, c4 })
                {
                    c.AutoSize = true; c.Location = new Point(24, y); c.ForeColor = Theme.Text;
                    f.Controls.Add(c);
                    y += 40;
                }
                var ok = Theme.MakeButton("확인", true);
                ok.DialogResult = DialogResult.OK;
                var cancel = Theme.MakeButton("취소", false);
                cancel.DialogResult = DialogResult.Cancel;
                f.Controls.Add(ok); f.Controls.Add(cancel);
                f.AcceptButton = ok; f.CancelButton = cancel;
                f.Shown += delegate
                {
                    cancel.Location = new Point(f.ClientSize.Width - 16 - cancel.Width, 236);
                    ok.Location = new Point(cancel.Left - 10 - ok.Width, 236);
                };
                if (f.ShowDialog(this) != DialogResult.OK) return;
                if (!c1.Checked && !c2.Checked && !c3.Checked && !c4.Checked)
                {
                    MessageBox.Show(this, "하나도 고르지 않아서 그대로 둘게요.", "지우개");
                    return;
                }
                eraseText = c1.Checked; eraseShape = c2.Checked; eraseBrush = c3.Checked; eraseBase = c4.Checked;
                UpdateHelp();
            }
        }

        string EraserTargets()
        {
            var parts = new List<string>();
            if (eraseText) parts.Add("자막");
            if (eraseShape) parts.Add("도형");
            if (eraseBrush) parts.Add("브러시 그림");
            if (eraseBase) parts.Add("원본 그림");
            return string.Join(", ", parts);
        }

        void UpdateHelp()
        {
            string s;
            switch (tool)
            {
                case Tool.Select: s = "자막이나 도형을 눌러서 끌면 옮겨져요. 두 번 누르면 글을 고치고, 오른쪽 클릭하면 맨 앞·맨 뒤·지우기를 골라요. Delete 키로도 지워요."; break;
                case Tool.Brush: s = "그림 위를 누른 채로 끌면 그려져요."; break;
                case Tool.Eraser: s = "지금 지우는 것: " + EraserTargets() + "\n지우개 단추를 다시 누르면 바꿀 수 있어요."; break;
                case Tool.Text: s = "자막을 넣을 곳을 누르면 글을 적는 창이 떠요. 글자만 들어가요. 흰 칸이 필요하면 '도형'으로 그려요."; break;
                case Tool.Cover: s = "원래 글자가 있는 칸을 끌어서 고르면 그 칸에 번역한 글이 맞춰 들어가요. '흰 칸으로 덮기'를 끄면 글자만 들어가요."; break;
                case Tool.Shape: s = "위에서 도형을 고르고 그림 위를 끌어서 크기를 정해요. '안쪽 채우기'를 켜면 채우기 색으로 채워져요."; break;
                default: s = "그림에서 원하는 곳을 누르면 그 색이 지금 고른 색 칸(글자·선 / 채우기 / 테두리)에 들어가고, 모은 색에 더해져요."; break;
            }
            help.Text = s + "\n\nCtrl + 휠: 확대·축소";
        }

        // ── 색 ──
        // 색 칸: 0 = 글자·선, 1 = 채우기, 2 = 테두리. 팔레트·스포이드는 고른 칸에 들어감
        void SetColor(Color c) { SetSlotColor(0, c); }
        void SetFill(Color c) { SetSlotColor(1, c); }
        void SetActive(Color c) { SetSlotColor(slot, c); }

        void SetSlotColor(int s, Color c)
        {
            if (s == 0) color = c; else if (s == 1) fillColor = c; else outlineColor = c;
            UpdateColorButtons();
            if (selected == null || tool != Tool.Select) return;
            PushUndo();
            if (s == 0) selected.Color = c;
            else if (s == 1) selected.Fill = c;
            else { selected.OutlineColor = c; if (selected.IsText) selected.Outline = true; }
            changed = true;
            canvas.Invalidate();
        }

        void UpdateColorButtons()
        {
            var btns = new[] { colorBtn, fillBtn, outlineBtn };
            var cols = new[] { color, fillColor, outlineColor };
            for (int i = 0; i < 3; i++)
            {
                var b = btns[i]; var c = cols[i];
                if (b == null) continue;
                b.BackColor = c;
                b.FlatAppearance.MouseOverBackColor = c;
                int lum = (c.R * 299 + c.G * 587 + c.B * 114) / 1000;
                b.ForeColor = lum > 140 ? Color.Black : Color.White;
                b.FlatAppearance.BorderSize = i == slot ? 4 : 1;
                b.FlatAppearance.BorderColor = i == slot ? Theme.Accent : Color.Gray;
            }
        }

        Color? AskColor(Color start)
        {
            using (var cd = new ColorDialog())
            {
                cd.Color = start;
                cd.FullOpen = true;
                cd.CustomColors = collected.Take(16).Select(c => c.R | (c.G << 8) | (c.B << 16)).ToArray();
                if (cd.ShowDialog(this) != DialogResult.OK) return null;
                return cd.Color;
            }
        }

        void AddCollected(Color c)
        {
            c = Color.FromArgb(255, c.R, c.G, c.B);
            collected.RemoveAll(x => x.ToArgb() == c.ToArgb());
            collected.Insert(0, c);
            while (collected.Count > 16) collected.RemoveAt(collected.Count - 1);
            Edits.SaveColors(collected);
            RebuildPalette();
        }

        void RebuildPalette()
        {
            palette.SuspendLayout();
            foreach (Control c in palette.Controls.Cast<Control>().ToList()) c.Dispose();
            palette.Controls.Clear();
            var tip = new ToolTip();
            foreach (var c in Basic.Concat(collected))
            {
                var sw = new Swatch(c);
                var cc = c;
                sw.MouseUp += delegate(object s, MouseEventArgs e)
                {
                    if (e.Button == MouseButtons.Left) SetActive(cc);
                    else if (e.Button == MouseButtons.Right) SetFill(cc);
                };
                palette.Controls.Add(sw);
            }
            palette.ResumeLayout();
        }

        // ── 보기 크기 ──
        void FitWidth()
        {
            float s = Math.Min(1f, (scroller.ClientSize.Width - 40f) / original.Width);
            SetScale(s, null);
            scroller.AutoScrollPosition = new Point(0, 0);
            PlaceCanvas();
        }

        void PlaceCanvas()
        {
            int contentLeft = Math.Max(10, (scroller.ClientSize.Width - canvas.Width) / 2);
            canvas.Location = new Point(contentLeft + scroller.AutoScrollPosition.X, 10 + scroller.AutoScrollPosition.Y);
        }

        void SetScale(float s, Point? mouseOnCanvas)
        {
            s = Math.Max(0.05f, Math.Min(4f, s));
            Point anchor = mouseOnCanvas ?? new Point(0, 0);
            PointF img = new PointF(anchor.X / scale, anchor.Y / scale);
            Point inScroller = scroller.PointToClient(canvas.PointToScreen(anchor));
            scale = s;
            scroller.SuspendLayout();
            canvas.Size = new Size(Math.Max(1, (int)(original.Width * s)), Math.Max(1, (int)(original.Height * s)));
            scroller.AutoScrollMinSize = new Size(canvas.Width + 20, canvas.Height + 20);
            PlaceCanvas();
            scroller.ResumeLayout();
            if (mouseOnCanvas != null)
            {
                int contentLeft = Math.Max(10, (scroller.ClientSize.Width - canvas.Width) / 2);
                int cx = (int)(img.X * s) + contentLeft - inScroller.X;
                int cy = (int)(img.Y * s) + 10 - inScroller.Y;
                scroller.AutoScrollPosition = new Point(Math.Max(0, cx), Math.Max(0, cy));
                PlaceCanvas();
            }
            canvas.Invalidate();
            zoomLabel.Text = "보기 " + (int)Math.Round(s * 100) + "%";
        }

        void CanvasWheel(object sender, MouseEventArgs e)
        {
            if ((ModifierKeys & Keys.Control) == 0) return;   // 그냥 휠은 아래 칸이 스크롤
            SetScale((float)(scale * Math.Pow(1.1, e.Delta / 120.0)), e.Location);
            var he = e as HandledMouseEventArgs;
            if (he != null) he.Handled = true;
        }

        // ── 화면에 그리기 ──
        void CanvasPaint(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Theme.ViewBack);
            var clip = e.ClipRectangle;
            g.ScaleTransform(scale, scale);
            var src = new RectangleF(clip.X / scale - 1, clip.Y / scale - 1, clip.Width / scale + 2, clip.Height / scale + 2);
            src.Intersect(new RectangleF(0, 0, original.Width, original.Height));
            if (src.Width <= 0 || src.Height <= 0) return;
            g.InterpolationMode = scale < 1f ? InterpolationMode.HighQualityBilinear : InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.DrawImage(baseImg, src, src, GraphicsUnit.Pixel);
            g.DrawImage(brush, src, src, GraphicsUnit.Pixel);
            g.PixelOffsetMode = PixelOffsetMode.Default;
            var area = src;
            area.Inflate(200, 200);
            foreach (var o in objs)
                if (Ink.Bounds(o).IntersectsWith(area)) Ink.Draw(g, o);
            if (temp != null) Ink.Draw(g, temp);
            using (var pen = new Pen(Theme.Accent, 2f / scale))
            {
                pen.DashStyle = DashStyle.Dash;
                if (dragging && tool == Tool.Cover) g.DrawRectangle(pen, Rectangle.Round(Ink.Norm(RectangleF.FromLTRB(dragA.X, dragA.Y, dragB.X, dragB.Y))));
                if (selected != null)
                {
                    var b = Ink.Bounds(selected);
                    b.Inflate(4 / scale, 4 / scale);
                    g.DrawRectangle(pen, b.X, b.Y, b.Width, b.Height);
                }
            }
        }

        PointF ToImg(Point p) { return new PointF(p.X / scale, p.Y / scale); }

        Obj HitObj(PointF p)
        {
            for (int i = objs.Count - 1; i >= 0; i--)
            {
                var b = Ink.Bounds(objs[i]);
                b.Inflate(6 / scale, 6 / scale);
                if (b.Contains(p)) return objs[i];
            }
            return null;
        }

        void CanvasDown(object sender, MouseEventArgs e)
        {
            var p = ToImg(e.Location);
            if (e.Button == MouseButtons.Right)
            {
                if (tool != Tool.Select) return;
                var hit = HitObj(p);
                if (hit == null) return;
                selected = hit;
                canvas.Invalidate();
                ShowObjMenu(e.Location);
                return;
            }
            if (e.Button != MouseButtons.Left) return;
            switch (tool)
            {
                case Tool.Select:
                    selected = HitObj(p);
                    if (selected != null)
                    {
                        PushUndo();
                        moving = true;
                        moved = false;
                        moveStart = p;
                        moveOrig = selected.R;
                    }
                    canvas.Invalidate();
                    break;
                case Tool.Brush:
                case Tool.Eraser:
                    PushUndo();
                    drawing = true;
                    last = p;
                    Stroke(p, p);
                    break;
                case Tool.Text:
                    PlaceText(p);
                    break;
                case Tool.Cover:
                case Tool.Shape:
                    dragging = true;
                    dragA = dragB = p;
                    if (tool == Tool.Shape) temp = NewShape(p, p);
                    break;
                case Tool.Picker:
                    var c = ScreenPicker.PixelAtCursor();
                    SetActive(c);
                    AddCollected(c);
                    break;
            }
        }

        void CanvasMove(object sender, MouseEventArgs e)
        {
            var p = ToImg(e.Location);
            if (moving && selected != null)
            {
                float dx = p.X - moveStart.X, dy = p.Y - moveStart.Y;
                if (Math.Abs(dx) + Math.Abs(dy) > 0.5f) moved = true;
                selected.R = new RectangleF(moveOrig.X + dx, moveOrig.Y + dy, moveOrig.Width, moveOrig.Height);
                changed = true;
                canvas.Invalidate();
            }
            else if (drawing)
            {
                Stroke(last, p);
                last = p;
            }
            else if (dragging)
            {
                dragB = p;
                if (tool == Tool.Shape) temp = NewShape(dragA, dragB);
                canvas.Invalidate();
            }
        }

        void CanvasUp(object sender, MouseEventArgs e)
        {
            if (moving)
            {
                moving = false;
                if (!moved && undo.Count > 0) { undo.RemoveAt(undo.Count - 1); UpdateUndo(); }
                return;
            }
            drawing = false;
            if (!dragging) return;
            dragging = false;
            var r = Ink.Norm(RectangleF.FromLTRB(dragA.X, dragA.Y, dragB.X, dragB.Y));
            bool big = r.Width * scale >= 6 || r.Height * scale >= 6;
            if (tool == Tool.Shape)
            {
                var o = temp;
                temp = null;
                if (o != null && big)
                {
                    PushUndo();
                    objs.Add(o);
                    selected = o;
                    changed = true;
                }
                canvas.Invalidate();
                return;
            }
            canvas.Invalidate();
            if (r.Width * scale < 8 || r.Height * scale < 8) return;
            PlaceCover(r);
        }

        void CanvasDoubleClick(object sender, MouseEventArgs e)
        {
            if (tool != Tool.Select || e.Button != MouseButtons.Left) return;
            var hit = HitObj(ToImg(e.Location));
            if (hit != null && hit.IsText) EditText(hit);
        }

        void ShowObjMenu(Point at)
        {
            var o = selected;
            objMenu.Items.Clear();
            if (o.IsText) objMenu.Items.Add("글 고치기", null, delegate { EditText(o); });
            objMenu.Items.Add("맨 앞으로", null, delegate { PushUndo(); objs.Remove(o); objs.Add(o); changed = true; canvas.Invalidate(); });
            objMenu.Items.Add("맨 뒤로", null, delegate { PushUndo(); objs.Remove(o); objs.Insert(0, o); changed = true; canvas.Invalidate(); });
            objMenu.Items.Add(new ToolStripSeparator());
            objMenu.Items.Add("지우기", null, delegate { DeleteSelected(); });
            objMenu.Show(canvas, at);
        }

        void EditText(Obj o)
        {
            string t = TextPrompt.Ask(this, "글 고치기", "자막 글을 고쳐 주세요.", o.Text);
            if (t == null) return;
            PushUndo();
            o.Text = t;
            changed = true;
            canvas.Invalidate();
        }

        void DeleteSelected()
        {
            if (selected == null) return;
            PushUndo();
            objs.Remove(selected);
            selected = null;
            changed = true;
            canvas.Invalidate();
        }

        Obj NewShape(PointF a, PointF b)
        {
            var o = new Obj();
            o.Type = "shape";
            o.Shape = shape;
            o.R = RectangleF.FromLTRB(a.X, a.Y, b.X, b.Y);
            if (o.Shape != ShapeKind.Line && o.Shape != ShapeKind.Arrow) o.R = Ink.Norm(o.R);
            else o.R = new RectangleF(a.X, a.Y, b.X - a.X, b.Y - a.Y);
            o.Color = color;
            o.Fill = fillColor;
            o.Filled = fillCheck.Checked;
            o.Width = Math.Max(1f, sizeBar.Value / scale);
            return o;
        }

        void Stroke(PointF a, PointF b)
        {
            float w = sizeBar.Value / scale;
            bool erase = tool == Tool.Eraser;
            if (erase) w *= 1.5f;
            if (!erase || eraseBrush)
            {
                using (var g = Graphics.FromImage(brush))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    if (erase) g.CompositingMode = CompositingMode.SourceCopy;
                    DrawSeg(g, erase ? Color.Transparent : color, w, a, b);
                }
            }
            if (erase && eraseBase)
            {
                using (var g = Graphics.FromImage(baseImg))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    DrawSeg(g, Color.White, w, a, b);
                }
                baseEdited = true;
            }
            if (erase && (eraseText || eraseShape))
            {
                var seg = RectangleF.FromLTRB(Math.Min(a.X, b.X) - w / 2, Math.Min(a.Y, b.Y) - w / 2, Math.Max(a.X, b.X) + w / 2, Math.Max(a.Y, b.Y) + w / 2);
                int n = objs.RemoveAll(o => ((o.IsText && eraseText) || (!o.IsText && eraseShape)) && Ink.Bounds(o).IntersectsWith(seg));
                if (n > 0) { selected = null; canvas.Invalidate(); }
            }
            changed = true;
            var rr = RectangleF.FromLTRB(Math.Min(a.X, b.X) - w, Math.Min(a.Y, b.Y) - w, Math.Max(a.X, b.X) + w, Math.Max(a.Y, b.Y) + w);
            var sr = Rectangle.Round(new RectangleF(rr.X * scale, rr.Y * scale, rr.Width * scale, rr.Height * scale));
            sr.Inflate(4, 4);
            canvas.Invalidate(sr);
        }

        static void DrawSeg(Graphics g, Color c, float w, PointF a, PointF b)
        {
            if (a == b)
            {
                using (var br = new SolidBrush(c)) g.FillEllipse(br, a.X - w / 2, a.Y - w / 2, w, w);
                return;
            }
            using (var pen = new Pen(c, w))
            {
                pen.StartCap = LineCap.Round; pen.EndCap = LineCap.Round; pen.LineJoin = LineJoin.Round;
                g.DrawLine(pen, a, b);
            }
        }

        void PlaceText(PointF at)
        {
            string text = TextPrompt.Ask(this, "자막 넣기", "넣을 글을 적어 주세요. (줄을 바꿔도 돼요)");
            if (text == null || text.Trim().Length == 0) return;
            var o = new Obj();
            o.Type = "text";
            o.R = new RectangleF(at.X, at.Y, 0, 0);
            o.Text = text;
            o.FontPx = (float)fontBox.Value;
            o.Color = color;
            o.Outline = outlineCheck.Checked;
            o.OutlineColor = outlineColor;
            PushUndo();
            objs.Add(o);
            changed = true;
            canvas.Invalidate();
        }

        void PlaceCover(RectangleF r)
        {
            string text = TextPrompt.Ask(this, "번역 자막", coverCheck.Checked
                ? "고른 칸에 넣을 글(번역한 글)을 적어 주세요.\n칸은 흰색으로 덮여요. 비워 두면 덮기만 해요."
                : "고른 칸에 넣을 글(번역한 글)을 적어 주세요.\n흰 칸 없이 글자만 들어가요.");
            if (text == null) return;
            if (!coverCheck.Checked && text.Trim().Length == 0) return;
            var o = new Obj();
            o.Type = "text";
            o.Boxed = true;
            o.R = r;
            o.Text = text;
            o.FontPx = (float)fontBox.Value;
            o.Color = color;
            o.Fill = Color.White;
            o.Filled = coverCheck.Checked;
            o.Outline = outlineCheck.Checked;
            o.OutlineColor = outlineColor;
            PushUndo();
            objs.Add(o);
            changed = true;
            canvas.Invalidate();
        }

        // ── 되돌리기·원래대로 ──
        static byte[] Png(Bitmap b)
        {
            using (var ms = new MemoryStream()) { b.Save(ms, ImageFormat.Png); return ms.ToArray(); }
        }

        static Bitmap FromPng(byte[] data)
        {
            using (var ms = new MemoryStream(data))
            using (var img = Image.FromStream(ms))
            {
                var b = new Bitmap(img.Width, img.Height, PixelFormat.Format32bppArgb);
                using (var g = Graphics.FromImage(b)) { g.CompositingMode = CompositingMode.SourceCopy; g.DrawImage(img, 0, 0, img.Width, img.Height); }
                return b;
            }
        }

        void PushUndo()
        {
            var s = new Snap();
            s.Brush = Png(brush);
            s.BaseEdited = baseEdited;
            s.Base = baseEdited ? Png(baseImg) : null;
            s.Objs = Ink.Save(objs);
            undo.Add(s);
            if (undo.Count > 20) undo.RemoveAt(0);
            UpdateUndo();
        }

        void Undo()
        {
            if (undo.Count == 0) return;
            var s = undo[undo.Count - 1];
            undo.RemoveAt(undo.Count - 1);
            brush.Dispose();
            brush = FromPng(s.Brush);
            baseImg.Dispose();
            baseImg = s.BaseEdited ? FromPng(s.Base) : new Bitmap(original);
            baseEdited = s.BaseEdited;
            objs = Ink.Load(s.Objs);
            selected = null;
            changed = true;
            UpdateUndo();
            canvas.Invalidate();
        }

        void UpdateUndo() { undoBtn.Enabled = undo.Count > 0; }

        void Restore()
        {
            var r = MessageBox.Show(this, "그린 것, 자막, 도형, 지운 것을 모두 없애고 처음 그림으로 돌릴까요?\n(저장을 눌러야 적용돼요. 되돌리기로 다시 살릴 수 있어요.)", "원래대로",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (r != DialogResult.Yes) return;
            PushUndo();
            using (var g = Graphics.FromImage(brush)) g.Clear(Color.Transparent);
            baseImg.Dispose();
            baseImg = new Bitmap(original);
            baseEdited = false;
            objs.Clear();
            selected = null;
            changed = true;
            canvas.Invalidate();
        }

        static bool IsEmpty(Bitmap b)
        {
            var d = b.LockBits(new Rectangle(0, 0, b.Width, b.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                var row = new byte[b.Width * 4];
                for (int y = 0; y < b.Height; y++)
                {
                    Marshal.Copy(d.Scan0 + y * d.Stride, row, 0, row.Length);
                    for (int i = 3; i < row.Length; i += 4) if (row[i] != 0) return false;
                }
                return true;
            }
            finally { b.UnlockBits(d); }
        }

        void Save()
        {
            try
            {
                bool brushEmpty = IsEmpty(brush);
                if (!brushEmpty || baseEdited || objs.Count > 0) Directory.CreateDirectory(Path.GetDirectoryName(ovPath));
                if (!brushEmpty) brush.Save(ovPath, ImageFormat.Png);
                else if (File.Exists(ovPath)) Store.Recycle(ovPath);
                if (baseEdited) baseImg.Save(basePath, ImageFormat.Png);
                else if (File.Exists(basePath)) Store.Recycle(basePath);
                if (objs.Count > 0) File.WriteAllText(objsPath, Ink.Save(objs), Utf8);
                else if (File.Exists(objsPath)) Store.Recycle(objsPath);
                saved = true;
                DialogResult = DialogResult.OK;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "저장하지 못했어요.\n" + ex.Message, "그림 고치기");
            }
        }

        void AskClose(object sender, FormClosingEventArgs e)
        {
            if (saved || !changed) return;
            var r = MessageBox.Show(this, "고친 것을 저장하지 않고 닫을까요?", "그림 고치기",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
            if (r != DialogResult.Yes) e.Cancel = true;
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.Z)) { Undo(); return true; }
            if (keyData == Keys.Delete && selected != null && !(ActiveControl is TextBoxBase) && !(ActiveControl is NumericUpDown))
            {
                DeleteSelected();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                original.Dispose();
                if (baseImg != null) baseImg.Dispose();
                if (brush != null) brush.Dispose();
                objMenu.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
