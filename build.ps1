$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) {
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
if (-not (Test-Path -LiteralPath $compiler)) {
    throw 'Não encontrei o compilador C# do .NET Framework neste Windows.'
}

$source = Join-Path $PSScriptRoot 'SlugcatDesktop.cs'
$manifest = Join-Path $PSScriptRoot 'app.manifest'
$output = Join-Path $PSScriptRoot 'SlugcatCompanion-v13.exe'
& $compiler /nologo /target:winexe /optimize+ /win32manifest:$manifest /out:$output `
    /reference:System.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll $source
if ($LASTEXITCODE -ne 0) {
    throw 'A compilação do Slugcat Companion falhou.'
}
Write-Host "Executável criado: $output"
