// 만화 보관함 — 유튜브 장면: 링크를 넣으면 장면 안에서 유튜브 공식 재생기로 봄 (마이크로소프트 WebView2 사용)
// WebView2 부품(dll 3개)은 exe 안에 넣어 두고, 처음 쓸 때 꺼내 씀 → 프로그램은 여전히 파일 하나
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace ComicShelf
{
    // ───────────────────────── 링크 장면 파일 (.youtube) ─────────────────────────
    // 화 폴더에 0005.youtube 처럼 저장: 주소·영상 번호·제목·크기·시작 초·썸네일(그림 글자)
    class YtLink
    {
        public const string Ext = ".youtube";
        public const string SaveGuide = "";
        static readonly System.Web.Script.Serialization.JavaScriptSerializer Json = new System.Web.Script.Serialization.JavaScriptSerializer { MaxJsonLength = int.MaxValue };
        public string Url = "", Id = "", Title = "";
        public int W = 16, H = 9, Start;
        public byte[] Thumb;

        public string WatchUrl { get { return "https://www.youtube.com/watch?v=" + Id + (Start > 0 ? "&t=" + Start + "s" : ""); } }

        public static YtLink Load(string path)
        {
            var l = new YtLink();
            try
            {
                var d = Json.Deserialize<Dictionary<string, object>>(File.ReadAllText(path, Encoding.UTF8));
                object o;
                if (d.TryGetValue("url", out o)) l.Url = o as string ?? "";
                if (d.TryGetValue("id", out o)) l.Id = o as string ?? "";
                if (d.TryGetValue("title", out o)) l.Title = o as string ?? "";
                if (d.TryGetValue("w", out o)) l.W = Convert.ToInt32(o);
                if (d.TryGetValue("h", out o)) l.H = Convert.ToInt32(o);
                if (d.TryGetValue("start", out o)) l.Start = Convert.ToInt32(o);
                if (d.TryGetValue("thumb", out o) && o is string && ((string)o).Length > 0) l.Thumb = Convert.FromBase64String((string)o);
            }
            catch { }
            if (l.W <= 0 || l.H <= 0) { l.W = 16; l.H = 9; }
            return l;
        }

        public void Save(string path)
        {
            var d = new Dictionary<string, object>
            {
                { "url", Url }, { "id", Id }, { "title", Title }, { "w", W }, { "h", H }, { "start", Start },
                { "thumb", Thumb == null ? "" : Convert.ToBase64String(Thumb) }
            };
            File.WriteAllText(path, Json.Serialize(d), new UTF8Encoding(false));
        }

        public Bitmap ThumbImage()
        {
            if (Thumb == null) return null;
            try { using (var ms = new MemoryStream(Thumb)) using (var img = Image.FromStream(ms)) return new Bitmap(img); }
            catch { return null; }
        }

        // 주소에서 영상 번호 꺼내기 (youtube.com/watch?v= · youtu.be/ · shorts/ · embed/ · live/), 못 찾으면 null
        public static string ParseId(string url, out int start)
        {
            start = 0;
            if (string.IsNullOrEmpty(url)) return null;
            url = url.Trim();
            var m = Regex.Match(url, @"(?:youtube(?:-nocookie)?\.com/(?:watch\?(?:[^#]*&)?v=|shorts/|embed/|live/|v/)|youtu\.be/)([A-Za-z0-9_-]{11})", RegexOptions.IgnoreCase);
            if (!m.Success) return null;
            var t = Regex.Match(url, @"[?&#](?:t|start)=(?:(\d+)h)?(?:(\d+)m)?(\d+)s?", RegexOptions.IgnoreCase);
            if (t.Success)
            {
                int h = t.Groups[1].Success ? int.Parse(t.Groups[1].Value) : 0;
                int mi = t.Groups[2].Success ? int.Parse(t.Groups[2].Value) : 0;
                start = h * 3600 + mi * 60 + int.Parse(t.Groups[3].Value);
            }
            return m.Groups[1].Value;
        }

        // 제목·크기·썸네일을 유튜브에서 받아 옴 (인터넷이 안 되면 번호만으로 만듦)
        public static YtLink Fetch(string url)
        {
            int start;
            string id = ParseId(url, out start);
            if (id == null) throw new Exception("유튜브 영상 주소가 아니에요.");
            var l = new YtLink();
            l.Id = id; l.Start = start; l.Url = url.Trim();
            l.Title = "유튜브 영상";
            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;
            bool shorts = url.IndexOf("/shorts/", StringComparison.OrdinalIgnoreCase) >= 0;
            try
            {
                string j = Get("https://www.youtube.com/oembed?format=json&url=" + Uri.EscapeDataString("https://www.youtube.com/watch?v=" + id));
                var d = Json.Deserialize<Dictionary<string, object>>(j);
                object o;
                if (d.TryGetValue("title", out o) && o is string) l.Title = (string)o;
                if (d.TryGetValue("width", out o) && d.ContainsKey("height"))
                {
                    int w = Convert.ToInt32(o), h = Convert.ToInt32(d["height"]);
                    if (w > 0 && h > 0) { l.W = w; l.H = h; }
                }
            }
            catch (WebException ex)
            {
                var hr = ex.Response as HttpWebResponse;
                if (hr != null && ((int)hr.StatusCode == 404 || (int)hr.StatusCode == 400))
                    throw new Exception("이 영상을 찾지 못했어요. 지워졌거나 비공개 영상일 수 있어요.");
                // 401 = 다른 곳에서 재생을 막은 영상. 넣는 건 되고, 볼 때 안내함
            }
            catch { }
            if (shorts && l.W >= l.H) { l.W = 9; l.H = 16; }   // 쇼츠는 세로 영상
            foreach (var name in new[] { "maxresdefault.jpg", "hqdefault.jpg" })
            {
                try { l.Thumb = GetBytes("https://i.ytimg.com/vi/" + id + "/" + name); break; } catch { }
            }
            return l;
        }

        static string Get(string url) { return Encoding.UTF8.GetString(GetBytes(url)); }

        static byte[] GetBytes(string url)
        {
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.UserAgent = "Mozilla/5.0 ComicShelf";
            req.Timeout = 15000;
            using (var res = req.GetResponse())
            using (var s = res.GetResponseStream())
            using (var ms = new MemoryStream()) { s.CopyTo(ms); return ms.ToArray(); }
        }
    }

    // ───────────────────────── WebView2 부품 ─────────────────────────
    static class WebParts
    {
        static readonly Dictionary<string, Assembly> loaded = new Dictionary<string, Assembly>();

        // 프로그램 시작 때 한 번: exe 안에 넣어 둔 WebView2 dll 을 찾을 수 있게
        public static void Hook()
        {
            AppDomain.CurrentDomain.AssemblyResolve += delegate(object s, ResolveEventArgs e)
            {
                string name = new AssemblyName(e.Name).Name;
                string res = name == "Microsoft.Web.WebView2.Core" ? "wv2.core" : name == "Microsoft.Web.WebView2.WinForms" ? "wv2.winforms" : null;
                if (res == null) return null;
                lock (loaded)
                {
                    Assembly a;
                    if (loaded.TryGetValue(res, out a)) return a;
                    using (var st = Assembly.GetExecutingAssembly().GetManifestResourceStream(res))
                    {
                        if (st == null) return null;
                        var ms = new MemoryStream(); st.CopyTo(ms);
                        a = Assembly.Load(ms.ToArray());
                    }
                    loaded[res] = a;
                    return a;
                }
            };
        }

        public static string BaseDir { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ComicShelf"); } }

        // 네이티브 부품(WebView2Loader.dll)은 파일로 꺼내 둠
        static string loaderDir;
        public static void PrepareLoader()
        {
            if (loaderDir != null) return;
            if (!Environment.Is64BitProcess) throw new Exception("유튜브 장면은 64비트 윈도우에서만 볼 수 있어요.");
            using (var st = Assembly.GetExecutingAssembly().GetManifestResourceStream("wv2.loader"))
            {
                if (st == null) throw new Exception("유튜브 재생 부품이 프로그램에 없어요.");
                var ms = new MemoryStream(); st.CopyTo(ms);
                byte[] b = ms.ToArray();
                string dir = Path.Combine(BaseDir, "bin", "wv2-" + b.Length);
                string f = Path.Combine(dir, "WebView2Loader.dll");
                if (!File.Exists(f) || new FileInfo(f).Length != b.Length)
                {
                    Directory.CreateDirectory(dir);
                    File.WriteAllBytes(f, b);
                }
                loaderDir = dir;
            }
            CoreWebView2Environment.SetLoaderDllFolderPath(loaderDir);
        }

        // 재생 쪽지 (가상 주소 https://comicshelf.example/yt.html 로 엶 — 유튜브는 어디서 트는지 알려 줘야 재생해 줌)
        const string Html = @"<!doctype html><html><head><meta charset='utf-8'><meta name='referrer' content='strict-origin-when-cross-origin'>
<style>html,body{margin:0;height:100%;background:#000;overflow:hidden}iframe{border:0;width:100%;height:100%;display:block}
#c{position:fixed;left:0;top:0;right:0;bottom:0;display:none;cursor:none;background:transparent;z-index:9}</style></head>
<body><iframe id='f' allow='autoplay; encrypted-media; fullscreen; picture-in-picture' allowfullscreen referrerpolicy='strict-origin-when-cross-origin'></iframe><div id='c'></div>
<script>
var q=new URLSearchParams(location.search),f=document.getElementById('f'),c=document.getElementById('c'),st=-1,want=0,heard=0,tries=0;
f.src='https://www.youtube.com/embed/'+encodeURIComponent(q.get('v'))+'?autoplay=1&rel=0&playsinline=1&hl=ko&cc_lang_pref=ko&enablejsapi=1&start='+(q.get('t')||'0')+'&origin='+encodeURIComponent(location.origin);
function cmd(x){f.contentWindow.postMessage(JSON.stringify({event:'command',func:x,args:[]}),'*');}
// 재생 상태 듣기 (1 = 재생 중)
function listen(){if(heard||tries++>40)return;try{f.contentWindow.postMessage(JSON.stringify({event:'listening',id:1,channel:'widget'}),'*');}catch(x){}setTimeout(listen,500);}
f.addEventListener('load',function(){tries=0;listen();});
window.addEventListener('message',function(e){if(e.source!==f.contentWindow)return;var d;try{d=typeof e.data==='string'?JSON.parse(e.data):e.data;}catch(x){return;}if(!d)return;heard=1;
var s=d.event==='onStateChange'?d.info:(d.info&&d.info.playerState);if(typeof s==='number'){if(s!==st&&window.chrome&&chrome.webview)chrome.webview.postMessage('st:'+s);st=s;upd();}});
// 재생 중에 마우스를 가만히 두면 투명한 막을 덮어 마우스를 숨김 (움직이면 프로그램이 다시 걷음)
function upd(){c.style.display=(want&&st===1&&!document.fullscreenElement)?'block':'none';}
function idle(on){want=on;upd();}
function state(){return st;}
document.addEventListener('fullscreenchange',upd);
c.addEventListener('mousedown',function(e){want=0;upd();if(e.button===0)cmd(st===1?'pauseVideo':'playVideo');});
</script></body></html>";

        public const string Host = "comicshelf.example";

        public static string WebDir
        {
            get
            {
                string d = Path.Combine(BaseDir, "web");
                Directory.CreateDirectory(d);
                string f = Path.Combine(d, "yt.html");
                if (!File.Exists(f) || File.ReadAllText(f, Encoding.UTF8) != Html) File.WriteAllText(f, Html, new UTF8Encoding(false));
                return d;
            }
        }

        static Task<CoreWebView2Environment> env;
        public static Task<CoreWebView2Environment> Env()
        {
            if (env == null)
            {
                PrepareLoader();
                var opt = new CoreWebView2EnvironmentOptions("--autoplay-policy=no-user-gesture-required");
                env = CoreWebView2Environment.CreateAsync(null, Path.Combine(BaseDir, "WebView2"), opt);
            }
            return env;
        }
    }

    // ───────────────────────── 유튜브 재생 칸 ─────────────────────────
    class YtPlayer : Panel
    {
        readonly YtLink link;
        WebView2 web;
        public event Action<bool> FullChanged;   // 유튜브 재생기의 전체 화면 단추
        public event Action<string> Failed;

        // 재생 중에 재생기 위에서 마우스를 3초 가만히 두면 마우스를 숨김 (유튜브 조작 막대가 사라질 때 같이)
        // 재생기는 다른 프로그램(엣지)이 그리지만 우리 창의 자식이라 윈도우의 마우스 숨기기가 같이 먹힘
        readonly Timer idleTimer = new Timer { Interval = 150 };
        Point lastPos;
        DateTime lastMove = DateTime.Now;
        bool idleOn, playing, cursorHidden;
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();

        public YtPlayer(YtLink link)
        {
            this.link = link;
            BackColor = Color.Black;
            idleTimer.Tick += delegate { CheckIdle(); };
            Init();
        }

        // 마우스가 움직였을 때 (MouseHook 이 바로 알려 줌)
        public void Moved()
        {
            lastMove = DateTime.Now;
            if (idleOn) CheckIdle();
        }

        void CheckIdle()
        {
            if (IsDisposed || web == null || web.CoreWebView2 == null) return;
            var m = Cursor.Position;
            var form = FindForm();
            bool inside = Visible && form != null && form.IsHandleCreated && GetForegroundWindow() == form.Handle && RectangleToScreen(ClientRectangle).Contains(m);
            if (m != lastPos) { lastPos = m; lastMove = DateTime.Now; }
            bool want = inside && playing && (DateTime.Now - lastMove).TotalSeconds >= 3;
            if (want != idleOn) { idleOn = want; Js("idle(" + (want ? 1 : 0) + ")"); }
            SetCursorHidden(want);
        }

        void SetCursorHidden(bool hide)
        {
            if (hide == cursorHidden) return;
            if (hide) Cursor.Hide(); else Cursor.Show();
            cursorHidden = hide;
        }

        async void Init()
        {
            try
            {
                var e = await WebParts.Env();
                if (IsDisposed) return;
                web = new WebView2();
                web.DefaultBackgroundColor = Color.Black;
                web.Dock = DockStyle.Fill;
                Controls.Add(web);
                await web.EnsureCoreWebView2Async(e);
                if (IsDisposed) return;
                var core = web.CoreWebView2;
                core.Settings.AreDevToolsEnabled = false;
                core.Settings.AreDefaultContextMenusEnabled = false;
                core.Settings.IsStatusBarEnabled = false;
                core.Settings.IsZoomControlEnabled = false;
                core.SetVirtualHostNameToFolderMapping(WebParts.Host, WebParts.WebDir, CoreWebView2HostResourceAccessKind.Allow);
                // 유튜브로 가는 링크(제목·'유튜브에서 보기')는 인터넷 창으로
                core.NewWindowRequested += delegate(object s, CoreWebView2NewWindowRequestedEventArgs a)
                {
                    a.Handled = true;
                    OpenOutside(a.Uri);
                };
                core.NavigationStarting += delegate(object s, CoreWebView2NavigationStartingEventArgs a)
                {
                    if (!a.Uri.StartsWith("https://" + WebParts.Host + "/", StringComparison.OrdinalIgnoreCase)) { a.Cancel = true; OpenOutside(a.Uri); }
                };
                // 재생 쪽지가 알려 주는 재생 상태 (1 = 재생 중)
                core.WebMessageReceived += delegate(object s, CoreWebView2WebMessageReceivedEventArgs a)
                {
                    string msg = null;
                    try { msg = a.TryGetWebMessageAsString(); } catch { }
                    if (msg != null && msg.StartsWith("st:")) { playing = msg == "st:1"; CheckIdle(); }
                };
                core.ContainsFullScreenElementChanged += delegate
                {
                    if (FullChanged != null) FullChanged(web.CoreWebView2.ContainsFullScreenElement);
                };
                core.Navigate("https://" + WebParts.Host + "/yt.html?v=" + Uri.EscapeDataString(link.Id) + "&t=" + link.Start);
                MouseHook.Add(this);
                idleTimer.Start();
            }
            catch (WebView2RuntimeNotFoundException)
            {
                Fail("이 컴퓨터에 'Microsoft Edge WebView2 런타임'이 없어서 장면 안에서 틀 수 없어요.\n오른쪽 클릭 → '유튜브에서 열기'로 봐 주세요.");
            }
            catch (Exception ex) { Fail("유튜브 재생기를 켜지 못했어요.\n" + ex.Message); }
        }

        void Fail(string msg) { if (Failed != null && !IsDisposed) Failed(msg); }

        static void OpenOutside(string uri)
        {
            if (string.IsNullOrEmpty(uri) || !(uri.StartsWith("https://") || uri.StartsWith("http://"))) return;
            try { Process.Start(uri); } catch { }
        }

        void Js(string code)
        {
            try { if (web != null && web.CoreWebView2 != null) web.CoreWebView2.ExecuteScriptAsync(code); } catch { }
        }

        public void Pause() { Js("cmd('pauseVideo')"); }
        public void Play() { Js("cmd('playVideo')"); }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                MouseHook.Remove(this);
                idleTimer.Stop();
                idleTimer.Dispose();
                SetCursorHidden(false);
                if (web != null) { try { web.Dispose(); } catch { } web = null; }
            }
            base.Dispose(disposing);
        }
    }

    // 유튜브 재생기 위에서 굴린 휠을 만화 화면으로 보냄 (재생기는 다른 프로그램이 그려서 휠을 먹어 버림)
    static class MouseHook
    {
        [StructLayout(LayoutKind.Sequential)]
        struct MSLL { public int x, y; public uint mouseData, flags, time; public IntPtr extra; }
        delegate IntPtr Proc(int code, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] static extern IntPtr SetWindowsHookEx(int id, Proc p, IntPtr mod, uint thread);
        [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr h);
        [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr h, int code, IntPtr w, IntPtr l);
        [DllImport("kernel32.dll")] static extern IntPtr GetModuleHandle(string n);

        static readonly List<YtPlayer> players = new List<YtPlayer>();
        static IntPtr hook;
        static Proc proc;   // 가비지 수집되지 않게 붙들어 둠

        public static void Add(YtPlayer p)
        {
            if (players.Contains(p)) return;
            players.Add(p);
            if (hook == IntPtr.Zero)
            {
                proc = Callback;
                hook = SetWindowsHookEx(14, proc, GetModuleHandle(null), 0);   // 14 = 낮은 단계 마우스
            }
        }

        public static void Remove(YtPlayer p)
        {
            players.Remove(p);
            if (players.Count == 0 && hook != IntPtr.Zero) { UnhookWindowsHookEx(hook); hook = IntPtr.Zero; }
        }

        static IntPtr Callback(int code, IntPtr w, IntPtr l)
        {
            if (code >= 0 && w.ToInt32() == 0x0200)
            {
                foreach (var p in players.ToArray()) if (!p.IsDisposed) p.Moved();
            }
            if (code >= 0 && w.ToInt32() == 0x020A)
            {
                var m = (MSLL)Marshal.PtrToStructure(l, typeof(MSLL));
                var pt = new Point(m.x, m.y);
                foreach (var p in players)
                {
                    if (p.IsDisposed || !p.Visible || !p.IsHandleCreated) continue;
                    var form = p.FindForm();
                    if (form == null || Form.ActiveForm != form) continue;
                    if (!p.RectangleToScreen(p.ClientRectangle).Contains(pt)) continue;
                    StripView strip = null;
                    for (Control c = p; c != null; c = c.Parent) { strip = c as StripView; if (strip != null) break; }
                    if (strip == null || strip.IsVideoFull) break;
                    int delta = (short)((m.mouseData >> 16) & 0xFFFF);
                    var s = strip;
                    s.BeginInvoke((MethodInvoker)delegate { s.WheelFromOutside(pt, delta); });
                    return (IntPtr)1;
                }
            }
            return CallNextHookEx(hook, code, w, l);
        }
    }

    // ───────────────────────── 유튜브 링크 넣기 창 ─────────────────────────
    class YtAddDialog : Form
    {
        readonly TextBox urlBox;
        readonly RadioButton playRadio, saveRadio;
        readonly ComboBox quality;
        public string Url { get { return urlBox.Text.Trim(); } }
        public bool SaveVideo { get { return saveRadio != null && saveRadio.Checked; } }
        public int MaxHeight { get { return quality == null ? 1080 : new[] { 480, 720, 1080 }[Math.Max(0, quality.SelectedIndex)]; } }

        public YtAddDialog()
        {
            Text = "유튜브 링크로 넣기";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Theme.Bar; ForeColor = Theme.Text; Font = Theme.F(10.5f);
            int W = 620;

            var l1 = new Label(); l1.Text = "유튜브 영상 주소 (주소 창이나 '공유'에서 복사해 붙여 넣기)"; l1.Location = new Point(20, 18); l1.AutoSize = true;
            Controls.Add(l1);
            urlBox = new TextBox(); urlBox.Location = new Point(20, 44); urlBox.Width = W - 40; urlBox.Font = Theme.F(11.5f);
            urlBox.AccessibleName = "유튜브 주소";
            Controls.Add(urlBox);
            try
            {
                string clip = Clipboard.ContainsText() ? Clipboard.GetText().Trim() : "";
                int st;
                if (clip.Length < 300 && YtLink.ParseId(clip, out st) != null) urlBox.Text = clip;
            }
            catch { }

            int y = 92;
            var l2 = new Label(); l2.Text = "어떻게 넣을까요?"; l2.Location = new Point(20, y); l2.AutoSize = true; l2.Font = Theme.F(11f, FontStyle.Bold);
            Controls.Add(l2);
            y += 30;
            playRadio = Radio("유튜브로 보기", y);
            Controls.Add(playRadio);
            Controls.Add(Note("장면 안에서 유튜브 재생기로 봐요. 인터넷이 있어야 하고, 광고는 유튜브 그대로 나와요. 파일이 거의 안 커요.", y + 26));
            y += 76;
            playRadio.Checked = true;
            y += 8;
            var ok = Theme.MakeButton("넣기", true);
            var cancel = Theme.MakeButton("취소", false);
            cancel.DialogResult = DialogResult.Cancel;
            Controls.Add(ok); Controls.Add(cancel);
            CancelButton = cancel;
            ClientSize = new Size(W, y + 60);
            cancel.Location = new Point(W - 20 - cancel.PreferredSize.Width, y + 8);
            ok.Location = new Point(cancel.Left - 10 - ok.PreferredSize.Width, y + 8);
            AcceptButton = ok;
            ok.Click += delegate
            {
                int st;
                if (YtLink.ParseId(Url, out st) == null)
                {
                    MessageBox.Show(this, "유튜브 영상 주소가 아니에요.\n\n예: https://www.youtube.com/watch?v=...  또는  https://youtu.be/...", Text);
                    urlBox.Focus();
                    return;
                }
                Edits.Set("유튜브방식", SaveVideo ? "받기" : "보기");
                if (quality != null) Edits.Set("유튜브화질", quality.SelectedIndex.ToString());
                DialogResult = DialogResult.OK;
            };
        }

        RadioButton Radio(string text, int y)
        {
            var r = new RadioButton(); r.Text = text; r.Location = new Point(24, y); r.AutoSize = true; r.Font = Theme.F(11f, FontStyle.Bold);
            r.ForeColor = Theme.Text;
            return r;
        }

        Label Note(string text, int y)
        {
            var l = new Label(); l.Text = text; l.Location = new Point(44, y); l.Size = new Size(560, 44); l.ForeColor = Theme.SubText;
            return l;
        }
    }
}
