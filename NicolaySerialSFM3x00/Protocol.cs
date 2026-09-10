using System;

namespace NicolaySerialSFM3x00
{
    /// <summary>
    /// Provides stateless protocol operations for framing, CRC calculation, and little-endian
    /// payload decoding. These operations perform no I/O and can be tested without hardware.
    /// </summary>
    internal static class Protocol
    {
        /// <summary>
        /// Number of bytes in a complete (non-exception) response frame for each function code,
        /// including the address, function code, data-length byte, data, and CRC. A zero means
        /// the response length is not fixed and cannot be framed from this table alone.
        /// </summary>
        private static readonly (byte Command, byte Length)[] ResponseLengthTable =
        [
            (0x01, 7),  // Get software version
            (0x02, 6),  // Get hardware version
            (0x05, 6),  // Test command
            (0x06, 13), // Get pressure sensor value
            (0x07, 6),  // Get pressure
            (0x09, 10), // Get flow and pressure
            (0x0A, 8),  // Get sensor article number
            (0x0B, 4),  // Board hardware reset
            (0x0C, 4),  // Sensor hard reset
            (0x0D, 4),  // Sensor soft reset
            (0x0E, 4),  // Start flow sensor
            (0x0F, 8),  // Get sensor serial number
            (0x10, 8),  // Get flow measurement
            (0x11, 6),  // Get raw flow measurement
            (0x12, 6),  // Get flow sensor scale
            (0x13, 6),  // Get flow sensor offset
            (0x14, 5),  // Get or set heater state
            (0x15, 5),  // Get or set heater power
            (0x18, 6),  // Get scale temperature
            (0x19, 6),  // Get offset temperature
            (0x1B, 6),  // Force temperature update
            (0x1C, 6),  // Force raw temperature update
            (0x22, 5)   // Get or set UART baud rate
        ];

        // Two function codes are deliberately absent from the table. Bulk read (0x1D) answers with
        // a variable-length frame and is not implemented. Stream send (0x1E) answers with
        // continuous headerless packets, which the parser frames in its streaming mode instead.

        private static readonly byte[] ResponseLengths = BuildResponseLengths();

        /// <summary>
        /// Number of bytes in a complete response frame for <paramref name="command"/>, or zero
        /// if the command has no fixed response length.
        /// </summary>
        public static byte ResponseLength(byte command) => ResponseLengths[command];

        private static byte[] BuildResponseLengths()
        {
            var lengths = new byte[256];
            foreach (var (command, length) in ResponseLengthTable)
            {
                lengths[command] = length;
            }

            return lengths;
        }

        /// <summary>
        /// Number of bytes in a streaming packet, including a 32-bit flow value, a 16-bit raw
        /// pressure value if the device has a pressure sensor, and the end-of-text marker.
        /// </summary>
        public static int StreamPacketLength(bool hasPressure) => hasPressure ? 8 : 6;

        /// <summary>
        /// Builds a complete request frame that contains the address, function code, data length,
        /// data, and CRC.
        /// </summary>
        public static byte[] BuildFrame(byte address, byte command, ReadOnlySpan<byte> data)
        {
            var frame = new byte[4 + data.Length];
            frame[0] = address;
            frame[1] = command;
            frame[2] = (byte)data.Length;
            data.CopyTo(frame.AsSpan(3));
            frame[frame.Length - 1] = Crc8(frame.AsSpan(0, frame.Length - 1));
            return frame;
        }

        /// <summary>
        /// CRC-8 with polynomial 0x31 and an initial value of 0x00, as specified by the Nicolay
        /// protocol and the Sensirion SFM3000 CRC application note.
        /// </summary>
        public static byte Crc8(ReadOnlySpan<byte> data)
        {
            byte crc = 0x00;
            foreach (var b in data)
            {
                crc ^= b;
                for (var i = 0; i < 8; i++)
                {
                    if ((crc & 0x80) != 0)
                        crc = (byte)((crc << 1) ^ 0x31);
                    else
                        crc <<= 1;
                }
            }

            return crc;
        }

        /// <summary>
        /// Checks the trailing CRC byte of a complete frame against the frame contents.
        /// </summary>
        public static bool HasValidCrc(ReadOnlySpan<byte> frame)
        {
            if (frame.Length < 2) return false;
            return Crc8(frame.Slice(0, frame.Length - 1)) == frame[frame.Length - 1];
        }

        // Multi-byte values are transmitted low byte first.

        public static ushort U16(ReadOnlySpan<byte> payload, int offset) =>
            (ushort)(payload[offset] | (payload[offset + 1] << 8));

        public static short I16(ReadOnlySpan<byte> payload, int offset) =>
            (short)U16(payload, offset);

        public static uint U32(ReadOnlySpan<byte> payload, int offset) =>
            (uint)(payload[offset]
                   | (payload[offset + 1] << 8)
                   | (payload[offset + 2] << 16)
                   | (payload[offset + 3] << 24));

        public static int I32(ReadOnlySpan<byte> payload, int offset) =>
            unchecked((int)U32(payload, offset));
    }
}
