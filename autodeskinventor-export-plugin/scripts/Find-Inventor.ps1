# Find-Inventor.ps1 - detect Autodesk Inventor 2027 (RegistryVersion 31.x) without hardcoding.
# Dot-source this file, then call Get-SynodeInventorInfo.
# ASCII-only on purpose (Windows PowerShell 5.1 reads BOM-less files as ANSI).
# Read-only: never writes to the registry.

Set-StrictMode -Version 2.0

function Get-SynodeInventorInfo {
    [CmdletBinding()]
    param(
        [string]$InventorBinDir,
        [int]$ExpectedMajor = 31,
        [string]$ReleaseYear = '2027'
    )

    $info = [ordered]@{
        Found             = $false
        Source            = $null
        BinDir            = $null
        ExePath           = $null
        FileVersion       = $null
        Major             = $null
        ProductName       = $null
        InteropPath       = $null
        InteropVersion    = $null
        StdolePath        = $null
        UserAddinsDir     = (Join-Path $env:APPDATA "Autodesk\Inventor $ReleaseYear\Addins")
        AllUsersAddinsDir = (Join-Path $env:ProgramData "Autodesk\Inventor $ReleaseYear\Addins")
        IsRunning         = $false
        RunningCount      = 0
        Problems          = @()
    }

    $candidates = @()
    if ($InventorBinDir) { $candidates += [pscustomobject]@{ Dir = $InventorBinDir; Source = 'parameter' } }
    if ($env:SYNODE_INVENTOR_BIN) { $candidates += [pscustomobject]@{ Dir = $env:SYNODE_INVENTOR_BIN; Source = 'SYNODE_INVENTOR_BIN' } }

    $regKey = "HKLM:\SOFTWARE\Autodesk\Inventor\RegistryVersion$ExpectedMajor.0"
    if (Test-Path -LiteralPath $regKey) {
        $p = Get-ItemProperty -LiteralPath $regKey -ErrorAction SilentlyContinue
        if ($p -and ($p.PSObject.Properties.Name -contains 'InventorLocation') -and $p.InventorLocation) {
            $candidates += [pscustomobject]@{ Dir = [string]$p.InventorLocation; Source = "registry $regKey" }
        }
        if ($p -and ($p.PSObject.Properties.Name -contains 'ProductName')) { $info.ProductName = [string]$p.ProductName }
    }

    $cvKey = 'HKLM:\SOFTWARE\Autodesk\Inventor\Current Version'
    if (Test-Path -LiteralPath $cvKey) {
        $cv = Get-ItemProperty -LiteralPath $cvKey -ErrorAction SilentlyContinue
        if ($cv -and ($cv.PSObject.Properties.Name -contains 'Executable') -and $cv.Executable) {
            $candidates += [pscustomobject]@{ Dir = (Split-Path -Parent ([string]$cv.Executable)); Source = "registry $cvKey" }
        }
    }

    if ($env:ProgramFiles) {
        $candidates += [pscustomobject]@{ Dir = (Join-Path $env:ProgramFiles "Autodesk\Inventor $ReleaseYear\Bin"); Source = 'default path' }
    }

    foreach ($c in $candidates) {
        $dir = $c.Dir.Trim().TrimEnd('\')
        $exe = Join-Path $dir 'Inventor.exe'
        if (-not (Test-Path -LiteralPath $exe)) { continue }
        $ver = (Get-Item -LiteralPath $exe).VersionInfo
        if ($ver.FileMajorPart -ne $ExpectedMajor) {
            $info.Problems += "Inventor.exe at '$exe' has version $($ver.FileVersion), expected major $ExpectedMajor (Inventor $ReleaseYear)."
            continue
        }
        $info.Found = $true
        $info.Source = $c.Source
        $info.BinDir = $dir
        $info.ExePath = $exe
        $info.FileVersion = $ver.FileVersion
        $info.Major = $ver.FileMajorPart
        break
    }

    if (-not $info.Found) {
        $info.Problems += "Autodesk Inventor $ReleaseYear (version $ExpectedMajor.x) was not found."
    }
    else {
        $interop = Join-Path $info.BinDir 'Public Assemblies\Autodesk.Inventor.Interop.dll'
        if (-not (Test-Path -LiteralPath $interop)) { $interop = Join-Path $info.BinDir 'Autodesk.Inventor.Interop.dll' }
        if (Test-Path -LiteralPath $interop) {
            $info.InteropPath = $interop
            $info.InteropVersion = (Get-Item -LiteralPath $interop).VersionInfo.FileVersion
        }
        else {
            $info.Problems += "Inventor API assembly Autodesk.Inventor.Interop.dll was not found under '$($info.BinDir)'."
        }
        $stdole = Join-Path $info.BinDir 'stdole.dll'
        if (Test-Path -LiteralPath $stdole) { $info.StdolePath = $stdole }
        else { $info.Problems += "stdole.dll was not found under '$($info.BinDir)'." }
    }

    $procs = @(Get-Process -Name 'Inventor' -ErrorAction SilentlyContinue)
    $info.RunningCount = $procs.Count
    $info.IsRunning = ($procs.Count -gt 0)

    return [pscustomobject]$info
}
