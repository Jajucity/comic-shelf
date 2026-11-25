// 만화 보관함 — 3. 보기 화면 (웹툰처럼 내려 보기·영상·음악)
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ComicShelf
{
    class ViewerPage : Panel
    {
        static bool sideOpen;              // 오른쪽 회차 목록을 열어 둘지 (프로그램을 켜 둔 동안 기억)
        static bool musicWanted = true;    // 음악을 켜 둘지 (프로그램을 켜 둔 동안 기억)

        readonly MainForm main;
        readonly Comic comic;
        readonly int ep;
        readonly List<int> eps;
        readonly StripView view;
        readonly Panel side;
        readonly Button musicBtn;
        readonly Slider musicVol;
        readonly Label zoomLabel;
        TopBar topBar;
        bool wasFull;
        dynamic music;
        bool musicOn;

        public ViewerPage(MainForm main, Comic comic, int ep)
        {
            this.main = main;
            this.comic = comic;
            this.ep = ep;
            BackColor = Theme.ViewBack;
            eps = Store.Episodes(comic);
            try { Store.MarkViewed(comic, ep); } catch { }

            view = new StripView();
            view.Dock = DockStyle.Fill;
            view.EndText = "— " + ep + "화 끝 —";
            view.DeleteRequested += DeleteItem;
            view.EditRequested += EditItem;
            view.ZoomChanged += delegate { zoomLabel.Text = "보기 " + (int)Math.Round(view.Zoom * 100) + "%"; };
            int idx = eps.IndexOf(ep);
            view.PrevButton.Enabled = idx > 0;
            view.NextButton.Enabled = idx >= 0 && idx < eps.Count - 1;
            view.PrevButton.Click += delegate { if (idx > 0) main.ShowViewer(comic, eps[idx - 1]); };
            view.NextButton.Click += delegate { if (idx >= 0 && idx < eps.Count - 1) main.ShowViewer(comic, eps[idx + 1]); };
            view.ListButton.Click += delegate { ToggleSide(); };
            view.VideoFullChanged += delegate(bool on)
            {
                if (on)
                {
                    wasFull = main.IsFullScreen;
                    topBar.Visible = false;
                    side.Visible = false;
                    if (!wasFull) main.ToggleFullScreen();
                }
                else
                {
                    topBar.Visible = true;
                    side.Visible = sideOpen;
                    if (!wasFull && main.IsFullScreen) main.ToggleFullScreen();
                }
            };

            side = BuildSide();
            side.Visible = sideOpen;
            Controls.Add(view);
            Controls.Add(side);

            var bar = new TopBar();
            topBar = bar;
            var back = Theme.MakeButton("←  회차 목록", false);
            back.Click += delegate { main.ShowEpisodes(comic); };
            bar.LeftArea.Controls.Add(back);
            bar.AddTitle(comic.Title + "  ·  " + ep + "화");
            // 가운데: 1화 2화 3화 … (휠로 옆으로 넘김, 누르면 그 화로)
            var strip = new EpisodeStrip(eps, ep);
            strip.Dock = DockStyle.Fill;
            strip.Picked += delegate(int n) { if (n != ep) main.ShowViewer(comic, n); };
            bar.Center.Controls.Add(strip);

            zoomLabel = new Label();
            zoomLabel.AutoSize = true;
            zoomLabel.ForeColor = Theme.SubText;
            zoomLabel.Font = Theme.F(10.5f);
            zoomLabel.Text = "보기 100%";
            zoomLabel.Margin = new Padding(0, 10, 6, 0);
            var resetBtn = Theme.MakeButton("원래 크기·위치", false);
            resetBtn.Margin = new Padding(0, 0, 8, 0);
            new ToolTip().SetToolTip(resetBtn, "100% 크기로, 가운데로 돌려요\n(Ctrl+휠: 확대·축소, 왼쪽 클릭으로 끌기: 옮기기)");
            resetBtn.Click += delegate { view.ResetView(); view.Focus(); };

            musicBtn = Theme.MakeButton("♪  음악 끄기", false);
            musicBtn.AccessibleName = "음악";
            musicBtn.Visible = false;
            musicBtn.Margin = new Padding(0, 0, 4, 0);
            musicBtn.Click += delegate { ToggleMusic(); view.Focus(); };
            var mm = new ContextMenuStrip();
            mm.Font = Theme.F(10.5f);
            mm.Items.Add("이 화의 음악 빼기 (휴지통으로)", null, delegate { RemoveMusic(); });
            musicBtn.ContextMenuStrip = mm;
            musicVol = new Slider();
            musicVol.Width = 90;
            musicVol.Height = 24;
            musicVol.BackColor = Theme.Bar;
            musicVol.Margin = new Padding(0, 8, 10, 0);
            musicVol.Visible = false;
            musicVol.AccessibleName = "음악 소리 크기";
            new ToolTip().SetToolTip(musicVol, "음악 소리 크기");
            musicVol.SetValue(Edits.GetInt("음악소리", 60) / 100.0);
            musicVol.Changed += delegate
            {
                try { if (music != null) music.settings.volume = (int)Math.Round(musicVol.Value * 100); } catch { }
            };
            musicVol.Released += delegate { Edits.Set("음악소리", ((int)Math.Round(musicVol.Value * 100)).ToString()); };

            var add = Theme.MakeButton("+  넣기", true);
            add.AccessibleName = "넣기";
            var addMenu = new ContextMenuStrip();
            addMenu.Font = Theme.F(11f);
            addMenu.Items.Add("파일에서 넣기 (그림·영상·음악)", null, delegate { AddItem(); });
            addMenu.Items.Add("새 캔버스 만들기 (빈 그림)", null, delegate { NewCanvas(); });
            add.Click += delegate { addMenu.Show(add, new Point(0, add.Height)); };
            bar.RightArea.Controls.Add(zoomLabel);
            bar.RightArea.Controls.Add(resetBtn);
            bar.RightArea.Controls.Add(musicBtn);
            bar.RightArea.Controls.Add(musicVol);
            bar.RightArea.Controls.Add(add);
            Controls.Add(bar);

            UpdateListButton();
            view.SetItems(Store.EpisodeItems(comic, ep));
            StartMusic();
            Disposed += delegate { StopMusic(); };
        }

        public void FocusView() { view.Focus(); }

        Panel BuildSide()
        {
            var p = new Panel();
            p.Dock = DockStyle.Right;
            p.Width = 220;
            p.BackColor = Theme.Bar;
            var head = new Label();
            head.Text = "회차 목록";
            head.Dock = DockStyle.Top;
            head.Height = 46;
            head.Font = Theme.F(12f, FontStyle.Bold);
            head.ForeColor = Theme.Text;
            head.Padding = new Padding(14, 12, 8, 0);
            var list = new FlowLayoutPanel();
            list.Dock = DockStyle.Fill;
            list.FlowDirection = FlowDirection.TopDown;
            list.WrapContents = false;
            list.AutoScroll = true;
            list.Padding = new Padding(10, 2, 6, 10);
            list.BackColor = Theme.Bar;
            Button cur = null;
            foreach (int n in eps)
            {
                var b = Theme.MakeButton(n + "화", n == ep);
                b.AutoSize = false;
                b.Size = new Size(176, 40);
                b.TextAlign = ContentAlignment.MiddleLeft;
                b.Margin = new Padding(0, 0, 0, 6);
                int k = n;
                b.Click += delegate { if (k != ep) main.ShowViewer(comic, k); };
                list.Controls.Add(b);
                if (n == ep) cur = b;
            }
            var close = Theme.MakeButton("목록 닫기", false);
            close.AutoSize = false;
            close.Dock = DockStyle.Bottom;
            close.Height = 46;
            close.Click += delegate { ToggleSide(); };
            p.Controls.Add(list);
            p.Controls.Add(head);
            p.Controls.Add(close);
            if (cur != null) p.VisibleChanged += delegate { if (p.Visible) list.ScrollControlIntoView(cur); };
            return p;
        }

        void ToggleSide()
        {
            sideOpen = !sideOpen;
            side.Visible = sideOpen;
            UpdateListButton();
        }

        void UpdateListButton() { view.ListButton.Text = sideOpen ? "목록 닫기" : "목록 열기"; }

        // ── 음악 (윈도우 미디어 플레이어를 화면 없이 씀) ──
        void StartMusic()
        {
            string mp = Store.MusicPath(comic, ep);
            musicBtn.Visible = mp != null;
            musicVol.Visible = mp != null;
            if (mp == null) return;
            try
            {
                music = Activator.CreateInstance(Type.GetTypeFromProgID("WMPlayer.OCX"));
                music.settings.autoStart = false;
                music.settings.setMode("loop", true);
                music.settings.volume = (int)Math.Round(musicVol.Value * 100);
                music.URL = mp;
                if (musicWanted) music.controls.play();
                musicOn = musicWanted;
            }
            catch
            {
                music = null;
                musicBtn.Visible = false;
                musicVol.Visible = false;
                MessageBox.Show("음악을 틀 수 없어요.\n(윈도우 미디어 플레이어가 꺼져 있을 수 있어요.)", "음악");
            }
            UpdateMusicButton();
        }

        void StopMusic()
        {
            if (music == null) return;
            try { music.controls.stop(); music.close(); Marshal.FinalReleaseComObject((object)music); } catch { }
            music = null;
        }

        void ToggleMusic()
        {
            if (music == null) return;
            try
            {
                if (musicOn) music.controls.pause(); else music.controls.play();
                musicOn = !musicOn;
                musicWanted = musicOn;
            }
            catch { }
            UpdateMusicButton();
        }

        void UpdateMusicButton()
        {
            musicBtn.Text = musicOn ? "♪  음악 끄기" : "♪  음악 켜기";
            musicBtn.ForeColor = musicOn ? Theme.Accent : Theme.Text;
        }

        void RemoveMusic()
        {
            string mp = Store.MusicPath(comic, ep);
            if (mp == null) return;
            var r = MessageBox.Show(FindForm(), ep + "화의 음악을 뺄까요?\n(음악 파일은 휴지통으로 가요.)", "음악 빼기",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
            if (r != DialogResult.Yes) return;
            StopMusic();
            try { Store.Recycle(mp); }
            catch (Exception ex) { MessageBox.Show(FindForm(), "빼지 못했어요.\n" + ex.Message, "음악 빼기"); }
            musicBtn.Visible = musicVol.Visible = Store.MusicPath(comic, ep) != null;
        }

        void AddItem()
        {
            using (var ofd = new OpenFileDialog())
            {
                ofd.Title = ep + "화에 넣을 그림·영상·음악 고르기";
                ofd.Filter = Media.AddFilter;
                if (ofd.ShowDialog(FindForm()) != DialogResult.OK) return;
                string f = ofd.FileName;
                var k = Media.KindOf(f);
                if (k == Kind.None)
                {
                    MessageBox.Show(FindForm(), "이 파일은 넣을 수 없어요.\n\n그림: jpg, png, bmp\n움직이는 그림: gif\n영상: mp4, mkv, avi, wmv, mov\n음악: mp3, wav, m4a, wma, flac", "넣기");
                    return;
                }
                if (k == Kind.Music) { SetMusic(f); return; }
                if (k == Kind.Image || k == Kind.Gif)
                {
                    try { Pics.ReadSize(f); }
                    catch
                    {
                        MessageBox.Show(FindForm(), "이 그림은 열 수 없어요.", "넣기");
                        return;
                    }
                }
                try { Store.AddItem(comic, ep, f); }
                catch (Exception ex) { MessageBox.Show(FindForm(), "넣지 못했어요.\n" + ex.Message, "넣기"); return; }
                view.SetItems(Store.EpisodeItems(comic, ep));
                view.ScrollToLast();
                view.Focus();
            }
        }

        // 이 화 그림들의 너비 (가장 많은 것). 없으면 800
        int CanvasWidth()
        {
            var widths = new List<int>();
            foreach (var f in Store.EpisodeItems(comic, ep))
            {
                if (Media.KindOf(f) == Kind.Video) continue;
                try { widths.Add(Pics.ReadSize(f).Width); } catch { }
            }
            if (widths.Count == 0) return 800;
            return widths.GroupBy(x => x).OrderByDescending(g => g.Count()).First().Key;
        }

        void NewCanvas()
        {
            int w = CanvasWidth();
            using (var d = new CanvasDialog(w))
            {
                if (d.ShowDialog(FindForm()) != DialogResult.OK) return;
                string tmp = Path.Combine(Path.GetTempPath(), "만화보관함_캔버스_" + Guid.NewGuid().ToString("N") + ".png");
                string path;
                try
                {
                    using (var b = new Bitmap(w, d.CanvasHeight))
                    {
                        using (var g = Graphics.FromImage(b)) g.Clear(d.Back);
                        b.Save(tmp, System.Drawing.Imaging.ImageFormat.Png);
                    }
                    path = Store.AddItem(comic, ep, tmp);
                }
                catch (Exception ex) { MessageBox.Show(FindForm(), "캔버스를 만들지 못했어요.\n" + ex.Message, "새 캔버스"); return; }
                finally { try { File.Delete(tmp); } catch { } }
                var list = Store.EpisodeItems(comic, ep);
                view.SetItems(list);
                view.ScrollToLast();
                EditItem(list.IndexOf(path), path);   // 바로 그리기 창 열기
            }
        }

        void SetMusic(string f)
        {
            if (Store.MusicPath(comic, ep) != null)
            {
                var r = MessageBox.Show(FindForm(), "이 화에는 이미 음악이 있어요. 새 음악으로 바꿀까요?\n(원래 음악은 휴지통으로 가요.)", "음악 넣기",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (r != DialogResult.Yes) return;
            }
            StopMusic();
            try { Store.SetMusic(comic, ep, f); }
            catch (Exception ex) { MessageBox.Show(FindForm(), "음악을 넣지 못했어요.\n" + ex.Message, "음악 넣기"); }
            musicWanted = true;
            StartMusic();
        }

        void DeleteItem(int index, string path)
        {
            string what = Media.KindOf(path) == Kind.Video ? "영상" : "그림";
            var r = MessageBox.Show(FindForm(),
                (index + 1) + "번째 " + what + "을 지울까요?\n\n파일이 휴지통으로 가요. (휴지통에서 되살릴 수 있어요.)",
                what + " 지우기", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
            if (r != DialogResult.Yes) return;
            view.ReleaseAll();
            try { Store.DeleteItem(path); }
            catch (Exception ex) { MessageBox.Show(FindForm(), "지우지 못했어요.\n" + ex.Message, what + " 지우기"); }
            view.SetItems(Store.EpisodeItems(comic, ep));
        }

        void EditItem(int index, string path)
        {
            EditorForm ed;
            try { ed = new EditorForm(path); }
            catch (Exception ex) { MessageBox.Show(FindForm(), "이 그림을 열 수 없어요.\n" + ex.Message, "그림 고치기"); return; }
            using (ed)
            {
                if (ed.ShowDialog(FindForm()) == DialogResult.OK)
                    view.SetItems(Store.EpisodeItems(comic, ep));
            }
            view.Focus();
        }
    }

    // 위쪽 가운데 회차 줄: 1화 2화 3화 … 지금 화는 주황색. 휠·화살표로 옆으로 넘김
    class EpisodeStrip : Control
    {
        readonly List<int> eps;
        readonly int current;
        int offset, hover = -1;
        bool centered;
        public event Action<int> Picked;
        const int ChipW = 62, Gap = 6, ArrowW = 24;

        public EpisodeStrip(List<int> eps, int current)
        {
            this.eps = eps;
            this.current = current;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Bar;
            Cursor = Cursors.Hand;
            Text = "회차 줄";
        }

        int TotalW { get { return Math.Max(0, eps.Count * (ChipW + Gap) - Gap); } }
        bool Overflow { get { return TotalW > Width; } }
        int AreaLeft { get { return Overflow ? ArrowW : 0; } }
        int AreaW { get { return Overflow ? Width - ArrowW * 2 : Width; } }
        int MaxOffset { get { return Math.Max(0, TotalW - AreaW); } }

        int ChipX(int i) { return Overflow ? AreaLeft + i * (ChipW + Gap) - offset : (Width - TotalW) / 2 + i * (ChipW + Gap); }

        void Clamp() { offset = Math.Max(0, Math.Min(MaxOffset, offset)); }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (!centered && Width > 0)
            {
                int i = eps.IndexOf(current);
                if (i >= 0) offset = i * (ChipW + Gap) - (AreaW - ChipW) / 2;
                centered = true;
            }
            Clamp();
            Invalidate();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            offset -= e.Delta / 120 * (ChipW + Gap) * 2;
            Clamp();
            Invalidate();
            var he = e as HandledMouseEventArgs;
            if (he != null) he.Handled = true;
        }

        int Hit(Point p)
        {
            if (Overflow && (p.X < ArrowW || p.X >= Width - ArrowW)) return -1;
            for (int i = 0; i < eps.Count; i++)
            {
                int x = ChipX(i);
                if (p.X >= x && p.X < x + ChipW) return i;
            }
            return -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int h = Hit(e.Location);
            if (h != hover) { hover = h; Invalidate(); }
        }

        protected override void OnMouseLeave(EventArgs e) { hover = -1; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (e.Button != MouseButtons.Left) return;
            if (Overflow && e.X < ArrowW) { offset -= (ChipW + Gap) * 3; Clamp(); Invalidate(); return; }
            if (Overflow && e.X >= Width - ArrowW) { offset += (ChipW + Gap) * 3; Clamp(); Invalidate(); return; }
            int i = Hit(e.Location);
            if (i >= 0 && Picked != null) Picked(eps[i]);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.SetClip(new Rectangle(AreaLeft, 0, AreaW, Height));
            for (int i = 0; i < eps.Count; i++)
            {
                int x = ChipX(i);
                if (x + ChipW < AreaLeft || x > AreaLeft + AreaW) continue;
                var r = new Rectangle(x, 2, ChipW, Height - 4);
                bool cur = eps[i] == current;
                Color bg = cur ? Theme.Accent : i == hover ? Theme.CardHover : Theme.Card;
                using (var path = Theme.Round(r, 8))
                using (var br = new SolidBrush(bg))
                    g.FillPath(br, path);
                TextRenderer.DrawText(g, eps[i] + "화", Theme.F(10.5f, FontStyle.Bold), r, cur ? Color.FromArgb(30, 20, 10) : Theme.Text,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.PreserveGraphicsClipping);
            }
            g.ResetClip();
            if (Overflow)
            {
                TextRenderer.DrawText(g, "‹", Theme.F(16f, FontStyle.Bold), new Rectangle(0, 0, ArrowW, Height), offset > 0 ? Theme.Text : Theme.CardHover,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                TextRenderer.DrawText(g, "›", Theme.F(16f, FontStyle.Bold), new Rectangle(Width - ArrowW, 0, ArrowW, Height), offset < MaxOffset ? Theme.Text : Theme.CardHover,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
        }
    }

    // 새 캔버스: 가로는 이 화 그림과 같게 고정, 세로는 숫자나 끌기로
    class CanvasDialog : Form
    {
        readonly int w;
        readonly NumericUpDown hBox;
        readonly Control preview;
        readonly Button bgBtn;
        public Color Back = Color.White;
        public int CanvasHeight { get { return (int)hBox.Value; } }
        float PScale { get { return 360f / (3f * w); } }

        class Pane : Control
        {
            public Pane() { SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true); }
        }

        public CanvasDialog(int width)
        {
            w = width;
            Text = "새 캔버스 만들기";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Theme.Bar; ForeColor = Theme.Text; Font = Theme.F(10.5f);
            ClientSize = new Size(560, 470);

            preview = new Pane();
            preview.Bounds = new Rectangle(16, 16, 200, 400);
            preview.BackColor = Theme.ViewBack;
            preview.Cursor = Cursors.SizeNS;
            preview.Paint += delegate(object s, PaintEventArgs e)
            {
                var g = e.Graphics;
                g.Clear(Theme.ViewBack);
                int pw = (int)(w * PScale), ph = (int)Math.Min(preview.Height - 30, CanvasHeight * PScale);
                var r = new Rectangle((preview.Width - pw) / 2, 20, pw, ph);
                using (var br = new SolidBrush(Back)) g.FillRectangle(br, r);
                using (var pen = new Pen(Theme.Accent, 2)) g.DrawRectangle(pen, r);
                TextRenderer.DrawText(g, w + " × " + CanvasHeight, Theme.F(9f), new Rectangle(0, 0, preview.Width, 18), Theme.SubText, TextFormatFlags.HorizontalCenter);
            };
            MouseEventHandler drag = delegate(object s, MouseEventArgs e)
            {
                if (e.Button != MouseButtons.Left) return;
                int h = (int)Math.Round((e.Y - 20) / PScale);
                hBox.Value = Math.Max(hBox.Minimum, Math.Min(hBox.Maximum, h));
            };
            preview.MouseDown += drag;
            preview.MouseMove += drag;
            Controls.Add(preview);

            int x = 240;
            Controls.Add(Lbl("가로: " + w + " 픽셀\n(이 화의 다른 그림과 같은 너비로 고정돼요)", x, 18, 300, 44));
            Controls.Add(Lbl("세로 (픽셀)", x, 80, 300, 22));
            hBox = new NumericUpDown();
            hBox.Minimum = 50; hBox.Maximum = 30000; hBox.Value = w;
            hBox.Location = new Point(x, 106); hBox.Width = 200; hBox.Font = Theme.F(12f);
            hBox.AccessibleName = "세로 픽셀";
            hBox.ValueChanged += delegate { preview.Invalidate(); };
            Controls.Add(hBox);
            int bx = x;
            foreach (var pair in new[] { new KeyValuePair<string, double>("정사각형", 1), new KeyValuePair<string, double>("세로 2배", 2), new KeyValuePair<string, double>("세로 3배", 3) })
            {
                var b = Theme.MakeButton(pair.Key, false);
                b.Font = Theme.F(9.5f, FontStyle.Bold);
                b.Location = new Point(bx, 146);
                double k = pair.Value;
                b.Click += delegate { hBox.Value = Math.Min(hBox.Maximum, (decimal)Math.Round(w * k)); };
                Controls.Add(b);
                bx += 98;
            }
            Controls.Add(Lbl("왼쪽 그림을 위아래로 끌어서 세로 길이를 정할 수도 있어요.", x, 196, 300, 44));
            bgBtn = Theme.MakeButton("바탕색 고르기", false);
            bgBtn.Location = new Point(x, 250);
            bgBtn.Click += delegate
            {
                using (var cd = new ColorDialog())
                {
                    cd.Color = Back; cd.FullOpen = true;
                    cd.CustomColors = Edits.LoadColors().Take(16).Select(c => c.R | (c.G << 8) | (c.B << 16)).ToArray();
                    if (cd.ShowDialog(this) == DialogResult.OK) { Back = cd.Color; preview.Invalidate(); }
                }
            };
            Controls.Add(bgBtn);

            var ok = Theme.MakeButton("만들기", true);
            ok.DialogResult = DialogResult.OK;
            var cancel = Theme.MakeButton("취소", false);
            cancel.DialogResult = DialogResult.Cancel;
            Controls.Add(ok); Controls.Add(cancel);
            AcceptButton = ok; CancelButton = cancel;
            Shown += delegate
            {
                cancel.Location = new Point(ClientSize.Width - 16 - cancel.Width, 412);
                ok.Location = new Point(cancel.Left - 10 - ok.Width, 412);
            };
        }

        Label Lbl(string t, int x, int y, int w2, int h)
        {
            var l = new Label();
            l.Text = t; l.Location = new Point(x, y); l.Size = new Size(w2, h); l.ForeColor = Theme.SubText;
            return l;
        }
    }

    // 윈도우 미디어 플레이어 (영상 그림만 쓰고, 조작 막대는 직접 그림)
    class WmpHost : AxHost
    {
        public WmpHost() : base("6bf52a52-394a-11d3-b153-00c04f79faa6") { }
        public object Ocx { get { return GetOcx(); } }
    }

    // ───────────────────────── 영상 한 칸 ─────────────────────────
    // 처음엔 첫 장면 + 큰 ▶. 누르면 재생. 아래 막대(재생·-5초·+5초·위치·소리)는 마우스를 움직일 때만 보임.
    class VideoBox : Control
    {
        [DllImport("user32.dll")]
        static extern short GetAsyncKeyState(int key);

        static int volume = -1;
        const int BarH = 46;

        readonly string path;
        Bitmap poster;
        WmpHost player;
        bool failed, started, wasDown, downInVideo, cursorHidden;
        readonly Panel bar;
        readonly IconButton playBtn, muteBtn, fullBtn;
        public event Action<VideoBox> FullToggle;   // 전체 화면 켜기·끄기 요청
        public bool IsFull;
        readonly Button back5, fwd5;
        readonly Slider seek, vol;
        readonly Label time;
        readonly Timer poll;
        Point lastMouse;
        DateTime lastMove = DateTime.Now;

        public VideoBox(string path)
        {
            this.path = path;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Color.Black;
            Cursor = Cursors.Hand;
            Text = "영상";
            if (volume < 0) volume = Edits.GetInt("영상소리", 80);
            poster = ShellThumb.Get(path, 720);

            bar = new Panel();
            bar.BackColor = Color.FromArgb(22, 22, 26);
            bar.Visible = false;
            playBtn = new IconButton(IconKind.Pause);
            playBtn.BackColor = bar.BackColor;
            playBtn.AccessibleName = "재생·멈춤";
            back5 = SmallButton("-5초");
            fwd5 = SmallButton("+5초");
            time = new Label();
            time.ForeColor = Color.White;
            time.Font = Theme.F(9.5f);
            time.AutoSize = false;
            time.TextAlign = ContentAlignment.MiddleLeft;
            time.Text = "0:00 / 0:00";
            seek = new Slider();
            seek.BackColor = bar.BackColor;
            seek.AccessibleName = "영상 위치";
            muteBtn = new IconButton(IconKind.Speaker);
            muteBtn.BackColor = bar.BackColor;
            muteBtn.AccessibleName = "소리 끄기";
            vol = new Slider();
            vol.BackColor = bar.BackColor;
            vol.AccessibleName = "영상 소리 크기";
            vol.SetValue(volume / 100.0);
            fullBtn = new IconButton(IconKind.FullOn);
            fullBtn.BackColor = bar.BackColor;
            fullBtn.AccessibleName = "전체 화면";
            new ToolTip().SetToolTip(fullBtn, "전체 화면 (다시 누르거나 Esc 로 돌아와요)");
            fullBtn.Click += delegate { if (FullToggle != null) FullToggle(this); };
            bar.Controls.AddRange(new Control[] { playBtn, back5, fwd5, time, seek, muteBtn, vol, fullBtn });
            bar.Resize += delegate { LayoutBar(); };
            Controls.Add(bar);

            playBtn.Click += delegate { TogglePlay(); };
            back5.Click += delegate { SeekBy(-5); };
            fwd5.Click += delegate { SeekBy(5); };
            seek.Changed += delegate
            {
                try { double d = Duration; if (d > 0) O.controls.currentPosition = seek.Value * d; } catch { }
            };
            vol.Changed += delegate
            {
                volume = (int)Math.Round(vol.Value * 100);
                try { O.settings.volume = volume; O.settings.mute = false; muteBtn.SetKind(IconKind.Speaker); } catch { }
            };
            vol.Released += delegate { Edits.Set("영상소리", volume.ToString()); };
            muteBtn.Click += delegate
            {
                try
                {
                    bool m = !(bool)O.settings.mute;
                    O.settings.mute = m;
                    muteBtn.SetKind(m ? IconKind.Mute : IconKind.Speaker);
                }
                catch { }
            };

            poll = new Timer();
            poll.Interval = 50;
            poll.Tick += delegate { Poll(); };
        }

        static Button SmallButton(string text)
        {
            var b = Theme.MakeButton(text, false);
            b.AutoSize = false;
            b.Size = new Size(54, 30);
            b.Padding = new Padding(0);
            b.Font = Theme.F(9.5f, FontStyle.Bold);
            b.BackColor = Color.FromArgb(48, 48, 54);
            return b;
        }

        dynamic O { get { return player == null ? null : player.Ocx; } }

        public bool Started { get { return started; } }

        public bool MouseInside
        {
            get { return Visible && IsHandleCreated && RectangleToScreen(ClientRectangle).Contains(Cursor.Position); }
        }

        double Duration
        {
            get
            {
                try { var m = O.currentMedia; return m == null ? 0 : (double)m.duration; } catch { return 0; }
            }
        }

        int State { get { try { return player == null ? 0 : (int)O.playState; } catch { return 0; } } }

        void LayoutBar()
        {
            int h = bar.Height, w = bar.Width;
            playBtn.Location = new Point(6, (h - 36) / 2);
            back5.Location = new Point(46, (h - 30) / 2);
            fwd5.Location = new Point(104, (h - 30) / 2);
            time.Bounds = new Rectangle(164, 0, 100, h);
            fullBtn.Location = new Point(w - 42, (h - 36) / 2);
            vol.Bounds = new Rectangle(w - 42 - 92, (h - 22) / 2, 88, 22);
            muteBtn.Location = new Point(vol.Left - 38, (h - 36) / 2);
            seek.Bounds = new Rectangle(268, (h - 22) / 2, Math.Max(20, muteBtn.Left - 8 - 268), 22);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            bar.Bounds = new Rectangle(0, Height - BarH, Width, BarH);
            if (player != null) player.Bounds = ClientRectangle;
            bar.BringToFront();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Color.Black);
            if (started && !failed) return;
            g.InterpolationMode = InterpolationMode.HighQualityBilinear;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            if (poster != null)
            {
                float s = Math.Min(Width / (float)poster.Width, Height / (float)poster.Height);
                int w = (int)(poster.Width * s), h = (int)(poster.Height * s);
                g.DrawImage(poster, (Width - w) / 2, (Height - h) / 2, w, h);
                using (var br = new SolidBrush(Color.FromArgb(70, 0, 0, 0))) g.FillRectangle(br, ClientRectangle);
            }
            if (failed)
            {
                TextRenderer.DrawText(g, "이 영상은 틀 수 없어요\n(윈도우 미디어 플레이어가 필요해요)", Theme.F(11f), ClientRectangle, Color.White,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
                return;
            }
            // 큰 재생 단추
            float cx = Width / 2f, cy = Height / 2f, rad = Math.Max(26, Math.Min(46, Math.Min(Width, Height) / 7f));
            using (var br = new SolidBrush(Color.FromArgb(185, 20, 20, 24))) g.FillEllipse(br, cx - rad, cy - rad, rad * 2, rad * 2);
            using (var pen = new Pen(Color.FromArgb(230, 255, 255, 255), 3f)) g.DrawEllipse(pen, cx - rad, cy - rad, rad * 2, rad * 2);
            float t = rad * 0.45f;
            g.FillPolygon(Brushes.White, new[] { new PointF(cx - t * 0.7f, cy - t), new PointF(cx - t * 0.7f, cy + t), new PointF(cx + t, cy) });
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (e.Button == MouseButtons.Left && !started) Start();
        }

        void Start()
        {
            if (started) return;
            try
            {
                var p = new WmpHost();
                ((ISupportInitialize)p).BeginInit();
                Controls.Add(p);
                ((ISupportInitialize)p).EndInit();
                p.Bounds = ClientRectangle;
                dynamic o = p.Ocx;
                o.uiMode = "none";
                o.stretchToFit = true;
                o.enableContextMenu = false;
                o.settings.autoStart = true;
                o.settings.volume = volume;
                o.URL = path;
                player = p;
                started = true;
                bar.Visible = true;
                bar.BringToFront();
                lastMove = DateTime.Now;
                poll.Start();
            }
            catch { failed = true; }
            Invalidate();
        }

        public void TogglePlay()
        {
            if (!started) { Start(); return; }
            try
            {
                if (State == 3) O.controls.pause();
                else O.controls.play();
            }
            catch { }
            lastMove = DateTime.Now;
        }

        public void SetFullLook(bool on)
        {
            IsFull = on;
            fullBtn.SetKind(on ? IconKind.FullOff : IconKind.FullOn);
            lastMove = DateTime.Now;
        }

        public void Pause()
        {
            try { if (State == 3) O.controls.pause(); } catch { }
        }

        public void SeekBy(double sec)
        {
            if (!started) return;
            try
            {
                double d = Duration;
                double p = (double)O.controls.currentPosition + sec;
                if (p < 0) p = 0;
                if (d > 0 && p > d - 0.2) p = Math.Max(0, d - 0.2);
                O.controls.currentPosition = p;
            }
            catch { }
            lastMove = DateTime.Now;
        }

        static string Fmt(double s)
        {
            if (double.IsNaN(s) || s < 0) s = 0;
            var t = TimeSpan.FromSeconds(s);
            return t.TotalHours >= 1 ? string.Format("{0}:{1:00}:{2:00}", (int)t.TotalHours, t.Minutes, t.Seconds) : string.Format("{0}:{1:00}", t.Minutes, t.Seconds);
        }

        void Poll()
        {
            if (player == null || IsDisposed) return;
            int st = State;
            double pos = 0, dur = Duration;
            try { pos = (double)O.controls.currentPosition; } catch { }
            if (!seek.Dragging && dur > 0) seek.SetValue(pos / dur);
            string tt = Fmt(pos) + " / " + Fmt(dur);
            if (time.Text != tt) time.Text = tt;
            playBtn.SetKind(st == 3 ? IconKind.Pause : IconKind.Play);
            try
            {
                if ((bool)O.fullScreen)
                {
                    O.fullScreen = false;
                    if (FullToggle != null) FullToggle(this);
                }
            }
            catch { }

            var m = Cursor.Position;
            bool inside = MouseInside;
            if (m != lastMouse) { lastMouse = m; if (inside) lastMove = DateTime.Now; }

            // 영상 위를 누르면 재생·멈춤 (막대 위는 빼고)
            var form = FindForm();
            bool active = form != null && Form.ActiveForm == form;
            bool down = (GetAsyncKeyState(0x01) & 0x8000) != 0;
            var local = PointToClient(m);
            bool inVideo = inside && !(bar.Visible && bar.Bounds.Contains(local));
            if (down && !wasDown) downInVideo = inVideo && active;
            if (!down && wasDown && downInVideo && inVideo && active) TogglePlay();
            wasDown = down;

            bool show = st != 3 || seek.Dragging || vol.Dragging || (inside && (DateTime.Now - lastMove).TotalSeconds < 2.5);
            if (bar.Visible != show)
            {
                bar.Visible = show;
                if (show) bar.BringToFront();
            }
            // 재생 중에 영상 위에서 마우스를 가만히 두면 막대와 함께 마우스도 숨김 (움직이면 다시 보임)
            SetCursorHidden(inside && active && st == 3 && !show);
        }

        void SetCursorHidden(bool hide)
        {
            if (hide == cursorHidden) return;
            if (hide) Cursor.Hide(); else Cursor.Show();
            cursorHidden = hide;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                SetCursorHidden(false);
                poll.Stop();
                poll.Dispose();
                if (player != null)
                {
                    try { O.controls.stop(); O.close(); } catch { }
                    player.Dispose();
                    player = null;
                }
                if (poster != null) { poster.Dispose(); poster = null; }
            }
            base.Dispose(disposing);
        }
    }

    // ───────────────────────── 이어 보기 화면 ─────────────────────────
    // 스크롤 막대를 직접 다뤄서 다시 열어도 스크롤이 항상 됨. 보이는 것만 불러와서 가벼움.
    class StripView : Control, IMessageFilter
    {
        class Item
        {
            public string Path;
            public Kind Kind;
            public Size Natural;
            public bool Edited;
            public int Top, Height;
            public Bitmap Source, Cache;
            public int CacheW;
            public Image Anim;
            public MemoryStream AnimStream;
            public bool Animating;
            public VideoBox Box;
        }

        const int MaxBase = 900;
        const int NavH = 140;
        const float MinZoom = 0.3f, MaxZoom = 3f;

        readonly List<Item> items = new List<Item>();
        float zoom = 1f;
        int dispW = 100, contentW, contentH, navTop, sx, sy;
        bool fast, updatingBars, panning, filterOn;
        Point panStart;
        int panSx, panSy;
        readonly Timer fastTimer;
        readonly VScrollBar vbar;
        readonly HScrollBar hbar;
        readonly FlowLayoutPanel nav;
        public readonly Button PrevButton, ListButton, NextButton;
        readonly ContextMenuStrip menu;
        readonly EventHandler frameChanged;
        public string EndText = "";

        public event Action<int, string> DeleteRequested;
        public event Action<int, string> EditRequested;
        public event Action ZoomChanged;
        public event Action<bool> VideoFullChanged;
        VideoBox fullBox;

        public StripView()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint |
                     ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            BackColor = Theme.ViewBack;
            Text = "만화 보기";
            AccessibleName = "만화 보기";

            vbar = new VScrollBar();
            vbar.Dock = DockStyle.Right;
            vbar.ValueChanged += delegate { if (!updatingBars) { sy = vbar.Value; AfterScroll(); } };
            hbar = new HScrollBar();
            hbar.Dock = DockStyle.Bottom;
            hbar.Visible = false;
            hbar.ValueChanged += delegate { if (!updatingBars) { sx = hbar.Value; AfterScroll(); } };
            Controls.Add(vbar);
            Controls.Add(hbar);

            nav = new FlowLayoutPanel();
            nav.AutoSize = true;
            nav.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            nav.WrapContents = false;
            nav.BackColor = BackColor;
            PrevButton = Theme.MakeButton("◀  이전 화", false);
            ListButton = Theme.MakeButton("목록 열기", false);
            NextButton = Theme.MakeButton("다음 화  ▶", false);
            foreach (var b in new[] { PrevButton, ListButton, NextButton })
            {
                b.Margin = new Padding(8, 0, 8, 0);
                b.Font = Theme.F(12f, FontStyle.Bold);
                b.Padding = new Padding(20, 10, 20, 10);
                nav.Controls.Add(b);
            }
            ListButton.MinimumSize = new Size(140, 0);
            Controls.Add(nav);

            menu = new ContextMenuStrip();
            menu.Font = Theme.F(10.5f);
            frameChanged = delegate
            {
                if (IsHandleCreated && !IsDisposed)
                {
                    try { BeginInvoke((MethodInvoker)Invalidate); } catch { }
                }
            };
            fastTimer = new Timer();
            fastTimer.Interval = 250;
            fastTimer.Tick += delegate { fastTimer.Stop(); fast = false; Invalidate(); };
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            if (!filterOn) { Application.AddMessageFilter(this); filterOn = true; }
        }

        // 휠·키를 영상이나 단추 위에서 눌러도 이 화면이 받게 함
        public bool PreFilterMessage(ref Message m)
        {
            const int WM_MOUSEWHEEL = 0x020A, WM_KEYDOWN = 0x0100;
            if (m.Msg != WM_MOUSEWHEEL && m.Msg != WM_KEYDOWN) return false;
            if (IsDisposed || !Visible || !IsHandleCreated) return false;
            var form = FindForm();
            if (form == null || Form.ActiveForm != form) return false;
            if (fullBox != null)
            {
                if (m.Msg == WM_MOUSEWHEEL) return true;
                var k = (Keys)(int)m.WParam.ToInt64();
                if (k == Keys.Escape) { ToggleVideoFull(fullBox); return true; }
                if (k == Keys.Left) { fullBox.SeekBy(-5); return true; }
                if (k == Keys.Right) { fullBox.SeekBy(5); return true; }
                if (k == Keys.Space) { fullBox.TogglePlay(); return true; }
                return false;
            }
            var c = Control.FromChildHandle(m.HWnd);
            if (c == null || c == this || !IsMine(c)) return false;
            if (m.Msg == WM_MOUSEWHEEL)
            {
                long wp = m.WParam.ToInt64(), lp = m.LParam.ToInt64();
                int delta = (short)((wp >> 16) & 0xFFFF);
                var screen = new Point((short)(lp & 0xFFFF), (short)((lp >> 16) & 0xFFFF));
                HandleWheel(PointToClient(screen), delta);
                return true;
            }
            // 영상에 손이 간 뒤 키보드: ← → 5초, 스페이스 재생·멈춤
            var box = FindBox(c);
            if (box == null) return false;
            var key = (Keys)(int)m.WParam.ToInt64();
            if (key == Keys.Left) { box.SeekBy(-5); return true; }
            if (key == Keys.Right) { box.SeekBy(5); return true; }
            if (key == Keys.Space) { box.TogglePlay(); return true; }
            return false;
        }

        bool IsMine(Control c)
        {
            for (var p = c; p != null; p = p.Parent) if (p == this) return true;
            return false;
        }

        static VideoBox FindBox(Control c)
        {
            for (var p = c; p != null; p = p.Parent) { var b = p as VideoBox; if (b != null) return b; }
            return null;
        }

        public float Zoom { get { return zoom; } }

        Rectangle View
        {
            get
            {
                int w = ClientSize.Width - vbar.Width;
                int h = ClientSize.Height - (hbar.Visible ? hbar.Height : 0);
                return new Rectangle(0, 0, Math.Max(1, w), Math.Max(1, h));
            }
        }

        int StripLeft { get { return (contentW - dispW) / 2 - sx; } }

        // 작게 볼 때도 옆으로 옮길 수 있게: 만화 가운데가 화면 왼쪽 끝~오른쪽 끝까지 갈 수 있음 (빈 곳은 검은색)
        int MinSx { get { return Math.Min(0, contentW / 2 - View.Width); } }
        int MaxSx { get { return Math.Max(contentW - View.Width, contentW / 2); } }

        public void SetItems(List<string> paths)
        {
            ReleaseAll();
            items.Clear();
            foreach (var p in paths)
            {
                var it = new Item();
                it.Path = p;
                it.Kind = Media.KindOf(p);
                if (it.Kind == Kind.Video) it.Natural = Media.VideoSize(p);
                else
                {
                    try { it.Natural = Pics.ReadSize(p); } catch { it.Natural = new Size(800, 200); }
                }
                if (it.Natural.Width <= 0 || it.Natural.Height <= 0) it.Natural = new Size(800, 200);
                it.Edited = it.Kind == Kind.Image && Edits.HasAny(p);
                items.Add(it);
            }
            Relayout();
        }

        void Relayout()
        {
            var v = View;
            int baseW = Math.Max(100, Math.Min(MaxBase, v.Width - 40));
            dispW = Math.Max(40, (int)Math.Round(baseW * zoom));
            int y = 0;
            foreach (var it in items)
            {
                int h = Math.Max(1, (int)Math.Round((double)it.Natural.Height * dispW / it.Natural.Width));
                if (it.Kind == Kind.Video)
                {
                    int cap = Math.Max(240, (int)((v.Height - 40) * zoom));
                    if (h > cap) h = cap;
                }
                it.Top = y;
                it.Height = h;
                y += h;
            }
            navTop = items.Count == 0 ? 40 : y + 70;
            contentH = navTop + NavH;
            contentW = Math.Max(v.Width, dispW + 40);
            bool needH = contentW > v.Width && fullBox == null;
            if (hbar.Visible != needH) hbar.Visible = needH;
            UpdateBars();
            PositionChildren();
            Invalidate();
        }

        void UpdateBars()
        {
            var v = View;
            int maxY = Math.Max(0, contentH - v.Height);
            int maxX = Math.Max(0, contentW - v.Width);
            sy = Math.Min(Math.Max(0, sy), maxY);
            sx = Math.Min(Math.Max(MinSx, sx), MaxSx);
            updatingBars = true;
            try
            {
                if (maxY > 0)
                {
                    vbar.Minimum = 0;
                    vbar.Maximum = contentH - 1;
                    vbar.LargeChange = v.Height;
                    vbar.SmallChange = 60;
                    vbar.Enabled = true;
                }
                else { vbar.Maximum = 0; vbar.LargeChange = 1; vbar.Enabled = false; }
                vbar.Value = sy;
                if (maxX > 0)
                {
                    hbar.Minimum = 0;
                    hbar.Maximum = contentW - 1;
                    hbar.LargeChange = v.Width;
                    hbar.SmallChange = 60;
                    hbar.Enabled = true;
                }
                else { hbar.Maximum = 0; hbar.LargeChange = 1; hbar.Enabled = false; }
                hbar.Value = Math.Min(Math.Max(0, sx), maxX);
            }
            finally { updatingBars = false; }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (vbar != null) Relayout();
        }

        public void SetScrollY(int y)
        {
            int max = Math.Max(0, contentH - View.Height);
            y = Math.Min(Math.Max(0, y), max);
            sy = y;
            updatingBars = true;
            try { if (vbar.Enabled && y <= vbar.Maximum) vbar.Value = y; } finally { updatingBars = false; }
            AfterScroll();
        }

        public void SetScrollX(int x)
        {
            x = Math.Min(Math.Max(MinSx, x), MaxSx);
            sx = x;
            updatingBars = true;
            try { if (hbar.Enabled) hbar.Value = Math.Min(Math.Max(0, x), Math.Max(0, contentW - View.Width)); } finally { updatingBars = false; }
            AfterScroll();
        }

        void AfterScroll()
        {
            PositionChildren();
            Invalidate();
        }

        public void ScrollToLast()
        {
            if (items.Count == 0) return;
            SetScrollY(items[items.Count - 1].Top);
        }

        // 100% 크기 + 가운데로
        public void ResetView()
        {
            var v = View;
            ZoomAt(1f, new Point(v.Width / 2, v.Height / 2));
            SetScrollX((contentW - v.Width) / 2);
            if (ZoomChanged != null) ZoomChanged();
        }

        // 마우스가 가리키는 곳을 기준으로 확대·축소
        public void ZoomAt(float z, Point p)
        {
            z = Math.Max(MinZoom, Math.Min(MaxZoom, z));
            if (Math.Abs(z - zoom) < 0.001f) return;
            double ry = (p.Y + sy) / (double)Math.Max(1, dispW);
            double rx = (p.X - StripLeft) / (double)Math.Max(1, dispW);
            zoom = z;
            fast = true;
            fastTimer.Stop();
            fastTimer.Start();
            Relayout();
            SetScrollY((int)Math.Round(ry * dispW - p.Y));
            SetScrollX((int)Math.Round(rx * dispW + (contentW - dispW) / 2.0 - p.X));
            if (ZoomChanged != null) ZoomChanged();
        }

        void HandleWheel(Point p, int delta)
        {
            if ((ModifierKeys & Keys.Control) != 0)
                ZoomAt((float)(zoom * Math.Pow(1.1, delta / 120.0)), p);   // 위로 = 확대, 아래로 = 축소
            else if ((ModifierKeys & Keys.Shift) != 0)
                SetScrollX(sx - delta * 3 / 2);
            else
                SetScrollY(sy - delta * 3 / 2);                            // 한 칸에 180 픽셀 정도
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            HandleWheel(e.Location, e.Delta);
            var he = e as HandledMouseEventArgs;
            if (he != null) he.Handled = true;
        }

        // 왼쪽 클릭으로 끌면 보고 싶은 쪽으로 옮김
        protected override void OnMouseDown(MouseEventArgs e)
        {
            Focus();
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left)
            {
                panning = true;
                panStart = e.Location;
                panSx = sx;
                panSy = sy;
                Capture = true;
                Cursor = Cursors.SizeAll;
                return;
            }
            if (e.Button != MouseButtons.Right) return;
            int idx = HitTest(e.Location);
            if (idx < 0) return;
            var it = items[idx];
            string path = it.Path;
            menu.Items.Clear();
            if (it.Kind == Kind.Image)
                menu.Items.Add("그림 고치기 (브러시·자막·도형)", null, delegate { if (EditRequested != null) EditRequested(idx, path); });
            else if (it.Kind == Kind.Gif)
            {
                var mi = new ToolStripMenuItem("움직이는 그림은 고칠 수 없어요");
                mi.Enabled = false;
                menu.Items.Add(mi);
            }
            menu.Items.Add("이 그림 지우기 (휴지통으로)", null, delegate { if (DeleteRequested != null) DeleteRequested(idx, path); });
            menu.Show(this, e.Location);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!panning) return;
            SetScrollX(panSx - (e.X - panStart.X));
            SetScrollY(panSy - (e.Y - panStart.Y));
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (!panning) return;
            panning = false;
            Capture = false;
            Cursor = Cursors.Default;
        }

        int HitTest(Point p)
        {
            int left = StripLeft;
            if (p.X < left || p.X >= left + dispW) return -1;
            int y = p.Y + sy;
            for (int i = 0; i < items.Count; i++)
                if (y >= items[i].Top && y < items[i].Top + items[i].Height) return i;
            return -1;
        }

        protected override bool IsInputKey(Keys keyData)
        {
            switch (keyData)
            {
                case Keys.Up: case Keys.Down: case Keys.Left: case Keys.Right:
                case Keys.PageUp: case Keys.PageDown: case Keys.Home: case Keys.End: case Keys.Space:
                    return true;
            }
            return base.IsInputKey(keyData);
        }

        VideoBox HoveredVideo()
        {
            foreach (var it in items) if (it.Box != null && it.Box.Started && it.Box.MouseInside) return it.Box;
            return null;
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            int page = Math.Max(100, View.Height - 80);
            if (e.Control && (e.KeyCode == Keys.D0 || e.KeyCode == Keys.NumPad0)) { ResetView(); e.Handled = true; return; }
            var vb = HoveredVideo();
            if (vb != null && (e.KeyCode == Keys.Left || e.KeyCode == Keys.Right))
            {
                vb.SeekBy(e.KeyCode == Keys.Left ? -5 : 5);
                e.Handled = true;
                return;
            }
            switch (e.KeyCode)
            {
                case Keys.Down: SetScrollY(sy + 90); break;
                case Keys.Up: SetScrollY(sy - 90); break;
                case Keys.Right: SetScrollX(sx + 90); break;
                case Keys.Left: SetScrollX(sx - 90); break;
                case Keys.PageDown: case Keys.Space: SetScrollY(sy + page); break;
                case Keys.PageUp: SetScrollY(sy - page); break;
                case Keys.Home: SetScrollY(0); break;
                case Keys.End: SetScrollY(contentH); break;
                default: base.OnKeyDown(e); return;
            }
            e.Handled = true;
        }

        // ── 영상 칸·끝 단추 위치 맞추기 ──
        void PositionChildren()
        {
            if (fullBox != null)
            {
                fullBox.Bounds = ClientRectangle;
                fullBox.Visible = true;
                fullBox.BringToFront();
                foreach (var it in items) if (it.Box != null && it.Box != fullBox) it.Box.Visible = false;
                return;
            }
            var v = View;
            int left = StripLeft;
            for (int i = 0; i < items.Count; i++)
            {
                var it = items[i];
                if (it.Kind != Kind.Video) continue;
                var r = new Rectangle(left, it.Top - sy, dispW, it.Height);
                bool vis = r.Bottom > 0 && r.Top < v.Height && r.Right > 0 && r.Left < v.Width;
                if (vis && it.Box == null) CreateBox(it, i);
                if (it.Box != null)
                {
                    it.Box.Bounds = r;
                    if (!vis && it.Box.Visible) { it.Box.Pause(); it.Box.Visible = false; }
                    else if (vis && !it.Box.Visible) it.Box.Visible = true;
                }
            }
            nav.Location = new Point((contentW - nav.Width) / 2 - sx, navTop - sy);
            vbar.BringToFront();
            hbar.BringToFront();
        }

        void CreateBox(Item it, int index)
        {
            var b = new VideoBox(it.Path);
            b.FullToggle += ToggleVideoFull;
            var m = new ContextMenuStrip();
            m.Font = Theme.F(10.5f);
            string path = it.Path;
            m.Items.Add("이 영상 지우기 (휴지통으로)", null, delegate { if (DeleteRequested != null) DeleteRequested(index, path); });
            b.ContextMenuStrip = m;
            foreach (Control c in b.Controls) c.ContextMenuStrip = m;
            b.Disposed += delegate { m.Dispose(); };
            Controls.Add(b);
            it.Box = b;
        }

        // 영상 하나를 이 화면 전체로 키우기 / 되돌리기
        public void ToggleVideoFull(VideoBox box)
        {
            if (fullBox == null)
            {
                fullBox = box;
                box.SetFullLook(true);
                vbar.Visible = false;
                hbar.Visible = false;
                nav.Visible = false;
                if (VideoFullChanged != null) VideoFullChanged(true);
                PositionChildren();
                Invalidate();
            }
            else
            {
                var b = fullBox;
                fullBox = null;
                b.SetFullLook(false);
                vbar.Visible = true;
                nav.Visible = true;
                if (VideoFullChanged != null) VideoFullChanged(false);
                Relayout();
                for (int i = 0; i < items.Count; i++)
                    if (items[i].Box == b) { SetScrollY(items[i].Top - Math.Max(0, (View.Height - items[i].Height) / 2)); break; }
            }
        }

        // ── 그리기 ──
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            if (fullBox != null) { g.Clear(Color.Black); return; }
            g.Clear(BackColor);
            var v = View;
            int left = StripLeft;
            int first = -1, last = -1;
            for (int i = 0; i < items.Count; i++)
            {
                var it = items[i];
                int y = it.Top - sy;
                if (y + it.Height < 0 || y > v.Height) continue;
                if (first < 0) first = i;
                last = i;
                var r = new Rectangle(left, y, dispW, it.Height);
                if (it.Kind == Kind.Image) DrawStill(g, it, r);
                else if (it.Kind == Kind.Gif) DrawGif(g, it, r);
                else g.FillRectangle(Brushes.Black, r);
            }
            if (items.Count > 0 && EndText.Length > 0)
            {
                var er = new Rectangle(left, navTop - 56 - sy, dispW, 36);
                TextRenderer.DrawText(g, EndText, Theme.F(12f), er, Theme.SubText,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
            Evict(first, last);
        }

        static void DrawBroken(Graphics g, Rectangle r, string text)
        {
            g.FillRectangle(Brushes.DimGray, r);
            TextRenderer.DrawText(g, text, Theme.F(11f), r, Color.White,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
        }

        void DrawStill(Graphics g, Item it, Rectangle r)
        {
            if (it.Cache != null && it.CacheW == dispW && !fast) { g.DrawImageUnscaled(it.Cache, r.X, r.Y); return; }
            if (it.Source == null)
            {
                try { it.Source = it.Edited ? Edits.Compose(it.Path) : Pics.Load(it.Path); } catch { it.Source = null; }
            }
            if (it.Source == null) { DrawBroken(g, r, "그림을 열 수 없어요"); return; }
            if (fast)
            {
                // 확대·축소 중에는 빠르게, 멈추면 곱게 다시 그림
                g.InterpolationMode = InterpolationMode.Low;
                g.DrawImage(it.Cache != null ? (Image)it.Cache : it.Source, r);
                g.InterpolationMode = InterpolationMode.Default;
                return;
            }
            if (it.Cache != null) it.Cache.Dispose();
            it.Cache = Pics.Scale(it.Source, r.Width, r.Height);
            it.CacheW = dispW;
            g.DrawImageUnscaled(it.Cache, r.X, r.Y);
        }

        void DrawGif(Graphics g, Item it, Rectangle r)
        {
            if (it.Anim == null)
            {
                try
                {
                    it.AnimStream = new MemoryStream(File.ReadAllBytes(it.Path));
                    it.Anim = Image.FromStream(it.AnimStream);
                }
                catch { DrawBroken(g, r, "그림을 열 수 없어요"); return; }
            }
            if (!it.Animating && ImageAnimator.CanAnimate(it.Anim))
            {
                ImageAnimator.Animate(it.Anim, frameChanged);
                it.Animating = true;
            }
            ImageAnimator.UpdateFrames(it.Anim);
            g.InterpolationMode = fast ? InterpolationMode.Low : InterpolationMode.HighQualityBilinear;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.DrawImage(it.Anim, r);
            g.InterpolationMode = InterpolationMode.Default;
            g.PixelOffsetMode = PixelOffsetMode.Default;
        }

        // 화면에서 멀리 벗어난 것은 메모리에서 내림
        void Evict(int first, int last)
        {
            for (int i = 0; i < items.Count; i++)
            {
                if (first >= 0 && i >= first - 2 && i <= last + 2) continue;
                Unload(items[i], false);
            }
        }

        void Unload(Item it, bool boxes)
        {
            if (it.Cache != null) { it.Cache.Dispose(); it.Cache = null; }
            if (it.Source != null) { it.Source.Dispose(); it.Source = null; }
            if (it.Anim != null)
            {
                if (it.Animating) { ImageAnimator.StopAnimate(it.Anim, frameChanged); it.Animating = false; }
                it.Anim.Dispose(); it.Anim = null;
            }
            if (it.AnimStream != null) { it.AnimStream.Dispose(); it.AnimStream = null; }
            if (boxes && it.Box != null)
            {
                Controls.Remove(it.Box);
                it.Box.Dispose();
                it.Box = null;
            }
        }

        public void ReleaseAll()
        {
            foreach (var it in items) Unload(it, true);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (filterOn) { Application.RemoveMessageFilter(this); filterOn = false; }
                ReleaseAll();
                fastTimer.Dispose();
                menu.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
