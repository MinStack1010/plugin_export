<#
.SYNOPSIS
    SHARED definition of the Synode Exporter registry keys (dot-sourced by install.ps1 / uninstall.ps1).
.DESCRIPTION
    The layout matches how the Solid Edge add-ins installed with the product are registered (read from
    the registry of this machine) and what RegAsm /codebase writes for a .NET COM class:
      HKxx\SOFTWARE\Classes\CLSID\{CLSID}
        (Default)          = SynodeExporter.AddIn.SynodeAddIn
        409                = Synode Exporter              <- title shown in the Add-In Manager
        AutoConnect (DWORD)= 1                            <- loaded when Solid Edge starts
        \Summary           409 = description
        \Implemented Categories\{26B1D2D1-...}            <- CATID "Solid Edge Add-In"
        \Implemented Categories\{62C8FE65-...}            <- CATID ".NET Category" (RegAsm)
        \Environment Categories\{env}                     <- Part/Sheet Metal/Assembly (ordered + sync)
        \InprocServer32 (+ \1.0.0.0)                      <- mscoree.dll + Class/Assembly/CodeBase
        \ProgId            = SynodeExporter.SolidEdge.AddIn
      HKxx\SOFTWARE\Classes\SynodeExporter.SolidEdge.AddIn\CLSID = {CLSID}
    Only the Synode Exporter CLSID/ProgId are used; keys of other add-ins are never touched.
#>

$script:SynodeClsid = '{42B484DA-1593-41C3-9623-1BFAA4DA645C}'
$script:SynodeProgId = 'SynodeExporter.SolidEdge.AddIn'
$script:SynodeClassName = 'SynodeExporter.AddIn.SynodeAddIn'
$script:SynodeTitle = 'Synode Exporter'
$script:SynodeSummary = 'Synode Exporter - exports the active Part/Assembly to GLB (glTF 2.0).'
$script:SynodeCategories = @(
    '{26B1D2D1-2B03-11d2-B589-080036E8B802}',   # Solid Edge Add-In
    '{62C8FE65-4EBB-45E7-B440-6E39B2CDBF29}'    # .NET Category (RegAsm)
)
$script:SynodeEnvironments = [ordered]@{
    'Part'               = '{26618396-09D6-11d1-BA07-080036230602}'
    'Synchronous Part'   = '{D9B0BB85-3A6C-4086-A0BB-88A1AAD57A58}'
    'Sheet Metal'        = '{26618398-09D6-11d1-BA07-080036230602}'
    'Synchronous Sheet Metal' = '{9CBF2809-FF80-4dbc-98F2-B82DABF3530F}'
    'Assembly'           = '{26618395-09D6-11d1-BA07-080036230602}'
    'Synchronous Assembly' = '{2C3C2A72-3A4A-471D-98B5-E3A8CFA4A2BF}'
}

function Get-SynodeHive([ValidateSet('Machine', 'CurrentUser')][string]$Scope) {
    $hive = if ($Scope -eq 'Machine') { [Microsoft.Win32.RegistryHive]::LocalMachine } else { [Microsoft.Win32.RegistryHive]::CurrentUser }
    # Always use the 64-bit registry view (Edge.exe is an x64 process).
    return [Microsoft.Win32.RegistryKey]::OpenBaseKey($hive, [Microsoft.Win32.RegistryView]::Registry64)
}

function Get-SynodeHiveName([string]$Scope) {
    if ($Scope -eq 'Machine') { 'HKLM' } else { 'HKCU' }
}

function Get-SynodeRootKeys {
    @("SOFTWARE\Classes\CLSID\$script:SynodeClsid", "SOFTWARE\Classes\$script:SynodeProgId")
}

<#
  Returns the list of registry entries to write: @{ Key; Name; Value; Kind }  (Name '' = the (Default) value).
#>
function Get-SynodeRegistryEntries([Parameter(Mandatory)][string]$DllPath) {
    $dllFull = [IO.Path]::GetFullPath($DllPath)
    $an = [Reflection.AssemblyName]::GetAssemblyName($dllFull)
    $asmFullName = $an.FullName
    $version = $an.Version.ToString()
    $codeBase = ([Uri]$dllFull).AbsoluteUri
    $clsidKey = "SOFTWARE\Classes\CLSID\$script:SynodeClsid"

    $e = New-Object System.Collections.Generic.List[object]
    function Add-E($k, $n, $v, $kind = 'String') { $e.Add([pscustomobject]@{ Key = $k; Name = $n; Value = $v; Kind = $kind }) }

    Add-E $clsidKey '' $script:SynodeClassName
    Add-E $clsidKey '409' $script:SynodeTitle
    Add-E $clsidKey 'AutoConnect' 1 'DWord'
    Add-E "$clsidKey\Summary" '409' $script:SynodeSummary
    foreach ($cat in $script:SynodeCategories) { Add-E "$clsidKey\Implemented Categories\$cat" $null $null 'KeyOnly' }
    foreach ($env in $script:SynodeEnvironments.Values) { Add-E "$clsidKey\Environment Categories\$env" $null $null 'KeyOnly' }
    foreach ($inproc in @("$clsidKey\InprocServer32", "$clsidKey\InprocServer32\$version")) {
        if ($inproc -eq "$clsidKey\InprocServer32") {
            Add-E $inproc '' 'mscoree.dll'
            Add-E $inproc 'ThreadingModel' 'Both'
        }
        Add-E $inproc 'Class' $script:SynodeClassName
        Add-E $inproc 'Assembly' $asmFullName
        Add-E $inproc 'RuntimeVersion' 'v4.0.30319'
        Add-E $inproc 'CodeBase' $codeBase
    }
    Add-E "$clsidKey\ProgId" '' $script:SynodeProgId
    Add-E "SOFTWARE\Classes\$script:SynodeProgId" '' $script:SynodeClassName
    Add-E "SOFTWARE\Classes\$script:SynodeProgId\CLSID" '' $script:SynodeClsid
    return $e.ToArray()
}

function Write-SynodeEntriesPreview([object[]]$Entries, [string]$Scope) {
    $hn = Get-SynodeHiveName $Scope
    foreach ($x in $Entries) {
        if ($x.Kind -eq 'KeyOnly') { Write-Host "  [key] $hn\$($x.Key)" }
        else {
            $n = if ($x.Name) { $x.Name } else { '(Default)' }
            Write-Host "  $hn\$($x.Key)  $n = $($x.Value)  [$($x.Kind)]"
        }
    }
}

<# Reads the current CodeBase (if the CLSID already exists) to detect conflicts. #>
function Get-SynodeExistingCodeBase([string]$Scope) {
    $base = Get-SynodeHive $Scope
    try {
        $k = $base.OpenSubKey("SOFTWARE\Classes\CLSID\$script:SynodeClsid\InprocServer32")
        if ($k) { try { return [string]$k.GetValue('CodeBase') } finally { $k.Close() } }
        $k2 = $base.OpenSubKey("SOFTWARE\Classes\CLSID\$script:SynodeClsid")
        if ($k2) { $k2.Close(); return '' }
        return $null
    } finally { $base.Close() }
}

function Test-SynodeIsAdmin {
    $id = [Security.Principal.WindowsIdentity]::GetCurrent()
    return (New-Object Security.Principal.WindowsPrincipal($id)).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}
