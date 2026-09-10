using System;
using System.Threading;
using System.Threading.Tasks;

namespace NicolaySerialSFM3x00
{
    /// <summary>
    /// Implements the device function codes defined in datasheet section 7.
    /// </summary>
    public partial class SfmDevice
    {
        /// <summary>Returned for flow when the device cannot read the flow sensor.</summary>
        private const int FlowUnreadable = int.MaxValue;

        /// <summary>Returned for the serial number when the sensor cannot be read.</summary>
        private const uint SerialNumberUnreadable = 0xFFFFFFFF;

        private PressureSensorInfo? _pressureSensorInfo;

        /// <summary>
        /// Reads the firmware version of the connector board (0x01).
        /// </summary>
        public async Task<SoftwareVersion> GetSoftwareVersionAsync(int? timeoutMs = null,
            CancellationToken cancellationToken = default)
        {
            var payload = await ExecuteAsync(0x01, [], timeoutMs, cancellationToken).ConfigureAwait(false);
            return new SoftwareVersion((char)payload[0], payload[2], payload[1]);
        }

        /// <summary>
        /// Reads the hardware version of the connector board (0x02).
        /// </summary>
        public async Task<HardwareVersion> GetHardwareVersionAsync(int? timeoutMs = null,
            CancellationToken cancellationToken = default)
        {
            var payload = await ExecuteAsync(0x02, [], timeoutMs, cancellationToken).ConfigureAwait(false);
            return new HardwareVersion(payload[1], payload[0]);
        }

        /// <summary>
        /// Reads which pressure sensor is installed and the calibration needed to convert its raw
        /// counts into pressure (0x06).
        /// </summary>
        /// <param name="refresh">
        /// Set to <see langword="true"/> to read from the device even if the information was read
        /// previously. The installed sensor cannot change while the device is running, so the
        /// result is cached by default.
        /// </param>
        /// <param name="timeoutMs">
        /// The time to wait for a response, in milliseconds. If <see langword="null"/>, the request
        /// uses <see cref="DefaultTimeoutMs"/>.
        /// </param>
        /// <param name="cancellationToken">The token that cancels the operation.</param>
        public async Task<PressureSensorInfo> GetPressureSensorInfoAsync(bool refresh = false,
            int? timeoutMs = null, CancellationToken cancellationToken = default)
        {
            if (!refresh && _pressureSensorInfo != null) return _pressureSensorInfo;

            var payload = await ExecuteAsync(0x06, [0x00, 0x00], timeoutMs, cancellationToken)
                .ConfigureAwait(false);

            _pressureSensorInfo = new PressureSensorInfo(
                (PressureSensorType)payload[0],
                Protocol.I16(payload, 1),
                Protocol.I16(payload, 3),
                Protocol.I16(payload, 5),
                Protocol.I16(payload, 7));

            return _pressureSensorInfo;
        }

        /// <summary>
        /// Reads the raw pressure count (0x07). Only the lower 14 bits carry a value.
        /// </summary>
        public async Task<ushort> GetRawPressureAsync(int? timeoutMs = null,
            CancellationToken cancellationToken = default)
        {
            var payload = await ExecuteAsync(0x07, [], timeoutMs, cancellationToken).ConfigureAwait(false);
            return Protocol.U16(payload, 0);
        }

        /// <summary>
        /// Reads the pressure (0x07) and converts it using the installed sensor's calibration, which
        /// is read from the device once and then cached.
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// The device has no pressure sensor installed.
        /// </exception>
        public async Task<double> GetPressureAsync(int? timeoutMs = null,
            CancellationToken cancellationToken = default)
        {
            var info = await GetPressureSensorInfoAsync(false, timeoutMs, cancellationToken)
                .ConfigureAwait(false);
            var raw = await GetRawPressureAsync(timeoutMs, cancellationToken).ConfigureAwait(false);
            return info.ToPressure(raw);
        }

        /// <summary>
        /// Reads the flow and raw pressure in one request (0x09). This command is faster than two
        /// separate requests and captures both readings at the same time.
        /// </summary>
        public async Task<FlowAndPressure> GetFlowAndPressureAsync(int? timeoutMs = null,
            CancellationToken cancellationToken = default)
        {
            var payload = await ExecuteAsync(0x09, [], timeoutMs, cancellationToken).ConfigureAwait(false);
            return new FlowAndPressure(Protocol.I32(payload, 0), Protocol.U16(payload, 4));
        }

        /// <summary>
        /// Reads the Sensirion article number of the flow sensor (0x0A), formatted as it appears on
        /// the sensor, for example "1-101625-01".
        /// </summary>
        public async Task<string> GetSensorArticleNumberAsync(int? timeoutMs = null,
            CancellationToken cancellationToken = default)
        {
            var raw = await GetRawSensorArticleNumberAsync(timeoutMs, cancellationToken).ConfigureAwait(false);

            // The three parts of the article number are packed into bits 31:28, 27:8, and 7:0.
            return $"{(raw >> 28) & 0xF}-{(raw >> 8) & 0xFFFFF:D6}-{raw & 0xFF:D2}";
        }

        /// <summary>
        /// Reads the packed article number of the flow sensor (0x0A) without formatting it.
        /// </summary>
        public async Task<uint> GetRawSensorArticleNumberAsync(int? timeoutMs = null,
            CancellationToken cancellationToken = default)
        {
            var payload = await ExecuteAsync(0x0A, [], timeoutMs, cancellationToken).ConfigureAwait(false);
            return Protocol.U32(payload, 0);
        }

        /// <summary>
        /// Restarts the connector board and its sensor (0x0B). Flow measurement restarts
        /// automatically.
        /// </summary>
        public Task BoardHardwareResetAsync(int? timeoutMs = null,
            CancellationToken cancellationToken = default) =>
            ExecuteAsync(0x0B, [], timeoutMs, cancellationToken);

        /// <summary>
        /// Switches the flow sensor's power supply off and on (0x0C). Flow measurement restarts
        /// automatically. The datasheet specifies this recovery when the sensor is unreadable.
        /// </summary>
        public Task SensorHardResetAsync(int? timeoutMs = null,
            CancellationToken cancellationToken = default) =>
            ExecuteAsync(0x0C, [], timeoutMs, cancellationToken);

        /// <summary>
        /// Performs a software reset of the flow sensor (0x0D). Flow measurement restarts
        /// automatically.
        /// </summary>
        public Task SensorSoftResetAsync(int? timeoutMs = null,
            CancellationToken cancellationToken = default) =>
            ExecuteAsync(0x0D, [], timeoutMs, cancellationToken);

        /// <summary>
        /// Restarts flow measurement (0x0E). The device normally does this automatically, so this is
        /// rarely needed.
        /// </summary>
        public Task StartFlowSensorAsync(int? timeoutMs = null,
            CancellationToken cancellationToken = default) =>
            ExecuteAsync(0x0E, [], timeoutMs, cancellationToken);

        /// <summary>
        /// Reads the serial number of the flow sensor (0x0F), or null if the sensor cannot be read.
        /// </summary>
        public async Task<uint?> GetSensorSerialNumberAsync(int? timeoutMs = null,
            CancellationToken cancellationToken = default)
        {
            var payload = await ExecuteAsync(0x0F, [], timeoutMs, cancellationToken).ConfigureAwait(false);
            var serialNumber = Protocol.U32(payload, 0);
            return serialNumber == SerialNumberUnreadable ? null : serialNumber;
        }

        /// <summary>
        /// Reads the flow calculated by the board, in millistandard liters per minute (0x10), or
        /// null if the sensor cannot be read, in which case
        /// <see cref="SensorHardResetAsync"/> should be performed.
        /// </summary>
        public async Task<int?> GetFlowMilliSlmAsync(int? timeoutMs = null,
            CancellationToken cancellationToken = default)
        {
            var payload = await ExecuteAsync(0x10, [], timeoutMs, cancellationToken).ConfigureAwait(false);
            var flow = Protocol.I32(payload, 0);
            return flow == FlowUnreadable ? null : flow;
        }

        /// <summary>
        /// Reads the flow calculated by the board, in standard liters per minute (0x10), or null
        /// if the sensor cannot be read.
        /// </summary>
        public async Task<double?> GetFlowSlmAsync(int? timeoutMs = null,
            CancellationToken cancellationToken = default)
        {
            var flow = await GetFlowMilliSlmAsync(timeoutMs, cancellationToken).ConfigureAwait(false);
            return flow / 1000.0;
        }

        /// <summary>
        /// Reads the raw measurement from the flow sensor (0x11). A value of 0xFFFF means the
        /// sensor could not be read, in which case <see cref="SensorHardResetAsync"/> should be
        /// performed. Convert a raw value to flow with
        /// (raw - <see cref="GetFlowOffsetAsync"/>) / <see cref="GetFlowScaleAsync"/>.
        /// </summary>
        public async Task<ushort> GetRawFlowAsync(int? timeoutMs = null,
            CancellationToken cancellationToken = default)
        {
            var payload = await ExecuteAsync(0x11, [], timeoutMs, cancellationToken).ConfigureAwait(false);
            return Protocol.U16(payload, 0);
        }

        /// <summary>
        /// Reads the raw flow measurement (0x11).
        /// </summary>
        [Obsolete("Use GetRawFlowAsync, which returns the value as the unsigned 16 bit count it is.")]
        public async Task<int> GetValue(int? timeoutMs = null, CancellationToken cancellationToken = default) =>
            await GetRawFlowAsync(timeoutMs, cancellationToken).ConfigureAwait(false);

        /// <summary>
        /// Reads the flow sensor's scale factor (0x12), used as the divisor when converting a raw
        /// flow measurement. A value of 0xFFFF means <see cref="SensorHardResetAsync"/> should be
        /// performed.
        /// </summary>
        public async Task<ushort> GetFlowScaleAsync(int? timeoutMs = null,
            CancellationToken cancellationToken = default)
        {
            var payload = await ExecuteAsync(0x12, [], timeoutMs, cancellationToken).ConfigureAwait(false);
            return Protocol.U16(payload, 0);
        }

        /// <summary>
        /// Reads the flow sensor's offset (0x13), subtracted from a raw flow measurement before
        /// scaling. A value of 0xFFFF means <see cref="SensorHardResetAsync"/> should be performed.
        /// </summary>
        public async Task<ushort> GetFlowOffsetAsync(int? timeoutMs = null,
            CancellationToken cancellationToken = default)
        {
            var payload = await ExecuteAsync(0x13, [], timeoutMs, cancellationToken).ConfigureAwait(false);
            return Protocol.U16(payload, 0);
        }

        /// <summary>
        /// Reads whether the sensor's heater is on (0x14).
        /// </summary>
        public async Task<bool> GetHeaterStateAsync(int? timeoutMs = null,
            CancellationToken cancellationToken = default)
        {
            var payload = await ExecuteAsync(0x14, [], timeoutMs, cancellationToken).ConfigureAwait(false);
            return (payload[0] & 0x01) != 0;
        }

        /// <summary>
        /// Turns the sensor heater on or off (0x14) at the power set by
        /// <see cref="SetHeaterPowerAsync"/> and returns the state reported by the device.
        /// </summary>
        public async Task<bool> SetHeaterStateAsync(bool on, int? timeoutMs = null,
            CancellationToken cancellationToken = default)
        {
            var payload = await ExecuteAsync(0x14, [(byte)(on ? 0x01 : 0x00)], timeoutMs, cancellationToken)
                .ConfigureAwait(false);
            return (payload[0] & 0x01) != 0;
        }

        /// <summary>
        /// Reads the heater's power setting, as a percentage (0x15).
        /// </summary>
        public async Task<byte> GetHeaterPowerAsync(int? timeoutMs = null,
            CancellationToken cancellationToken = default)
        {
            var payload = await ExecuteAsync(0x15, [], timeoutMs, cancellationToken).ConfigureAwait(false);
            return payload[0];
        }

        /// <summary>
        /// Sets the heater power as a percentage (0x15) and returns the value reported by the
        /// device. This command does not turn the heater on or off. Use
        /// <see cref="SetHeaterStateAsync"/> to change the heater state.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">
        /// The datasheet warns that values above 100 can make the heater behave unexpectedly.
        /// </exception>
        public async Task<byte> SetHeaterPowerAsync(byte percent, int? timeoutMs = null,
            CancellationToken cancellationToken = default)
        {
            if (percent > 100)
            {
                throw new ArgumentOutOfRangeException(nameof(percent), percent,
                    "Heater power must be between 0 and 100 percent");
            }

            var payload = await ExecuteAsync(0x15, [percent], timeoutMs, cancellationToken)
                .ConfigureAwait(false);
            return payload[0];
        }

        /// <summary>
        /// Reads the scale factor used to calculate the chip temperature (0x18).
        /// </summary>
        public async Task<ushort> GetTemperatureScaleAsync(int? timeoutMs = null,
            CancellationToken cancellationToken = default)
        {
            var payload = await ExecuteAsync(0x18, [], timeoutMs, cancellationToken).ConfigureAwait(false);
            return Protocol.U16(payload, 0);
        }

        /// <summary>
        /// Reads the offset used to calculate the chip temperature (0x19).
        /// </summary>
        public async Task<ushort> GetTemperatureOffsetAsync(int? timeoutMs = null,
            CancellationToken cancellationToken = default)
        {
            var payload = await ExecuteAsync(0x19, [], timeoutMs, cancellationToken).ConfigureAwait(false);
            return Protocol.U16(payload, 0);
        }

        /// <summary>
        /// Triggers a temperature measurement and reads the chip temperature in degrees Celsius
        /// (0x1B). This command measures the sensor chip temperature. It does not measure the gas
        /// temperature.
        /// </summary>
        /// <remarks>
        /// The datasheet warns that the next flow measurement after this can take noticeably
        /// longer than usual.
        ///
        /// Reading the sensor EEPROM, as <see cref="GetSensorArticleNumberAsync"/> and
        /// <see cref="GetSensorSerialNumberAsync"/> do, leaves the sensor busy for a few hundred
        /// milliseconds. During that interval, a temperature read has been observed to return the
        /// temperature offset rather than a measurement. The returned value appears to be roughly
        /// 200 degrees Celsius. Wait a few hundred milliseconds after either EEPROM read before
        /// reading the temperature.
        /// </remarks>
        public async Task<double> ForceTemperatureUpdateAsync(int? timeoutMs = null,
            CancellationToken cancellationToken = default)
        {
            var payload = await ExecuteAsync(0x1B, [], timeoutMs, cancellationToken).ConfigureAwait(false);
            return Protocol.I16(payload, 0) / 100.0;
        }

        /// <summary>
        /// Triggers a temperature measurement and reads the raw chip temperature (0x1C). A value
        /// of 0xFFFF means the sensor could not be read. This condition can also occur for a few
        /// hundred milliseconds after reading the sensor EEPROM. See
        /// <see cref="ForceTemperatureUpdateAsync"/>.
        /// </summary>
        public async Task<ushort> ForceRawTemperatureUpdateAsync(int? timeoutMs = null,
            CancellationToken cancellationToken = default)
        {
            var payload = await ExecuteAsync(0x1C, [], timeoutMs, cancellationToken).ConfigureAwait(false);
            return Protocol.U16(payload, 0);
        }

        /// <summary>
        /// Changes the baud rate of the device's UART (0x22) and reconfigures the serial port to
        /// match.
        /// </summary>
        /// <remarks>
        /// The device responds at the old rate and then switches immediately. The port is
        /// reconfigured after the response arrives. The device returns to
        /// <see cref="DefaultBaudRate"/> when it is next powered on.
        /// </remarks>
        public async Task<BaudRateCode> SetBaudRateAsync(BaudRateCode code, int? timeoutMs = null,
            CancellationToken cancellationToken = default)
        {
            if (!Enum.IsDefined(typeof(BaudRateCode), code))
            {
                throw new ArgumentOutOfRangeException(nameof(code), code, "Unknown baud rate code");
            }

            var payload = await ExecuteAsync(0x22, [(byte)code], timeoutMs, cancellationToken)
                .ConfigureAwait(false);

            var accepted = (BaudRateCode)payload[0];
            if (_serialPort != null && Enum.IsDefined(typeof(BaudRateCode), accepted))
            {
                _serialPort.BaudRate = accepted.ToBaudRate();
            }

            return accepted;
        }
    }
}
