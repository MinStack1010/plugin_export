<#
.SYNOPSIS
  Check every .ps1 in this project with the Windows PowerShell parser and check encoding.
  Fails if a script has parse errors, or contains non-ASCII characters without a UTF-8 BOM
  (Windows PowerShell 5.1 would read it as ANSI and break strings).
#>
[CmdletBinding()]
param([string]$Root = '')

$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
if (-not $Root) { $Root = Split-Path -Parent $here }

$bad = 0
$files = Get-ChildItem -LiteralPath $Root -Filter '*.ps1' -Recurse |
    Where-Object { $_.FullName -notmatch '\\(bin|obj|dist|test-output)\\' }
foreach ($f in $files) {
    $bytes = [System.IO.File]::ReadAllBytes($f.FullName)
    $hasBom = ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF)
    $nonAscii = @($bytes | Where-Object { $_ -gt 127 }).Count
    if ($hasBom) { $nonAscii = [Math]::Max(0, $nonAscii - 3) }

    $tokens = $null; $errors = $null
    [void][System.Management.Automation.Language.Parser]::ParseFile($f.FullName, [ref]$tokens, [ref]$errors)
    $rel = $f.FullName.Substring($Root.Length).TrimStart('\')
    if ($errors.Count -gt 0) {
        $bad++
        Write-Host "FAIL  $rel" -ForegroundColor Red
        $errors | ForEach-Object { Write-Host ("      line {0}: {1}" -f $_.Extent.StartLineNumber, $_.Message) -ForegroundColor Red }
    }
    elseif ($nonAscii -gt 0 -and -not $hasBom) {
        $bad++
        Write-Host "FAIL  $rel  (non-ASCII text without UTF-8 BOM)" -ForegroundColor Red
    }
    else {
        Write-Host ("OK    {0}  ({1})" -f $rel, $(if ($hasBom) { 'UTF-8 BOM' } else { 'ASCII' })) -ForegroundColor Green
    }
}
Write-Host ''
if ($bad -gt 0) { Write-Host "$bad script(s) with problems." -ForegroundColor Red; exit 1 }
Write-Host "All $($files.Count) scripts parse correctly." -ForegroundColor Green
exit 0
