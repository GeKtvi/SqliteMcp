#Requires -Version 5.1
<#
.SYNOPSIS
  Adds this folder (where SqliteMcp.exe lives) to your user PATH.

.DESCRIPTION
  Run from the extracted release folder. Does not move or copy the binary.
  Open a new terminal afterward (or restart the app) so PATH updates apply.

.PARAMETER Remove
  Removes this folder from the user PATH instead.
#>
param(
    [switch] $Remove
)

$ErrorActionPreference = 'Stop'

$dir = $PSScriptRoot
if (-not $dir) {
    throw 'Could not determine script directory.'
}

$exe = Join-Path $dir 'SqliteMcp.exe'
if (-not (Test-Path -LiteralPath $exe)) {
    Write-Warning "SqliteMcp.exe not found next to this script ($dir). Continuing anyway."
}

function Get-UserPathParts {
    $raw = [Environment]::GetEnvironmentVariable('Path', 'User')
    $list = [System.Collections.Generic.List[string]]::new()
    if ([string]::IsNullOrWhiteSpace($raw)) {
        # Unary comma: stop PowerShell from unwrapping the List into a fixed array.
        return ,$list
    }

    foreach ($part in ($raw -split ';')) {
        if (-not [string]::IsNullOrWhiteSpace($part)) {
            $list.Add($part.TrimEnd('\'))
        }
    }
    return ,$list
}

function Set-UserPathParts([System.Collections.Generic.IEnumerable[string]] $parts) {
    $value = [string]::Join(';', @($parts))
    [Environment]::SetEnvironmentVariable('Path', $value, 'User')

    # Refresh PATH for the current PowerShell session
    $machine = [Environment]::GetEnvironmentVariable('Path', 'Machine')
    $env:Path = if ($machine) { "$machine;$value" } else { $value }
}

$parts = Get-UserPathParts
$dirNormalized = $dir.TrimEnd('\')
$alreadyPresent = $false
foreach ($part in $parts) {
    if ($part.Equals($dirNormalized, [StringComparison]::OrdinalIgnoreCase)) {
        $alreadyPresent = $true
        break
    }
}

if ($Remove) {
    if (-not $alreadyPresent) {
        Write-Host "Not on user PATH: $dirNormalized"
        exit 0
    }

    $kept = [System.Collections.Generic.List[string]]::new()
    foreach ($part in $parts) {
        if (-not $part.Equals($dirNormalized, [StringComparison]::OrdinalIgnoreCase)) {
            $kept.Add($part)
        }
    }
    Set-UserPathParts $kept
    Write-Host "Removed from user PATH: $dirNormalized"
    Write-Host 'Open a new terminal for the change to take effect everywhere.'
    exit 0
}

if ($alreadyPresent) {
    Write-Host "Already on user PATH: $dirNormalized"
    exit 0
}

$parts.Add($dirNormalized)
Set-UserPathParts $parts
Write-Host "Added to user PATH: $dirNormalized"
Write-Host 'Open a new terminal (or restart Cursor) so "SqliteMcp" resolves from PATH.'
