using System;

namespace NicolaySerialSFM3x00
{
    /// <summary>
    /// The firmware version of the connector board.
    /// </summary>
    /// <param name="Index">The index character the device reports alongside the version.</param>
    /// <param name="Major">The major version number.</param>
    /// <param name="Minor">The minor version number.</param>
    public readonly record struct SoftwareVersion(char Index, int Major, int Minor)
    {
        public override string ToString() => $"{Major}.{Minor}{Index}";
    }

    /// <summary>
    /// The hardware version of the connector board.
    /// </summary>
    /// <param name="Major">The major version number.</param>
    /// <param name="Minor">The minor version number.</param>
    public readonly record struct HardwareVersion(int Major, int Minor)
    {
        public override string ToString() => $"{Major}.{Minor}";
    }

    /// <summary>
    /// The pressure sensor installed in the connector, as reported by the device.
    /// </summary>
    public enum PressureSensorType : byte
    {
        None = 0,
        Ams5915_0005_D = 1,
        Ams5915_0005_D_B = 2,
        Ams5915_0010_D = 3,
        Ams5915_0010_D_B = 4,
        Ams5915_0020_D = 5,
        Ams5915_0020_D_B = 6,
        Ams5915_0050_D = 7,
        Ams5915_0050_D_B = 8,
        Ams5915_0100_D = 9,
        Ams5915_0100_D_B = 10,
        Ams5915_0200_D = 11,
        Ams5915_0200_D_B = 12,
        Ams5915_0350_D = 13,
        Ams5915_0350_D_B = 14,
        Ams5915_1000_D = 15,
        Ams5915_1000_D_B = 16,
        Ams5915_2000_D = 17,
        Ams5915_4000_D = 18,
        Ams5915_7000_D = 19,
        Ams5915_10000_D = 20,
        Ams5915_1000_A = 21,
        Ams5915_1200_B = 22
    }

    /// <summary>
    /// The pressure sensor installed in the connector and the calibration needed to convert its
    /// raw counts to pressure.
    /// </summary>
    /// <param name="Type">The installed pressure sensor type.</param>
    /// <param name="MinPressure">The lowest pressure the sensor is specified for.</param>
    /// <param name="MaxPressure">The highest pressure the sensor is specified for.</param>
    /// <param name="DigitalMin">The count the sensor reports at <paramref name="MinPressure"/>.</param>
    /// <param name="DigitalMax">The count the sensor reports at <paramref name="MaxPressure"/>.</param>
    public sealed record PressureSensorInfo(
        PressureSensorType Type,
        short MinPressure,
        short MaxPressure,
        short DigitalMin,
        short DigitalMax)
    {
        /// <summary>Whether a pressure sensor is installed.</summary>
        public bool IsPresent => Type != PressureSensorType.None;

        /// <summary>
        /// Converts a raw pressure count into pressure, in the same unit as
        /// <see cref="MinPressure"/> and <see cref="MaxPressure"/>.
        /// </summary>
        public double ToPressure(ushort raw)
        {
            if (!IsPresent)
            {
                throw new InvalidOperationException("The device has no pressure sensor fitted");
            }

            if (DigitalMax == DigitalMin)
            {
                throw new InvalidOperationException(
                    "The device reported a pressure sensor calibration with no span");
            }

            // Sensitivity in counts per unit of pressure, as specified in datasheet section 7.7.
            var sensitivity = (double)(DigitalMax - DigitalMin) / (MaxPressure - MinPressure);
            return (raw - DigitalMin) / sensitivity + MinPressure;
        }
    }

    /// <summary>
    /// A flow measurement paired with the raw pressure count sampled at the same time.
    /// </summary>
    /// <param name="FlowMilliSlm">
    /// Flow in millistandard liters per minute, or <see cref="int.MaxValue"/> if the flow sensor
    /// could not be read.
    /// </param>
    /// <param name="RawPressure">The raw pressure count.</param>
    public readonly record struct FlowAndPressure(int FlowMilliSlm, ushort RawPressure)
    {
        /// <summary>Whether the device was able to read the flow sensor.</summary>
        public bool IsFlowReadable => FlowMilliSlm != int.MaxValue;

        /// <summary>
        /// Flow in standard liters per minute, or null if the sensor could not be read.
        /// </summary>
        public double? FlowSlm => IsFlowReadable ? FlowMilliSlm / 1000.0 : null;
    }

    /// <summary>
    /// A single packet from the device's continuous measurement stream.
    /// </summary>
    /// <param name="FlowMilliSlm">Flow in millistandard liters per minute.</param>
    /// <param name="RawPressure">
    /// The raw pressure count, or null if the device has no pressure sensor installed.
    /// </param>
    public readonly record struct StreamSample(int FlowMilliSlm, ushort? RawPressure)
    {
        /// <summary>Whether the device was able to read the flow sensor.</summary>
        public bool IsFlowReadable => FlowMilliSlm != int.MaxValue;

        /// <summary>
        /// Flow in standard liters per minute, or null if the sensor could not be read.
        /// </summary>
        public double? FlowSlm => IsFlowReadable ? FlowMilliSlm / 1000.0 : null;
    }

    /// <summary>
    /// The UART baud-rate codes defined in datasheet section 7.34. Each name includes the nominal
    /// rate selected by the code. For most codes, the rate produced by the device differs slightly
    /// from the nominal rate.
    /// </summary>
    public enum BaudRateCode : byte
    {
        Baud4800 = 0,
        Baud9600 = 1,
        Baud14400 = 2,
        Baud19200 = 3,
        Baud28800 = 4,
        Baud31250 = 5,
        Baud38400 = 6,
        Baud57600 = 7,

        /// <summary>The rate the device uses after it powers on.</summary>
        Baud115200 = 8,

        Baud128000 = 9,
        Baud230400 = 10,
        Baud250000 = 11,
        Baud256000 = 12,
        Baud384000 = 13,
        Baud500000 = 14,
        Baud576000 = 15
    }

    public static class BaudRateCodeExtensions
    {
        /// <summary>
        /// Returns the nominal baud rate selected by a code. Set the host serial port to this rate
        /// after the device switches.
        /// </summary>
        public static int ToBaudRate(this BaudRateCode code) => code switch
        {
            BaudRateCode.Baud4800 => 4800,
            BaudRateCode.Baud9600 => 9600,
            BaudRateCode.Baud14400 => 14400,
            BaudRateCode.Baud19200 => 19200,
            BaudRateCode.Baud28800 => 28800,
            BaudRateCode.Baud31250 => 31250,
            BaudRateCode.Baud38400 => 38400,
            BaudRateCode.Baud57600 => 57600,
            BaudRateCode.Baud115200 => 115200,
            BaudRateCode.Baud128000 => 128000,
            BaudRateCode.Baud230400 => 230400,
            BaudRateCode.Baud250000 => 250000,
            BaudRateCode.Baud256000 => 256000,
            BaudRateCode.Baud384000 => 384000,
            BaudRateCode.Baud500000 => 500000,
            BaudRateCode.Baud576000 => 576000,
            _ => throw new ArgumentOutOfRangeException(nameof(code), code, "Unknown baud rate code")
        };
    }
}
