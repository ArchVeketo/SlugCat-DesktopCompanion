$ErrorActionPreference = 'Stop'

$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) {
    $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
if (-not (Test-Path $csc)) {
    throw 'Nao encontrei o compilador csc.exe do .NET Framework.'
}

$root   = $PSScriptRoot
$src    = Join-Path $root 'SlugcatDesktop.cs'
$mani   = Join-Path $root 'app.manifest'
$out    = Join-Path $root 'SlugcatCompanion.exe'

Write-Host "Compilando..." -ForegroundColor Cyan
& $csc /nologo /target:winexe /optimize+ `
       /win32manifest:$mani /out:$out `
       /reference:System.dll `
       /reference:System.Drawing.dll `
       /reference:System.Windows.Forms.dll `
       $src

if ($LASTEXITCODE -ne 0) { throw 'Compilacao falhou.' }
Write-Host "OK: $out" -ForegroundColor Green
