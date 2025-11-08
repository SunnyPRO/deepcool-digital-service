# Installs the Deepcool Digital Windows Service from a published folder.
# Usage: run from the folder containing the built binaries or pass -SourceDir.
param(
  [string]$SourceDir = '.',
  [string]$ServiceName = 'DeepCool',
  [string]$DisplayName = 'Deepcool Digital Service',
  [string]$InstallDir = 'C:\Program Files\DeepcoolService'
)

$ErrorActionPreference = 'Stop'

Write-Host "Installing $DisplayName..." -ForegroundColor Cyan
$exe = Join-Path $SourceDir 'Deepcool Digital.exe'
if(!(Test-Path $exe)){ throw "Executable not found: $exe" }

# Create target directory
if(!(Test-Path $InstallDir)){ New-Item -ItemType Directory -Path $InstallDir | Out-Null }

Copy-Item "$SourceDir\*" $InstallDir -Force

# Copy sample config to real config if missing
$cfgSample = Join-Path $InstallDir 'DeepcoolDisplay.sample.cfg'
$cfgReal   = Join-Path $InstallDir 'DeepcoolDisplay.cfg'
if((Test-Path $cfgSample) -and !(Test-Path $cfgReal)){
  Copy-Item $cfgSample $cfgReal
}

# Remove existing service if present
$existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if($existing){
  Write-Host "Service $ServiceName exists; removing old instance" -ForegroundColor Yellow
  Stop-Service $ServiceName -ErrorAction SilentlyContinue
  sc.exe delete $ServiceName | Out-Null
  Start-Sleep -Seconds 2
}

Write-Host "Creating service $ServiceName..." -ForegroundColor Cyan
$installedExe = Join-Path $InstallDir 'Deepcool Digital.exe'
$createResult = sc.exe create $ServiceName binPath= "`"$installedExe`"" start= auto DisplayName= "`"$DisplayName`""
if($LASTEXITCODE -ne 0){
  Write-Host "Service creation output: $createResult" -ForegroundColor Red
  throw "Failed to create service. Exit code: $LASTEXITCODE"
}

Write-Host "Starting service..." -ForegroundColor Cyan
Start-Service $ServiceName
Write-Host "Service started successfully." -ForegroundColor Green
