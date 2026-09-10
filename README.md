# Nicolay Serial SFM3x00

A .NET library for operating Sensirion SFM3x00 flow meters through the Nicolay flow meter connector
over an RS232 or RS485 serial interface. The library supports polling, continuous measurement
streaming, request-response commands, and the optional AMS5915 pressure sensor available in some
connectors.

The manufacturer's protocol specification lists these supported flow meters:

- SFM3200-AW
- SFM3300-AW
- SFM3300-D
- SFM3400-AW
- SFM3400-D

## Installation

```bash
dotnet add package NicolaySerialSFM3x00
```

The library targets `netstandard2.1`.

## Getting started

```csharp
using NicolaySerialSFM3x00;

await using var device = new SfmDevice("/dev/ttyUSB0");
await device.Connect();

if (!await device.Check())
{
    throw new InvalidOperationException("The device did not pass its self test");
}

// Flow in standard liters per minute, or null if the sensor cannot be read.
double? flow = await device.GetFlowSlmAsync();
```

The device address defaults to 1, which is the factory default. On a bus with several devices, pass
the address to the constructor: `new SfmDevice("/dev/ttyUSB0", address: 2)`.

Every request-response command accepts an optional timeout and cancellation token. For example:
`await device.GetFlowSlmAsync(timeoutMs: 500, cancellationToken: token)`. Requests that go
unanswered throw after the timeout. The default timeout is 250 ms and can be changed with
`device.DefaultTimeoutMs`.

## Streaming

The round trip to the device limits polling to about 60 measurements per second at the default
115200 baud. Continuous measurement streaming is roughly eighteen times faster:

```csharp
using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));

await foreach (var sample in device.StreamAsync(cancellation.Token))
{
    Console.WriteLine($"{sample.FlowSlm:F4} slm");
}
```

Measured on a connector running firmware 0.99a, polling achieves about 62 Hz and streaming about
1100 Hz, which is close to the theoretical limit of the serial link.

The device stops streaming as soon as it receives any byte, so no other command can be sent while a
stream is running. Calls to other methods wait until the stream ends. Leaving the loop, canceling
the token, or disposing the enumerator stops the stream and returns the device to processing
requests. Streamed packets carry no checksum, and the manufacturer recommends streaming only over
RS232 or onboard USB.

Packets are buffered until they are read, so a consumer that cannot keep up will cause the buffer to
grow.

## Pressure

If a pressure sensor is installed, `GetPressureAsync` returns pressure in the sensor's own unit. The
method uses calibration data that it reads from the device and caches:

```csharp
var sensor = await device.GetPressureSensorInfoAsync();
if (sensor.IsPresent)
{
    Console.WriteLine($"{sensor.Type}: {await device.GetPressureAsync():F3}");
}
```

`GetFlowAndPressureAsync` reads both quantities in one request. This command is faster than two
separate requests and captures both readings at the same time. To convert a raw count, such as one
from a streamed packet, use `sensor.ToPressure(raw)`.

## Commands

| Method | Code | Purpose |
| --- | --- | --- |
| `Check` | 0x05 | Device self test |
| `GetSoftwareVersionAsync` | 0x01 | Board firmware version |
| `GetHardwareVersionAsync` | 0x02 | Board hardware version |
| `GetPressureSensorInfoAsync` | 0x06 | Installed pressure sensor and its calibration |
| `GetRawPressureAsync`, `GetPressureAsync` | 0x07 | Raw or converted pressure |
| `GetFlowAndPressureAsync` | 0x09 | Flow and pressure in one request |
| `GetSensorArticleNumberAsync` | 0x0A | Sensirion article number |
| `BoardHardwareResetAsync` | 0x0B | Restart the board and sensor |
| `SensorHardResetAsync` | 0x0C | Power cycle the sensor |
| `SensorSoftResetAsync` | 0x0D | Soft reset the sensor |
| `StartFlowSensorAsync` | 0x0E | Restart flow measurement |
| `GetSensorSerialNumberAsync` | 0x0F | Sensor serial number |
| `GetFlowMilliSlmAsync`, `GetFlowSlmAsync` | 0x10 | Flow calculated by the board |
| `GetRawFlowAsync` | 0x11 | Raw flow measurement |
| `GetFlowScaleAsync`, `GetFlowOffsetAsync` | 0x12, 0x13 | Flow calibration |
| `GetHeaterStateAsync`, `SetHeaterStateAsync` | 0x14 | Sensor heater on or off |
| `GetHeaterPowerAsync`, `SetHeaterPowerAsync` | 0x15 | Sensor heater power |
| `GetTemperatureScaleAsync`, `GetTemperatureOffsetAsync` | 0x18, 0x19 | Temperature calibration |
| `ForceTemperatureUpdateAsync` | 0x1B | Chip temperature in degrees Celsius |
| `ForceRawTemperatureUpdateAsync` | 0x1C | Raw chip temperature |
| `StreamAsync` | 0x1E | Continuous measurement stream |
| `SetBaudRateAsync` | 0x22 | Change the UART baud rate |

Bulk read (0x1D) is not implemented. Streaming covers the same need and is simpler to consume. The
protocol also defines a get form of the baud rate command. Its request layout is unspecified, so
the library supports only the set form.

## Errors

Failures are reported as exceptions deriving from `SfmException`:

- `SfmDeviceException` when the device refuses a request. The exception contains the reason as an
  `SfmExceptionCode`, such as `Busy` or `ShutdownHardwareResetRequired`.
- `SfmTimeoutException` when no response arrives in time.
- `SfmCrcException` when a response fails its checksum.

If the device reports that it could not read a sensor, the affected methods return `null`. This is an
expected condition and does not indicate a protocol failure. The datasheet specifies
`SensorHardResetAsync` as the recovery procedure. Methods that return a raw value return it
unchanged, with the sentinel documented on the method.

If the read loop stops unexpectedly, for instance because the port is removed, the `ReadFault`
event is raised and any request in flight fails with the same error.

## Device behavior

Reading the sensor EEPROM, which `GetSensorArticleNumberAsync` and `GetSensorSerialNumberAsync` do,
leaves the sensor busy for a few hundred milliseconds. During that interval, a temperature read has
been observed to return the temperature offset instead of a measurement. The returned value appears
to be roughly 200 degrees Celsius, and the raw temperature is `0xFFFF`. Wait a few hundred
milliseconds after reading either identifier before reading the temperature.

## Building

```bash
dotnet build
dotnet test
```

The test suite covers framing, checksums, and response decoding against the values in the
manufacturer's protocol specification, and exercises the device through a fake serial port, so no
hardware is needed to run it.

`Example/` is a console program that prints the connected device's information and benchmarks
polling against streaming:

```bash
dotnet run --project Example -- /dev/ttyUSB0
```

## License

MIT, see [LICENSE](LICENSE).

## Versioning

Version 1.0.0 changes the API exposed by version 0.2.0. `GetValue` is obsolete in favor of
`GetRawFlowAsync`, which returns the unsigned 16-bit count sent by the device. The byte-level parser
is no longer public.
