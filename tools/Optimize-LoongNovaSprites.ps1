# Pack design originals into small alpha-cropped pixel sprites. No interpolation or smoothing.
# Root images are consumed by the nonrecursive EmbeddedResource glob; source/ is excluded.
param([string]$Root = (Join-Path $PSScriptRoot '../src/DDEN.LoongNova/images'))
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$sourceDir = Join-Path $root 'source'
New-Item -ItemType Directory -Path $sourceDir -Force | Out-Null
$totalBefore=0L; $totalAfter=0L
foreach($file in Get-ChildItem -LiteralPath $root -Filter '*.png' -File) {
 $totalBefore += $file.Length
 $backup=Join-Path $sourceDir $file.Name
 if(!(Test-Path -LiteralPath $backup)) { Copy-Item -LiteralPath $file.FullName -Destination $backup }
 $bitmap=[Drawing.Bitmap]::new($backup)
 try {
  $minX=$bitmap.Width; $minY=$bitmap.Height; $maxX=-1; $maxY=-1
  for($y=0;$y -lt $bitmap.Height;$y++){ for($x=0;$x -lt $bitmap.Width;$x++){
   if($bitmap.GetPixel($x,$y).A -lt 32){continue}
   $minX=[Math]::Min($minX,$x); $minY=[Math]::Min($minY,$y); $maxX=[Math]::Max($maxX,$x); $maxY=[Math]::Max($maxY,$y)
  }}
  if($maxX -lt 0){throw "Empty sprite: $($file.Name)"}
  $width=$maxX-$minX+1; $height=$maxY-$minY+1
  $targetWidth=switch -Regex ($file.Name){ 'medical-injector' {36;break} 'medical-ampoule|regeneration-ampoule|empty-ampoule' {10;break} 'charge-sniper-cannon' {64;break} 'bio-erasure-round' {12;break} 'demolition-round|explosive-round|universal-ammo-carrier|drill-round|spatial-drain-round|emp-breach-round' {12;break} 'dragon-scorch' {64;break} 'dragon-wing-open|dragon-wing-back' {64;break} 'rock-dragon-pickaxe' {40;break} 'dragon-claw-wrench' {48;break} 'belt|dragon-wing' {32;break} default {16} }
  $targetHeight=[Math]::Max(1,[int][Math]::Round($height*$targetWidth/$width))
  if($file.Name -match '^(medical-ampoule|regeneration-ampoule|empty-ampoule)\.png$'){$targetWidth=10;$targetHeight=20}
  if($targetHeight -gt 64){$targetWidth=[Math]::Max(1,[int][Math]::Round($targetWidth*64/$targetHeight));$targetHeight=64}
  $small=[Drawing.Bitmap]::new($targetWidth,$targetHeight,[Drawing.Imaging.PixelFormat]::Format32bppArgb)
  try{
   for($y=0;$y -lt $targetHeight;$y++){for($x=0;$x -lt $targetWidth;$x++){
    $sx=$minX+[Math]::Min($width-1,[int][Math]::Floor(($x+0.5)*$width/$targetWidth))
    $sy=$minY+[Math]::Min($height-1,[int][Math]::Floor(($y+0.5)*$height/$targetHeight))
    $small.SetPixel($x,$y,$bitmap.GetPixel($sx,$sy))
   }}
   $small.Save($file.FullName,[Drawing.Imaging.ImageFormat]::Png)
  }finally{$small.Dispose()}
 }finally{$bitmap.Dispose()}
 $totalAfter+=(Get-Item -LiteralPath $file.FullName).Length
 Write-Output ('{0}: {1}x{2}, {3} bytes' -f $file.Name,$targetWidth,$targetHeight,(Get-Item -LiteralPath $file.FullName).Length)
}
Write-Output ('Root PNG total: {0} -> {1} bytes' -f $totalBefore,$totalAfter)
