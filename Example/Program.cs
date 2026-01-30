// See https://aka.ms/new-console-template for more information

using System.Diagnostics;
using NicolaySerialSFM3x00;

Console.WriteLine("Starting");
var device = new SfmDevice("/dev/ttyUSB0");
device.Connect();

device.Check();
await Task.Delay(1000);

var stopwatch = new Stopwatch();
stopwatch.Start();

var count = 0;
while (stopwatch.ElapsedMilliseconds < 10000)
{
    var _ = await device.GetValue();
    count++;
}
stopwatch.Stop();
Console.WriteLine($"Received {count} measurements in {stopwatch.ElapsedMilliseconds} ms ({count / (stopwatch.ElapsedMilliseconds / 1000.0)} Hz)");


