# DeepcoolService

**DeepcoolService** is a minimal Windows background service that replaces the official Deepcool software ([DEEPCOOL DEEPCREATIVE](https://ru.deepcool.com/downloadpage/)). The official software includes always-on tray applications and startup entries that consume resources and clutter the system. This project provides a clean, lightweight, and non-intrusive alternative that runs entirely as a native Windows service.

---

## ❄️ Supported Hardware

Currently, the project supports **Deepcool LD-series AIO liquid coolers**. Functionality has been tested on the **Deepcool LD360 WH** model.

The communication with the device is based on the **HID mapping table** from the open-source Linux project [deepcool-digital-linux](https://github.com/Nortank12/deepcool-digital-linux), specifically the [LD-series table](https://github.com/Nortank12/deepcool-digital-linux/blob/main/device-list/tables/ld-series.md). You can easily extend support for other devices by adding custom HID mappings if needed.

---

## ✨ Features

- ✅ Runs silently as a **Windows Service** (no tray icon, no UI).
- ✅ Replaces official Deepcool background applications.
- ✅ No autostart clutter or persistent user-mode apps.
- ✅ HID communication with LD-series Deepcool AIO coolers.
- ✅ Uses [**LibreHardwareMonitor**](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor) for system temperature monitoring.

---

## 📌 Future Improvements

🧩 Support for additional Deepcool AIO coolers (e.g. LS, CASTLE series)

📋 Configurable temperature display behavior

📄 Logging to file for diagnostics and debugging

🧪 Improved error handling
