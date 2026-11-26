// 만화 보관함 — 여러 AI 고르기 (Claude · ChatGPT · Grok · Gemini)
// ChatGPT·Grok·Gemini 는 같은 모양(OpenAI 방식)으로 부름. 키는 각각 따로 암호화해서 저장
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace ComicShelf
{
    static partial class Translate
    {
        public static readonly string[] Providers = { "Claude", "ChatGPT", "Grok", "Gemini" };

        public static string ProviderName(string p)
        {
            switch (p)
            {
                case "ChatGPT": return "ChatGPT (OpenAI)";
                case "Grok": return "Grok (xAI)";
                case "Gemini": return "Gemini (구글)";
                default: return "Claude (Anthropic)";
            }
        }

        public static string Provider
        {
            get { string p = Edits.Get("AI종류", "Claude"); return Providers.Contains(p) ? p : "Claude"; }
            set { Edits.Set("AI종류", value); }
        }

        static string KeySetting(string p) { return p == "Claude" ? "AI키" : "AI키_" + p; }

        public static string KeyFor(string p)
        {
            string enc = Edits.Get(KeySetting(p), "");
            if (enc.Length == 0) return "";
            try { return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(enc), null, DataProtectionScope.CurrentUser)); }
            catch { return ""; }
        }

        public static void SetKey(string p, string value)
        {
            if (string.IsNullOrEmpty(value)) { Edits.Set(KeySetting(p), ""); return; }
            Edits.Set(KeySetting(p), Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(value.Trim()), null, DataProtectionScope.CurrentUser)));
        }

        public static string KeyStart(string p)
        {
            switch (p) { case "ChatGPT": return "sk-"; case "Grok": return "xai-"; case "Gemini": return "AIza"; default: return "sk-ant-"; }
        }

        public static string HowToKey(string p)
        {
            switch (p)
            {
                case "ChatGPT": return "키 받는 법: platform.openai.com 에 로그인 → API keys → Create new secret key. 쓴 만큼 OpenAI 결제 금액이 줄어요. (ChatGPT Plus 구독과는 따로예요)";
                case "Grok": return "키 받는 법: console.x.ai 에 로그인 → API Keys → Create API Key. 쓴 만큼 xAI 크레딧이 줄어요.";
                case "Gemini": return "키 받는 법: aistudio.google.com 에 로그인 → Get API key → 키 만들기. 무료 사용량이 있어요(하루 횟수 제한). 무료로 쓰면 보낸 그림·글이 구글 서비스 개선에 쓰일 수 있어요.";
                default: return "키 받는 법: platform.claude.com 에 로그인 → API Keys → Create Key. (Claude Max 플랜이면 설정 → 결제에서 매달 나오는 API 크레딧을 연결할 수 있어요)";
            }
        }

        // 처음 쓸 모델 (목록 불러오기로 바꿀 수 있음)
        public static string DefaultModel(string p)
        {
            switch (p)
            {
                case "ChatGPT": return "gpt-5.5";
                case "Grok": return "grok-4.7";
                case "Gemini": return "gemini-3.8-flash";
                default: return Saving ? "claude-sonnet-5-5" : "claude-opus-5-5";
            }
        }

        public static string ModelFor(string p)
        {
            string m = Edits.Get("AI모델_" + p, "");
            return m.Length > 0 ? m : DefaultModel(p);
        }
        public static void SetModel(string p, string m) { Edits.Set("AI모델_" + p, (m ?? "").Trim()); }

        public static string[] SuggestedModels(string p)
        {
            if (p == "Claude") return new[] { "claude-opus-5-5", "claude-sonnet-5-5", "claude-haiku-5-5" };
            return new[] { DefaultModel(p) };
        }

        static string BaseUrl(string p)
        {
            switch (p)
            {
                case "ChatGPT": return "https://api.openai.com/v1/";
                case "Grok": return "https://api.x.ai/v1/";
                case "Gemini": return "https://generativelanguage.googleapis.com/v1beta/openai/";
                default: return "https://api.anthropic.com/v1/";
            }
        }

        // ── 그 키로 쓸 수 있는 모델 목록 ──
        public static List<string> ListModels(string p)
        {
            string key = KeyFor(p);
            if (key.Length == 0) throw new Exception("먼저 키를 넣어 주세요.");
            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;
            var req = (HttpWebRequest)WebRequest.Create(BaseUrl(p) + "models" + (p == "Claude" ? "?limit=100" : ""));
            req.Timeout = 30000;
            if (p == "Claude") { req.Headers["x-api-key"] = key; req.Headers["anthropic-version"] = "2023-06-01"; }
            else req.Headers["Authorization"] = "Bearer " + key;
            string text = Send(req, null, p);
            var res = Json.Deserialize<Dictionary<string, object>>(text);
            var ids = new List<string>();
            object data;
            if (res.TryGetValue("data", out data) && data is System.Collections.ArrayList)
                foreach (var o in (System.Collections.ArrayList)data)
                {
                    var d = o as Dictionary<string, object>;
                    if (d == null || !(d.ContainsKey("id"))) continue;
                    string id = (d["id"] as string ?? "");
                    if (id.StartsWith("models/")) id = id.Substring(7);
                    if (UsefulModel(p, id)) ids.Add(id);
                }
            ids = ids.Distinct().OrderByDescending(x => x, StringComparer.Ordinal).ToList();
            return ids;
        }

        static bool UsefulModel(string p, string id)
        {
            string s = id.ToLowerInvariant();
            string[] bad = { "embed", "tts", "whisper", "dall", "image", "imagen", "imagine", "audio", "realtime", "transcribe", "moderation", "search", "video", "veo", "aqa", "codex", "instruct", "babbage", "davinci", "computer-use" };
            if (bad.Any(b => s.Contains(b))) return false;
            switch (p)
            {
                case "ChatGPT": return s.StartsWith("gpt-") || s.StartsWith("o3") || s.StartsWith("o4") || s.StartsWith("o5") || s.StartsWith("chatgpt");
                case "Grok": return s.StartsWith("grok");
                case "Gemini": return s.StartsWith("gemini") || s.StartsWith("gemma");
                default: return s.StartsWith("claude");
            }
        }

        // ── ChatGPT·Grok·Gemini 부르기 (OpenAI 방식) ──
        static string CallCompat(List<object> claudeContent, string system, bool wantJson)
        {
            string p = Provider;
            var parts = new List<object>();
            foreach (var o in claudeContent)
            {
                var blk = o as Dictionary<string, object>;
                if (blk == null) continue;
                if ((blk["type"] as string) == "text") parts.Add(new Dictionary<string, object> { { "type", "text" }, { "text", blk["text"] } });
                else if ((blk["type"] as string) == "image")
                {
                    var src = (Dictionary<string, object>)blk["source"];
                    parts.Add(new Dictionary<string, object>
                    {
                        { "type", "image_url" },
                        { "image_url", new Dictionary<string, object> { { "url", "data:" + src["media_type"] + ";base64," + src["data"] } } }
                    });
                }
            }
            var body = new Dictionary<string, object>
            {
                { "model", ModelFor(p) },
                { "messages", new object[]
                    {
                        new Dictionary<string, object> { { "role", "system" }, { "content", system } },
                        new Dictionary<string, object> { { "role", "user" }, { "content", parts } }
                    }
                }
            };
            if (wantJson && p != "Gemini") body["response_format"] = new Dictionary<string, object> { { "type", "json_object" } };
            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;
            var req = (HttpWebRequest)WebRequest.Create(BaseUrl(p) + "chat/completions");
            req.Method = "POST";
            req.ContentType = "application/json";
            req.Timeout = 180000;
            req.ReadWriteTimeout = 180000;
            req.Headers["Authorization"] = "Bearer " + KeyFor(p);
            string text = Send(req, Encoding.UTF8.GetBytes(Json.Serialize(body)), p);
            var res = Json.Deserialize<Dictionary<string, object>>(text);
            var choices = res.ContainsKey("choices") ? res["choices"] as System.Collections.ArrayList : null;
            if (choices == null || choices.Count == 0) throw new Exception("AI 가 답을 주지 않았어요. 다시 해 보세요.");
            var ch = (Dictionary<string, object>)choices[0];
            if ((ch.ContainsKey("finish_reason") ? ch["finish_reason"] as string : "") == "content_filter")
                throw new Exception("AI 가 이 장면은 번역하지 않겠다고 했어요. 직접 적어 주세요.");
            var msg = ch["message"] as Dictionary<string, object>;
            string answer = msg != null && msg.ContainsKey("content") ? msg["content"] as string : null;
            if (string.IsNullOrEmpty(answer))
            {
                string refusal = msg != null && msg.ContainsKey("refusal") ? msg["refusal"] as string : null;
                throw new Exception(string.IsNullOrEmpty(refusal) ? "AI 가 빈 답을 줬어요. 다시 해 보세요." : "AI 가 이 장면은 번역하지 않겠다고 했어요. 직접 적어 주세요.");
            }
            return answer;
        }

        // 요청 보내고 받기 + 오류를 쉬운 말로
        static string Send(HttpWebRequest req, byte[] body, string p)
        {
            string who = ProviderName(p).Split(' ')[0];
            if (body != null) using (var s = req.GetRequestStream()) s.Write(body, 0, body.Length);
            try
            {
                using (var resp = (HttpWebResponse)req.GetResponse())
                using (var r = new StreamReader(resp.GetResponseStream(), Encoding.UTF8)) return r.ReadToEnd();
            }
            catch (WebException ex)
            {
                var hr = ex.Response as HttpWebResponse;
                if (hr == null) throw new Exception("인터넷에 연결하지 못했어요. (" + ex.Message + ")");
                string err = "";
                try { using (var r = new StreamReader(hr.GetResponseStream(), Encoding.UTF8)) err = r.ReadToEnd(); } catch { }
                int code = (int)hr.StatusCode;
                string msg = "";
                try
                {
                    var o = Json.DeserializeObject(err);
                    if (o is object[] && ((object[])o).Length > 0) o = ((object[])o)[0];   // Gemini 는 [ {error} ] 로 줄 때가 있음
                    var e = ((Dictionary<string, object>)o)["error"];
                    msg = e is string ? (string)e : (((Dictionary<string, object>)e)["message"] as string ?? "");
                }
                catch { }
                string low = (msg + " " + err).ToLowerInvariant();
                if (code == 401 || code == 403 || low.Contains("api key not valid") || low.Contains("valid api key") || low.Contains("api_key_invalid") || low.Contains("incorrect api key") || low.Contains("invalid api key"))
                    throw new Exception(who + " 키가 맞지 않아요. 설정에서 키를 다시 넣어 주세요.");
                if (low.Contains("insufficient_quota") || low.Contains("credit") || low.Contains("billing") || code == 402)
                    throw new Exception(who + " 사용 금액(크레딧)이 부족해요. " + who + " 결제 화면을 확인해 주세요.");
                if (code == 429) throw new Exception(who + " 요청이 너무 많거나 무료 사용량을 다 썼어요. 조금 뒤에 다시 해 주세요.");
                if (code == 404 || low.Contains("model_not_found") || low.Contains("does not exist") || (low.Contains("model") && low.Contains("not found")))
                    throw new Exception("모델 이름 '" + ModelFor(p) + "' 을(를) " + who + " 에서 찾지 못했어요. 설정 → AI 번역에서 '모델 목록 불러오기'로 다시 골라 주세요.");
                if (code >= 500) throw new Exception(who + " 서버에 잠깐 문제가 있어요. 조금 뒤에 다시 해 주세요.");
                throw new Exception(who + " 요청이 실패했어요. (" + code + ") " + msg);
            }
        }

        // AI 답에서 { … } 만 꺼내기 (앞뒤에 다른 말이 붙어 와도)
        static Dictionary<string, object> ParseJsonLoose(string s)
        {
            int a = s.IndexOf('{'), b = s.LastIndexOf('}');
            if (a < 0 || b <= a) throw new Exception("AI 답을 알아보지 못했어요. 다시 해 보세요.");
            return Json.Deserialize<Dictionary<string, object>>(s.Substring(a, b - a + 1));
        }

        const string JsonAsk = "\n\n다음 JSON 하나로만 답해 (다른 말 없이): {\"original\": \"칸 안의 원문 그대로(줄바꿈은 \\n)\", \"translation\": \"자연스러운 한국어 번역\", \"note\": \"번역할 때 알아 둘 점을 한국어로 짧게, 없으면 빈 문자열\"}";
    }
}
