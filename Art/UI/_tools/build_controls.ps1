$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

# Procedural UI primitives, rendered at 4x and downsampled for consistent edges.
# This script only writes beneath Art/UI and requires the approved style sheet.
$uiRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (-not (Test-Path -LiteralPath (Join-Path $uiRoot 'style/style_sheet.png'))) {
    throw 'Lock style/style_sheet.png before rendering the controls.'
}
$outDir = Join-Path $uiRoot 'buttons'
$sheetDir = Join-Path $uiRoot '_sheets'
[IO.Directory]::CreateDirectory($outDir) | Out-Null
[IO.Directory]::CreateDirectory($sheetDir) | Out-Null
$script:entries = [Collections.Generic.List[object]]::new()
$script:scale = 4
$script:C = @{
    paper='#F4EFE3'; manila='#E8C98A'; green='#2F6B4F'; red='#D2382F'
    navy='#1F2A44'; yellow='#FFE066'; teal='#3BB9B0'
}

function Color([string]$hex, [int]$alpha = 255) {
    $rgb = [Drawing.ColorTranslator]::FromHtml($hex)
    return [Drawing.Color]::FromArgb($alpha,$rgb.R,$rgb.G,$rgb.B)
}
function Brush([string]$hex, [int]$alpha = 255) {
    return [Drawing.SolidBrush]::new((Color $hex $alpha))
}
function Pen([string]$hex, [single]$width = 3, [int]$alpha = 255) {
    $p = [Drawing.Pen]::new((Color $hex $alpha),$width)
    $p.LineJoin = [Drawing.Drawing2D.LineJoin]::Round
    $p.StartCap = [Drawing.Drawing2D.LineCap]::Round
    $p.EndCap = [Drawing.Drawing2D.LineCap]::Round
    return $p
}
function RoundPath([single]$x,[single]$y,[single]$w,[single]$h,[single]$r) {
    $p = [Drawing.Drawing2D.GraphicsPath]::new()
    $d = [single]($r*2)
    $p.AddArc($x,$y,$d,$d,180,90)
    $p.AddArc(($x+$w-$d),$y,$d,$d,270,90)
    $p.AddArc(($x+$w-$d),($y+$h-$d),$d,$d,0,90)
    $p.AddArc($x,($y+$h-$d),$d,$d,90,90)
    $p.CloseFigure()
    return $p
}
function Round([single]$x,[single]$y,[single]$w,[single]$h,[single]$r,[string]$fill,[string]$stroke=$script:C.navy,[single]$line=3,[int]$alpha=255) {
    $p = RoundPath $x $y $w $h $r
    if ($fill) { $b=Brush $fill $alpha; $script:g.FillPath($b,$p); $b.Dispose() }
    if ($stroke -and $line -gt 0) { $pen=Pen $stroke $line; $script:g.DrawPath($pen,$p); $pen.Dispose() }
    $p.Dispose()
}
function Line([single]$x1,[single]$y1,[single]$x2,[single]$y2,[string]$hex,[single]$width=2,[int]$alpha=255) {
    $p=Pen $hex $width $alpha
    $script:g.DrawLine($p,$x1,$y1,$x2,$y2)
    $p.Dispose()
}
function Circle([single]$x,[single]$y,[single]$d,[string]$fill,[string]$stroke=$script:C.navy,[single]$line=3,[int]$alpha=255) {
    if ($fill) { $b=Brush $fill $alpha; $script:g.FillEllipse($b,$x,$y,$d,$d); $b.Dispose() }
    if ($stroke -and $line -gt 0) { $p=Pen $stroke $line; $script:g.DrawEllipse($p,$x,$y,$d,$d); $p.Dispose() }
}
function Grain([single]$x,[single]$y,[single]$w,[single]$h,[int]$seed=41) {
    $rnd=[Random]::new($seed)
    $b=Brush $script:C.navy 12
    for ($i=0;$i -lt [int]($w*$h/170);$i++) {
        $px=[single]($x+$rnd.NextDouble()*$w)
        $py=[single]($y+$rnd.NextDouble()*$h)
        $script:g.FillEllipse($b,$px,$py,[single]0.7,[single]0.7)
    }
    $b.Dispose()
}
function Canvas([int]$w,[int]$h) {
    $script:work = [Drawing.Bitmap]::new(($w*$script:scale),($h*$script:scale),[Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $script:g = [Drawing.Graphics]::FromImage($script:work)
    $script:g.Clear([Drawing.Color]::Transparent)
    $script:g.SmoothingMode=[Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $script:g.PixelOffsetMode=[Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $script:g.ScaleTransform($script:scale,$script:scale)
}
function SaveAsset([string]$name,[int]$w,[int]$h,$slices,[string]$description) {
    $script:g.Dispose()
    $small=[Drawing.Bitmap]::new($w,$h,[Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $sg=[Drawing.Graphics]::FromImage($small)
    $sg.CompositingMode=[Drawing.Drawing2D.CompositingMode]::SourceCopy
    $sg.InterpolationMode=[Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $sg.PixelOffsetMode=[Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $sg.DrawImage($script:work,[Drawing.Rectangle]::new(0,0,$w,$h),0,0,$script:work.Width,$script:work.Height,[Drawing.GraphicsUnit]::Pixel)
    $sg.Dispose(); $script:work.Dispose()
    foreach ($p in @(@(0,0),@(($w-1),0),@(0,($h-1)),@(($w-1),($h-1)))) {
        if ($small.GetPixel($p[0],$p[1]).A -ne 0) { throw "Opaque corner in $name" }
    }
    $path=Join-Path $outDir ($name+'.png')
    $small.Save($path,[Drawing.Imaging.ImageFormat]::Png)
    $small.Dispose()
    $script:entries.Add([ordered]@{id=$name;path=('Art/UI/buttons/'+$name+'.png');w=$w;h=$h;category='buttons';nineSlice=$slices;screenRect=$null;hasText=$false;description=$description})
}

$colors=@{primary=$C.green;secondary=$C.manila;danger=$C.red;ghost=$null}
$hover=@{primary='#397E5D';secondary='#F2D9A6';danger='#E34C42';ghost=$null}
$pressed=@{primary='#285C44';secondary='#D8B575';danger='#B52D29';ghost=$null}
foreach ($kind in @('primary','secondary','danger','ghost')) {
    foreach ($state in @('normal','hover','pressed','disabled')) {
        Canvas 192 96
        $fill=$colors[$kind]; $y=8; $height=74; $edge=$C.navy
        if ($state -eq 'hover') { $fill=$hover[$kind]; $y=6 }
        if ($state -eq 'pressed') { $fill=$pressed[$kind]; $y=12 }
        if ($state -eq 'disabled') { $fill='#D4D3C7'; $edge='#929A99'; $y=10 }
        if ($kind -eq 'ghost') {
            $fill=$null
            if ($state -eq 'hover') { $edge=$C.green }
            if ($state -eq 'pressed') { $edge='#285C44' }
            Round 8 $y 176 $height 20 $null $edge 3.5
            if ($state -eq 'hover') { Round 12 ($y+4) 168 ($height-8) 16 $null $C.teal 1.5 }
        } else {
            if ($state -ne 'disabled') { Round 8 15 176 73 20 $C.navy $null 0 }
            Round 8 $y 176 $height 20 $fill $edge 3.5
            if ($state -ne 'disabled') {
                Line 32 ($y+7) 160 ($y+7) $C.paper 2 85
                Line 31 ($y+$height-6) 161 ($y+$height-6) $C.navy 2 26
            }
        }
        SaveAsset ($kind+'_'+$state) 192 96 @(32,32,32,32) "$kind $state button skin; 2x; overlay localized label at runtime."
    }
}

Canvas 512 512
Round 17 22 480 478 18 $C.navy $null 0 35
Round 12 12 480 478 18 $C.paper $C.navy 4
Round 21 21 462 460 12 $null '#D5CDBA' 1.5
Grain 27 27 449 445
SaveAsset 'panel_paper' 512 512 @(32,32,32,32) 'Warm stationery paper panel; quiet center for UI content.'

Canvas 640 480
$p=[Drawing.Drawing2D.GraphicsPath]::new()
$p.StartFigure(); $p.AddLine(18,83,18,45); $p.AddBezier(18,45,18,31,26,23,40,23)
$p.AddLine(40,23,199,23); $p.AddBezier(199,23,213,23,218,29,226,39)
$p.AddLine(226,39,248,65); $p.AddLine(248,65,600,65)
$p.AddBezier(600,65,615,65,623,73,623,88); $p.AddLine(623,88,623,440)
$p.AddBezier(623,440,623,455,616,463,601,463); $p.AddLine(601,463,39,463)
$p.AddBezier(39,463,25,463,18,455,18,441); $p.CloseFigure()
$b=Brush '#C5A66B';$g.FillPath($b,$p);$b.Dispose();$pen=Pen $C.navy 4;$g.DrawPath($pen,$p);$pen.Dispose();$p.Dispose()
Round 18 80 605 383 18 $C.manila $C.navy 4
Line 38 91 600 91 '#FFF0C5' 3 170
Line 40 448 600 448 '#B8985E' 2 100
Grain 34 104 572 330
SaveAsset 'panel_folder' 640 480 @(256,112,32,32) 'Manila folder panel with preserved left tab; minimum sliced width 288 px, height 144 px.'

Canvas 384 384
$p=[Drawing.Drawing2D.GraphicsPath]::new()
$p.AddPolygon([Drawing.PointF[]]@([Drawing.PointF]::new(18,14),[Drawing.PointF]::new(368,14),[Drawing.PointF]::new(368,307),[Drawing.PointF]::new(309,369),[Drawing.PointF]::new(18,369)))
$b=Brush $C.yellow;$g.FillPath($b,$p);$b.Dispose();$pen=Pen $C.navy 3.5;$g.DrawPath($pen,$p);$pen.Dispose();$p.Dispose()
$b=Brush '#E8C954';$g.FillRectangle($b,20,17,346,36);$b.Dispose()
Line 25 20 358 20 '#FFF5B7' 2 160
$p=[Drawing.Drawing2D.GraphicsPath]::new();$p.AddPolygon([Drawing.PointF[]]@([Drawing.PointF]::new(309,369),[Drawing.PointF]::new(309,311),[Drawing.PointF]::new(368,307)))
$b=Brush '#FFF2AD';$g.FillPath($b,$p);$b.Dispose();$pen=Pen $C.navy 3.5;$g.DrawPath($pen,$p);$pen.Dispose();$p.Dispose()
Grain 29 65 316 230
SaveAsset 'panel_sticky' 384 384 @(32,64,80,88) 'Yellow sticky note with adhesive band and folded bottom-right corner.'

Canvas 320 160
Round 11 17 300 132 13 $C.navy $null 0 38
Round 8 8 300 132 13 $C.paper $C.navy 3
Round 16 16 284 116 8 $null '#D5CDBA' 1.5
SaveAsset 'tooltip' 320 160 @(24,24,24,36) 'Compact paper tooltip; inset content by at least 24 px.'

Canvas 320 32
Round 2 2 316 28 14 '#DED9CC' $C.navy 3
Line 18 8 302 8 '#BDBDAF' 2 110
SaveAsset 'progress_bar_bg' 320 32 @(16,0,16,0) 'Progress meter recessed paper trough; slice horizontally at fixed 32 px height.'
foreach ($color in @('green','red')) {
    Canvas 320 32
    Round 3 3 314 26 13 $C[$color] $C.navy 2
    Line 18 8 302 8 $C.paper 2 92
    SaveAsset ('progress_bar_fill_'+$color) 320 32 @(16,0,16,0) "$color progress meter fill; crop or mask horizontally for progress; fixed 32 px height."
}

foreach ($on in @($true,$false)) {
    Canvas 112 64
    $fill=if ($on) {$C.green} else {'#D9D6CA'}
    Round 4 7 104 50 25 $fill $C.navy 3
    $x=if ($on) {58} else {8}
    Circle $x 11 42 $C.paper $C.navy 3
    if ($on) { Line 70 32 78 39 $C.green 3; Line 78 39 91 24 $C.green 3 }
    else { Circle 24 27 10 $null '#969E98' 2 }
    $name=if($on){'toggle_on'}else{'toggle_off'}
    SaveAsset $name 112 64 $null 'Fixed-size toggle switch; state conveyed by knob position and mark.'
}

Canvas 320 24
Round 2 5 316 14 7 '#D2D0C2' $C.navy 2.5
Line 12 9 308 9 '#B2B6AA' 1.5 100
SaveAsset 'slider_track' 320 24 @(12,0,12,0) 'Recessed slider track; slice horizontally at fixed 24 px height.'
Canvas 64 64
Circle 8 10 48 $C.navy $null 0 38
Circle 7 6 48 $C.paper $C.navy 3
Line 26 22 26 38 $C.green 2.5
Line 35 22 35 38 $C.green 2.5
SaveAsset 'slider_knob' 64 64 $null 'Paper slider thumb with office-green grip.'

foreach ($active in @($true,$false)) {
    Canvas 192 72
    $fill=if($active){$C.manila}else{'#D8D4C8'}
    Round 7 7 178 60 14 $fill $C.navy 3
    if($active) { Round 17 57 158 5 2 $C.green $null 0 }
    else { Line 24 60 168 60 '#B1B7AA' 2 }
    Line 24 14 168 14 $C.paper 2 125
    $name=if($active){'tab_active'}else{'tab_inactive'}
    SaveAsset $name 192 72 @(24,24,24,24) 'Folder tab; overlay localized label in the center.'
}

$json=$entries | ConvertTo-Json -Depth 5
[IO.File]::WriteAllText((Join-Path $outDir '_metadata.json'),$json,[Text.UTF8Encoding]::new($false))

# Review contact sheet, all transparent artwork shown on neutral mid-grey.
$cellW=280;$cellH=174;$cols=5;$rows=[int][Math]::Ceiling($entries.Count/$cols)
$sheet=[Drawing.Bitmap]::new(($cols*$cellW),($rows*$cellH+62),[Drawing.Imaging.PixelFormat]::Format32bppArgb)
$sg=[Drawing.Graphics]::FromImage($sheet);$sg.Clear((Color '#787B80'))
$sg.SmoothingMode=[Drawing.Drawing2D.SmoothingMode]::AntiAlias
$sg.InterpolationMode=[Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$font=[Drawing.Font]::new('Consolas',11,[Drawing.FontStyle]::Regular,[Drawing.GraphicsUnit]::Pixel)
$head=[Drawing.Font]::new('Segoe UI',22,[Drawing.FontStyle]::Bold,[Drawing.GraphicsUnit]::Pixel)
$white=Brush '#F4EFE3';$sg.DrawString('BUTTONS / 29 PNG', $head,$white,20,16)
$i=0
foreach($entry in $entries) {
    $x=($i%$cols)*$cellW;$y=[int][Math]::Floor($i/$cols)*$cellH+62
    $img=[Drawing.Image]::FromFile((Join-Path $outDir ($entry.id+'.png')))
    $s=[Math]::Min(244.0/$img.Width,123.0/$img.Height)
    $dw=[int]($img.Width*$s);$dh=[int]($img.Height*$s)
    $sg.DrawImage($img,[Drawing.Rectangle]::new(($x+[int](($cellW-$dw)/2)),($y+5+[int]((123-$dh)/2)),$dw,$dh))
    $sg.DrawString($entry.id,$font,$white,($x+18),($y+137))
    $sg.DrawString(($entry.w.ToString()+' x '+$entry.h),$font,$white,($x+18),($y+153))
    $img.Dispose();$i++
}
$sg.Dispose();$font.Dispose();$head.Dispose();$white.Dispose()
$sheet.Save((Join-Path $sheetDir 'buttons.png'),[Drawing.Imaging.ImageFormat]::Png);$sheet.Dispose()
Write-Output ('Rendered '+$entries.Count+' button assets; all four corner alpha checks passed.')
