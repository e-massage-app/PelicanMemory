# Publie une nouvelle version : reconstruit le mod, met à jour dist/, et reconstruit l'installeur.
# Usage : .\publish.ps1          (puis git commit + git push)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot

# 1. version lue dans le manifeste, source de vérité
$manifest = Get-Content "$root\manifest.json" -Raw | ConvertFrom-Json
$version = $manifest.Version
Write-Host "Pelican Memory $version" -ForegroundColor Cyan

# 2. compiler le mod (ModBuildConfig produit le zip de distribution)
Write-Host "Compilation du mod..."
dotnet build "$root\PelicanMemory.csproj" -c Release -v quiet | Out-Null

$zip = Get-ChildItem "$root\bin\Release\net6.0\*.zip" | Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (-not $zip) { throw "Zip du mod introuvable dans bin\Release\net6.0." }

# 3. dist\ : ce que l'installeur télécharge
New-Item -ItemType Directory -Force "$root\dist" | Out-Null
Copy-Item $zip.FullName "$root\dist\PelicanMemory.zip" -Force
[System.IO.File]::WriteAllText("$root\dist\version.txt", $version, (New-Object System.Text.UTF8Encoding $false))  # sans BOM
Write-Host "dist\PelicanMemory.zip et dist\version.txt mis a jour." -ForegroundColor Green

# 4. l'installeur, en un seul .exe autonome
Write-Host "Compilation de l'installeur..."
dotnet publish "$root\installer\PelicanMemory.Installer.csproj" -c Release -o "$root\publish" -v quiet | Out-Null
Write-Host "publish\Installer Pelican Memory.exe pret a envoyer." -ForegroundColor Green

Write-Host ""
Write-Host "Il reste a faire : git add -A ; git commit ; git push" -ForegroundColor Yellow
