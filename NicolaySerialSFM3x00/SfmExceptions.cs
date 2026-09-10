using System;

namespace NicolaySerialSFM3x00
{
    /// <summary>
    /// Reasons the device gives for rejecting a request, as defined in datasheet section 6.2.1.
    /// </summary>
    public enum SfmExceptionCode : byte
    {
        /// <summary>The function code is unknown or unsupported.</summary>
        UnknownFunctionCode = 1,

        /// <summary>The device cannot start its firmware and remains in bootloader mode.</summary>
        FirmwareNotStarted = 2,

        /// <summary>The device is initializing and cannot process the request.</summary>
        NotInitialized = 3,

        /// <summary>The device is busy and cannot currently process the request.</summary>
        Busy = 4,

        /// <summary>The request contained too many or too few data bytes.</summary>
        WrongDataCount = 5,

        /// <summary>The amount of data requested overflows or underflows the buffer.</summary>
        RequestedDataCountInvalid = 6,

        /// <summary>The subcode is out of range or unsupported.</summary>
        SubcodeNotSupported = 7,

        /// <summary>The value to be set is out of range.</summary>
        ValueOutOfRange = 8,

        /// <summary>A read from or write to the sensor EEPROM was not acknowledged.</summary>
        EepromNoAcknowledge = 9,

        /// <summary>A read from or write to the sensor EEPROM timed out.</summary>
        EepromTimeout = 10,

        /// <summary>The checksum of a generic I2C command is not valid.</summary>
        I2cChecksumInvalid = 11,

        /// <summary>The sensor is in shutdown mode and requires a hardware reset.</summary>
        ShutdownHardwareResetRequired = 15,

        /// <summary>An update was offered, but the bootloader is not started.</summary>
        BootloaderNotStarted = 16,

        /// <summary>The checksum of a line in the update hex file is invalid.</summary>
        HexLineChecksumInvalid = 17,

        /// <summary>A line of the update hex file does not begin with a colon.</summary>
        HexLineSyntaxError = 18
    }

    /// <summary>
    /// Base class for communication failures involving the device.
    /// </summary>
    public class SfmException : Exception
    {
        public SfmException(string message) : base(message)
        {
        }

        public SfmException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }

    /// <summary>
    /// The device received the request but refused to carry it out and returned an exception frame.
    /// </summary>
    public class SfmDeviceException : SfmException
    {
        public SfmDeviceException(byte functionCode, byte code)
            : base($"The device rejected function code 0x{functionCode:X2} with exception code {code} ({Describe(code)})")
        {
            FunctionCode = functionCode;
            RawCode = code;
            Code = (SfmExceptionCode)code;
        }

        /// <summary>The function code that was rejected.</summary>
        public byte FunctionCode { get; }

        /// <summary>
        /// The reason that the device provided. Values outside <see cref="SfmExceptionCode"/> are
        /// possible if the device reports a code that this library does not recognize.
        /// <see cref="RawCode"/> always contains the value as received.
        /// </summary>
        public SfmExceptionCode Code { get; }

        /// <summary>The exception code reported by the device.</summary>
        public byte RawCode { get; }

        private static string Describe(byte code) =>
            Enum.IsDefined(typeof(SfmExceptionCode), code)
                ? ((SfmExceptionCode)code).ToString()
                : "unknown";
    }

    /// <summary>
    /// The device did not respond to a request within the allowed time.
    /// </summary>
    public class SfmTimeoutException : SfmException
    {
        public SfmTimeoutException(byte functionCode, int timeoutMs)
            : base($"The device did not answer function code 0x{functionCode:X2} within {timeoutMs} ms")
        {
            FunctionCode = functionCode;
            TimeoutMs = timeoutMs;
        }

        /// <summary>The function code of the request that received no response.</summary>
        public byte FunctionCode { get; }

        /// <summary>The time spent waiting for the response, in milliseconds.</summary>
        public int TimeoutMs { get; }
    }

    /// <summary>
    /// A response arrived whose checksum does not match its contents, so it was discarded.
    /// </summary>
    public class SfmCrcException : SfmException
    {
        public SfmCrcException(byte functionCode)
            : base($"The response to function code 0x{functionCode:X2} failed its checksum")
        {
            FunctionCode = functionCode;
        }

        /// <summary>The function code whose response was corrupt.</summary>
        public byte FunctionCode { get; }
    }
}
