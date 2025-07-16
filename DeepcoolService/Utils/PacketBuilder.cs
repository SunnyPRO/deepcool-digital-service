using System;

namespace DeepcoolService.Utils
{
    public static class PacketBuilder
    {
        private static readonly byte[] FixedHeader = { 16, 104, 1, 1, 11, 1, 2, 5 };

        public static byte[] BuildPacket(ushort power, float temperature, bool fahrenheit, byte utilization)
        {
            byte[] packet = new byte[64];

            Array.Copy(FixedHeader, 0, packet, 0, FixedHeader.Length);

            // --- Power: D8–D9 (UInt16, big endian) ---
            byte[] powerBytes = BitConverter.GetBytes(power);
            if (BitConverter.IsLittleEndian)
                Array.Reverse(powerBytes);
            packet[8] = powerBytes[0];
            packet[9] = powerBytes[1];

            // --- Temp Unit: D10 (0 = C, 1 = F) ---
            packet[10] = (byte)(fahrenheit ? 1 : 0);

            // --- Temperature: D11–D14 (float32, big endian) ---
            byte[] tempBytes = BitConverter.GetBytes(temperature);
            if (BitConverter.IsLittleEndian)
                Array.Reverse(tempBytes);
            Array.Copy(tempBytes, 0, packet, 11, 4);

            // --- Utilization: D15 (0–100) ---
            packet[15] = utilization;

            // --- Checksum: D16 = (D1–D15) % 256 ---
            ushort checksum = 0;
            for (int i = 1; i <= 15; i++)
                checksum += packet[i];
            packet[16] = (byte)(checksum % 256);

            // --- Termination Byte: D17 ---
            packet[17] = 22;

            return packet;
        }
    }
}