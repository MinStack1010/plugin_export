<#
.SYNOPSIS
    Check every *.ps1 of solid_plugin: UTF-8 BOM present (required by Windows PowerShell 5.1
    for non-ASCII text) and no parser errors (System.Management.Automation.Language.Parser).
    Read-only. This file is intentionally pure ASCII.
#>
[CmdletBinding()]
param([string]$Root)

$ErrorActionPreference = 'Stop'
$here = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $MyInvocation.MyCommand.Path }
if (-not $Root) { $Root = Split-Path $here -Parent }

$failed = 0
Get-ChildItem $Root -Recurse -Filter *.ps1 | Where-Object { $_.FullName -notmatch '\\(dist|bin|obj)\\' } | ForEach-Object {
    $bytes = [IO.File]::ReadAllBytes($_.FullName)
    $hasBom = $bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF
    $nonAscii = @($bytes | Where-Object { $_ -gt 127 }).Count
    $tokens = $null; $errors = $null
    [void][Management.Automation.Language.Parser]::ParseFile($_.FullName, [ref]$tokens, [ref]$errors)
    $problem = @()
    if ($nonAscii -gt 0 -and -not $hasBom) { $problem += 'non-ASCII text without UTF-8 BOM' }
    if ($errors.Count -gt 0) { $problem += ($errors | ForEach-Object { "line $($_.Extent.StartLineNumber): $($_.Message)" }) }
    $rel = $_.FullName.Substring($Root.Length).TrimStart('\')
    if ($problem.Count -eq 0) {
        Write-Host ("  [OK]   {0} (BOM={1}, parser errors=0)" -f $rel, $hasBom) -ForegroundColor Green
    } else {
        $failed++
        Write-Host ("  [FAIL] {0}: {1}" -f $rel, ($problem -join '; ')) -ForegroundColor Red
    }
}
if ($failed -gt 0) { throw "$failed script(s) failed the encoding/syntax check." }
