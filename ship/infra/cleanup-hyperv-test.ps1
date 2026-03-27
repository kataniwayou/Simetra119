# ============================================================================
# Cleanup script for Hyper-V K3s test environment
# Run as Administrator in PowerShell after testing is complete
#
# This removes all VMs, virtual switches, NAT rules, and disk files
# created during the DEPLOY_K3S_MULTISITE proof of concept.
#
# Safe: Does NOT touch Docker Desktop, Hyper-V feature, or any other VMs.
# ============================================================================

Write-Host "=== Stopping VMs ===" -ForegroundColor Yellow
Stop-VM -Name "simetra-k3s-a" -Force -ErrorAction SilentlyContinue
Stop-VM -Name "simetra-k3s-b" -Force -ErrorAction SilentlyContinue
Stop-VM -Name "simetra-k3s-c" -Force -ErrorAction SilentlyContinue

Write-Host "=== Removing VMs ===" -ForegroundColor Yellow
Remove-VM -Name "simetra-k3s-a" -Force -ErrorAction SilentlyContinue
Remove-VM -Name "simetra-k3s-b" -Force -ErrorAction SilentlyContinue
Remove-VM -Name "simetra-k3s-c" -Force -ErrorAction SilentlyContinue

Write-Host "=== Deleting VM disk files ===" -ForegroundColor Yellow
Remove-Item -Path "C:\VMs\simetra-k3s-a" -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -Path "C:\VMs\simetra-k3s-b" -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -Path "C:\VMs\simetra-k3s-c" -Recurse -Force -ErrorAction SilentlyContinue

Write-Host "=== Deleting ship deployment folder ===" -ForegroundColor Yellow
Remove-Item -Path "C:\Simetra" -Recurse -Force -ErrorAction SilentlyContinue

Write-Host "=== Removing virtual switch and NAT ===" -ForegroundColor Yellow
Remove-VMSwitch -Name "SimetraSwitch" -Force -ErrorAction SilentlyContinue
Remove-NetNat -Name "SimetraNAT" -Confirm:$false -ErrorAction SilentlyContinue
Remove-NetIPAddress -IPAddress 172.20.0.1 -Confirm:$false -ErrorAction SilentlyContinue

Write-Host ""
Write-Host "=== Verification ===" -ForegroundColor Green

$vms = Get-VM | Where-Object { $_.Name -like "simetra*" }
if ($vms) { Write-Host "WARNING: VMs still exist: $($vms.Name)" -ForegroundColor Red }
else { Write-Host "  VMs: clean" -ForegroundColor Green }

$switches = Get-VMSwitch | Where-Object { $_.Name -like "Simetra*" }
if ($switches) { Write-Host "WARNING: Switches still exist: $($switches.Name)" -ForegroundColor Red }
else { Write-Host "  Switches: clean" -ForegroundColor Green }

$nats = Get-NetNat | Where-Object { $_.Name -like "Simetra*" }
if ($nats) { Write-Host "WARNING: NAT rules still exist: $($nats.Name)" -ForegroundColor Red }
else { Write-Host "  NAT rules: clean" -ForegroundColor Green }

$diskA = Test-Path "C:\VMs\simetra-k3s-a"
$diskB = Test-Path "C:\VMs\simetra-k3s-b"
$diskC = Test-Path "C:\VMs\simetra-k3s-c"
if ($diskA -or $diskB -or $diskC) { Write-Host "WARNING: VM disk folders still exist" -ForegroundColor Red }
else { Write-Host "  Disk files: clean" -ForegroundColor Green }

$shipDir = Test-Path "C:\Simetra"
if ($shipDir) { Write-Host "WARNING: C:\Simetra still exists" -ForegroundColor Red }
else { Write-Host "  Ship folder: clean" -ForegroundColor Green }

Write-Host ""
Write-Host "Cleanup complete. Docker Desktop and Hyper-V feature are untouched." -ForegroundColor Cyan
