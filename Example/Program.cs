// See https://aka.ms/new-console-template for more information

using System.Diagnostics;
using NicolaySerialSFM3x00;

var device = new SfmDevice("/dev/ttyUSB0");
Console.WriteLine("Connecting...");
await device.Connect();

if (!await device.Check())
{
    Console.WriteLine("Error, device check failed!");
    return;
}
Console.WriteLine("Device check passed, starting measurements for 10 seconds...");

var stopwatch = new Stopwatch();
stopwatch.Start();

var count = 0;
while (stopwatch.ElapsedMilliseconds < 10000)
{
    var raw = await device.GetValue();
    var value = (raw - 32768) / 120.0;
    // Console.WriteLine($"Measurement: {value:F2} ml/min");
    
    count++;
}

stopwatch.Stop();
Console.WriteLine($"Received {count} measurements in {stopwatch.ElapsedMilliseconds} ms ({count / (stopwatch.ElapsedMilliseconds / 1000.0)} Hz)");
Console.WriteLine($"(Took approx {stopwatch.ElapsedMilliseconds / (double)count:F2}ms per measurement)");

await device.DisposeAsync();


