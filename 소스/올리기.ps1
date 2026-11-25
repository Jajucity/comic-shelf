# 만화 보관함 새 판 내놓기: 공개해도 되는 파일만 GitHub 저장소로 옮기고, 사이트를 올리고, 새 판(Release)을 만든다.
# 쓰는 법: powershell -File 올리기.ps1 -Notes "바뀐 점"
#  - 판 번호는 업데이트.cs 의 AppVersion 을 씀 (먼저 올려 두기)
#  - 내 만화(자료)·진행 기록·시험 화면·배포 zip 은 절대 올라가지 않음 (아래 목록에 있는 것만 복사)
param([string]$Notes = "")
$ErrorActionPreference = 'Continue'   # git 은 알림을 오류 줄로 내보내서, 결과는 종료 코드로 확인
$src  = $PSScriptRoot                       # ...\만화 보관함\소스
$base = Split-Path $src -Parent             # ...\만화 보관함
$gh   = Join-Path $env:LOCALAPPDATA 'gh-cli\bin\gh.exe'
$repo = Join-Path $env:LOCALAPPDATA 'comic-shelf-repo'
$owner = 'Jajucity'; $name = 'comic-shelf'

$ver = [regex]::Match([IO.File]::ReadAllText((Join-Path $src '업데이트.cs')), 'AppVersion = "([^"]+)"').Groups[1].Value
if (-not $ver) { throw '판 번호를 못 찾음' }
$tag = "v$ver"
"판: $tag"

# 1) 저장소 준비
if (-not (Test-Path (Join-Path $repo '.git'))) {
    & git clone "https://github.com/$owner/$name.git" $repo
    if ($LASTEXITCODE -ne 0) { throw '저장소를 받지 못함' }
}
Push-Location $repo
try {
    & git config user.name $owner
    & git config user.email '78552753+Jajucity@users.noreply.github.com'   # 진짜 메일 대신 GitHub 가짜 주소
    $null = & git pull --quiet 2>&1   # 처음(빈 저장소)엔 실패해도 괜찮음

    # 2) 공개할 파일만 복사 (목록 방식)
    foreach ($d in '소스', 'docs') { if (Test-Path $d) { Remove-Item -Recurse -Force $d } }
    New-Item -ItemType Directory -Force '소스', 'docs' | Out-Null
    Get-ChildItem $src -File | Where-Object { $_.Extension -in '.cs', '.ps1' } | Copy-Item -Destination '소스'
    Copy-Item -Recurse (Join-Path $base 'docs\*') 'docs'
    @"
# 만화 보관함

좋아하는 만화를 모아 두고 웹툰처럼 내려 보는 윈도우 프로그램이에요.

- **받기**: https://$($owner.ToLower()).github.io/$name/
- 설치 없이 ``만화 보관함.exe`` 를 두 번 누르면 열려요. (윈도우 10·11)
- 만화는 내 컴퓨터의 '자료' 폴더에만 저장돼요. 새 판 확인 말고는 인터넷을 쓰지 않아요.

소스: ``소스`` 폴더 (C# WinForms, ``만들기.ps1`` 로 빌드)
"@ | Out-File -Encoding utf8 'README.md'

    # 3) 올리기 전 개인정보 글자 검사
    $bad = Get-ChildItem -Recurse -File -Exclude '*.png' | Select-String -Pattern 'minsu', 'gmail', 'C:\\Users', 'OneDrive', 'AppData' -SimpleMatch -ErrorAction SilentlyContinue |
        Where-Object { $_.Path -notmatch '\\\.git\\' -and $_.Path -notmatch '올리기\.ps1$' }
    if ($bad) { $bad | ForEach-Object { "개인정보 의심: " + $_.Path + " : " + $_.Line }; throw '개인정보 의심 글자가 있어서 멈춤' }

    & git add -A
    & git diff --cached --quiet
    if ($LASTEXITCODE -ne 0) {
        & git commit -q -m "만화 보관함 $tag" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
        & git push -q origin HEAD
        if ($LASTEXITCODE -ne 0) { throw '올리기(push) 실패' }
        "저장소 올림"
    } else { "저장소는 바뀐 것 없음" }
}
finally { Pop-Location }

# 4) 프로그램 빌드 → zip
$stage = Join-Path $env:TEMP ("만화보관함_판_" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force (Join-Path $stage '만화 보관함') | Out-Null
$env:COMIC_OUT = Join-Path $stage '만화 보관함\만화 보관함.exe'
& powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $src '만들기.ps1')
Remove-Item Env:COMIC_OUT
if (-not (Test-Path (Join-Path $stage '만화 보관함\만화 보관함.exe'))) { throw '빌드 실패' }
@"
만화 보관함 $ver
==========

1. 이 '만화 보관함' 폴더를 원하는 곳(예: 문서, 바탕화면)에 풀어 두세요.
2. '만화 보관함.exe' 를 두 번 누르면 바로 열려요. 설치는 필요 없어요.
3. 처음 열면 사용법이 나와요. 나중에는 첫 화면의 '사용법' 단추나 F1 키로 다시 볼 수 있어요.

- 처음 열 때 윈도우가 'Windows의 PC 보호' 창을 띄우면 '추가 정보' → '실행'을 누르면 돼요.
  (서명이 없는 개인이 만든 프로그램이라 뜨는 안내예요.)
- 만화는 프로그램 옆에 생기는 '자료' 폴더에 저장돼요. 프로그램을 옮길 때는 '자료' 폴더도 같이 옮기세요.
- 새 판이 나오면 프로그램이 알려 줘요. (첫 화면 '설정'에서 끌 수 있어요)
- 윈도우 10·11 에서 동작해요.
"@ | Out-File -Encoding utf8 (Join-Path $stage '만화 보관함\처음 읽어 주세요.txt')
$zip = Join-Path $env:TEMP 'ComicShelf.zip'
if (Test-Path $zip) { Remove-Item $zip }
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::CreateFromDirectory($stage, $zip, 'Optimal', $false)
# 내 컴퓨터의 배포용 zip 도 같은 것으로
$dist = Join-Path $base '배포'; New-Item -ItemType Directory -Force $dist | Out-Null
Copy-Item $zip (Join-Path $dist '만화 보관함 (프로그램만).zip') -Force

# 5) 새 판 내놓기
if (-not $Notes) { $Notes = "만화 보관함 $ver" }
& $gh release view $tag --repo "$owner/$name" *> $null
if ($LASTEXITCODE -eq 0) {
    & $gh release upload $tag $zip --repo "$owner/$name" --clobber
} else {
    & $gh release create $tag $zip --repo "$owner/$name" --title "만화 보관함 $ver" --notes $Notes --latest
}
if ($LASTEXITCODE -ne 0) { throw '새 판 내놓기 실패' }
"새 판 내놓음: https://github.com/$owner/$name/releases/tag/$tag"
