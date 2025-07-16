using System.Linq;
using System.Threading;
using DeepcoolService.Utils;
using HidLibrary;
using LibreHardwareMonitor.Hardware;

namespace DeepcoolService.Monitoring
{
    public class MonitorWorker
    {
        private const ushort VendorID = 0x3633;
        private const ushort ProductId = 0x000A;

        private static HidDevice device;
        private static Computer computer;
        private Thread workerThread;
        private bool running;
        private readonly ManualResetEvent stopEvent = new ManualResetEvent(false);
        public void Start()
        {
            device = HidDevices.Enumerate(VendorID, ProductId).FirstOrDefault();

            if (device == null)
            {
                return;
            }

            InitDevice();

            computer = new Computer()
            {
                IsCpuEnabled = true,
                IsMotherboardEnabled = true,
                IsGpuEnabled = false,
                IsMemoryEnabled = false,
                IsStorageEnabled = false,
                IsNetworkEnabled = false
            };

            computer.Open();

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
            running = false;
            stopEvent.Set();

            workerThread?.Join(1000);

            computer?.Close();
            device?.CloseDevice();
            stopEvent?.Close();
        }

        private void RunLoop()
        {
            while (running)
            {
                float? power = null;
                float? temp = null;
                float? load = null;

                foreach (var hardware in computer.Hardware)
                {
                    if (hardware.HardwareType == HardwareType.Cpu)
                    {
                        hardware.Update();

                        foreach (var sensor in hardware.Sensors)
                        {
                            if (sensor.Value == null) continue;

                            if (sensor.SensorType == SensorType.Power && sensor.Name.Contains("Package"))
                                power = sensor.Value.GetValueOrDefault();

                            if (sensor.SensorType == SensorType.Temperature && sensor.Name.Contains("Core"))
                                temp = sensor.Value.GetValueOrDefault();

                            if (sensor.SensorType == SensorType.Load && sensor.Name.Contains("CPU Total"))
                                load = sensor.Value.GetValueOrDefault();
                        }
                    }
                }

                if (temp.HasValue && power.HasValue && load.HasValue)
                {
                    var packet = PacketBuilder.BuildPacket(
                        (ushort)power.Value,
                        temp.Value,
                        false, // false = °C
                        (byte)load.Value
                    );

                    var report = new HidReport(packet.Length, new HidDeviceData(packet, HidDeviceData.ReadStatus.Success));
                    device.WriteReport(report);
                }

                stopEvent.WaitOne(1000);
            }
        }

        private void InitDevice()
        {
            int[,] init_data = {
                { 16, 104, 1, 1, 2, 3, 1, 112, 22 },
                { 16, 104, 1, 1, 2, 2, 0, 110, 22 }
            };

            for (int i = 0; i < init_data.GetLength(0); i++)
            {
                byte[] packet = new byte[init_data.GetLength(1)];
                for (int j = 0; j < packet.Length; j++)
                {
                    packet[j] = (byte)init_data[i, j];
                }

                HidReport report = new HidReport(packet.Length, new HidDeviceData(packet, HidDeviceData.ReadStatus.Success));
                device.WriteReport(report);
            }
        }
    }
}

