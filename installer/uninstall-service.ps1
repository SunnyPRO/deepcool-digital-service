# Uninstalls the Deepcool Digital Windows Service.
param(
  [string]$ServiceName = 'DeepCool'
)
$ErrorActionPreference = 'Stop'
$svc = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if(!$svc){ Write-Host "Service $ServiceName not found." -ForegroundColor Yellow; exit 0 }
Write-Host "Stopping service $ServiceName..." -ForegroundColor Cyan
Stop-Service $ServiceName -ErrorAction SilentlyContinue
Start-Sleep -Seconds 2
Write-Host "Deleting service..." -ForegroundColor Cyan
sc.exe delete $ServiceName | Out-Null
Write-Host "Removed service $ServiceName." -ForegroundColor Green
