using System;

namespace DeepcoolService.Utils
{
    public static class PacketBuilder
    {

    // LD-series telemetry header (Linux table ld-series.md)
    private static readonly byte[] FixedHeaderLD = { 16, 104, 1, 1, 11, 1, 2, 5 };
    // CH-series (Morpheus) telemetry header (Linux table ch-series.md)
    private static readonly byte[] FixedHeaderCH = { 16, 104, 1, 1, 12, 1, 2, 5 };

    // Optional variable telemetry packet length: default 64, can be overridden via env DEEPCOOL_PACKET_LEN=18
    private static readonly int TelemetryPacketLength;
    // Endianness control: default Big Endian per current implementation, override via DEEPCOOL_ENDIAN=LE to send little-endian without reversal
    private static readonly bool UseBigEndian = true;

    static PacketBuilder()
    {
        var lenEnv = Environment.GetEnvironmentVariable("DEEPCOOL_PACKET_LEN");
        if (!string.IsNullOrWhiteSpace(lenEnv) && int.TryParse(lenEnv, out int parsed) && (parsed == 18 || parsed == 64))
        {
            TelemetryPacketLength = parsed;
        }
        else
        {
            TelemetryPacketLength = 64; // default full HID report
        }
        var endianEnv = Environment.GetEnvironmentVariable("DEEPCOOL_ENDIAN")?.ToUpperInvariant();
        if (endianEnv == "LE" || endianEnv == "LITTLE" || endianEnv == "LITTLEENDIAN")
        {
            UseBigEndian = false;
        }
    }

    // Morpheus / CH table-based frame (11 bytes: D0..D10) according to ch-series.md
    // Layout:
    // D0: 16 (Report ID)
    // D1: CPU mode (19=C temp, 35=F temp, 76=usage, 170=animation)
    // D2: CPU status bar value (1-10)
    // D3: CPU digit hundreds (1-9) or placeholder
    // D4: CPU digit tens (1-9)
    // D5: CPU digit ones (1-9)
    // D6: GPU mode (same codes as D1) (we currently mirror CPU or set fixed temp mode)
    // D7: GPU status bar value (1-10)
    // D8: GPU digit hundreds
    // D9: GPU digit tens
    // D10: GPU digit ones
    // NOTE: Table shows ranges 1-9 (no zero). We map numeric digit 0 => 1 as a best-effort placeholder.
    public static byte[] BuildPacketCHTable(
        float cpuTempC,
        byte cpuLoadPercent,
        float? gpuTempC = null,
        byte? gpuLoadPercent = null,
        bool fahrenheit = false,
        float? cpuPowerW = null,
        float? gpuPowerW = null)
    {
        // Determine CPU mode code
        string cpuModeEnv = Environment.GetEnvironmentVariable("DEEPCOOL_CH_CPU_MODE")?.ToLowerInvariant();
        byte cpuModeCode = 19; // default C temp
        if (cpuModeEnv == "usage") cpuModeCode = 76;
        else if (cpuModeEnv == "f" || cpuModeEnv == "tempf" || (fahrenheit && cpuModeEnv != "usage")) cpuModeCode = 35;
        else if (cpuModeEnv == "anim") cpuModeCode = 170;
        else if (cpuModeEnv == "power")
        {
            // reuse usage icon/code unless explicit override is provided
            cpuModeCode = 76;
        }
        // explicit override numeric code
        if (byte.TryParse(Environment.GetEnvironmentVariable("DEEPCOOL_CH_CPU_MODE_CODE"), out byte overrideCpuCode))
        {
            cpuModeCode = overrideCpuCode;
        }

        // GPU mode selection (mirror CPU by default)
        string gpuModeEnv = Environment.GetEnvironmentVariable("DEEPCOOL_CH_GPU_MODE")?.ToLowerInvariant();
        byte gpuModeCode = cpuModeCode;
        if (!string.IsNullOrWhiteSpace(gpuModeEnv))
        {
            if (gpuModeEnv == "usage") gpuModeCode = 76;
            else if (gpuModeEnv == "c" || gpuModeEnv == "tempc") gpuModeCode = 19;
            else if (gpuModeEnv == "f" || gpuModeEnv == "tempf") gpuModeCode = 35;
            else if (gpuModeEnv == "anim") gpuModeCode = 170;
            else if (gpuModeEnv == "power") gpuModeCode = 76;
        }
        if (byte.TryParse(Environment.GetEnvironmentVariable("DEEPCOOL_CH_GPU_MODE_CODE"), out byte overrideGpuCode))
        {
            gpuModeCode = overrideGpuCode;
        }

        // Status bar value scaling
        int cpuScaled;
        if (cpuModeEnv == "power" && cpuPowerW.HasValue)
        {
            float maxCpu = 0;
            float.TryParse(Environment.GetEnvironmentVariable("DEEPCOOL_CPU_POWER_MAX"), out maxCpu);
            if (maxCpu <= 0) maxCpu = 200; // default CPU power ceiling
            cpuScaled = (int)Math.Ceiling(Math.Min(1.0, cpuPowerW.Value / maxCpu) * 10.0);
        }
        else
        {
            cpuScaled = (int)Math.Ceiling((cpuLoadPercent / 100.0) * 10.0);
        }
        if (cpuScaled < 1) cpuScaled = 1; if (cpuScaled > 10) cpuScaled = 10;
        byte cpuBar = (byte)cpuScaled;

        // Choose numeric value for CPU: power, usage or temperature
        int cpuValue;
        if (cpuModeEnv == "power" && cpuPowerW.HasValue)
        {
            cpuValue = (int)Math.Round(cpuPowerW.Value);
        }
        else if (cpuModeCode == 76 && cpuModeEnv == "usage")
        {
            cpuValue = cpuLoadPercent;
        }
        else
        {
            cpuValue = (int)Math.Round(cpuTempC);
        }
        if (cpuModeCode == 35) // Fahrenheit conversion if needed
        {
            cpuValue = (int)Math.Round(cpuTempC * 9 / 5 + 32);
        }

        // Extract digits (hundreds, tens, ones) limited to 0..999
        if (cpuValue < 0) cpuValue = 0; if (cpuValue > 999) cpuValue = 999;
        int cpuHundreds = cpuValue / 100;
        int cpuTens = (cpuValue / 10) % 10;
        int cpuOnes = cpuValue % 10;

    // Digits: allow 0..9; treat 0 as blank/zero directly (previously coerced to 1 causing leading '1').
    Func<int, byte> mapDigit = d => (byte)(d < 0 ? 0 : (d > 9 ? 9 : d));

        // GPU values (optional). If null replicate CPU or set minimal placeholders.
        int gpuValue;
        byte gpuBar;
        if (gpuTempC.HasValue || gpuLoadPercent.HasValue || gpuPowerW.HasValue)
        {
            if (gpuModeEnv == "power" && gpuPowerW.HasValue)
            {
                float maxGpu = 0;
                float.TryParse(Environment.GetEnvironmentVariable("DEEPCOOL_GPU_POWER_MAX"), out maxGpu);
                if (maxGpu <= 0) maxGpu = 400; // default GPU power ceiling
                gpuValue = (int)Math.Round(gpuPowerW.Value);
                int barScaled = (int)Math.Ceiling(Math.Min(1.0, gpuPowerW.Value / maxGpu) * 10.0);
                if (barScaled < 1) barScaled = 1; if (barScaled > 10) barScaled = 10;
                gpuBar = (byte)barScaled;
            }
            else if (gpuModeCode == 76 && gpuModeEnv == "usage") // usage
            {
                byte gpuLoad = gpuLoadPercent ?? cpuLoadPercent;
                gpuValue = gpuLoad;
                int gpuScaled = (int)Math.Ceiling((gpuLoad / 100.0) * 10.0);
                if (gpuScaled < 1) gpuScaled = 1; if (gpuScaled > 10) gpuScaled = 10;
                gpuBar = (byte)gpuScaled;
            }
            else
            {
                float tempSrc = gpuTempC ?? cpuTempC;
                if (gpuModeCode == 35) tempSrc = tempSrc * 9 / 5 + 32;
                gpuValue = (int)Math.Round(tempSrc);
                int gpuScaled = (int)Math.Ceiling((Math.Min(100, gpuValue) / 100.0) * 10.0);
                if (gpuScaled < 1) gpuScaled = 1; if (gpuScaled > 10) gpuScaled = 10;
                gpuBar = (byte)gpuScaled; // crude bar scaling
            }
        }
        else
        {
            // replicate CPU
            gpuValue = cpuValue;
            gpuBar = cpuBar;
        }
        if (gpuValue < 0) gpuValue = 0; if (gpuValue > 999) gpuValue = 999;
        int gpuHundreds = gpuValue / 100;
        int gpuTens = (gpuValue / 10) % 10;
        int gpuOnes = gpuValue % 10;

        var frame = new byte[11];
        frame[0] = 16; // Report ID
        frame[1] = cpuModeCode;
        frame[2] = cpuBar; // 1-10
        frame[3] = mapDigit(cpuHundreds);
        frame[4] = mapDigit(cpuTens);
        frame[5] = mapDigit(cpuOnes);
        frame[6] = gpuModeCode;
        frame[7] = gpuBar;
        frame[8] = mapDigit(gpuHundreds);
        frame[9] = mapDigit(gpuTens);
        frame[10] = mapDigit(gpuOnes);
        return frame;
    }

        // LD-series (legacy) packet
        public static byte[] BuildPacketLD(ushort power, float temperature, bool fahrenheit, byte utilization)
        {
            byte[] packet = new byte[TelemetryPacketLength];
            Array.Copy(FixedHeaderLD, 0, packet, 0, FixedHeaderLD.Length);
            // Power: D8–D9 (UInt16, big endian)
            byte[] powerBytes = BitConverter.GetBytes(power);
            // Current protocol assumed big endian; allow override
            if (BitConverter.IsLittleEndian && UseBigEndian)
                Array.Reverse(powerBytes);
            packet[8] = powerBytes[0];
            packet[9] = powerBytes[1];
            // Temp Unit: D10 (0 = C, 1 = F)
            packet[10] = (byte)(fahrenheit ? 1 : 0);
            // Temperature: D11–D14 (float32, big endian)
            byte[] tempBytes = BitConverter.GetBytes(temperature);
            if (BitConverter.IsLittleEndian && UseBigEndian)
                Array.Reverse(tempBytes);
            Array.Copy(tempBytes, 0, packet, 11, 4);
            // Utilization: D15 (0–100)
            packet[15] = utilization;
            // Checksum: D16 = (D1–D15) % 256
            ushort checksum = 0;
            for (int i = 1; i <= 15; i++)
                checksum += packet[i];
            packet[16] = (byte)(checksum % 256);
            // Termination Byte: D17
            packet[17] = 22;
            // If shortened (18 bytes) we only keep header + telemetry section
            if (packet.Length == 18)
            {
                // Already populated indices 0..17
            }
            return packet;
        }

        // CH-series (Morpheus) packet
        public static byte[] BuildPacketCH(ushort power, float temperature, bool fahrenheit, byte utilization)
        {
            byte[] packet = new byte[TelemetryPacketLength];
            Array.Copy(FixedHeaderCH, 0, packet, 0, FixedHeaderCH.Length);
            // Power: D8–D9 (UInt16, big endian)
            byte[] powerBytes = BitConverter.GetBytes(power);
            if (BitConverter.IsLittleEndian && UseBigEndian)
                Array.Reverse(powerBytes);
            packet[8] = powerBytes[0];
            packet[9] = powerBytes[1];
            // Temp Unit: D10 (0 = C, 1 = F)
            packet[10] = (byte)(fahrenheit ? 1 : 0);
            // Temperature: D11–D14 (float32, big endian)
            byte[] tempBytes = BitConverter.GetBytes(temperature);
            if (BitConverter.IsLittleEndian && UseBigEndian)
                Array.Reverse(tempBytes);
            Array.Copy(tempBytes, 0, packet, 11, 4);
            // Utilization: D15 (0–100)
            packet[15] = utilization;
            // Checksum: D16 = (D1–D15) % 256
            ushort checksum = 0;
            for (int i = 1; i <= 15; i++)
                checksum += packet[i];
            packet[16] = (byte)(checksum % 256);
            // Termination Byte: D17
            packet[17] = 22;
            if (packet.Length == 18)
            {
                // shortened variant
            }
            return packet;
        }
    }
}