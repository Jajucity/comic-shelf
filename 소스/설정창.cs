// 만화 보관함 — 설정 창 (일반 · AI 번역 · 자동 번역·글자 읽기)
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace ComicShelf
{
    class SettingsForm : Form
    {
        readonly List<Button> tabs = new List<Button>();
        readonly List<Panel> pages = new List<Panel>();

        public SettingsForm()
        {
            Text = "설정";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Theme.Bar; ForeColor = Theme.Text; Font = Theme.F(10.5f);
            ClientSize = new Size(620, 640);

            var ver = new Label();
            ver.Text = "만화 보관함  " + Updater.AppVersion + " 판";
            ver.Font = Theme.F(14f, FontStyle.Bold);
            ver.AutoSize = true;
            ver.Location = new Point(20, 14);
            Controls.Add(ver);

            string[] names = { "일반", "AI 번역", "자동 번역·글자 읽기" };
            int tx = 20;
            for (int i = 0; i < names.Length; i++)
            {
                var b = Theme.MakeButton(names[i], false);
                b.Location = new Point(tx, 54);
                int k = i;
                b.Click += delegate { ShowPage(k); };
                Controls.Add(b);
                tabs.Add(b);
                tx += b.PreferredSize.Width + 8;
                var p = new Panel();
                p.Bounds = new Rectangle(0, 100, ClientSize.Width, ClientSize.Height - 100 - 70);
                p.BackColor = Theme.Bar;
                p.Visible = false;
                Controls.Add(p);
                pages.Add(p);
            }
            BuildGeneral(pages[0]);
            BuildAi(pages[1]);
            BuildMachine(pages[2]);

            var close = Theme.MakeButton("닫기", true);
            close.DialogResult = DialogResult.Cancel;
            Controls.Add(close);
            CancelButton = close;
            Shown += delegate { close.Location = new Point(ClientSize.Width - 20 - close.Width, ClientSize.Height - 20 - close.Height); };
            int last; int.TryParse(Edits.Get("설정쪽", "0"), out last);
            ShowPage(Math.Max(0, Math.Min(pages.Count - 1, last)));
        }

        public void ShowPage(int k)
        {
            for (int i = 0; i < pages.Count; i++)
            {
                pages[i].Visible = i == k;
                tabs[i].BackColor = i == k ? Theme.Accent : Theme.Card;
                tabs[i].ForeColor = i == k ? Color.FromArgb(30, 20, 10) : Theme.Text;
            }
            Edits.Set("설정쪽", k.ToString());
        }

        static Label Head(string t, int y)
        {
            var l = new Label();
            l.Text = t;
            l.AutoSize = true;
            l.Font = Theme.F(11f, FontStyle.Bold);
            l.ForeColor = Theme.Text;
            l.Location = new Point(20, y);
            return l;
        }

        static Label Note(string t, int y, int h)
        {
            var l = new Label();
            l.Text = t;
            l.ForeColor = Theme.SubText;
            l.Location = new Point(22, y);
            l.Size = new Size(576, h);
            return l;
        }

        static TextBox KeyBox(int y, string name)
        {
            var k = new TextBox();
            k.Location = new Point(22, y);
            k.Width = 330;
            k.UseSystemPasswordChar = true;
            k.Font = Theme.F(11f);
            k.AccessibleName = name;
            return k;
        }

        // 뒤에서 일하고 끝나면 화면에 반영
        void Later(Action job, Action<Exception> done)
        {
            UseWaitCursor = true;
            var t = new Thread(delegate()
            {
                Exception err = null;
                try { job(); } catch (Exception ex) { err = ex; }
                try { BeginInvoke((MethodInvoker)delegate { UseWaitCursor = false; done(err); }); } catch { }
            });
            t.IsBackground = true;
            t.Start();
        }

        // ── 일반 ──
        void BuildGeneral(Panel p)
        {
            p.Controls.Add(Head("새 판", 6));
            var auto = new CheckBox();
            auto.Text = "켤 때 새 판이 나왔는지 확인하기 (하루 한 번)";
            auto.AutoSize = true;
            auto.Location = new Point(22, 36);
            auto.Checked = Updater.AutoCheck;
            auto.Enabled = Updater.Configured;
            auto.CheckedChanged += delegate { Updater.AutoCheck = auto.Checked; };
            p.Controls.Add(auto);
            var now = Theme.MakeButton("지금 새 판 확인하기", false);
            now.Location = new Point(20, 68);
            now.Enabled = Updater.Configured;
            now.Click += delegate { Updater.CheckNow(this); };
            p.Controls.Add(now);
            if (Updater.Configured)
            {
                var site = Theme.MakeButton("다운로드 사이트 열기", false);
                site.Location = new Point(220, 68);
                site.Click += delegate { try { Process.Start(Updater.SiteUrl); } catch { } };
                p.Controls.Add(site);
            }
        }

        // ── AI 번역 (Claude · ChatGPT · Grok · Gemini) ──
        void BuildAi(Panel p)
        {
            p.Controls.Add(Head("AI 번역 (번역 자막에서 'AI로 읽고 번역')", 6));
            p.Controls.Add(Note("AI 키를 넣으면 AI 가 칸의 외국어를 직접 보고(세로 글씨·손글씨 포함) 자연스럽게 번역해 줘요. 쓴 만큼 그 AI 의 크레딧이 줄어요. " +
                                "키는 이 컴퓨터에만 암호화해서 저장돼요. (번역할 칸 그림은 번역을 위해 고른 AI 회사에 보내져요)", 34, 64));

            var provLbl = new Label(); provLbl.Text = "쓸 AI"; provLbl.AutoSize = true; provLbl.Location = new Point(22, 108); provLbl.ForeColor = Theme.SubText;
            p.Controls.Add(provLbl);
            var prov = new ComboBox();
            prov.DropDownStyle = ComboBoxStyle.DropDownList;
            prov.Location = new Point(100, 104);
            prov.Width = 250;
            prov.AccessibleName = "쓸 AI";
            foreach (var x in Translate.Providers) prov.Items.Add(Translate.ProviderName(x));
            prov.SelectedIndex = Array.IndexOf(Translate.Providers, Translate.Provider);
            p.Controls.Add(prov);

            var keyState = new Label(); keyState.AutoSize = true; keyState.Location = new Point(22, 146);
            p.Controls.Add(keyState);
            var keyBox = KeyBox(176, "AI 키");
            p.Controls.Add(keyBox);
            var saveKey = Theme.MakeButton("키 넣기", true);
            saveKey.Location = new Point(362, 172);
            p.Controls.Add(saveKey);
            var clearKey = Theme.MakeButton("키 지우기", false);
            clearKey.Location = new Point(466, 172);
            p.Controls.Add(clearKey);

            var modelLbl = new Label(); modelLbl.Text = "모델"; modelLbl.AutoSize = true; modelLbl.Location = new Point(22, 228); modelLbl.ForeColor = Theme.SubText;
            p.Controls.Add(modelLbl);
            var model = new ComboBox();
            model.DropDownStyle = ComboBoxStyle.DropDown;   // 직접 적어도 됨
            model.Location = new Point(100, 224);
            model.Width = 250;
            model.AccessibleName = "모델";
            p.Controls.Add(model);
            var listBtn = Theme.MakeButton("모델 목록 불러오기", false);
            listBtn.Location = new Point(362, 220);
            p.Controls.Add(listBtn);
            var modelNote = Note("", 262, 44);
            p.Controls.Add(modelNote);
            var how = Note("", 312, 80);
            p.Controls.Add(how);

            bool loading = false;
            Func<string> cur = delegate { return Translate.Providers[Math.Max(0, prov.SelectedIndex)]; };
            Action refresh = delegate
            {
                loading = true;
                string pv = cur();
                bool has = Translate.KeyFor(pv).Length > 0;
                keyState.Text = has ? Translate.ProviderName(pv) + " 키: 넣어 둠 ✓" : Translate.ProviderName(pv) + " 키: 없음 (자동 번역을 써요)";
                keyState.ForeColor = has ? Theme.Accent : Theme.SubText;
                model.Items.Clear();
                foreach (var m in Translate.SuggestedModels(pv)) model.Items.Add(m);
                model.Text = Translate.ModelFor(pv);
                modelNote.Text = pv == "Claude"
                    ? "claude-opus-5-5 = 최고 품질, claude-sonnet-5-5 = 비용 약 절반, claude-haiku-5-5 = 가장 쌈."
                    : "키를 넣으면 쓸 수 있는 모델 목록을 불러와요. 이름에 mini·flash·lite 가 붙은 것은 싸고 빠르고, 안 붙은 것은 비싸고 더 똑똑해요.";
                how.Text = Translate.HowToKey(pv);
                listBtn.Enabled = has;
                loading = false;
            };
            Action fetch = null;
            fetch = delegate
            {
                string pv = cur();
                listBtn.Enabled = false;
                List<string> got = null;
                Later(delegate { got = Translate.ListModels(pv); }, delegate(Exception err)
                {
                    listBtn.Enabled = Translate.KeyFor(pv).Length > 0;
                    if (err != null) { MessageBox.Show(this, err.Message, "모델 목록"); return; }
                    if (cur() != pv) return;
                    loading = true;
                    model.Items.Clear();
                    foreach (var m in got) model.Items.Add(m);
                    loading = false;
                    if (got.Count > 0 && !got.Contains(Translate.ModelFor(pv)))
                    {
                        Translate.SetModel(pv, got[0]);
                        model.Text = got[0];
                        modelNote.Text = "지금 고른 모델이 목록에 없어서 '" + got[0] + "' 로 바꿨어요. 아래 칸을 눌러 다른 모델을 골라도 돼요.";
                    }
                    else modelNote.Text = "모델 " + got.Count + "개를 불러왔어요. 칸을 눌러 골라요.";
                    model.DroppedDown = got.Count > 0;
                });
            };
            prov.SelectedIndexChanged += delegate { Translate.Provider = cur(); refresh(); };
            model.TextChanged += delegate { if (!loading && model.Text.Trim().Length > 0) Translate.SetModel(cur(), model.Text); };
            listBtn.Click += delegate { fetch(); };
            saveKey.Click += delegate
            {
                string pv = cur(), k = keyBox.Text.Trim();
                if (!k.StartsWith(Translate.KeyStart(pv)))
                {
                    MessageBox.Show(this, Translate.ProviderName(pv) + " 키는 '" + Translate.KeyStart(pv) + "' 로 시작해요. 다시 확인해 주세요.", "AI 키");
                    return;
                }
                Translate.SetKey(pv, k);
                keyBox.Text = "";
                refresh();
                if (pv != "Claude") fetch();   // 키가 맞는지도 바로 확인됨
            };
            clearKey.Click += delegate
            {
                string pv = cur();
                if (MessageBox.Show(this, "넣어 둔 " + Translate.ProviderName(pv) + " 키를 지울까요?", "AI 키", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
                Translate.SetKey(pv, "");
                refresh();
            };
            refresh();
        }

        // ── 자동 번역 (구글 / DeepL) · 만화 글씨 읽기 ──
        void BuildMachine(Panel p)
        {
            p.Controls.Add(Head("자동 번역기 (AI 키가 없을 때 쓰는 무료 번역)", 6));
            var useG = new RadioButton();
            useG.Text = "구글 번역 (키 없이 바로 써요)";
            useG.AutoSize = true;
            useG.Location = new Point(22, 34);
            var useD = new RadioButton();
            useD.Text = "DeepL (무료 키가 필요해요, 말투가 더 자연스러운 편)";
            useD.AutoSize = true;
            useD.Location = new Point(22, 60);
            p.Controls.Add(useG); p.Controls.Add(useD);

            var dState = new Label();
            dState.AutoSize = true;
            dState.Location = new Point(22, 92);
            p.Controls.Add(dState);
            bool loading = true;
            Action dRefresh = delegate
            {
                bool has = Translate.DeepLKey.Length > 0;
                dState.Text = has ? "DeepL 키: 넣어 둠 ✓" : "DeepL 키: 없음 (DeepL 을 고르려면 키를 넣어 주세요)";
                dState.ForeColor = has ? Theme.Accent : Theme.SubText;
                loading = true;
                useD.Checked = Translate.UseDeepL;
                useG.Checked = !Translate.UseDeepL;
                loading = false;
            };
            dRefresh();
            useD.CheckedChanged += delegate
            {
                if (loading) return;
                if (useD.Checked && Translate.DeepLKey.Length == 0)
                {
                    MessageBox.Show(this, "DeepL 을 쓰려면 먼저 아래 칸에 DeepL 키를 넣어 주세요.", "DeepL");
                    dRefresh();
                    return;
                }
                Translate.PreferDeepL = useD.Checked;
            };
            var dBox = KeyBox(120, "DeepL 키");
            p.Controls.Add(dBox);
            var dSave = Theme.MakeButton("키 넣기", true);
            dSave.Location = new Point(362, 116);
            dSave.Click += delegate
            {
                string k = dBox.Text.Trim();
                if (k.Length < 20) { MessageBox.Show(this, "DeepL 키가 너무 짧아요. 다시 확인해 주세요. (무료 키는 끝이 ':fx' 예요)", "DeepL 키"); return; }
                Translate.DeepLKey = k;
                Translate.PreferDeepL = true;
                dBox.Text = "";
                dRefresh();
            };
            p.Controls.Add(dSave);
            var dClear = Theme.MakeButton("키 지우기", false);
            dClear.Location = new Point(466, 116);
            dClear.Click += delegate
            {
                if (MessageBox.Show(this, "넣어 둔 DeepL 키를 지울까요? (그다음부터는 구글 번역을 써요)", "DeepL 키", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
                Translate.DeepLKey = "";
                Translate.PreferDeepL = false;
                dRefresh();
            };
            p.Controls.Add(dClear);
            p.Controls.Add(Note("DeepL 무료 키 받는 법: deepl.com 에서 'DeepL API Free' 가입 → 계정 → API 키. 한 달에 50만 글자까지 무료예요.", 158, 44));

            // ── 만화 글씨 읽기 (추가 기능) ──
            p.Controls.Add(Head("만화 글씨 읽기 (일본어, 원할 때만 받는 추가 기능)", 214));
            p.Controls.Add(Note("만화 글씨 전용 프로그램(manga-ocr)이에요. 세로 글씨·만화 글씨체를 윈도우 기본 글자 읽기보다 훨씬 잘 읽어요. " +
                                "인터넷 없이 이 컴퓨터에서 돌아가요. " + MangaOcr.SizeText + ", 저장 위치 " + MangaOcr.Dir, 242, 64));
            var mState = new Label(); mState.AutoSize = true; mState.Location = new Point(22, 312);
            p.Controls.Add(mState);
            var mUse = new CheckBox(); mUse.Text = "일본어 글자 읽기에 쓰기"; mUse.AutoSize = true; mUse.Location = new Point(22, 340);
            p.Controls.Add(mUse);
            var mGet = Theme.MakeButton("받기", true);
            mGet.Location = new Point(22, 372);
            p.Controls.Add(mGet);
            var mDel = Theme.MakeButton("지우기 (휴지통으로)", false);
            mDel.Location = new Point(140, 372);
            p.Controls.Add(mDel);
            bool mLoading = false;
            Action mRefresh = delegate
            {
                bool ok = MangaOcr.Installed;
                mState.Text = ok ? "받아 둠 ✓" : "아직 안 받음 (지금은 윈도우 기본 글자 읽기를 써요)";
                mState.ForeColor = ok ? Theme.Accent : Theme.SubText;
                mLoading = true; mUse.Checked = MangaOcr.Enabled; mLoading = false;
                mUse.Enabled = ok;
                mGet.Text = ok ? "다시 받기" : "받기";
                mDel.Enabled = ok || System.IO.Directory.Exists(MangaOcr.Dir);
            };
            mRefresh();
            mUse.CheckedChanged += delegate { if (!mLoading) MangaOcr.Enabled = mUse.Checked; };
            mGet.Click += delegate
            {
                var r = MessageBox.Show(this,
                    "만화 글씨 읽기 프로그램을 받을까요?\n\n" +
                    "받는 것: 파이썬(python.org, 약 10MB), 계산 도구 torch(약 106MB), 만화 글씨 모델 manga-ocr(huggingface, 약 424MB), 그 밖 부품 약 100MB\n" +
                    "→ " + MangaOcr.SizeText + "\n저장 위치: " + MangaOcr.Dir + "\n\n" +
                    "인터넷 속도에 따라 몇 분에서 수십 분 걸려요.",
                    "만화 글씨 읽기 받기", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (r != DialogResult.Yes) return;
                using (var f = new MangaOcrInstallForm())
                {
                    f.ShowDialog(this);
                    if (f.Error is OperationCanceledException) MessageBox.Show(this, "받기를 그만뒀어요. 다시 누르면 이어서 받아요.", "만화 글씨 읽기");
                    else if (f.Error != null) MessageBox.Show(this, "받지 못했어요.\n" + f.Error.Message, "만화 글씨 읽기");
                    else { MangaOcr.Enabled = true; MessageBox.Show(this, "다 받았어요! 이제 일본어는 만화 글씨 읽기로 읽어요.\n(처음 한 번은 준비하느라 몇 초 걸려요)", "만화 글씨 읽기"); }
                }
                mRefresh();
            };
            mDel.Click += delegate
            {
                if (MessageBox.Show(this, "만화 글씨 읽기 프로그램(약 1.5GB)을 지울까요?\n휴지통으로 보내요. 크기가 커서 윈도우가 '바로 지울까요?' 하고 물어볼 수 있어요.",
                    "만화 글씨 읽기", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
                try { MangaOcr.Remove(); }
                catch (Exception ex) { MessageBox.Show(this, "지우지 못했어요.\n" + ex.Message, "만화 글씨 읽기"); }
                mRefresh();
            };
        }
    }
}
