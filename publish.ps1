# Publie une nouvelle version : reconstruit le mod, met à jour dist/, et reconstruit l'installeur.
# Usage : .\publish.ps1          (puis git commit + git push)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot

# 1. version lue dans le manifeste, source de vérité
$manifest = Get-Content "$root\manifest.json" -Raw | ConvertFrom-Json
$version = $manifest.Version
Write-Host "Pelican Memory $version" -ForegroundColor Cyan

# 2. compiler le mod (ModBuildConfig produit le zip de distribution)
# Rebuild et pas build : une compilation incrementale met bien la DLL a jour mais ne regenere pas le zip,
# et on repartait alors avec celui de la version precedente sans rien voir.
Write-Host "Compilation du mod..."
dotnet build "$root\PelicanMemory.csproj" -c Release -t:Rebuild -v quiet | Out-Null

# choisi par son nom et pas par sa date : seule facon de garantir que c'est bien cette version-la
$zip = Get-Item "$root\bin\Release\net6.0\PelicanMemory $version.zip" -ErrorAction SilentlyContinue
if (-not $zip) { throw "Zip de la version $version introuvable dans bin\Release\net6.0." }

# 3. les nouveautes : la fenetre de mise a jour du jeu les affiche, donc la version doit avoir les siennes
# (lu en UTF-8 explicitement : PowerShell 5 lirait sinon les accents en ANSI)
$utf8 = New-Object System.Text.UTF8Encoding $false
$changelog = [System.IO.File]::ReadAllText("$root\changelog.json", $utf8)
$latest = ($changelog | ConvertFrom-Json)[0].Version
if ($latest -ne $version) { throw "changelog.json commence par la $latest, pas par la $version : ajoute les nouveautes de cette version en tete." }

# 4. dist\ : ce que l'installeur et le mod telechargent
New-Item -ItemType Directory -Force "$root\dist" | Out-Null
Copy-Item $zip.FullName "$root\dist\PelicanMemory.zip" -Force
[System.IO.File]::WriteAllText("$root\dist\version.txt", $version, $utf8)  # sans BOM
[System.IO.File]::WriteAllText("$root\dist\update.json", $changelog, $utf8)
Write-Host "dist\PelicanMemory.zip, version.txt et update.json mis a jour." -ForegroundColor Green

# 5. l'installeur, en un seul .exe autonome
Write-Host "Compilation de l'installeur..."
dotnet publish "$root\installer\PelicanMemory.Installer.csproj" -c Release -o "$root\publish" -v quiet | Out-Null
Write-Host "publish\Installer Pelican Memory.exe pret a envoyer." -ForegroundColor Green

Write-Host ""
Write-Host "Il reste a faire : git add -A ; git commit ; git push" -ForegroundColor Yellow
