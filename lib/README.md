# Library Dependencies

This folder contains LibreHardwareMonitorLib nightly build (post-0.9.4) with **PawnIO driver** support.

## Contents

**Complete runtime dependencies from LibreHardwareMonitor nightly build (Nov 6, 2025):**
Source: https://nightly.link/LibreHardwareMonitor/LibreHardwareMonitor/workflows/master/master/LibreHardwareMonitor.zip

All DLLs below are required for .NET Framework 4.7.2 compatibility. The nightly build targets multiple frameworks (.NET 4.7.2, .NET 8, .NET 9) and includes modern BCL dependencies.

### Core Library
- `LibreHardwareMonitorLib.dll` (1.1M) - Main hardware monitoring library with PawnIO driver support

### Microsoft BCL Extensions
- `Microsoft.Bcl.AsyncInterfaces.dll` (27K) - Async/await interfaces for older frameworks
- `Microsoft.Bcl.HashCode.dll` (23K) - HashCode utility for older frameworks

### System Runtime Dependencies
- `System.Buffers.dll` (24K) - Buffer pooling
- `System.Collections.Immutable.dll` (259K) - Immutable collections
- `System.IO.Pipelines.dll` (85K) - High-performance I/O
- `System.Memory.dll` (145K) - Span<T> and Memory<T> support
- `System.Numerics.Vectors.dll` (110K) - SIMD vector operations
- `System.Reflection.Metadata.dll` (511K) - Metadata reading
- `System.Runtime.CompilerServices.Unsafe.dll` (19K) - Unsafe memory operations
- `System.Threading.Tasks.Extensions.dll` (26K) - ValueTask support
- `System.ValueTuple.dll` (25K) - ValueTuple support

### Serialization & Encoding
- `System.CodeDom.dll` (30K) - Code DOM support
- `System.Formats.Nrbf.dll` (71K) - .NET binary format support
- `System.Resources.Extensions.dll` (112K) - Resource extensions
- `System.Text.Encodings.Web.dll` (80K) - Web encoding utilities
- `System.Text.Json.dll` (727K) - JSON serialization

### Security
- `System.Security.AccessControl.dll` (36K) - Access control lists
- `System.Security.Principal.Windows.dll` (18K) - Windows principal support
- `System.Threading.AccessControl.dll` (33K) - Threading security

## Why Local DLLs?

The official NuGet package (LibreHardwareMonitorLib 0.9.4) still ships with WinRing0, which:
- Is unmaintained since 2008
- Triggers Windows Defender alerts
- Has known security vulnerabilities
- May be blocked by Microsoft's driver blocklist

The nightly build migrates to **PawnIO**, a modern, actively maintained driver framework with better security practices.

## Upgrading

When LibreHardwareMonitorLib releases an official version with PawnIO on NuGet:

1. Update `packages.config`:
   ```xml
   <package id="LibreHardwareMonitorLib" version="X.X.X" targetFramework="net472" />
   ```

2. Update `DeepcoolService.csproj` references back to NuGet packages

3. Remove this `lib/` folder

4. Restore packages:
   ```powershell
   nuget restore
   ```

## PawnIO vs WinRing0

| Feature | PawnIO (Current) | WinRing0 (Legacy) |
|---------|------------------|-------------------|
| Maintained | ✅ Yes (2020+) | ❌ No (2008) |
| Security | ✅ Restrictive | ⚠️ Over-privileged |
| Windows 11 | ✅ Compatible | ⚠️ Blocklist risk |
| Malware abuse | ✅ Clean | ❌ Known exploits |
| Driver signature | ✅ Modern | ⚠️ Legacy |

## Driver Installation

PawnIO.sys is installed automatically by LibreHardwareMonitorLib on first run. See `INSTALL.md` Section 10 for:
- Whitelisting instructions
- Verification commands
- Manual uninstall steps
