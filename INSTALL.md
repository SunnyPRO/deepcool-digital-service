# Installation Guide

This guide covers multiple ways to install the Deepcool Digital Service on Windows (manual scripts, MSI) and outlines future Linux considerations.

---
## 1. Windows Manual Installation (No MSI)

Use the published binaries (Release build) and install the service directly.

Steps:
1. Build or obtain the published folder containing `Deepcool Digital.exe` and required DLLs.
2. Open PowerShell as Administrator in that folder.
3. Run:
   ```powershell
   ../installer/install-service.ps1
   ```
4. Edit `C:\Program Files\DeepcoolService\DeepcoolDisplay.cfg` to change display behavior.
5. To remove:
   ```powershell
   ../installer/uninstall-service.ps1
   ```

What the script does:
- Copies binaries to `C:\Program Files\DeepcoolService`.
- Creates the Windows Service `DeepCool` (auto start).
- Starts the service immediately.

---
## 2. Windows MSI (WiX Toolset)

A WiX project (`installer/InstallerWiX/`) builds a formal MSI including the service registration and sample config.

Prerequisites:
- Visual Studio (or Build Tools) with MSBuild.
- WiX Toolset v3.11 installed.

Build manually:
```powershell
msbuild DeepcoolService.sln /p:Configuration=Release
msbuild installer\InstallerWiX\InstallerWiX.wixproj /p:Configuration=Release
```
Result: `installer\InstallerWiX\bin\Release\DeepcoolServiceInstaller.msi`

Automated build (script):
```powershell
.\installer\build-msi.ps1 -Configuration Release -Version 1.0.0
```

Installation:
- Double-click the MSI or use `msiexec /i DeepcoolServiceInstaller.msi /qn` for silent install.
- Service appears as `DeepCool` in Services list.

Uninstall:
```powershell
msiexec /x DeepcoolServiceInstaller.msi
```

### Code Signing (CI or Local)

Signing increases trust (SmartScreen, corporate environments). You need a code signing certificate in PFX format.

1. Obtain certificate (from CA like DigiCert, Sectigo) and export as `codesign.pfx` with a strong password.
2. Base64 encode the PFX:
   ```powershell
   [Convert]::ToBase64String([IO.File]::ReadAllBytes('codesign.pfx')) | Out-File pfx.b64
   ```
3. Create GitHub repository secrets:
   - `WINDOWS_CERT_PFX_BASE64` = contents of `pfx.b64`
   - `WINDOWS_CERT_PASSWORD` = PFX password
4. Workflow signing snippet (already integrated conditionally if you add the step):
   ```powershell
   Set-Content cert.b64 "$env:WINDOWS_CERT_PFX_BASE64"
   certutil -decode cert.b64 code-sign.pfx >nul
   signtool sign /f code-sign.pfx /p $env:WINDOWS_CERT_PASSWORD /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 installer\InstallerWiX\bin\Release\DeepcoolServiceInstaller.msi
   signtool verify /pa installer\InstallerWiX\bin\Release\DeepcoolServiceInstaller.msi
   ```
5. (Optional) Re-sign after version bump or if timestamp server changes.

If secrets are absent, the workflow simply produces an unsigned MSI.

---
## 3. GitHub Actions Windows MSI Workflow

The workflow (`.github/workflows/windows-msi.yml`) builds and uploads the MSI artifact on pushes (configure triggers as needed).

Artifact name: `DeepcoolServiceInstaller`.

To enable signing, add environment secrets (`WINDOWS_CERT_PFX_BASE64`, `WINDOWS_CERT_PASSWORD`) and insert the signing step after the MSI build.

---
## 4. Legacy Setup Project (.vdproj)

The `installer/legacy/DeepcoolServiceSetup/DeepcoolServiceSetup.vdproj` remains for reference but is deprecated in favor of WiX. You can remove it once WiX packaging is validated.

---
## 5. Configuration File

- `DeepcoolDisplay.sample.cfg` installed as a sample.
- Real config: `DeepcoolDisplay.cfg` (ignored by git) created automatically if absent.
- Restart service after edits.

---
## 6. Linux (Future Path)

The current code targets .NET Framework and Windows-only libraries. For Linux support you would:
- Port to .NET 8 SDK project.
- Replace hardware sensor and HID libraries with cross-platform equivalents.
- Provide a systemd unit + tarball or .deb packaging.

No Linux installer is provided yet; this is a roadmap item.

---
## 7. Verification

After install, confirm service status:
```powershell
Get-Service DeepCool
```
Check logs (if enabled) under the service install directory.

---
## 8. Signing (Optional)

Add code-signing before distribution:
```powershell
signtool sign /fd SHA256 /a /tr http://timestamp.digicert.com /td SHA256 InstallerWiX\bin\Release\DeepcoolServiceInstaller.msi
```

---
## 9. Silent Deployment Example

```powershell
msiexec /i DeepcoolServiceInstaller.msi /qn /norestart
```

---
## 10. Security Considerations: PawnIO Kernel Driver (Upgraded)

**Update (Nov 2025)**: This project now uses **LibreHardwareMonitorLib nightly build with PawnIO** instead of the legacy WinRing0 driver.

**What is PawnIO?**
PawnIO is a modern kernel-mode driver framework for low-level hardware access (CPU/GPU temperatures, power sensors, fan speeds). At runtime, the library installs `PawnIO.sys` if not already present.

**Why does a .sys file appear?**
When the Deepcool service starts, LibreHardwareMonitorLib automatically:
1. Checks for PawnIO driver installation
2. Installs PawnIO.sys to system drivers folder if absent
3. Loads binary modules (.bin files) for specific hardware access:
   - `IntelMSR.bin` - Intel CPU MSR reads
   - `AMDFamily17.bin` - AMD Ryzen SMU access
   - `LpcACPIEC.bin` - Embedded controller access
   - `RyzenSMU.bin` - Ryzen PM table reads
   - SMBus modules for memory SPD reading

This is **expected behavior** for hardware monitoring applications. Similar tools (HWiNFO, AIDA64, CPU-Z) also use kernel drivers.

**Windows Defender Alerts**
PawnIO is a legitimate, open-source driver ([LibreHardwareMonitor GitHub](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor)) but may trigger SmartScreen/Defender warnings because:
- Kernel drivers require elevated privileges
- PawnIO performs low-level hardware I/O (port access, MSR/PCI reads)
- Actively maintained (2020+) with better security practices than legacy WinRing0
- More restrictive permissions model than predecessors

**Advantages Over WinRing0**
PawnIO (current) vs WinRing0 (legacy):
- ✅ Actively maintained (2020+) vs unmaintained since 2008
- ✅ Modular binary loading vs monolithic driver
- ✅ Better Windows 11 compatibility
- ✅ More restrictive permission model
- ✅ Open source and auditable
- ✅ Not flagged by Microsoft driver blocklist

**Whitelisting Instructions**
If Windows Defender blocks PawnIO:
1. Open Windows Security → Virus & threat protection
2. Click "Manage settings" → Scroll to "Exclusions"
3. Add exclusion for:
   - Process: `C:\Program Files\DeepcoolService\Deepcool Digital.exe`
   - File: `C:\Windows\System32\drivers\PawnIO.sys` (driver path)
4. Restart the Deepcool service

**Alternatives (If Driver Blocked)**
If Windows Defender or corporate policy blocks PawnIO:
- **Option 1**: Use WMI/Performance Counters (limited sensor access, CPU temp only)
- **Option 2**: Remove LibreHardwareMonitor dependency (display static/fake data)
- **Option 3**: Request IT whitelist exception (provide GitHub source link)

**Driver Safety**
PawnIO source code is auditable at [LibreHardwareMonitor repository](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/tree/main/LibreHardwareMonitorLib/PawnIo). The driver:
- Does not collect telemetry
- Does not modify system files beyond installation
- Only performs read operations on hardware registers (with write access for fan control if enabled)
- Is uninstalled when LibreHardwareMonitor applications are removed

**Library Version**
This project uses LibreHardwareMonitorLib nightly build (post-0.9.4) from the `lib/` folder:
- `lib/LibreHardwareMonitorLib.dll` (1.1M, Nov 2025 nightly)
- `lib/System.CodeDom.dll` (dependency)

These are tracked in git for reproducible builds until official NuGet package updates.

**Verification**
Check installed driver:
```powershell
Get-WindowsDriver -Online | Where-Object {$_.OriginalFileName -like "*pawnio*"}
# Or check file properties
Get-ItemProperty 'C:\Windows\System32\drivers\PawnIO.sys' -ErrorAction SilentlyContinue
```

Uninstall PawnIO manually (stops all dependent services):
```powershell
sc.exe stop PawnIO
sc.exe delete PawnIO
Remove-Item 'C:\Windows\System32\drivers\PawnIO.sys' -Force
```

---
## 11. Troubleshooting

| Issue | Resolution |
|-------|------------|
| Service fails to start | Check Event Viewer (Application) and config syntax. |
| Service hangs when stopping | Fixed in latest build (thread abort with timeout). Restart Windows if service stuck in "Stopping" state. |
| `FileNotFoundException` at startup | Missing runtime DLLs. Ensure all DLLs from `lib/` folder are copied to install directory. |
| Missing DLL on startup | Ensure all DLLs copied by script or included in MSI. |
| Config changes ignored | Restart the service (`Restart-Service DeepCool`). |
| MSI build fails | Confirm WiX Toolset installed and paths in `InstallerWiX.wixproj`. |
| Windows Defender blocks PawnIO | Add exclusions for service EXE and PawnIO.sys driver (see Security section). |
| "PawnIO not installed" prompt | Normal on first run; allow LibreHardwareMonitor to install driver. |

---
## 12. Removing Everything Manually

```powershell
Stop-Service DeepCool
sc.exe delete DeepCool
Remove-Item -Recurse -Force 'C:\Program Files\DeepcoolService'
```

Optional: Remove PawnIO driver if no other hardware monitoring tools need it:
```powershell
sc.exe stop PawnIO
sc.exe delete PawnIO
Remove-Item -Force 'C:\Windows\System32\drivers\PawnIO.sys'
```

---
## 13. Folder Summary

| Path | Purpose |
|------|---------|
| `installer/` | PowerShell scripts (install/uninstall/build-msi), WiX project, and legacy .vdproj. |
| `installer/InstallerWiX/` | WiX XML & project for MSI packaging. |
| `installer/legacy/DeepcoolServiceSetup/` | Legacy Visual Studio setup project (deprecated). |

---
End of INSTALL.md
