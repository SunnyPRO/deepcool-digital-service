# DeepcoolService

**DeepcoolService** is a minimal Windows background service that replaces the official Deepcool software ([DEEPCOOL DEEPCREATIVE](https://ru.deepcool.com/downloadpage/)). The official software includes always-on tray applications and startup entries that consume resources and clutter the system. This project provides a clean, lightweight, and non-intrusive alternative that runs entirely as a native Windows service.

---

## ❄️ Supported Hardware

Currently supported:

- **Deepcool LD-series AIO liquid coolers** (tested: LD360 WH) – HID mapping via the [LD-series table](https://github.com/Nortank12/deepcool-digital-linux/blob/main/device-list/tables/ld-series.md)
- **Deepcool Morpheus / CH-series Display** – Detected as VendorID `0x3633` ProductID `0x0007` (mapped internally to CH-series header byte `0x0C`). HID mapping aligned with the [CH-series table](https://github.com/Nortank12/deepcool-digital-linux/blob/main/device-list/tables/ch-series.md). Older documentation may list PID `0x000B`; both are treated as CH.

If you own a different Deepcool digital device, you can add support by translating the Linux HID mapping table into a packet builder inside `PacketBuilder.cs` and adjusting detection logic in `MonitorWorker.cs`.

## Configuration File (Preferred)

Instead of environment variables you can create a text file `DeepcoolDisplay.cfg` alongside the service executable. Each line is `KEY=VALUE`. Comments start with `#` or `//`.

Example:

```
# Deepcool Morpheus display configuration
CPU_MODE=tempC          # tempC|tempF|usage|anim
GPU_MODE=usage          # optional; defaults to mirror CPU_MODE
ENABLE_GPU=1            # enable GPU sensors (table mode digits & bar)
TABLE_MODE=1            # force CH table-frame mode immediately
DUAL_MODE=0             # 1 = send telemetry + table each cycle (CH only)
UPDATE_MS=1000          # loop interval in ms
PACKET_LEN=64           # 18 or 64 for telemetry mode
ENDIAN=BE               # BE or LE for telemetry numeric fields
AUTO_TABLE_SEC=8        # seconds before auto-switch to table mode (CH)
VID=0x3633              # override vendor id (hex)
PID=0x0007              # override product id (hex)
GPU_SELECT=discrete     # discrete|integrated|maxload (multi-GPU heuristic)
GPU_INDEX=0             # explicit GPU index (overrides heuristic if valid)
GPU_VENDOR=nvidia       # substring match (e.g. nvidia / amd / intel)
GPU_LOAD_SENSOR=total   # substring to force a specific GPU load sensor
GPU_TEMP_SENSOR=core    # substring to force a specific GPU temp sensor
CPU_MODE=power          # NEW: show CPU package power (W) instead of temp
GPU_MODE=power          # NEW: show GPU board power (W) (needs power sensor)
CPU_POWER_MAX=200       # Scale top bar (CPU) for power mode (default 200W)
GPU_POWER_MAX=450       # Scale top bar (GPU) for power mode (default 400W)
CPU_MODE_CODE=76        # Optional raw mode byte override (advanced)
GPU_MODE_CODE=76        # Optional raw mode byte override (advanced)
TABLE_DEBUG=1           # Enable detailed per-frame value logging (digits/bars)
```

Keys map internally to the previous environment variable behavior. If the file exists, you do not need to set any environment variables. Invalid or missing keys fall back to defaults.

### Available KEY values

CPU_MODE / GPU_MODE:
- tempC / c  -> Temperature in Celsius (default for CPU)
- tempF / f  -> Temperature in Fahrenheit
- usage      -> Utilization percent (0–100)
- power      -> Integer watts (requires matching power sensor; bars scale using *_POWER_MAX)
- anim       -> Animation placeholder (sends animation mode code; digits may be ignored)

When set to power, the numeric digits show the (rounded) watt value and the bar length is normalized against CPU_POWER_MAX / GPU_POWER_MAX (defaults 200W / 400W if not provided). Values over the max clamp to a full bar (10). A zero value now correctly renders as 0 (previous builds displayed a leading 1 due to digit mapping limitations).

Multi-GPU selection (if ENABLE_GPU=1):
The service will score all detected GPU devices and pick one. You can refine/override with:
- GPU_INDEX          -> Explicit zero-based index in the detected list
- GPU_VENDOR         -> Substring match (e.g. nvidia / amd / intel)
- GPU_SELECT         -> Heuristic: discrete | integrated | maxload
If multiple hints are given, precedence: GPU_INDEX > GPU_VENDOR > GPU_SELECT heuristic. Absent hints, a scoring function prefers discrete GPUs (NVIDIA/AMD) over integrated.

Sensor substring overrides:
- GPU_LOAD_SENSOR    -> Substring matched (case-insensitive) against available Load sensors (e.g. "total", "core")
- GPU_TEMP_SENSOR    -> Substring matched against Temperature sensors (e.g. "core", "hotspot"). Invalid or non-matching strings are ignored silently.

Power scaling:
- CPU_POWER_MAX / GPU_POWER_MAX define the watt value that fills the bar (10/10). If unset or <= 0 defaults to 200 / 400 respectively.

Advanced (rarely needed):
- CPU_MODE_CODE / GPU_MODE_CODE let you hard-set the raw mode byte placed in the table frame (bytes D1 / D6). This overrides any decoding of CPU_MODE/GPU_MODE text values. Use for experimentation or undocumented icons.

Tip: Remove or comment a key (# or //) to revert to defaults next restart.

### Logging & Runtime Controls

Add these keys (or environment variables) to manage verbosity and log size:

```
LOG_LEVEL=INFO        # OFF|ERROR|WARN|INFO (default INFO)
PACKET_SAMPLE=0       # If >0, only log every Nth packet (e.g. 100)
MAX_LOG_MB=4          # Rotate when primary log exceeds this size (integer MB)
```

Behavior:
- LOG_LEVEL=OFF disables all non-critical logging (packets, sensor traces, table debug). Errors during startup still surface to help diagnose failures.
- PACKET_SAMPLE lets you reduce packet spam; when set to 250 only the 250th, 500th, ... packet line is written.
- MAX_LOG_MB adjusts the single-file size threshold before rotation; rotated files get a numeric suffix.
- Changes in `DeepcoolDisplay.cfg` are reapplied at service start; to apply dynamically, restart the service (planned: live reload watcher).

TABLE_DEBUG produces an expanded per-frame line: digits, bar lengths, raw sensor readings and scaling decisions—useful for verifying power mode or unexpected clamping.

If power sensors are absent the service silently falls back to temperature or usage depending on the underlying mode code; a WARN entry is emitted when LOG_LEVEL is at least WARN.

## Environment Variables (Legacy / Alternative)

The service still honors environment variables if you prefer that deployment style:

| Variable | Description |
|----------|-------------|
| `DEEPCOOL_VID` | Override Vendor ID (hex), default `0x3633` |
| `DEEPCOOL_PID` | Force Product ID selection (hex) |
| `DEEPCOOL_TEST_BOTH` | If `1` or `true`, send both LD & CH packets when series unknown |
| `DEEPCOOL_PACKET_LEN` | `18` or `64` telemetry packet length |
| `DEEPCOOL_CH_TABLE` | If `1`, force CH table frame mode |
| `DEEPCOOL_CH_DUAL` | If `1`, send telemetry and table frame each cycle (CH only) |
| `DEEPCOOL_CH_CPU_MODE` | `tempC`/`c`, `tempF`/`f`, `usage`, `anim` |
| `DEEPCOOL_CH_GPU_MODE` | Same options as CPU mode for GPU digits |
| `DEEPCOOL_CH_GPU` | If `1`, enable GPU sensors (for table mode) |
| `DEEPCOOL_CPU_POWER_MAX` | Max watts for CPU bar scaling in power mode |
| `DEEPCOOL_GPU_POWER_MAX` | Max watts for GPU bar scaling in power mode |
| `DEEPCOOL_GPU_INDEX` | Explicit GPU index override (0-based) |
| `DEEPCOOL_GPU_VENDOR` | Vendor/device name substring to select GPU |
| `DEEPCOOL_GPU_SELECT` | Heuristic: `discrete` / `integrated` / `maxload` |
| `DEEPCOOL_GPU_LOAD_SENSOR` | Substring to force a specific GPU load sensor |
| `DEEPCOOL_GPU_TEMP_SENSOR` | Substring to force a specific GPU temp sensor |
| `DEEPCOOL_CH_CPU_MODE_CODE` | Raw mode byte override for CPU (table frame) |
| `DEEPCOOL_CH_GPU_MODE_CODE` | Raw mode byte override for GPU (table frame) |
| `DEEPCOOL_CH_TABLE_DEBUG` | If `1`, log per-frame digits/bars & raw sensor values |

Power Mode:
Add the value `power` to `DEEPCOOL_CH_CPU_MODE` / `DEEPCOOL_CH_GPU_MODE` (or CPU_MODE / GPU_MODE in config) to display watts. Bars scale using `DEEPCOOL_CPU_POWER_MAX` / `DEEPCOOL_GPU_POWER_MAX`. GPU power picks the first suitable Power sensor (prefers names containing Board/Power/GPU). CPU power uses the Package sensor. If a power sensor isn't found the mode gracefully falls back to temperature or usage depending on the code.
| `DEEPCOOL_UPDATE_MS` | Loop delay (200–10000) |
| `DEEPCOOL_ENDIAN` | `LE` to disable big-endian swap (send little-endian) |
| `DEEPCOOL_VERBOSE_INPUT` | If `1`, log non-success input statuses |
| `DEEPCOOL_AUTO_TABLE_SEC` | Seconds before auto table fallback (CH) |

### Auto Fallback

If CH series telemetry packets produce no display reaction for the configured `AUTO_TABLE_SEC` (default 8) the service auto enables table mode.

| Variable | Purpose | Example |
|----------|---------|---------|
| `DEEPCOOL_VID` | Override USB Vendor ID if hardware differs | `DEEPCOOL_VID=0x3633` |
| `DEEPCOOL_PID` | Force specific Product ID selection | `DEEPCOOL_PID=0x0007` |
| `DEEPCOOL_TEST_BOTH` | When unknown series, send both LD & CH packets | `DEEPCOOL_TEST_BOTH=1` |
| `DEEPCOOL_PACKET_LEN` | Telemetry packet length (18 or 64) for compatibility testing | `DEEPCOOL_PACKET_LEN=18` |

Telemetry packet layout (indices zero-based):

```
0..7   Fixed header (LD: 10 68 01 01 0B 01 02 05 / CH: 10 68 01 01 0C 01 02 05)
8..9   Power (UInt16, big-endian, watts truncated)
10     Temperature unit (0 = C, 1 = F)
11..14 Temperature (float32, big-endian)
15     CPU utilization percent (0–100)
16     Checksum = (sum bytes 1..15) % 256
17     Terminator 0x16 (decimal 22)
18..63 Zero padding (when `DEEPCOOL_PACKET_LEN=64`)
```

If `DEEPCOOL_PACKET_LEN=18`, only bytes 0..17 are sent.

---

## ✨ Features

- ✅ Runs silently as a **Windows Service** (no tray icon, no UI).
- ✅ Replaces official Deepcool background applications.
- ✅ No autostart clutter or persistent user-mode apps.
- ✅ HID communication with LD-series Deepcool AIO coolers.
- ✅ Uses [**LibreHardwareMonitor**](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor) for system temperature monitoring.

## 🛠 Installation (Brief)

Multiple installation options are supported (manual scripts, WiX MSI, legacy Visual Studio setup). To keep this README focused, all detailed instructions—including CI packaging and optional code signing—are in `INSTALL.md`.

Summary:
- Manual: PowerShell scripts in `installer/` to copy files and create the Windows Service.
- MSI: WiX project in `installer/InstallerWiX/` for automated builds (recommended for distribution).
- Legacy: `.vdproj` kept in `installer/legacy/` temporarily for fallback; can be removed later.
- Signing: CI workflow can sign the MSI using GitHub secrets (see `INSTALL.md` for creating PFX and secrets).

Refer to `INSTALL.md` for full commands, workflow configuration, and signing setup.

---

## 📌 Future Improvements

🧩 Broaden support for additional Deepcool AIO coolers (e.g. LS, CASTLE series) and refine CH-series

📋 Configurable temperature display behavior

📄 Logging to file for diagnostics and debugging

🧪 Improved error handling

---

## 🤝 Contributing

Contributions are welcome! Special thanks to:

- **SunnyPRO** – Deepcool Morpheus display support
