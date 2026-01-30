// See https://aka.ms/new-console-template for more information

using NicolaySerialSFM3x00;

Console.WriteLine("Starting");
var device = new SfmDevice("/dev/ttyUSB0");
device.Connect();

device.Check();

await Task.Delay(2000);
Console.WriteLine("Finished");