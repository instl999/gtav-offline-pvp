# Verifies that a built OfflinePvP.dll can bind to a given ScriptHookVDotNet3.dll at runtime:
# every type and member our assembly references must exist there with the same signature.
# Uses reflection-only loading, so no code from either DLL runs.
# Usage (Windows PowerShell 5.1, .NET Framework):
#   .\tools\check-compat.ps1 -ModDll bin\OfflinePvP.dll -ShvdnDll C:\path\to\ScriptHookVDotNet3.dll
param(
    [Parameter(Mandatory = $true)][string]$ModDll,
    [Parameter(Mandatory = $true)][string]$ShvdnDll
)
$ErrorActionPreference = 'Stop'
$fwDir = [Runtime.InteropServices.RuntimeEnvironment]::GetRuntimeDirectory()
$shv = [Reflection.Assembly]::ReflectionOnlyLoadFrom((Resolve-Path $ShvdnDll).Path)

[AppDomain]::CurrentDomain.add_ReflectionOnlyAssemblyResolve([ResolveEventHandler] {
    param($sender, $e)
    $name = New-Object Reflection.AssemblyName($e.Name)
    if ($name.Name -eq 'ScriptHookVDotNet3') { return $shv }  # whatever version the mod was built against
    try { return [Reflection.Assembly]::ReflectionOnlyLoad($e.Name) } catch { }
    $path = Join-Path $fwDir ($name.Name + '.dll')
    if (Test-Path $path) { return [Reflection.Assembly]::ReflectionOnlyLoadFrom($path) }
    return $null
})

$mod = [Reflection.Assembly]::ReflectionOnlyLoadFrom((Resolve-Path $ModDll).Path)
$module = $mod.ManifestModule
$checked = 0
$missing = New-Object System.Collections.Generic.List[string]

foreach ($table in @(0x01, 0x0A)) {  # TypeRef, MemberRef
    for ($row = 1; $row -lt 20000; $row++) {
        $token = ($table -shl 24) -bor $row
        try {
            if ($table -eq 0x01) { [void]$module.ResolveType($token) } else { [void]$module.ResolveMember($token) }
            $checked++
        }
        catch {
            $ex = $_.Exception
            while ($ex.InnerException) { $ex = $ex.InnerException }
            if ($ex -is [ArgumentException]) { break }  # past the end of the table
            $missing.Add(("0x{0:X8}: {1}" -f $token, $ex.Message))
        }
    }
}

Write-Host ("Checked {0} references against {1} ({2})" -f $checked, $shv.GetName().Name, $shv.GetName().Version)
if ($missing.Count -gt 0) {
    $missing | ForEach-Object { Write-Host "MISSING $_" }
    throw "$($missing.Count) reference(s) would fail to bind"
}
Write-Host "OK: every reference binds."
