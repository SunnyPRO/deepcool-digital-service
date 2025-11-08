using System;
using System.Linq;
using System.Threading;
using DeepcoolService.Utils;
using HidLibrary;
using LibreHardwareMonitor.Hardware;
using System.Reflection;

namespace DeepcoolService.Monitoring
{
    public class MonitorWorker
    {
        // Default Vendor ID (from Linux tables assumption). Can be overridden via DEEPCOOL_VID env var.
        private const ushort DefaultVendorID = 0x3633;
        // Known product IDs
    private const ushort ProductIdLD = 0x000A; // LD-series AIO (confirmed)
    private const ushort ProductIdCH = 0x000B; // CH-series Morpheus Display (older assumption)
    private const ushort ProductIdMorpheusAlt = 0x0007; // Observed in log: treat as CH-series actual PID
    // Additional candidates observed / guessed future devices:
    private static readonly ushort[] CandidateProductIds = { ProductIdMorpheusAlt, ProductIdLD, ProductIdCH, 0x000C, 0x0010 };

        private enum DeviceSeries
        {
            Unknown,
            LD,
            CH
        }

        private DeviceSeries activeSeries = DeviceSeries.Unknown;

        private static HidDevice device;
        private static ushort activeVendorId = DefaultVendorID;
        private static bool testBothMode = false;
        private static Computer computer;
        private ISensor gpuLoadSensor;
        private ISensor gpuTempSensor;
        private ISensor gpuPowerSensor;
        private Thread workerThread;
        private Thread readThread;
        private bool running;
        private readonly ManualResetEvent stopEvent = new ManualResetEvent(false);
        public void Start()
        {
            // Load file-based configuration (if present) before reading environment overrides
            var cfg = DeepcoolService.Config.ConfigLoader.Load();
            if (cfg.Count > 0)
            {
                Logger.Info("Config file loaded with keys: " + string.Join(",", cfg.Keys));
                // Apply logging settings derived from config (LOG_LEVEL, PACKET_SAMPLE, MAX_LOG_MB)
                Logger.ApplyRuntimeConfig();
            }
            // Allow manual override via environment variable DEEPCOOL_VID (hex). Example: 0x1E71
            string overrideVidEnv = Environment.GetEnvironmentVariable("DEEPCOOL_VID");
            // Version / build banner
            var asm = Assembly.GetExecutingAssembly();
            Logger.Info($"Banner: AssemblyVersion={asm.GetName().Version} Location={asm.Location} DefaultVID=0x{DefaultVendorID:X4} ActiveVID=0x{activeVendorId:X4} TestBoth={testBothMode}");
            Logger.Info($"Env Overrides: DEEPCOOL_VID='{overrideVidEnv ?? ""}' DEEPCOOL_PID='{Environment.GetEnvironmentVariable("DEEPCOOL_PID") ?? ""}' DEEPCOOL_TEST_BOTH='{Environment.GetEnvironmentVariable("DEEPCOOL_TEST_BOTH") ?? ""}'");
            if (!string.IsNullOrWhiteSpace(overrideVidEnv) && ushort.TryParse(overrideVidEnv.Replace("0x", ""), System.Globalization.NumberStyles.HexNumber, null, out ushort overrideVid))
            {
                activeVendorId = overrideVid;
                Logger.Info("Using override VID 0x" + activeVendorId.ToString("X4"));
            }

            // Enable test-both mode (send both LD & CH packet variants when series unknown)
            string testBothEnv = Environment.GetEnvironmentVariable("DEEPCOOL_TEST_BOTH");
            if (!string.IsNullOrWhiteSpace(testBothEnv) && (testBothEnv == "1" || testBothEnv.ToLowerInvariant() == "true"))
            {
                testBothMode = true;
                Logger.Info("Test-both mode enabled: will send both LD and CH packets when series=Unknown.");
            }

            // Log all devices for diagnostic if vendor scan fails later
            var vendorDevices = HidDevices.Enumerate(activeVendorId).ToList();
            if (!vendorDevices.Any())
            {
                Logger.Warn("No HID devices found for Vendor 0x" + activeVendorId.ToString("X4") + ". Will dump all HID devices for troubleshooting.");
                try
                {
                    foreach (var any in HidDevices.Enumerate())
                    {
                        try
                        {
                            Logger.Info($"HID Device: Path={any.DevicePath} VID=0x{any.Attributes.VendorId:X4} PID=0x{any.Attributes.ProductId:X4}");
                        }
                        catch { }
                    }
                }
                catch (Exception ex)
                {
                    Logger.Warn("Global HID enumeration failed: " + ex.Message);
                }
            }
            else
            {
                foreach (var d in vendorDevices)
                {
                    try
                    {
                        Logger.Info($"Found Vendor device Path={d.DevicePath} PID=0x{d.Attributes.ProductId:X4}");
                    }
                    catch { }
                }
            }

            // Allow manual override via environment variable DEEPCOOL_PID (hex)
            string overridePidEnv = Environment.GetEnvironmentVariable("DEEPCOOL_PID");
            if (!string.IsNullOrWhiteSpace(overridePidEnv) && ushort.TryParse(overridePidEnv.Replace("0x", ""), System.Globalization.NumberStyles.HexNumber, null, out ushort overridePid))
            {
                device = HidDevices.Enumerate(activeVendorId, overridePid).FirstOrDefault();
                if (device != null)
                {
                    Logger.Info("Using override PID 0x" + overridePid.ToString("X4"));
                    activeSeries = overridePid == ProductIdLD ? DeviceSeries.LD : (overridePid == ProductIdCH ? DeviceSeries.CH : DeviceSeries.Unknown);
                }
            }

            if (device == null)
            {
                // Attempt known candidate IDs in order
                foreach (var pid in CandidateProductIds.Distinct())
                {
                    device = HidDevices.Enumerate(activeVendorId, pid).FirstOrDefault();
                    if (device != null)
                    {
                        activeSeries = (pid == ProductIdLD) ? DeviceSeries.LD : (pid == ProductIdCH || pid == ProductIdMorpheusAlt ? DeviceSeries.CH : DeviceSeries.Unknown);
                        Logger.Info($"Selected device PID=0x{pid:X4} Series={activeSeries}");
                        break;
                    }
                }
            }

            if (device == null)
            {
                Logger.Warn("No matching Deepcool candidate device found (Vendor 0x" + activeVendorId.ToString("X4") + "). Worker not started.");
                return;
            }

            try
            {
                device.OpenDevice();
                Logger.Info("Device opened successfully.");
            }
            catch (Exception ex)
            {
                Logger.Warn("Failed to explicitly open device: " + ex.Message);
            }

            InitDevice();
            Logger.Info("Initialization sequence sent for series=" + activeSeries);

            bool enableGpu = (Environment.GetEnvironmentVariable("DEEPCOOL_CH_GPU") == "1" || Environment.GetEnvironmentVariable("DEEPCOOL_CH_GPU")?.ToLowerInvariant() == "true");
            computer = new Computer()
            {
                IsCpuEnabled = true,
                IsMotherboardEnabled = true,
                IsGpuEnabled = enableGpu,
                IsMemoryEnabled = false,
                IsStorageEnabled = false,
                IsNetworkEnabled = false
            };
            if (enableGpu) Logger.Info("GPU metrics enabled.");

            computer.Open();

            // Pre-select GPU sensors if enabled
            if (enableGpu)
            {
                SelectGpuSensors();
            }

            // Optional update interval override
            int updateMs = 1000;
            if (int.TryParse(Environment.GetEnvironmentVariable("DEEPCOOL_UPDATE_MS"), out int envMs) && envMs >= 200 && envMs <= 10000)
            {
                updateMs = envMs;
                Logger.Info("Using custom update interval " + updateMs + "ms");
            }

            loopDelayMs = updateMs;

            // Start read thread for input reports (diagnostics)
            readThread = new Thread(ReadLoop) { IsBackground = true };
            readThread.Start();

            running = true;
            stopEvent.Reset();
            workerThread = new Thread(RunLoop)
            {
                IsBackground = true
            };
            workerThread.Start();
        }

        public void Stop()
        {
            Logger.Info("MonitorWorker stopping...");
            running = false;
            stopEvent.Set();

            // Wait for threads with timeout
            if (workerThread != null && workerThread.IsAlive)
            {
                if (!workerThread.Join(2000)) // 2 second timeout
                {
                    Logger.Warn("Worker thread did not exit gracefully, aborting.");
                    try
                    {
                        workerThread.Abort();
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn("Thread abort failed: " + ex.Message);
                    }
                }
            }

            if (readThread != null && readThread.IsAlive)
            {
                if (!readThread.Join(1000)) // 1 second timeout
                {
                    Logger.Warn("Read thread did not exit gracefully, aborting.");
                    try
                    {
                        readThread.Abort();
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn("Read thread abort failed: " + ex.Message);
                    }
                }
            }

            // Close hardware resources
            try
            {
                computer?.Close();
                Logger.Info("Hardware monitor closed.");
            }
            catch (Exception ex)
            {
                Logger.Warn("Error closing computer: " + ex.Message);
            }

            // Close HID device
            try
            {
                device?.CloseDevice();
                Logger.Info("HID device closed.");
            }
            catch (Exception ex)
            {
                Logger.Warn("Error closing device: " + ex.Message);
            }

            // Dispose stop event
            try
            {
                stopEvent?.Close();
            }
            catch (Exception ex)
            {
                Logger.Warn("Error closing stop event: " + ex.Message);
            }

            Logger.Info("MonitorWorker stopped.");
        }

        private int loopDelayMs = 1000;
        private DateTime chStartTime = DateTime.UtcNow;
        private bool chAutoTableActivated = false;
        private void RunLoop()
        {
            while (running)
            {
                float? power = null;
                float? temp = null;
                float? gpuTemp = null;
                float? load = null;
                float? gpuLoad = null;
                float? gpuPower = null;

                // Update CPU hardware first
                var cpuHardware = computer.Hardware.FirstOrDefault(h => h.HardwareType == HardwareType.Cpu);
                cpuHardware?.Update();
                if (cpuHardware != null)
                {
                    foreach (var sensor in cpuHardware.Sensors)
                    {
                        if (sensor.Value == null) continue;
                        if (sensor.SensorType == SensorType.Power && sensor.Name.Contains("Package")) power = sensor.Value.GetValueOrDefault();
                        if (sensor.SensorType == SensorType.Temperature && sensor.Name.Contains("Core")) temp = sensor.Value.GetValueOrDefault();
                        if (sensor.SensorType == SensorType.Load && sensor.Name.Contains("CPU Total")) load = sensor.Value.GetValueOrDefault();
                    }
                }

                // GPU sensors (pre-selected or fallback scan if not set)
                if (gpuLoadSensor != null || gpuTempSensor != null || gpuPowerSensor != null)
                {
                    gpuLoadSensor?.Hardware.Update();
                    gpuTempSensor?.Hardware.Update();
                    gpuPowerSensor?.Hardware.Update();
                    if (gpuTempSensor?.Value != null) gpuTemp = gpuTempSensor.Value.GetValueOrDefault();
                    if (gpuLoadSensor?.Value != null) gpuLoad = gpuLoadSensor.Value.GetValueOrDefault();
                    if (gpuPowerSensor?.Value != null) gpuPower = gpuPowerSensor.Value.GetValueOrDefault();
                }
                else
                {
                    SelectGpuSensors();
                }

                if (temp.HasValue && power.HasValue && load.HasValue)
                {
                    bool chTableMode = activeSeries == DeviceSeries.CH && (Environment.GetEnvironmentVariable("DEEPCOOL_CH_TABLE") == "1" || Environment.GetEnvironmentVariable("DEEPCOOL_CH_TABLE")?.ToLowerInvariant() == "true");
                    bool chDualMode = activeSeries == DeviceSeries.CH && (Environment.GetEnvironmentVariable("DEEPCOOL_CH_DUAL") == "1" || Environment.GetEnvironmentVariable("DEEPCOOL_CH_DUAL")?.ToLowerInvariant() == "true");

                    if (chDualMode)
                    {
                        var telem = PacketBuilder.BuildPacketCH((ushort)power.Value, temp.Value, false, (byte)load.Value);
                        device.WriteReport(new HidReport(telem.Length, new HidDeviceData(telem, HidDeviceData.ReadStatus.Success)));
                        Logger.Info($"CH telemetry frame sent (dual). Power={power.Value:F1}W Temp={temp.Value:F1}C Load={load.Value:F1}%");
                        Logger.Packet(telem);
                        var frame = PacketBuilder.BuildPacketCHTable(temp.Value, (byte)load.Value, gpuTemp, gpuLoad.HasValue ? (byte?)gpuLoad.Value : null, false, power, gpuPower);
                        device.WriteReport(new HidReport(frame.Length, new HidDeviceData(frame, HidDeviceData.ReadStatus.Success)));
                        Logger.Info($"CH table frame sent (dual). CPU Temp={temp.Value:F1}C Load={load.Value:F1}%");
                        Logger.Packet(frame);
                        MaybeLogTableDebug(frame, power, temp, load, gpuPower, gpuTemp, gpuLoad);
                    }
                    else if (chTableMode)
                    {
                        // Build CH table frame (digits + bars). Use CPU metrics only for now; GPU mirrors CPU unless available.
                        var frame = PacketBuilder.BuildPacketCHTable(temp.Value, (byte)load.Value, gpuTemp, gpuLoad.HasValue ? (byte?)gpuLoad.Value : null, false, power, gpuPower);
                        var reportTable = new HidReport(frame.Length, new HidDeviceData(frame, HidDeviceData.ReadStatus.Success));
                        device.WriteReport(reportTable);
                        Logger.Info($"CH table frame sent. CPU Temp={temp.Value:F1}C Load={load.Value:F1}%");
                        Logger.Packet(frame);
                        MaybeLogTableDebug(frame, power, temp, load, gpuPower, gpuTemp, gpuLoad);
                    }
                    else if (activeSeries == DeviceSeries.Unknown && testBothMode)
                    {
                        // Send LD then CH variant to see which lights up the display
                        var ldPacket = PacketBuilder.BuildPacketLD((ushort)power.Value, temp.Value, false, (byte)load.Value);
                        var chPacket = PacketBuilder.BuildPacketCH((ushort)power.Value, temp.Value, false, (byte)load.Value);
                        device.WriteReport(new HidReport(ldPacket.Length, new HidDeviceData(ldPacket, HidDeviceData.ReadStatus.Success)));
                        device.WriteReport(new HidReport(chPacket.Length, new HidDeviceData(chPacket, HidDeviceData.ReadStatus.Success)));
                        Logger.Info($"TestBoth packets sent (LD & CH). Power={power.Value:F1}W Temp={temp.Value:F1}C Load={load.Value:F1}%");
                        Logger.Packet(ldPacket);
                        Logger.Packet(chPacket);
                    }
                    else
                    {
                        byte[] packet = activeSeries == DeviceSeries.CH
                            ? PacketBuilder.BuildPacketCH((ushort)power.Value, temp.Value, false, (byte)load.Value)
                            : PacketBuilder.BuildPacketLD((ushort)power.Value, temp.Value, false, (byte)load.Value);
                        device.WriteReport(new HidReport(packet.Length, new HidDeviceData(packet, HidDeviceData.ReadStatus.Success)));
                        Logger.Info($"Packet sent. Power={power.Value:F1}W Temp={temp.Value:F1}C Load={load.Value:F1}%");
                        Logger.Packet(packet);
                        if (activeSeries == DeviceSeries.CH && (Environment.GetEnvironmentVariable("DEEPCOOL_CH_TABLE") == "1" || Environment.GetEnvironmentVariable("DEEPCOOL_CH_TABLE")?.ToLowerInvariant() == "true"))
                        {
                            // If table mode toggled dynamically mid-loop, build a debug table representation without sending
                            var debugFrame = PacketBuilder.BuildPacketCHTable(temp.Value, (byte)load.Value, gpuTemp, gpuLoad.HasValue ? (byte?)gpuLoad.Value : null, false, power, gpuPower);
                            MaybeLogTableDebug(debugFrame, power, temp, load, gpuPower, gpuTemp, gpuLoad, onlyIfEnabled:true);
                        }
                    }
                }

                // Auto fallback to table mode after 8 seconds if CH and not already in table
                if (activeSeries == DeviceSeries.CH && !chAutoTableActivated)
                {
                    bool tableEnv = (Environment.GetEnvironmentVariable("DEEPCOOL_CH_TABLE") == "1" || Environment.GetEnvironmentVariable("DEEPCOOL_CH_TABLE")?.ToLowerInvariant() == "true");
                    if (!tableEnv && (DateTime.UtcNow - chStartTime).TotalSeconds > 8)
                    {
                        Logger.Warn("Activating CH table mode automatically (no display reaction suspected). Set DEEPCOOL_CH_TABLE=0 to disable.");
                        Environment.SetEnvironmentVariable("DEEPCOOL_CH_TABLE", "1");
                        chAutoTableActivated = true;
                    }
                }
                stopEvent.WaitOne(loopDelayMs);
            }
        }

        private int readWarnCount = 0;
        private void ReadLoop()
        {
            bool verbose = (Environment.GetEnvironmentVariable("DEEPCOOL_VERBOSE_INPUT") == "1" || Environment.GetEnvironmentVariable("DEEPCOOL_VERBOSE_INPUT")?.ToLowerInvariant() == "true");
            while (running)
            {
                try
                {
                    device.ReadReport(report =>
                    {
                        try
                        {
                            if (report.ReadStatus == HidDeviceData.ReadStatus.Success && report.Data != null && report.Data.Length > 0)
                            {
                                int len = Math.Min(32, report.Data.Length);
                                byte[] slice = new byte[len];
                                Array.Copy(report.Data, slice, len);
                                Logger.Info("INPUT " + BitConverter.ToString(slice).Replace('-', ' '));
                            }
                            else if (verbose)
                            {
                                if (readWarnCount < 10)
                                {
                                    readWarnCount++;
                                    Logger.Info("INPUT status=" + report.ReadStatus + " len=" + (report.Data?.Length ?? 0));
                                }
                            }
                        }
                        catch (Exception cbEx)
                        {
                            if (readWarnCount < 5)
                            {
                                readWarnCount++;
                                Logger.Warn("Read callback exception: " + cbEx.Message);
                            }
                        }
                    });
                }
                catch (Exception ex)
                {
                    if (readWarnCount < 5)
                    {
                        readWarnCount++;
                        Logger.Warn("ReadLoop exception: " + ex.Message);
                    }
                }
                Thread.Sleep(500);
            }
        }

        private void InitDevice()
        {
            int[,] init_data;
            if (activeSeries == DeviceSeries.CH)
            {
                // CH-series may not require LD-style init; use animation setup if requested
                bool chTableMode = (Environment.GetEnvironmentVariable("DEEPCOOL_CH_TABLE") == "1" || Environment.GetEnvironmentVariable("DEEPCOOL_CH_TABLE")?.ToLowerInvariant() == "true");
                if (chTableMode)
                {
                    init_data = new int[,] {
                        { 16, 170, 5, 1, 1, 1, 170, 5, 1, 1, 1 }
                    };
                }
                else
                {
                    init_data = new int[,] {
                        { 16, 104, 1, 1, 2, 3, 1, 112, 22 },
                        { 16, 104, 1, 1, 2, 2, 0, 110, 22 }
                    };
                }
            }
            else
            {
                init_data = new int[,] {
                    { 16, 104, 1, 1, 2, 3, 1, 112, 22 },
                    { 16, 104, 1, 1, 2, 2, 0, 110, 22 }
                };
            }
            if (activeSeries == DeviceSeries.Unknown)
            {
                Logger.Warn("Running with Unknown series; using LD init sequence as fallback. Provide correct PID via DEEPCOOL_PID env var if misdetected.");
            }
            for (int i = 0; i < init_data.GetLength(0); i++)
            {
                byte[] packet = new byte[init_data.GetLength(1)];
                for (int j = 0; j < packet.Length; j++)
                    packet[j] = (byte)init_data[i, j];
                HidReport report = new HidReport(packet.Length, new HidDeviceData(packet, HidDeviceData.ReadStatus.Success));
                device.WriteReport(report);
                Logger.Packet(packet);
            }
        }

        private void SelectGpuSensors()
        {
            try
            {
                string overrideLoad = Environment.GetEnvironmentVariable("DEEPCOOL_GPU_LOAD_SENSOR");
                string overrideTemp = Environment.GetEnvironmentVariable("DEEPCOOL_GPU_TEMP_SENSOR");
                string selectIndexStr = Environment.GetEnvironmentVariable("DEEPCOOL_GPU_INDEX");
                string selectVendor = Environment.GetEnvironmentVariable("DEEPCOOL_GPU_VENDOR");
                string selectMode = Environment.GetEnvironmentVariable("DEEPCOOL_GPU_SELECT");
                var gpuHardware = computer.Hardware.Where(h => h.HardwareType == HardwareType.GpuNvidia || h.HardwareType == HardwareType.GpuAmd || h.HardwareType == HardwareType.GpuIntel).ToList();
                if (!gpuHardware.Any()) return;
                foreach (var hw in gpuHardware) hw.Update();
                Logger.Info("GPU sensor scan: hardware count=" + gpuHardware.Count);
                foreach (var hw in gpuHardware)
                {
                    foreach (var s in hw.Sensors)
                    {
                        if (s.Value == null) continue;
                        if (s.SensorType == SensorType.Load || s.SensorType == SensorType.Temperature)
                        {
                            Logger.Info($"GPU SENSOR {s.SensorType} Name='{s.Name}' Value={s.Value:F1}");
                        }
                    }
                }
                // Choose hardware based on overrides/scoring
                IHardware selected = null;
                int explicitIndex;
                if (int.TryParse(selectIndexStr, out explicitIndex) && explicitIndex >= 0 && explicitIndex < gpuHardware.Count)
                {
                    selected = gpuHardware[explicitIndex];
                    Logger.Info("GPU selection by index: " + explicitIndex);
                }
                if (selected == null && !string.IsNullOrWhiteSpace(selectVendor))
                {
                    string v = selectVendor.ToLowerInvariant();
                    selected = gpuHardware.FirstOrDefault(h => h.Name.ToLowerInvariant().Contains(v));
                    if (selected != null) Logger.Info("GPU selection by vendor substring: " + selectVendor);
                }
                if (selected == null)
                {
                    int bestScore = int.MinValue;
                    foreach (var hw in gpuHardware)
                    {
                        int score = ScoreGpuHardware(hw, selectMode);
                        if (score > bestScore)
                        {
                            bestScore = score;
                            selected = hw;
                        }
                    }
                    Logger.Info("GPU selection by score: " + (selected != null ? selected.Name : "<none>"));
                }
                // If still null fallback to first
                if (selected == null)
                    selected = gpuHardware.First();

                var chosenSensors = selected != null ? selected.Sensors : gpuHardware.SelectMany(h => h.Sensors).ToArray();
                gpuTempSensor = chosenSensors
                    .Where(s => s.SensorType == SensorType.Temperature && s.Value != null && s.Value < 200) // ignore bogus 255C
                    .OrderByDescending(s => MatchScoreTemp(s, overrideTemp))
                    .FirstOrDefault();
                gpuLoadSensor = chosenSensors
                    .Where(s => s.SensorType == SensorType.Load && s.Value != null)
                    .OrderByDescending(s => MatchScoreLoad(s, overrideLoad))
                    .FirstOrDefault();
                // GPU power sensor pick
                gpuPowerSensor = chosenSensors
                    .Where(s => s.SensorType == SensorType.Power && s.Value != null)
                    .OrderByDescending(s => ScoreGpuPowerSensor(s))
                    .FirstOrDefault();
                if (gpuTempSensor != null) Logger.Info("Selected GPU temp sensor: " + gpuTempSensor.Name);
                if (gpuLoadSensor != null) Logger.Info("Selected GPU load sensor: " + gpuLoadSensor.Name);
                if (gpuPowerSensor != null) Logger.Info("Selected GPU power sensor: " + gpuPowerSensor.Name);
            }
            catch (Exception ex)
            {
                Logger.Warn("SelectGpuSensors failed: " + ex.Message);
            }
        }

        private int ScoreGpuHardware(IHardware hw, string selectMode)
        {
            string name = hw.Name.ToLowerInvariant();
            int score = 0;
            bool discrete = name.Contains("nvidia") || name.Contains("geforce") || name.Contains("radeon") || name.Contains("amd ");
            if (discrete) score += 50;
            if (name.Contains("intel")) score += 10; // integrated preference lower
            if (selectMode != null)
            {
                string mode = selectMode.ToLowerInvariant();
                if (mode == "discrete" && discrete) score += 100;
                else if (mode == "integrated" && !discrete) score += 100;
                else if (mode == "maxload")
                {
                    // approximate current core load if exists to prefer busy device
                    var coreLoad = hw.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Load && s.Name.ToLowerInvariant().Contains("core") && s.Value != null);
                    if (coreLoad != null) score += (int)Math.Min(100, coreLoad.Value.GetValueOrDefault());
                }
            }
            return score;
        }

        private int MatchScoreTemp(ISensor s, string overrideName)
        {
            string name = s.Name.ToLowerInvariant();
            if (!string.IsNullOrWhiteSpace(overrideName) && name.Contains(overrideName.ToLowerInvariant())) return 100;
            int score = 0;
            if (name.Contains("core")) score += 40;
            if (name.Contains("gpu")) score += 30;
            if (name.Contains("temp")) score += 20;
            return score;
        }

        private int MatchScoreLoad(ISensor s, string overrideName)
        {
            string name = s.Name.ToLowerInvariant();
            if (!string.IsNullOrWhiteSpace(overrideName) && name.Contains(overrideName.ToLowerInvariant())) return 100;
            int score = 0;
            if (name.Contains("gpu")) score += 40;
            if (name.Contains("core")) score += 15;
            if (name.Contains("load")) score += 25;
            if (name.Contains("total")) score += 30;
            return score;
        }

        private int ScoreGpuPowerSensor(ISensor s)
        {
            string name = s.Name.ToLowerInvariant();
            int score = 0;
            if (name.Contains("board")) score += 50; // board power often total card
            if (name.Contains("power")) score += 30;
            if (name.Contains("gpu")) score += 10;
            return score;
        }

        private void MaybeLogTableDebug(byte[] frame, float? cpuPower, float? cpuTemp, float? cpuLoad, float? gpuPower, float? gpuTemp, float? gpuLoad, bool onlyIfEnabled = false)
        {
            bool enabled = (Environment.GetEnvironmentVariable("DEEPCOOL_CH_TABLE_DEBUG") == "1" || Environment.GetEnvironmentVariable("DEEPCOOL_CH_TABLE_DEBUG")?.ToLowerInvariant() == "true");
            if (onlyIfEnabled && !enabled) return;
            if (!enabled) return;
            try
            {
                if (frame == null || frame.Length < 11) return;
                byte cpuMode = frame[1];
                byte cpuBar = frame[2];
                int cpuVal = frame[3] * 100 + frame[4] * 10 + frame[5];
                byte gpuMode = frame[6];
                byte gpuBar = frame[7];
                int gpuVal = frame[8] * 100 + frame[9] * 10 + frame[10];
                string cpuModeEnv = Environment.GetEnvironmentVariable("DEEPCOOL_CH_CPU_MODE") ?? "(default)";
                string gpuModeEnv = Environment.GetEnvironmentVariable("DEEPCOOL_CH_GPU_MODE") ?? "(mirror)";
                Logger.Info($"TABLE-DEBUG CPU(modeEnv={cpuModeEnv},code={cpuMode}) digits={cpuVal} bar={cpuBar} rawPower={(cpuPower?.ToString("F1") ?? "-")}W rawTemp={(cpuTemp?.ToString("F1") ?? "-")}C rawLoad={(cpuLoad?.ToString("F1") ?? "-")}%  |  GPU(modeEnv={gpuModeEnv},code={gpuMode}) digits={gpuVal} bar={gpuBar} rawPower={(gpuPower?.ToString("F1") ?? "-")}W rawTemp={(gpuTemp?.ToString("F1") ?? "-")}C rawLoad={(gpuLoad?.ToString("F1") ?? "-")}%");
            }
            catch (Exception ex)
            {
                Logger.Warn("TABLE-DEBUG logging failed: " + ex.Message);
            }
        }
    }
}

