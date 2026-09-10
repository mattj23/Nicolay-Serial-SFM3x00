using System.Diagnostics;
using NicolaySerialSFM3x00;

var portName = args.Length > 0 ? args[0] : "/dev/ttyUSB0";

Console.WriteLine($"Connecting to {portName}...");
await using var device = new SfmDevice(portName);
await device.Connect();

if (!await device.Check())
{
    Console.WriteLine("Error, device check failed!");
    return;
}

Console.WriteLine();
Console.WriteLine("Device information");
Console.WriteLine($"  Software version : {await device.GetSoftwareVersionAsync()}");
Console.WriteLine($"  Hardware version : {await device.GetHardwareVersionAsync()}");
Console.WriteLine($"  Flow scale       : {await device.GetFlowScaleAsync()}");
Console.WriteLine($"  Flow offset      : {await device.GetFlowOffsetAsync()}");

// Read the temperature before the article and serial numbers. Reading those values leaves the
// sensor busy for a few hundred milliseconds and can invalidate an immediate temperature reading.
Console.WriteLine($"  Chip temperature : {await device.ForceTemperatureUpdateAsync():F2} C");
Console.WriteLine($"  Sensor article   : {await device.GetSensorArticleNumberAsync()}");
Console.WriteLine($"  Sensor serial    : {(await device.GetSensorSerialNumberAsync())?.ToString() ?? "unreadable"}");

var pressureSensor = await device.GetPressureSensorInfoAsync();
Console.WriteLine($"  Pressure sensor  : {(pressureSensor.IsPresent ? pressureSensor.Type.ToString() : "none")}");

Console.WriteLine();
Console.WriteLine("Polling measurements for 5 seconds...");

var stopwatch = Stopwatch.StartNew();
var polled = 0;
while (stopwatch.ElapsedMilliseconds < 5000)
{
    await device.GetFlowSlmAsync();
    polled++;
}

stopwatch.Stop();
Report(polled, stopwatch.Elapsed);

Console.WriteLine();
Console.WriteLine("Streaming measurements for 5 seconds...");

using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
var streamed = 0;
StreamSample last = default;
stopwatch.Restart();

try
{
    await foreach (var sample in device.StreamAsync(cancellation.Token))
    {
        last = sample;
        streamed++;
    }
}
catch (OperationCanceledException)
{
    // The stream runs until the five-second cancellation timer stops it.
}

stopwatch.Stop();
Report(streamed, stopwatch.Elapsed);

Console.WriteLine();
Console.WriteLine($"Last streamed flow: {last.FlowSlm?.ToString("F4") ?? "unreadable"} slm");
if (pressureSensor.IsPresent && last.RawPressure is { } raw)
{
    Console.WriteLine($"Last streamed pressure: {pressureSensor.ToPressure(raw):F3}");
}

return;

void Report(int count, TimeSpan elapsed)
{
    Console.WriteLine($"  Received {count} measurements in {elapsed.TotalMilliseconds:F0} ms " +
                      $"({count / elapsed.TotalSeconds:F1} Hz, {elapsed.TotalMilliseconds / count:F2} ms each)");
}
