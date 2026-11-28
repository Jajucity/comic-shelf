// 만화 보관함 — 사용법 창 (처음 켤 때 한 번 자동, '사용법' 단추나 F1 로 언제든)
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace ComicShelf
{
    class GuideForm : Form
    {
        // 제목, 내용 (# 로 시작하는 줄 = 작은 제목)
        static readonly string[][] Pages =
        {
            new[] { "처음 시작하기", @"# 만화 보관함에 오신 걸 환영해요!
좋아하는 만화를 모아 두고, 웹툰처럼 위에서 아래로 내려 보는 프로그램이에요.
인터넷에 연결하지 않고, 모든 만화는 이 컴퓨터의 '자료' 폴더에만 저장돼요.

# 3단계로 바로 시작
1. 첫 화면의 큰 + (만화 추가)를 눌러 썸네일·제목·설명을 넣고 저장해요.
2. 생긴 만화 카드를 누르고 '+ 새 화 만들기'로 1화를 만들어요.
3. 1화를 누르고 위쪽의 '+ 넣기'로 그림을 넣어요. 넣은 순서대로 아래에 이어 붙어요.

# 알아 두면 좋은 것
- 넣은 그림은 프로그램 '자료' 폴더로 복사돼요. 원래 파일을 지우거나 옮겨도 만화는 남아요.
- 지우는 것은 모두 한 번 물어보고 휴지통으로 보내요. 실수해도 휴지통에서 되살릴 수 있어요.
- 이 사용법은 첫 화면의 '사용법' 단추나 F1 키로 언제든 다시 볼 수 있어요." },

            new[] { "만화 목록 (첫 화면)", @"# 만화 추가
- 큰 + 카드를 눌러요. 썸네일은 가운데를 정사각형으로 잘라 보여 줘요.
- 움직이는 gif 를 썸네일로 고르면 카드에서도 움직여요.

# 검색
- 오른쪽 위 칸에 제목이나 설명의 일부를 적으면 바로 찾아 줘요. 띄어쓰기는 상관없어요.

# 카드를 오른쪽 클릭하면
- 정보 고치기: 제목·썸네일·설명을 바꿔요.
- 꾸러미 파일로 내보내기: 친구에게 줄 파일을 만들어요. ('친구와 주고받기' 참고)
- 이 만화 지우기: 휴지통으로 보내요." },

            new[] { "회차 목록", @"# 새 화 만들기
- '+ 새 화 만들기'를 누를 때마다 1화, 2화… 가 생겨요.

# 화 검색·정렬
- '화 검색' 칸에 숫자를 적으면 그 숫자가 들어간 화만 보여요. (예: 3 → 3화, 13화)
- 정렬 단추로 '1화부터 / 최근 화부터'를 바꿔요.

# 언제 봤는지
- 본 화에는 본 날이 나오고, 가장 최근에 본 화는 주황색으로 표시돼요.

# 화 지우기
- 화를 오른쪽 클릭 → '이 화 지우기'. 뒤의 화 번호는 하나씩 당겨져요." },

            new[] { "만화 보기", @"# 내려 보기
- 마우스 휠, ↑ ↓ 키, 스페이스, Page Up/Down 으로 내려 봐요.
- 왼쪽 클릭한 채로 끌면 보고 싶은 쪽으로 옮겨져요. (작게 볼 때도 옆으로 옮길 수 있어요)

# 확대·축소
- Ctrl 을 누른 채 휠을 위로 = 확대, 아래로 = 축소.
- '원래 크기·위치' 단추 = 100% 크기로, 가운데로.

# 다른 화로 가기
- 위쪽 가운데의 1화 2화 3화… 줄을 누르면 그 화로 가요. 지금 보는 화는 주황색이에요. (휠로 옆으로 넘겨요)
- 맨 아래의 '◀ 이전 화 / 목록 열기 / 다음 화 ▶' 단추도 있어요. 목록은 오른쪽에 열리고 맨 아래 '목록 닫기'로 닫아요.

# 전체 화면
- F11 또는 Alt+Enter = 전체 화면, Esc = 돌아오기.

# 그림을 오른쪽 클릭하면
- 그림 고치기 / 구글 이미지 번역 열기 / 번역된 그림으로 바꾸기 / 이 그림 지우기

# 편집 (위쪽 '✎ 편집' 단추)
- 순서 바꾸기: 그림을 끌어서 주황 줄이 보이는 자리에 놓으면 순서가 바뀌어요.
- 빈 공간 만들기: 그림을 아래로 끌면 그 위에 빈 공간이 생기고, 위로 끌면 줄어요.
- 모든 그림 사이 간격: 숫자만큼 모든 그림 사이를 띄워요.
- 칸 테두리: 두께와 색을 정하면 그림마다 만화 칸처럼 테두리가 생겨요. 편집 중 그림을 오른쪽 클릭하면 그 그림만 뺄 수 있어요.
- 바탕: 흰 종이·검은 종이로 하면 만화책처럼 옆 여백도 생겨요.
- 다 했으면 '다 했어요'. 바꾼 것은 바로 저장되고, 꾸러미 파일로 보낼 때도 같이 가요." },

            new[] { "넣기 (그림·영상·음악)", @"# '+ 넣기' 단추를 누르면
- 파일에서 넣기: 그림(jpg·png·bmp), 움직이는 그림(gif), 영상(mp4·mkv·avi·wmv·mov), 음악(mp3·wav·m4a·wma·flac)
- 유튜브 링크로 넣기: 유튜브 영상 주소를 붙여 넣어요. (복사해 두었으면 저절로 들어가 있어요)
- 새 캔버스 만들기: 빈 그림을 만들어 바로 그려요. 가로는 그 화의 다른 그림과 같고, 세로는 숫자나 끌기로 정해요.

# 유튜브 장면
- 유튜브로 보기: 장면 안에서 유튜브 재생기로 봐요. 인터넷이 있어야 하고, 광고는 유튜브 그대로 나와요. 조작(재생·소리·자막·전체 화면)은 유튜브 재생기 것을 써요.
- 오른쪽 클릭 → '유튜브에서 열기'로 인터넷 창에서도 볼 수 있어요. 다른 곳에서 재생을 막아 둔 영상은 거기서 봐야 해요." + YtLink.SaveGuide + @"

# 영상
- 처음엔 첫 장면 위에 큰 ▶ 가 보여요. 누르면 재생돼요.
- 아래 막대: 재생·멈춤, -5초, +5초, 장면 옮기기, 소리, 전체 화면.
- 영상 위에서 마우스를 가만히 두면 막대와 마우스가 숨어요. 움직이면 다시 나와요.
- 영상 위에서 ← → = 5초씩, 스페이스 = 재생·멈춤.

# 음악
- 화마다 음악 하나를 넣을 수 있어요. 그 화를 보는 동안 반복해서 나와요.
- 위쪽 '♪ 음악 끄기/켜기'와 옆의 막대로 소리를 조절해요. 단추를 오른쪽 클릭하면 음악을 뺄 수 있어요." },

            new[] { "그림 고치기", @"그림을 오른쪽 클릭 → '그림 고치기'. 원래 그림 파일은 절대 바뀌지 않아요. '원래대로'로 언제든 처음으로 돌릴 수 있어요.

# 도구
- 옮기기·고치기: 자막·도형을 끌어 옮겨요. 두 번 누르면 글 고치기, 오른쪽 클릭하면 맨 앞·맨 뒤·지우기.
- 브러시: 누른 채 끌어서 그려요.
- 지우개: 누를 때마다 무엇을 지울지(자막·도형·브러시 그림·원본 그림) 골라요.
- 자막 넣기: 누른 곳에 글자를 넣어요. 테두리를 켤 수 있어요.
- 번역 자막: 원래 글자가 있는 칸을 끌어서 고르면 번역 창이 떠요. 그 칸의 외국어(일본어·영어·중국어 등, 위에서 고르거나 자동 감지)를 읽고 번역을 미리 채워 줘요(설정에 AI 키가 있으면 AI, 없으면 설정에서 고른 무료 자동 번역: 구글 또는 DeepL). 자연스럽게 다듬고 '넣기'. 흰 칸으로 덮을지 고를 수 있어요.
- 그림 통째로 번역: 보는 화면에서 그림을 오른쪽 클릭 → '구글 이미지 번역 열기'. 구글 사이트에서 Ctrl+V 로 붙여 넣고 번역본을 받은 뒤, 다시 오른쪽 클릭 → '번역된 그림으로 바꾸기…'로 받은 파일을 골라요. 원본은 남아요.
- 번역 창의 'AI에게 물어보기'로 뜻이나 뉘앙스를 물어볼 수 있어요. (AI 키가 있을 때)
- 자막 글씨체: 왼쪽 아래 '자막 글씨체'에서 컴퓨터에 있는 한글 글씨체를 골라요.
- 도형: 사각형·원·별·화살표·말풍선 등. 끌어서 크기를 정하고, '안쪽 채우기'를 켤 수 있어요.
- 스포이드: 그림에서 색을 골라요.

# 색
- 먼저 바꿀 칸(글자·선 / 채우기 / 테두리)을 누르고, 아래 색이나 스포이드로 골라요.
- '화면에서 색 고르기'로 화면 아무 곳의 색도 가져올 수 있어요. 고른 색은 모아 둬요.

# 그 밖에
- Ctrl+Z = 되돌리기, Ctrl+휠 = 확대·축소, 다 했으면 '저장'." },

            new[] { "친구와 주고받기", @"# 만화 보내기
1. 만화 카드를 오른쪽 클릭 → '꾸러미 파일로 내보내기'.
2. 생긴 .comicpack 파일 하나를 메신저나 USB 로 건네요.
- 언제 봤는지 기록은 담기지 않아요.

# 만화 받기
- 첫 화면 위의 '꾸러미로 추가' → 받은 .comicpack 파일을 고르면 끝이에요.

# 프로그램을 친구에게 줄 때
- '만화 보관함.exe' 파일 하나만 주면 돼요. 내 '자료' 폴더(내 만화)는 주지 마세요.
- 처음 열 때 윈도우가 'PC 보호' 창을 띄우면 '추가 정보' → '실행'을 누르면 돼요. (인터넷에서 받은, 서명이 없는 프로그램이라 뜨는 안내예요)" },

            new[] { "자주 묻는 것", @"# 내 만화는 어디에 저장되나요?
- 프로그램 파일 옆의 '자료' 폴더예요. 프로그램을 옮길 때는 '자료' 폴더도 같이 옮겨야 해요.

# 인터넷을 쓰나요? 내 정보가 나가나요?
- 내 만화와 개인 정보는 밖으로 보내지 않아요. 인터넷은 이럴 때만 써요: 새 판 확인(끌 수 있어요), 번역할 때 고른 칸의 글자나 그림을 번역기(구글·DeepL·AI)에 보낼 때, 유튜브 장면을 넣거나 볼 때.

# 영상이나 음악이 안 나와요
- 윈도우 미디어 플레이어 기능을 써요. 윈도우 설정에서 '선택적 기능'에 Windows Media Player 가 켜져 있는지 확인해 주세요.

# AI 번역은 어떻게 켜요?
- 첫 화면 '설정' → 'AI 번역'에서 쓸 AI(Claude·ChatGPT·Grok·Gemini)를 고르고 그 AI 의 API 키를 넣으면 돼요. 쓴 만큼 그 AI 의 크레딧이 줄어요(Gemini 는 무료 사용량이 있어요). 키는 이 컴퓨터에만 암호화해서 저장돼요.
- 키를 넣으면 '모델 목록 불러오기'로 쓸 모델을 골라요.
- 키가 없으면 무료 자동 번역을 써요. 설정에서 구글(키 필요 없음)과 DeepL(무료 키 필요, 한 달 50만 글자) 중에 골라요.
- 자동 번역은 글자를 읽으려면 윈도우 설정 → 시간 및 언어 → 언어에서 그 언어(예: '일본어')를 추가해야 해요. 세로 글씨는 잘못 읽을 수 있어요.
- 일본어 만화는 설정 → '자동 번역·글자 읽기'에서 '만화 글씨 읽기'(약 650MB)를 받으면 세로 글씨도 훨씬 잘 읽어요.

# 번역한 만화를 친구에게 줘도 돼요?
- 혼자 보는 건 괜찮지만, 남의 만화를 번역해서 나눠 주거나 올리면 저작권 문제가 될 수 있어요.

# webp 그림이 안 들어가요
- 윈도우 기본 그림 기능이 webp 를 못 읽어요. 그림판에서 png 로 저장한 뒤 넣어 주세요." },
        };

        readonly RichTextBox box;
        readonly List<Button> tabs = new List<Button>();

        public GuideForm() : this(0) { }

        public GuideForm(int page)
        {
            Text = "만화 보관함 사용법";
            Size = new Size(940, 640);
            MinimumSize = new Size(720, 480);
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            BackColor = Theme.Back;
            ForeColor = Theme.Text;
            Font = Theme.F(10.5f);
            KeyPreview = true;

            var left = new FlowLayoutPanel();
            left.Dock = DockStyle.Left;
            left.Width = 236;
            left.FlowDirection = FlowDirection.TopDown;
            left.WrapContents = false;
            left.AutoScroll = true;
            left.Padding = new Padding(12, 12, 8, 12);
            left.BackColor = Theme.Bar;
            var head = new Label();
            head.Text = "사용법";
            head.AutoSize = true;
            head.Font = Theme.F(15f, FontStyle.Bold);
            head.ForeColor = Theme.Text;
            head.Margin = new Padding(4, 2, 4, 12);
            left.Controls.Add(head);
            for (int i = 0; i < Pages.Length; i++)
            {
                var b = Theme.MakeButton(Pages[i][0], false);
                b.AutoSize = false;
                b.Size = new Size(208, 40);
                b.TextAlign = ContentAlignment.MiddleLeft;
                b.Margin = new Padding(0, 0, 0, 6);
                int k = i;
                b.Click += delegate { ShowPage(k); };
                tabs.Add(b);
                left.Controls.Add(b);
            }

            var bottom = new FlowLayoutPanel();
            bottom.Dock = DockStyle.Bottom;
            bottom.Height = 58;
            bottom.FlowDirection = FlowDirection.RightToLeft;
            bottom.Padding = new Padding(10);
            bottom.BackColor = Theme.Bar;
            var close = Theme.MakeButton("닫기", true);
            close.Click += delegate { Close(); };
            var next = Theme.MakeButton("다음 ▶", false);
            next.Click += delegate { ShowPage(Math.Min(Pages.Length - 1, current + 1)); };
            var prev = Theme.MakeButton("◀ 이전", false);
            prev.Click += delegate { ShowPage(Math.Max(0, current - 1)); };
            bottom.Controls.Add(close);
            bottom.Controls.Add(next);
            bottom.Controls.Add(prev);
            CancelButton = close;

            box = new RichTextBox();
            box.Dock = DockStyle.Fill;
            box.ReadOnly = true;
            box.BorderStyle = BorderStyle.None;
            box.BackColor = Theme.Back;
            box.ForeColor = Theme.Text;
            box.Font = Theme.F(11.5f);
            box.DetectUrls = false;
            box.TabStop = false;
            var pad = new Panel();
            pad.Dock = DockStyle.Fill;
            pad.Padding = new Padding(28, 22, 20, 10);
            pad.BackColor = Theme.Back;
            pad.Controls.Add(box);

            Controls.Add(pad);
            Controls.Add(left);
            Controls.Add(bottom);
            Shown += delegate { ShowPage(Math.Max(0, Math.Min(Pages.Length - 1, page))); };
        }

        int current;

        void ShowPage(int i)
        {
            current = i;
            for (int k = 0; k < tabs.Count; k++)
            {
                tabs[k].BackColor = k == i ? Theme.Accent : Theme.Card;
                tabs[k].ForeColor = k == i ? Color.FromArgb(30, 20, 10) : Theme.Text;
            }
            box.Clear();
            AddLine(Pages[i][0], Theme.F(17f, FontStyle.Bold), Theme.Accent);
            AddLine("", Theme.F(6f), Theme.Text);
            foreach (var raw in Pages[i][1].Replace("\r\n", "\n").Split('\n'))
            {
                if (raw.StartsWith("# ")) { AddLine("", Theme.F(5f), Theme.Text); AddLine(raw.Substring(2), Theme.F(12.5f, FontStyle.Bold), Theme.Text); }
                else AddLine(raw, Theme.F(11.5f), raw.StartsWith("-") || char.IsDigit(raw.Length > 0 ? raw[0] : ' ') ? Theme.Text : Theme.SubText);
            }
            box.SelectionStart = 0;
            box.ScrollToCaret();
        }

        void AddLine(string text, Font f, Color c)
        {
            box.SelectionStart = box.TextLength;
            box.SelectionLength = 0;
            box.SelectionFont = f;
            box.SelectionColor = c;
            box.AppendText(text + "\n");
        }

        // 처음 켰을 때 한 번만 보여 줌
        public static void ShowFirstTime(Form owner)
        {
            if (Edits.Get("사용법본", "") == "1") return;
            Edits.Set("사용법본", "1");
            using (var g = new GuideForm()) g.ShowDialog(owner);
        }

        public static void Open(Form owner)
        {
            using (var g = new GuideForm()) g.ShowDialog(owner);
        }
    }
}
