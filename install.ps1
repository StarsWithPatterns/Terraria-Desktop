<#
  Links the TerrariaDesktop mod folder from this repository into tModLoader's
  ModSources folder, so "Workshop > Develop Mods" can build it, and every
  `git pull` is picked up the next time you press "Build + Reload".

  Run from the repository folder:
      powershell -ExecutionPolicy Bypass -File .\install.ps1
#>
$ErrorActionPreference = 'Stop'

$documents  = [Environment]::GetFolderPath('MyDocuments')
$modSources = Join-Path $documents 'My Games\Terraria\tModLoader\ModSources'
$source     = Join-Path $PSScriptRoot 'TerrariaDesktop'
$link       = Join-Path $modSources 'TerrariaDesktop'

if (-not (Test-Path $modSources)) {
    Write-Host "Couldn't find $modSources"
    Write-Host "Start tModLoader once and open Workshop > Develop Mods, then run this script again."
    exit 1
}

if (Test-Path $link) {
    $item = Get-Item $link -Force
    if ($item.LinkType -eq 'Junction' -or $item.LinkType -eq 'SymbolicLink') {
        Write-Host "Already linked: $link -> $($item.Target)"
        exit 0
    }
    Write-Host "$link already exists and is a normal folder. Move or delete it, then run this again."
    exit 1
}

# A junction needs no admin rights.
New-Item -ItemType Junction -Path $link -Target $source | Out-Null
Write-Host "Linked $link -> $source"
Write-Host "Now open tModLoader > Workshop > Develop Mods and press 'Build + Reload' next to Terraria Desktop."
