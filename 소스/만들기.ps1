# 만화 보관함 빌드: 아이콘을 만들고 윈도우에 들어 있는 C# 컴파일러로 exe 를 만든다.
$ErrorActionPreference = 'Stop'
$src = $PSScriptRoot
$out = Split-Path $src -Parent
Add-Type -AssemblyName System.Drawing

# ── 아이콘 (책 모양 + 말풍선) ──
function New-IconPng([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.Clear([System.Drawing.Color]::Transparent)
    $k = $size / 256.0
    function RR($x, $y, $w, $h, $r) {
        $p = New-Object System.Drawing.Drawing2D.GraphicsPath
        $d = $r * 2
        $p.AddArc($x, $y, $d, $d, 180, 90); $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
        $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90); $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
        $p.CloseFigure(); return $p
    }
    $bg = RR (8*$k) (8*$k) (240*$k) (240*$k) (44*$k)
    $g.FillPath((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 145, 70))), $bg)
    # 펼친 책
    $white = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 252, 246))
    $lp = RR (40*$k) (70*$k) (84*$k) (130*$k) (10*$k)
    $rp = RR (132*$k) (70*$k) (84*$k) (130*$k) (10*$k)
    $g.FillPath($white, $lp); $g.FillPath($white, $rp)
    $ink = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(60, 40, 30)), ([Math]::Max(1, 6*$k))
    # 만화 칸
    $g.DrawRectangle($ink, 52*$k, 84*$k, 60*$k, 44*$k)
    $g.DrawRectangle($ink, 52*$k, 140*$k, 60*$k, 46*$k)
    $g.DrawRectangle($ink, 144*$k, 84*$k, 60*$k, 102*$k)
    # 말풍선
    $g.FillEllipse((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(60, 40, 30))), 150*$k, 26*$k, 80*$k, 52*$k)
    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    return ,$ms.ToArray()
}

$sizes = 256, 48, 32, 16
$pngs = @(); foreach ($s in $sizes) { $pngs += ,(New-IconPng $s) }
$ico = Join-Path $src '아이콘.ico'
$fs = [System.IO.File]::Create($ico)
$bw = New-Object System.IO.BinaryWriter $fs
$bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $b = if ($s -ge 256) { 0 } else { $s }
    $bw.Write([byte]$b); $bw.Write([byte]$b); $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([UInt16]1); $bw.Write([UInt16]32)
    $bw.Write([UInt32]$pngs[$i].Length); $bw.Write([UInt32]$offset)
    $offset += $pngs[$i].Length
}
foreach ($p in $pngs) { $bw.Write($p) }
$bw.Close()

# ── 컴파일 ──
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$exe = if ($env:COMIC_OUT) { $env:COMIC_OUT } else { Join-Path $out '만화 보관함.exe' }
# 유튜브 재생 부품(마이크로소프트 WebView2, 소스\부품\WebView2): exe 안에 넣음
$wv = Join-Path $src '부품\WebView2'
foreach ($f in 'Microsoft.Web.WebView2.Core.dll', 'Microsoft.Web.WebView2.WinForms.dll', 'WebView2Loader.dll') {
    if (-not (Test-Path (Join-Path $wv $f))) { throw "부품이 없어요: $wv\$f (NuGet Microsoft.Web.WebView2 에서 꺼내 두기)" }
}
$defs = @()
$files = Get-ChildItem $src -Filter *.cs | % FullName
& $csc /nologo /target:winexe /optimize+ /codepage:65001 "/out:$exe" "/win32icon:$ico" $defs `
    /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:Microsoft.VisualBasic.dll /r:System.Core.dll /r:Microsoft.CSharp.dll /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll /r:System.Web.Extensions.dll /r:System.Security.dll `
    "/r:$wv\Microsoft.Web.WebView2.Core.dll" "/r:$wv\Microsoft.Web.WebView2.WinForms.dll" `
    "/resource:$wv\Microsoft.Web.WebView2.Core.dll,wv2.core" "/resource:$wv\Microsoft.Web.WebView2.WinForms.dll,wv2.winforms" "/resource:$wv\WebView2Loader.dll,wv2.loader" `
    $files
if ($LASTEXITCODE -ne 0) { throw "컴파일 실패" }
"만들기 끝: $exe"
