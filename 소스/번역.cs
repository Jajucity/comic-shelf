// 만화 보관함 — 번역 자막 도우미
// 칸을 고르면 그 칸의 일본어를 읽고 번역을 미리 채워 줌. 사용자가 다듬어서 넣음.
//  - AI 번역: Claude API (사용자가 설정에 넣은 자기 키로만. 키는 이 컴퓨터에만 암호화해서 저장, 코드에 들어가지 않음)
//  - 구글 번역: 무료. 글자 읽기는 윈도우 기본 글자 읽기(OCR, 일본어 기능이 깔려 있어야 함)
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace ComicShelf
{
    static partial class Translate
    {
        // ── AI 키 (윈도우 사용자 계정으로 암호화해서 자료\설정.txt 에 저장) ──
        // 지금 고른 AI 의 키 (AI 마다 따로 저장 — 번역AI.cs)
        public static string AiKey
        {
            get { return KeyFor(Provider); }
            set { SetKey(Provider, value); }
        }

        public static bool HasAi { get { return AiKey.Length > 0; } }

        // 번역 품질: 최고 = Claude Opus 5.5, 절약 = Claude Sonnet 5.5
        public static bool Saving
        {
            get { return Edits.Get("AI품질", "최고") == "절약"; }
            set { Edits.Set("AI품질", value ? "절약" : "최고"); }
        }
        static string Model { get { return ModelFor("Claude"); } }

        // ── 원문 언어 (이름, 구글 번역 코드, 윈도우 글자 읽기 코드) ──
        public static readonly string[][] Langs =
        {
            new[] { "자동 감지", "auto", "" },
            new[] { "일본어", "ja", "ja" },
            new[] { "영어", "en", "en-US" },
            new[] { "중국어 (간체)", "zh-CN", "zh-Hans" },
            new[] { "중국어 (번체)", "zh-TW", "zh-Hant" },
            new[] { "스페인어", "es", "es" },
            new[] { "프랑스어", "fr", "fr" },
            new[] { "독일어", "de", "de" },
            new[] { "러시아어", "ru", "ru" },
            new[] { "태국어", "th", "th" },
            new[] { "베트남어", "vi", "vi" },
            new[] { "인도네시아어", "id", "id" },
        };

        public static int LangIndex
        {
            get { int i; return int.TryParse(Edits.Get("번역원문언어", "0"), out i) && i >= 0 && i < Langs.Length ? i : 0; }
            set { Edits.Set("번역원문언어", value.ToString()); }
        }

        static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };

        // ── 그림 → 보낼 데이터 ──
        public static string ToBase64(Image img, int maxSide, bool jpeg)
        {
            double s = Math.Min(1.0, maxSide / (double)Math.Max(img.Width, img.Height));
            int w = Math.Max(1, (int)(img.Width * s)), h = Math.Max(1, (int)(img.Height * s));
            using (var b = new Bitmap(w, h, PixelFormat.Format24bppRgb))
            {
                using (var g = Graphics.FromImage(b))
                {
                    g.Clear(Color.White);
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.DrawImage(img, 0, 0, w, h);
                }
                using (var ms = new MemoryStream())
                {
                    b.Save(ms, jpeg ? ImageFormat.Jpeg : ImageFormat.Png);
                    return Convert.ToBase64String(ms.ToArray());
                }
            }
        }

        // ── Claude API 부르기 ──
        static Dictionary<string, object> CallClaude(List<object> content, string system, Dictionary<string, object> format)
        {
            var output = new Dictionary<string, object> { { "effort", "medium" } };
            if (format != null) output["format"] = format;
            var body = new Dictionary<string, object>
            {
                { "model", Model },
                { "max_tokens", 8000 },
                { "system", system },
                { "output_config", output },
                { "fallbacks", "default" },   // 안전 거절이 나면 서버가 다른 모델로 다시 해 봄
                { "messages", new object[] { new Dictionary<string, object> { { "role", "user" }, { "content", content } } } }
            };
            byte[] data = Encoding.UTF8.GetBytes(Json.Serialize(body));
            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;   // TLS 1.2
            var req = (HttpWebRequest)WebRequest.Create("https://api.anthropic.com/v1/messages");
            req.Method = "POST";
            req.ContentType = "application/json";
            req.Timeout = 180000;
            req.ReadWriteTimeout = 180000;
            req.Headers["x-api-key"] = AiKey;
            req.Headers["anthropic-version"] = "2023-06-01";
            req.Headers["anthropic-beta"] = "server-side-fallback-2026-07-01";
            using (var s = req.GetRequestStream()) s.Write(data, 0, data.Length);
            string text;
            try
            {
                using (var resp = (HttpWebResponse)req.GetResponse())
                using (var r = new StreamReader(resp.GetResponseStream(), Encoding.UTF8)) text = r.ReadToEnd();
            }
            catch (WebException ex)
            {
                var hr = ex.Response as HttpWebResponse;
                if (hr == null) throw new Exception("인터넷에 연결하지 못했어요. (" + ex.Message + ")");
                string err;
                using (var r = new StreamReader(hr.GetResponseStream(), Encoding.UTF8)) err = r.ReadToEnd();
                int code = (int)hr.StatusCode;
                string msg = "";
                try { msg = (string)((Dictionary<string, object>)Json.Deserialize<Dictionary<string, object>>(err)["error"])["message"]; } catch { }
                if (code == 401) throw new Exception("AI 키가 맞지 않아요. 설정에서 키를 다시 넣어 주세요.");
                if (code == 429) throw new Exception("AI 요청이 잠깐 너무 많아요. 조금 뒤에 다시 해 주세요.");
                if (msg.IndexOf("credit", StringComparison.OrdinalIgnoreCase) >= 0) throw new Exception("AI 크레딧(사용 금액)이 부족해요. Claude 콘솔에서 크레딧을 확인해 주세요.");
                if (code >= 500) throw new Exception("AI 서버에 잠깐 문제가 있어요. 조금 뒤에 다시 해 주세요.");
                throw new Exception("AI 번역 요청이 실패했어요. (" + code + ") " + msg);
            }
            var res = Json.Deserialize<Dictionary<string, object>>(text);
            if ((res.ContainsKey("stop_reason") ? res["stop_reason"] as string : "") == "refusal")
                throw new Exception("AI 가 이 장면은 번역하지 않겠다고 했어요. 직접 적어 주세요.");
            return res;
        }

        static string TextOf(Dictionary<string, object> res)
        {
            var sb = new StringBuilder();
            foreach (var o in (System.Collections.ArrayList)res["content"])
            {
                var blk = o as Dictionary<string, object>;
                if (blk != null && (blk["type"] as string) == "text") sb.Append(blk["text"] as string);
            }
            return sb.ToString();
        }

        static List<object> Images(Image crop, Image page)
        {
            var list = new List<object>();
            if (page != null)
            {
                list.Add(new Dictionary<string, object> { { "type", "text" }, { "text", "만화 한 쪽 전체 (흐름·인물 이름 참고용):" } });
                list.Add(ImageBlock(ToBase64(page, 1400, true), "image/jpeg"));
            }
            list.Add(new Dictionary<string, object> { { "type", "text" }, { "text", "번역할 칸 (확대):" } });
            list.Add(ImageBlock(ToBase64(crop, 1200, false), "image/png"));
            return list;
        }

        static Dictionary<string, object> ImageBlock(string b64, string type)
        {
            return new Dictionary<string, object>
            {
                { "type", "image" },
                { "source", new Dictionary<string, object> { { "type", "base64" }, { "media_type", type }, { "data", b64 } } }
            };
        }

        const string SystemPrompt =
            "너는 외국 만화(일본 만화, 영어 코믹스, 중국 만화 등)를 한국어로 옮기는 숙련된 만화 번역가야. 사용자가 보내는 '번역할 칸' 안의 글자(세로쓰기, 작은 글씨, 후리가나 포함)를 정확히 읽고, " +
            "한국 정식 번역 만화처럼 자연스러운 한국어 대사로 옮겨. 인물의 말투·감정·말끝(…, !, ?)을 살리고, 인물 이름은 '한 쪽 전체' 그림의 흐름과 후리가나를 참고해 원어 읽는 소리대로 한글로 적어. " +
            "후리가나는 원문에 따로 적지 말고 본문 글자만 적어. 효과음이면 한국 만화식 의성어·의태어로 짧게 옮겨. 칸에 글자가 없으면 원문과 번역을 빈칸으로 둬.";

        static string LangHint()
        {
            int i = LangIndex;
            return i == 0 ? "원문 언어는 그림을 보고 알아서 판단해." : "원문 언어: " + Langs[i][0] + ".";
        }

        // AI 로 읽고 번역. note = 번역하면서 알아 둘 점(말장난, 애매한 뜻 등), 없으면 빈칸
        public static void Ai(Image crop, Image page, out string original, out string translation, out string note)
        {
            var schema = new Dictionary<string, object>
            {
                { "type", "object" },
                { "properties", new Dictionary<string, object>
                    {
                        { "original", new Dictionary<string, object> { { "type", "string" }, { "description", "칸 안의 원문 그대로 (줄바꿈은 \\n)" } } },
                        { "translation", new Dictionary<string, object> { { "type", "string" }, { "description", "자연스러운 한국어 번역 (말풍선에 넣을 그대로, 줄바꿈 가능)" } } },
                        { "note", new Dictionary<string, object> { { "type", "string" }, { "description", "번역할 때 알아 둘 점을 한국어로 짧게. 없으면 빈 문자열" } } }
                    }
                },
                { "required", new object[] { "original", "translation", "note" } },
                { "additionalProperties", false }
            };
            var content = Images(crop, page);
            string ask = "'번역할 칸'의 글자를 읽고 한국어로 번역해 줘. " + LangHint();
            Dictionary<string, object> obj;
            if (Provider != "Claude")
            {
                content.Add(new Dictionary<string, object> { { "type", "text" }, { "text", ask + JsonAsk } });
                obj = ParseJsonLoose(CallCompat(content, SystemPrompt, true));
            }
            else
            {
                content.Add(new Dictionary<string, object> { { "type", "text" }, { "text", ask } });
                var res = CallClaude(content, SystemPrompt, new Dictionary<string, object> { { "type", "json_schema" }, { "schema", schema } });
                obj = Json.Deserialize<Dictionary<string, object>>(TextOf(res));
            }
            original = (obj.ContainsKey("original") ? obj["original"] as string ?? "" : "").Replace("\n", "\r\n");
            translation = (obj.ContainsKey("translation") ? obj["translation"] as string ?? "" : "").Replace("\n", "\r\n");
            note = obj.ContainsKey("note") ? (obj["note"] as string ?? "") : "";
        }

        // AI 에게 물어보기 (그 장면을 보고 뜻·뉘앙스 설명)
        public static string Ask(Image crop, Image page, string original, string current, string question)
        {
            var content = Images(crop, page);
            content.Add(new Dictionary<string, object>
            {
                { "type", "text" },
                { "text", LangHint() + "\n원문: " + original + "\n지금 번역: " + current + "\n\n질문: " + question + "\n\n한국어로 쉽고 짧게 답해 줘. 더 자연스러운 번역이 있으면 함께 알려 줘." }
            });
            if (Provider != "Claude") return CallCompat(content, SystemPrompt, false).Trim();
            return TextOf(CallClaude(content, SystemPrompt, null)).Trim();
        }

        // ── 자동 번역기: 구글(키 없음) 또는 DeepL(무료 키) ──
        public static bool UseDeepL
        {
            get { return Edits.Get("자동번역기", "구글") == "DeepL" && DeepLKey.Length > 0; }
        }
        public static bool PreferDeepL
        {
            get { return Edits.Get("자동번역기", "구글") == "DeepL"; }
            set { Edits.Set("자동번역기", value ? "DeepL" : "구글"); }
        }
        public static string MtName { get { return UseDeepL ? "DeepL" : "구글"; } }

        public static string DeepLKey
        {
            get
            {
                string enc = Edits.Get("DeepL키", "");
                if (enc.Length == 0) return "";
                try { return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(enc), null, DataProtectionScope.CurrentUser)); }
                catch { return ""; }
            }
            set
            {
                if (string.IsNullOrEmpty(value)) { Edits.Set("DeepL키", ""); return; }
                Edits.Set("DeepL키", Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(value.Trim()), null, DataProtectionScope.CurrentUser)));
            }
        }

        public static string Machine(string source)
        {
            return UseDeepL ? DeepL(source) : Google(source);
        }

        // DeepL: 무료 키(끝이 :fx)는 api-free 주소, 유료 키는 api 주소
        public static string DeepL(string source)
        {
            if (string.IsNullOrWhiteSpace(source)) return "";
            string key = DeepLKey;
            string host = key.EndsWith(":fx") ? "https://api-free.deepl.com" : "https://api.deepl.com";
            var body = new Dictionary<string, object> { { "text", new object[] { source } }, { "target_lang", "KO" } };
            string g = Langs[LangIndex][1];
            var map = new Dictionary<string, string> { { "ja", "JA" }, { "en", "EN" }, { "zh-CN", "ZH" }, { "zh-TW", "ZH" }, { "es", "ES" }, { "fr", "FR" }, { "de", "DE" }, { "ru", "RU" }, { "id", "ID" } };
            if (map.ContainsKey(g)) body["source_lang"] = map[g];   // 모르면 DeepL 이 알아서 판단
            byte[] data = Encoding.UTF8.GetBytes(Json.Serialize(body));
            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;
            var req = (HttpWebRequest)WebRequest.Create(host + "/v2/translate");
            req.Method = "POST";
            req.ContentType = "application/json";
            req.Timeout = 60000;
            req.Headers["Authorization"] = "DeepL-Auth-Key " + key;
            using (var s = req.GetRequestStream()) s.Write(data, 0, data.Length);
            string text;
            try
            {
                using (var resp = (HttpWebResponse)req.GetResponse())
                using (var r = new StreamReader(resp.GetResponseStream(), Encoding.UTF8)) text = r.ReadToEnd();
            }
            catch (WebException ex)
            {
                var hr = ex.Response as HttpWebResponse;
                if (hr == null) throw new Exception("DeepL 에 연결하지 못했어요. (" + ex.Message + ")");
                int code = (int)hr.StatusCode;
                if (code == 401 || code == 403) throw new Exception("DeepL 키가 맞지 않아요. 설정에서 키를 다시 넣어 주세요.");
                if (code == 456) throw new Exception("이번 달 DeepL 무료 번역량을 다 썼어요. 다음 달에 다시 쓰거나 구글 번역을 써 주세요.");
                if (code == 429) throw new Exception("DeepL 요청이 잠깐 너무 많아요. 조금 뒤에 다시 해 주세요.");
                throw new Exception("DeepL 번역이 실패했어요. (" + code + ")");
            }
            var res = Json.Deserialize<Dictionary<string, object>>(text);
            var sb = new StringBuilder();
            foreach (var o in (System.Collections.ArrayList)res["translations"])
            {
                var t = o as Dictionary<string, object>;
                if (t != null) sb.Append(t["text"] as string);
            }
            return sb.ToString().Replace("\r\n", "\n").Replace("\n", "\r\n");
        }

        // ── 구글 번역 (무료, 키 없음) ──
        public static string Google(string source)
        {
            if (string.IsNullOrWhiteSpace(source)) return "";
            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;
            using (var wc = new WebClient())
            {
                wc.Encoding = Encoding.UTF8;
                wc.Headers[HttpRequestHeader.UserAgent] = "Mozilla/5.0";
                string url = "https://translate.googleapis.com/translate_a/single?client=gtx&sl=" + Langs[LangIndex][1] + "&tl=ko&dt=t&q=" + Uri.EscapeDataString(source);
                string text;
                try { text = wc.DownloadString(url); }
                catch (Exception ex) { throw new Exception("구글 번역에 연결하지 못했어요. (" + ex.Message + ")"); }
                var arr = Json.DeserializeObject(text) as object[];
                var sb = new StringBuilder();
                if (arr != null && arr.Length > 0 && arr[0] is object[])
                    foreach (var seg in (object[])arr[0])
                    {
                        var s = seg as object[];
                        if (s != null && s.Length > 0 && s[0] is string) sb.Append((string)s[0]);
                    }
                return sb.ToString().Replace("\n", "\r\n");
            }
        }

        // ── 윈도우 기본 글자 읽기 (일본어) ── PowerShell 을 잠깐 불러 씀
        const string OcrScript = @"
[Console]::OutputEncoding = [Text.Encoding]::UTF8
Add-Type -AssemblyName System.Runtime.WindowsRuntime
$null = [Windows.Media.Ocr.OcrEngine, Windows.Foundation, ContentType = WindowsRuntime]
$null = [Windows.Storage.StorageFile, Windows.Storage, ContentType = WindowsRuntime]
$null = [Windows.Graphics.Imaging.BitmapDecoder, Windows.Graphics, ContentType = WindowsRuntime]
$null = [Windows.Globalization.Language, Windows.Globalization, ContentType = WindowsRuntime]
$asTask = ([System.WindowsRuntimeSystemExtensions].GetMethods() | ? { $_.Name -eq 'AsTask' -and $_.GetParameters().Count -eq 1 -and $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncOperation`1' })[0]
function Await($op, $t) { $task = $asTask.MakeGenericMethod($t).Invoke($null, @($op)); $task.Wait(-1) | Out-Null; $task.Result }
# 읽을 언어: 정한 언어 하나, 또는 '자동'이면 깔려 있는 언어 중 한국어를 뺀 전부를 해 보고 가장 많이 읽은 것
$want = '__LANG__'
$tags = @()
if ($want -ne '') { $tags = @($want) } else { $tags = @([Windows.Media.Ocr.OcrEngine]::AvailableRecognizerLanguages | % { $_.LanguageTag } | ? { $_ -notlike 'ko*' }) }
$engs = @($tags | % { [Windows.Media.Ocr.OcrEngine]::TryCreateFromLanguage((New-Object Windows.Globalization.Language $_)) } | ? { $_ -ne $null })
if ($engs.Count -eq 0) { '__NOLANG__'; exit }
$f = Await ([Windows.Storage.StorageFile]::GetFileFromPathAsync('__PATH__')) ([Windows.Storage.StorageFile])
$s = Await ($f.OpenAsync([Windows.Storage.FileAccessMode]::Read)) ([Windows.Storage.Streams.IRandomAccessStream])
$d = Await ([Windows.Graphics.Imaging.BitmapDecoder]::CreateAsync($s)) ([Windows.Graphics.Imaging.BitmapDecoder])
$b = Await ($d.GetSoftwareBitmapAsync()) ([Windows.Graphics.Imaging.SoftwareBitmap])
$best = $null; $bestLen = -1
foreach ($e in $engs) {
  $r = Await ($e.RecognizeAsync($b)) ([Windows.Media.Ocr.OcrResult])
  $len = ($r.Text -replace '\s', '').Length
  if ($len -gt $bestLen) { $best = $r; $bestLen = $len; $bestTag = $e.RecognizerLanguage.LanguageTag }
}
'__TAG__' + $bestTag
$best.Lines | % { $_.Text }
";

        public class NoJapaneseOcr : Exception
        {
            public NoJapaneseOcr() : base("이 컴퓨터에는 그 언어의 글자 읽기 기능이 없어요.") { }
        }

        static readonly string[][] DakutenFix = {
            new[] { "くたさい", "ください" }, new[] { "ありかとう", "ありがとう" }, new[] { "こさいま", "ございま" },
            new[] { "(?<!あ)んた(?=[よねなぞ！？…。、]|$)", "んだ" }, new[] { "てす(?=[よねかが！？…。、]|$)", "です" },
            new[] { "てしょ", "でしょ" }, new[] { "たけと", "だけど" }, new[] { "けと(?=[！？…。、]|$)", "けど" },
            new[] { "そ！", "ぞ！" }, new[] { "すっと", "ずっと" }, new[] { "とうして", "どうして" },
            new[] { "たいしょうふ|たいじょうぶ|だいしょうぶ", "だいじょうぶ" }, new[] { "こめん", "ごめん" },
            new[] { "かんはって|かんばって|がんはって", "がんばって" }, new[] { "ちかう(?=[！？…。、よ]|$)", "ちがう" },
        };

        // 세로 글씨를 읽을 때 자주 틀리는 것 고치기 (시험해 보니 끝의 ！가 ー로, …가 . 로, 줄 앞에 쓸데없는 、・ 가 붙음)
        static string FixJa(string l)
        {
            l = Regex.Replace(l, "^[、。・,.\\-ー]+", "");
            l = Regex.Replace(l, "(?<=[\\p{IsHiragana}\\p{IsCJKUnifiedIdeographs}])[ー\\-]+$", "！");
            l = Regex.Replace(l, "[.・]+$", "…");
            l = l.Replace("?", "？").Replace("!", "！");
            // 탁점(゛)을 빠뜨리기 쉬운 흔한 말 (빠지면 구글이 엉뚱하게 번역함)
            foreach (var p in DakutenFix) l = Regex.Replace(l, p[0], p[1]);
            return l;
        }

        // 글자 읽기: 만화 글씨 읽기(추가 기능)가 있으면 일본어는 그걸로, 아니면 윈도우 기본 글자 읽기
        public static string Ocr(Image crop)
        {
            string lang = Langs[LangIndex][1];
            if (MangaOcr.Enabled && lang == "ja") return MangaOcr.Read(crop);
            if (!MangaOcr.Enabled || lang != "auto") { string tg; return WinOcr(crop, out tg); }
            // 자동 감지: 윈도우로 먼저 언어를 보고, 일본어(또는 못 읽음)면 만화 글씨 읽기로
            string tag = "", r = "";
            try { r = WinOcr(crop, out tag); } catch (NoJapaneseOcr) { }
            if (tag.StartsWith("ja") || r.Length == 0)
            {
                try { return MangaOcr.Read(crop); }
                catch { if (r.Length > 0) return r; throw; }
            }
            return r;
        }

        static string WinOcr(Image crop, out string tag)
        {
            tag = "";
            string tmp = Path.Combine(Path.GetTempPath(), "만화보관함_읽기_" + Guid.NewGuid().ToString("N") + ".png");
            try
            {
                // 작은 칸은 키워야 잘 읽힘
                double s = Math.Max(1.0, Math.Min(4.0, 900.0 / Math.Max(crop.Width, crop.Height)));
                using (var b = new Bitmap((int)(crop.Width * s), (int)(crop.Height * s), PixelFormat.Format24bppRgb))
                {
                    using (var g = Graphics.FromImage(b)) { g.Clear(Color.White); g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.DrawImage(crop, 0, 0, b.Width, b.Height); }
                    b.Save(tmp, ImageFormat.Png);
                }
                string script = OcrScript.Replace("__PATH__", tmp.Replace("'", "''")).Replace("__LANG__", Langs[LangIndex][2]);
                var psi = new ProcessStartInfo("powershell.exe",
                    "-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand " + Convert.ToBase64String(Encoding.Unicode.GetBytes(script)));
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                psi.StandardOutputEncoding = Encoding.UTF8;
                using (var p = Process.Start(psi))
                {
                    string output = p.StandardOutput.ReadToEnd();
                    p.WaitForExit(30000);
                    if (output.Contains("__NOLANG__")) throw new NoJapaneseOcr();
                    var all = output.Replace("\r", "").Split('\n').ToList();
                    tag = all.Where(l => l.StartsWith("__TAG__")).Select(l => l.Substring(7)).FirstOrDefault() ?? "";
                    // 일본어·중국어 글자 읽기는 글자 사이에 빈칸을 넣음 → 빼기 (영어 같은 말은 그대로)
                    bool cjk = tag.StartsWith("ja") || tag.StartsWith("zh");
                    var lines = all.Where(l => !l.StartsWith("__TAG__")).Select(l => cjk ? l.Replace(" ", "").Trim() : l.Trim()).Where(l => l.Length > 0);
                    if (tag.StartsWith("ja")) lines = lines.Select(FixJa).Where(l => l.Length > 0);
                    // 일본어·중국어 세로 글씨의 줄 바꿈은 문장이 끊긴 게 아니라서 한 줄로 이어야 번역이 자연스러움
                    return string.Join(cjk ? "" : "\r\n", lines);
                }
            }
            finally { try { File.Delete(tmp); } catch { } }
        }
    }

    // ───────────────────────── 번역 자막 창 ─────────────────────────
    class TranslateDialog : Form
    {
        readonly Image crop, page;
        readonly TextBox origBox, transBox, directBox;
        readonly Label status;
        readonly Button aiBtn, googleBtn, gTextBtn, askBtn;
        // 맨 아래 '직접 적기' 칸에 글이 있으면 그 글을, 없으면 번역 칸 글을 넣음
        public string Result { get { return directBox.Text.Trim().Length > 0 ? directBox.Text : transBox.Text; } }

        public TranslateDialog(Image crop, Image page, bool coverWhite)
        {
            this.crop = crop;
            this.page = page;
            Text = "번역 자막";
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimizeBox = false; ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Theme.Bar; ForeColor = Theme.Text; Font = Theme.F(10.5f);
            ClientSize = new Size(1010, 640);
            MinimumSize = new Size(760, 520);

            var pic = new PictureBox();
            pic.Image = crop;
            pic.SizeMode = PictureBoxSizeMode.Zoom;
            pic.BackColor = Theme.ViewBack;
            pic.Bounds = new Rectangle(16, 16, 280, 530);
            pic.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Bottom;
            Controls.Add(pic);

            int x = 312;
            var langLbl = Lbl("원문 언어", x, 18);
            langLbl.Size = new Size(80, 22);
            langLbl.Anchor = AnchorStyles.Top | AnchorStyles.Left;   // 창이 넓어질 때 늘어나서 옆 고르기 칸을 덮지 않게
            Controls.Add(langLbl);
            var lang = new ComboBox();
            lang.DropDownStyle = ComboBoxStyle.DropDownList;
            lang.Location = new Point(x + 84, 14);
            lang.Width = 200;
            lang.AccessibleName = "원문 언어";
            foreach (var l in Translate.Langs) lang.Items.Add(l[0]);
            lang.SelectedIndex = Translate.LangIndex;
            lang.SelectedIndexChanged += delegate { Translate.LangIndex = lang.SelectedIndex; };
            Controls.Add(lang);

            int y0 = 30;
            Controls.Add(Lbl("원문 — 틀린 글자는 고쳐도 돼요", x, 14 + y0));
            origBox = Box(x, 38 + y0, 100);
            Controls.Add(origBox);
            Controls.Add(Lbl("번역 (말풍선에 들어갈 글) — 자연스럽게 다듬어 주세요", x, 148 + y0));
            transBox = Box(x, 172 + y0, 110);
            transBox.Font = Theme.F(12f);
            Controls.Add(transBox);

            aiBtn = Theme.MakeButton("AI로 읽고 번역", true);
            googleBtn = Theme.MakeButton(Translate.MtName + "로 읽고 번역", false);
            gTextBtn = Theme.MakeButton("원문만 " + Translate.MtName + " 번역", false);
            askBtn = Theme.MakeButton("AI에게 물어보기", false);
            var row = new FlowLayoutPanel();
            row.Bounds = new Rectangle(x - 4, 290 + y0, 690, 46);
            row.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            row.BackColor = Theme.Bar;
            row.WrapContents = false;
            var imgBtn = Theme.MakeButton("구글 이미지 번역", false);
            foreach (var b in new[] { aiBtn, imgBtn, googleBtn, gTextBtn, askBtn }) { b.Margin = new Padding(3, 3, 6, 3); row.Controls.Add(b); }
            Controls.Add(row);
            imgBtn.Click += delegate { OpenGoogleImage(); };

            status = Lbl("", x, 340 + y0);
            status.Size = new Size(680, 44);
            status.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            Controls.Add(status);

            // 번역 없이 글자만 바꾸고 싶을 때 여기에 적음 → 이 글이 그대로 들어감
            var directLbl = Lbl("직접 적기 — 번역 없이 바꿀 글을 여기에 적으면, 이 글이 그대로 들어가요", x, 386 + y0);
            directLbl.ForeColor = Theme.Accent;
            Controls.Add(directLbl);
            directBox = Box(x, 410 + y0, 104);
            directBox.BackColor = Theme.Card;
            directBox.ForeColor = Theme.Text;
            directBox.Font = Theme.F(12f);
            directBox.AccessibleName = "직접 적기";
            directBox.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            Controls.Add(directBox);

            var ok = Theme.MakeButton("넣기", true);
            ok.DialogResult = DialogResult.OK;
            var cancel = Theme.MakeButton("취소", false);
            cancel.DialogResult = DialogResult.Cancel;
            ok.Anchor = cancel.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            Controls.Add(ok); Controls.Add(cancel);
            CancelButton = cancel;
            Shown += delegate
            {
                cancel.Location = new Point(ClientSize.Width - 16 - cancel.Width, ClientSize.Height - 16 - cancel.Height);
                ok.Location = new Point(cancel.Left - 10 - ok.Width, cancel.Top);
                transBox.Focus();
                // 처음 열면 알아서: AI 키가 있으면 AI, 없으면 구글
                if (Translate.HasAi) RunAi(); else RunGoogle();
            };

            aiBtn.Enabled = askBtn.Enabled = Translate.HasAi;
            PaintBtn(aiBtn); PaintBtn(askBtn);
            if (!Translate.HasAi)
                new ToolTip().SetToolTip(aiBtn, "AI 번역은 첫 화면 '설정'에서 AI 키를 넣으면 쓸 수 있어요");
            aiBtn.Click += delegate { RunAi(); };
            googleBtn.Click += delegate { RunGoogle(); };
            gTextBtn.Click += delegate { RunGoogleText(); };
            askBtn.Click += delegate { RunAsk(); };
            ShowHint(coverWhite ? "" : "흰 칸 없이 글자만 들어가요.");
            ClientSize = new Size(1150, 640);   // 단추 다섯 개가 한 줄에 들어가게 (오른쪽에 붙은 칸들도 같이 늘어남)
        }

        // 고른 칸 그림을 복사하고 구글 번역 '이미지' 화면을 열어 줌 → 사용자가 Ctrl+V 로 붙여 넣고 번역을 보고 적음
        void OpenGoogleImage()
        {
            try { Clipboard.SetImage(crop); }
            catch (Exception ex) { ShowHint("그림을 복사하지 못했어요. " + ex.Message); return; }
            try { System.Diagnostics.Process.Start("https://translate.google.com/?sl=auto&tl=ko&op=images"); }
            catch (Exception ex) { ShowHint("인터넷 창을 열지 못했어요. " + ex.Message); return; }
            ShowHint("구글 번역 사이트가 열렸어요. 그곳에서 Ctrl+V 를 누르면 이 칸의 번역이 보여요. 보고 위 '번역' 칸에 한국어로 적어 주세요. " +
                     "(사이트에서 원문 글자를 복사해 오면 '원문' 칸에 붙여 넣고 다른 단추도 쓸 수 있어요)");
        }

        Label Lbl(string t, int x, int y)
        {
            var l = new Label();
            l.Text = t; l.AutoSize = false; l.Location = new Point(x, y); l.Size = new Size(680, 22);
            l.ForeColor = Theme.SubText;
            l.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            return l;
        }

        TextBox Box(int x, int y, int h)
        {
            var tb = new TextBox();
            tb.Multiline = true; tb.AcceptsReturn = true; tb.ScrollBars = ScrollBars.Vertical;
            tb.Bounds = new Rectangle(x, y, 682, h);
            tb.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            tb.Font = Theme.F(11.5f);
            return tb;
        }

        readonly ToolTip hintTip = new ToolTip();
        // 안내 글 (길어서 잘리면 마우스를 올려 전부 봄)
        void ShowHint(string s) { status.Text = s; hintTip.SetToolTip(status, s); }

        // 꺼진 단추는 회색으로 (주황이면 눌러지는 것처럼 보여서)
        static void PaintBtn(Button b)
        {
            b.BackColor = b.Enabled ? (b.Text.StartsWith("AI로") ? Theme.Accent : Theme.Card) : Color.FromArgb(52, 54, 62);
            b.ForeColor = b.Enabled ? (b.Text.StartsWith("AI로") ? Color.FromArgb(30, 20, 10) : Theme.Text) : Color.FromArgb(120, 124, 134);
        }

        void Busy(bool on, string msg)
        {
            foreach (var b in new[] { aiBtn, googleBtn, gTextBtn, askBtn }) b.Enabled = !on && (b == googleBtn || b == gTextBtn || Translate.HasAi);
            PaintBtn(aiBtn); PaintBtn(askBtn);
            UseWaitCursor = on;
            ShowHint(msg);
        }

        // 뒤에서 일하고, 끝나면 화면에 반영
        void Work(string msg, Action job, Action done)
        {
            Busy(true, msg);
            var t = new Thread(delegate()
            {
                Exception err = null;
                try { job(); } catch (Exception ex) { err = ex; }
                try
                {
                    BeginInvoke((MethodInvoker)delegate
                    {
                        Busy(false, "");
                        if (err != null)
                        {
                            if (err is Translate.NoJapaneseOcr)
                                ShowHint("이 컴퓨터에는 그 언어의 글자 읽기 기능이 없어서 원문을 읽지 못했어요. '구글 이미지 번역'을 눌러 번역을 보고 적거나, AI 번역을 써 주세요. (윈도우 설정 → 시간 및 언어 → 언어에서 그 언어를 추가하면 읽을 수 있어요)");
                            else ShowHint(err.Message);
                        }
                        else done();
                    });
                }
                catch { }
            });
            t.IsBackground = true;
            t.Start();
        }

        void RunAi()
        {
            string o = "", tr = "", note = "";
            Work("AI가 읽고 번역하는 중… (10~40초쯤 걸려요)", delegate { Translate.Ai(crop, page, out o, out tr, out note); }, delegate
            {
                origBox.Text = o;
                transBox.Text = tr;
                ShowHint((tr.Length == 0 ? "이 칸에서 글자를 찾지 못했어요." : "AI 번역을 채웠어요. 자연스럽게 다듬고 '넣기'를 눌러요.") +
                         (note.Length > 0 ? "  AI 메모: " + note : ""));
            });
        }

        void RunGoogle()
        {
            string o = "", tr = "";
            string mt = Translate.MtName;
            Work("글자를 읽고 " + mt + "로 번역하는 중…", delegate
            {
                o = Translate.Ocr(crop);
                if (o.Length > 0) tr = Translate.Machine(o);
            }, delegate
            {
                origBox.Text = o;
                transBox.Text = tr;
                ShowHint(o.Length == 0 ? "글자를 읽지 못했어요. '구글 이미지 번역'을 눌러 번역을 보고 적어 주세요." : mt + " 번역을 채웠어요. 말투를 자연스럽게 다듬어 주세요. (손글씨·흐린 글씨는 잘못 읽을 수 있어요. 이상하면 '구글 이미지 번역'으로 확인해 보세요)");
            });
        }

        void RunGoogleText()
        {
            string src = origBox.Text, tr = "";
            if (src.Trim().Length == 0) { ShowHint("원문 칸에 원문을 먼저 적어 주세요."); return; }
            string mt = Translate.MtName;
            Work(mt + "로 번역하는 중…", delegate { tr = Translate.Machine(src); }, delegate
            {
                transBox.Text = tr;
                ShowHint(mt + " 번역을 채웠어요. 말투를 자연스럽게 다듬어 주세요.");
            });
        }

        void RunAsk()
        {
            string q = TextPrompt.Ask(this, "AI에게 물어보기", "이 장면에서 궁금한 걸 적어 주세요. (예: 이 말이 무슨 뜻이야? 더 자연스럽게 하면?)");
            if (q == null || q.Trim().Length == 0) return;
            string o = origBox.Text, cur = transBox.Text, ans = "";
            Work("AI에게 물어보는 중…", delegate { ans = Translate.Ask(crop, page, o, cur, q); }, delegate
            {
                ShowHint("");
                MessageBox.Show(this, "질문: " + q.Trim() + "\n\n" + ans, "AI 답");
            });
        }
    }
}
