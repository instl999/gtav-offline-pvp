# Builds OfflinePvP.dll with the C# compiler that ships with .NET Framework 4.x (no Visual Studio needed).
# Usage:  .\build.ps1 -ShvdnDll "C:\path\to\ScriptHookVDotNet3.dll" [-GtaDir "D:\Games\Grand Theft Auto V"]
param(
    [Parameter(Mandatory = $true)][string]$ShvdnDll,
    [string]$GtaDir
)
$ErrorActionPreference = 'Stop'
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$fw  = Split-Path $csc
$out = Join-Path $PSScriptRoot 'bin'
New-Item -ItemType Directory -Force $out | Out-Null
$dll = Join-Path $out 'OfflinePvP.dll'

$sources = Get-ChildItem (Join-Path $PSScriptRoot 'src') -Filter *.cs | ForEach-Object { $_.FullName }
# 612/618: SHVDN Enhanced marks many v3 members obsolete; we keep using them so one DLL binds to SHVDN 3.6 and SHVDNE.
$output = & $csc /nologo /target:library /platform:x64 /optimize+ /nowarn:612,618 "/out:$dll" "/reference:$ShvdnDll" `
    "/reference:$fw\System.Windows.Forms.dll" "/reference:$fw\System.Drawing.dll" "/reference:$fw\System.Core.dll" `
    $sources
$output | ForEach-Object { Write-Host $_ }
if ($LASTEXITCODE -ne 0) { throw "Build failed" }
Copy-Item (Join-Path $PSScriptRoot 'dist\*.ini') $out -Force
Write-Host "Built $dll"

if ($GtaDir) {
    $scripts = Join-Path $GtaDir 'scripts'
    New-Item -ItemType Directory -Force $scripts | Out-Null
    Copy-Item $dll $scripts -Force
    foreach ($ini in Get-ChildItem $out -Filter *.ini) {
        $target = Join-Path $scripts $ini.Name
        if (-not (Test-Path $target)) { Copy-Item $ini.FullName $scripts }  # never overwrite user settings
    }
    Write-Host "Installed to $scripts"
}
