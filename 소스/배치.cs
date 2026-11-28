// 만화 보관함 — 화마다 그림 배치 (순서·그림 사이 간격·직접 만든 빈 공간·칸 테두리·바탕)
// 화 폴더의 '배치.txt' 에 저장 → 꾸러미 파일로 주고받을 때도 같이 감
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace ComicShelf
{
    class EpLayout
    {
        public const string FileName = "배치.txt";
        public const int RefWidth = 900;          // 간격·테두리 두께는 너비 900 기준 (확대·축소하면 같이 커짐)

        public int Gap;                           // 모든 그림 사이 간격
        public int Border;                        // 칸 테두리 두께 (0 = 없음)
        public Color BorderColor = Color.Black;
        public int Back;                          // 0 = 그대로(어둡게), 1 = 흰 종이, 2 = 검은 종이
        public List<string> Order = new List<string>();                   // 파일 이름 순서
        public Dictionary<string, int> Extra = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);   // 그림 위 직접 만든 빈 공간
        public HashSet<string> NoBorder = new HashSet<string>(StringComparer.OrdinalIgnoreCase);              // 테두리 뺀 그림

        public bool Paper { get { return Back == 1 || Back == 2; } }
        public Color BackColor { get { return Back == 1 ? Color.White : Color.Black; } }

        public int ExtraOf(string path) { int v; return Extra.TryGetValue(Path.GetFileName(path), out v) ? v : 0; }
        public void SetExtra(string path, int v) { string n = Path.GetFileName(path); if (v <= 0) Extra.Remove(n); else Extra[n] = v; }
        public bool HasBorder(string path) { return Border > 0 && !NoBorder.Contains(Path.GetFileName(path)); }

        public static EpLayout Load(string dir)
        {
            var l = new EpLayout();
            string f = Path.Combine(dir, FileName);
            if (!File.Exists(f)) return l;
            try
            {
                foreach (var line in File.ReadAllLines(f, Encoding.UTF8))
                {
                    if (line.StartsWith("그림\t"))
                    {
                        var p = line.Split('\t');
                        if (p.Length < 2 || p[1].Length == 0) continue;
                        l.Order.Add(p[1]);
                        int e;
                        if (p.Length > 2 && int.TryParse(p[2], out e) && e > 0) l.Extra[p[1]] = e;
                        if (p.Length > 3 && p[3] == "1") l.NoBorder.Add(p[1]);
                        continue;
                    }
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    string k = line.Substring(0, eq), v = line.Substring(eq + 1);
                    int n;
                    if (k == "간격" && int.TryParse(v, out n)) l.Gap = Math.Max(0, Math.Min(2000, n));
                    else if (k == "테두리" && int.TryParse(v, out n)) l.Border = Math.Max(0, Math.Min(60, n));
                    else if (k == "테두리색" && int.TryParse(v, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out n)) l.BorderColor = Color.FromArgb(n);
                    else if (k == "바탕" && int.TryParse(v, out n)) l.Back = Math.Max(0, Math.Min(2, n));
                }
            }
            catch { }
            return l;
        }

        // 지금 화에 있는 그림 순서대로 저장 (없어진 그림은 빠짐)
        public void Save(string dir, IList<string> paths)
        {
            Order = paths.Select(p => Path.GetFileName(p)).ToList();
            var sb = new StringBuilder();
            sb.AppendLine("간격=" + Gap);
            sb.AppendLine("테두리=" + Border);
            sb.AppendLine("테두리색=" + BorderColor.ToArgb().ToString("X8"));
            sb.AppendLine("바탕=" + Back);
            foreach (var n in Order)
            {
                int e; Extra.TryGetValue(n, out e);
                sb.AppendLine("그림\t" + n + "\t" + e + "\t" + (NoBorder.Contains(n) ? "1" : "0"));
            }
            File.WriteAllText(Path.Combine(dir, FileName), sb.ToString(), new UTF8Encoding(false));
        }

        // 저장된 순서대로 줄 세우기 (목록에 없는 새 그림은 뒤에 이름순)
        public static List<string> Apply(string dir, List<string> byName)
        {
            var l = Load(dir);
            if (l.Order.Count == 0) return byName;
            var rank = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < l.Order.Count; i++) if (!rank.ContainsKey(l.Order[i])) rank[l.Order[i]] = i;
            return byName.Select((p, i) => new { p, i })
                .OrderBy(x => { int r; return rank.TryGetValue(Path.GetFileName(x.p), out r) ? r : 100000 + x.i; })
                .Select(x => x.p).ToList();
        }
    }
}
