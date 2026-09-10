using Xunit;

using static NicolaySerialSFM3x00.Tests.SfmDeviceTests;

namespace NicolaySerialSFM3x00.Tests;

public class SfmCommandTests
{
    /// <summary>
    /// The pressure sensor information for the AMS5915 0200 D B in the datasheet example. The
    /// sensor range is -200 to +200 mbar, and its count range is 1638 to 14745.
    /// </summary>
    private static byte[] PressureSensorInfoResponse() =>
        Response(0x06, 12, 0x38, 0xFF, 0xC8, 0x00, 0x66, 0x06, 0x99, 0x39);

    [Fact]
    public async Task ReadsTheSoftwareVersion()
    {
        // The datasheet gives 0x61, 0x5A, 0x00 as firmware version 0.90a.
        var (device, _) = await ConnectAsync(_ => Response(0x01, 0x61, 0x5A, 0x00));
        await using var __ = device;

        var version = await device.GetSoftwareVersionAsync();

        Assert.Equal(0, version.Major);
        Assert.Equal(90, version.Minor);
        Assert.Equal('a', version.Index);
        Assert.Equal("0.90a", version.ToString());
    }

    [Fact]
    public async Task ReadsTheHardwareVersion()
    {
        // The datasheet gives major version 12 and minor version 34 as hardware version 12.34.
        var (device, _) = await ConnectAsync(_ => Response(0x02, 34, 12));
        await using var __ = device;

        var version = await device.GetHardwareVersionAsync();

        Assert.Equal(12, version.Major);
        Assert.Equal(34, version.Minor);
        Assert.Equal("12.34", version.ToString());
    }

    [Fact]
    public async Task ReadsThePressureSensorInformation()
    {
        var (device, stream) = await ConnectAsync(_ => PressureSensorInfoResponse());
        await using var __ = device;

        var info = await device.GetPressureSensorInfoAsync();

        Assert.Equal(PressureSensorType.Ams5915_0200_D_B, info.Type);
        Assert.True(info.IsPresent);
        Assert.Equal(-200, info.MinPressure);
        Assert.Equal(200, info.MaxPressure);
        Assert.Equal(1638, info.DigitalMin);
        Assert.Equal(14745, info.DigitalMax);

        // The request contains two zero data bytes.
        Assert.Equal(new byte[] { 0x01, 0x06, 0x02, 0x00, 0x00 }, Assert.Single(stream.Requests)[..5]);
    }

    [Fact]
    public async Task CachesThePressureSensorInformation()
    {
        var (device, stream) = await ConnectAsync(_ => PressureSensorInfoResponse());
        await using var __ = device;

        await device.GetPressureSensorInfoAsync();
        await device.GetPressureSensorInfoAsync();

        // The installed sensor cannot change while the device is running.
        Assert.Single(stream.Requests);

        await device.GetPressureSensorInfoAsync(refresh: true);
        Assert.Equal(2, stream.Requests.Count);
    }

    [Fact]
    public void ConvertsRawPressureUsingTheDatasheetExample()
    {
        var info = new PressureSensorInfo(PressureSensorType.Ams5915_0200_D_B, -200, 200, 1638, 14745);

        // The datasheet converts a raw count of 0x1FFD to approximately -0.08 mbar.
        Assert.Equal(-0.08, info.ToPressure(0x1FFD), 2);
    }

    [Fact]
    public void ConvertsRawPressureAtTheEndsOfTheRange()
    {
        var info = new PressureSensorInfo(PressureSensorType.Ams5915_0200_D_B, -200, 200, 1638, 14745);

        Assert.Equal(-200, info.ToPressure(1638), 6);
        Assert.Equal(200, info.ToPressure(14745), 6);
    }

    [Fact]
    public void RefusesToConvertPressureWithoutASensor()
    {
        var info = new PressureSensorInfo(PressureSensorType.None, 0, 0, 0, 0);

        Assert.False(info.IsPresent);
        Assert.Throws<InvalidOperationException>(() => info.ToPressure(1234));
    }

    [Fact]
    public async Task ReadsAndConvertsThePressure()
    {
        var (device, _) = await ConnectAsync(request => request[1] == 0x06
            ? PressureSensorInfoResponse()
            : Response(0x07, 0xFD, 0x1F));
        await using var __ = device;

        Assert.Equal(-0.08, await device.GetPressureAsync(), 2);
    }

    [Fact]
    public async Task ReadsTheRawPressure()
    {
        var (device, _) = await ConnectAsync(_ => Response(0x07, 0xFD, 0x1F));
        await using var __ = device;

        Assert.Equal(0x1FFD, await device.GetRawPressureAsync());
    }

    [Fact]
    public async Task ReadsFlowAndPressureTogether()
    {
        // A flow of 12345 mSlm and a raw pressure count of 0x1FFD.
        var (device, _) = await ConnectAsync(_ => Response(0x09, 0x39, 0x30, 0x00, 0x00, 0xFD, 0x1F));
        await using var __ = device;

        var reading = await device.GetFlowAndPressureAsync();

        Assert.Equal(12345, reading.FlowMilliSlm);
        Assert.Equal(12.345, reading.FlowSlm);
        Assert.Equal(0x1FFD, reading.RawPressure);
        Assert.True(reading.IsFlowReadable);
    }

    [Fact]
    public async Task ReportsUnreadableFlowFromACombinedReading()
    {
        var (device, _) = await ConnectAsync(_ => Response(0x09, 0xFF, 0xFF, 0xFF, 0x7F, 0x00, 0x00));
        await using var __ = device;

        var reading = await device.GetFlowAndPressureAsync();

        Assert.False(reading.IsFlowReadable);
        Assert.Null(reading.FlowSlm);
    }

    [Fact]
    public async Task FormatsTheSensorArticleNumber()
    {
        // Article number 1-101625-01, packed as 4 bits, 20 bits, and 8 bits.
        var packed = (1u << 28) | (101625u << 8) | 1u;
        var (device, _) = await ConnectAsync(_ => Response(0x0A,
            (byte)packed, (byte)(packed >> 8), (byte)(packed >> 16), (byte)(packed >> 24)));
        await using var __ = device;

        Assert.Equal("1-101625-01", await device.GetSensorArticleNumberAsync());
        Assert.Equal(packed, await device.GetRawSensorArticleNumberAsync());
    }

    [Fact]
    public async Task ReadsTheSensorSerialNumber()
    {
        var (device, _) = await ConnectAsync(_ => Response(0x0F, 0x78, 0x56, 0x34, 0x12));
        await using var __ = device;

        Assert.Equal(0x12345678u, await device.GetSensorSerialNumberAsync());
    }

    [Fact]
    public async Task ReportsAnUnreadableSensorSerialNumber()
    {
        var (device, _) = await ConnectAsync(_ => Response(0x0F, 0xFF, 0xFF, 0xFF, 0xFF));
        await using var __ = device;

        Assert.Null(await device.GetSensorSerialNumberAsync());
    }

    [Fact]
    public async Task ReadsTheCalculatedFlow()
    {
        // 12345 mSlm, which is 12.345 slm.
        var (device, _) = await ConnectAsync(_ => Response(0x10, 0x39, 0x30, 0x00, 0x00));
        await using var __ = device;

        Assert.Equal(12345, await device.GetFlowMilliSlmAsync());
        Assert.Equal(12.345, await device.GetFlowSlmAsync());
    }

    [Fact]
    public async Task ReadsNegativeFlow()
    {
        var (device, _) = await ConnectAsync(_ => Response(0x10, 0xC7, 0xCF, 0xFF, 0xFF));
        await using var __ = device;

        Assert.Equal(-12345, await device.GetFlowMilliSlmAsync());
    }

    [Fact]
    public async Task ReportsUnreadableFlow()
    {
        var (device, _) = await ConnectAsync(_ => Response(0x10, 0xFF, 0xFF, 0xFF, 0x7F));
        await using var __ = device;

        Assert.Null(await device.GetFlowMilliSlmAsync());
        Assert.Null(await device.GetFlowSlmAsync());
    }

    [Fact]
    public async Task ReadsTheFlowScaleAndOffset()
    {
        var (device, _) = await ConnectAsync(request =>
            request[1] == 0x12 ? Response(0x12, 0x78, 0x00) : Response(0x13, 0x00, 0x80));
        await using var __ = device;

        Assert.Equal(120, await device.GetFlowScaleAsync());
        Assert.Equal(32768, await device.GetFlowOffsetAsync());
    }

    [Fact]
    public async Task ReadsTheHeaterState()
    {
        var (device, _) = await ConnectAsync(_ => Response(0x14, 0x01));
        await using var __ = device;

        Assert.True(await device.GetHeaterStateAsync());
    }

    [Fact]
    public async Task SetsTheHeaterState()
    {
        var (device, stream) = await ConnectAsync(request => Response(0x14, request[3]));
        await using var __ = device;

        Assert.True(await device.SetHeaterStateAsync(true));
        Assert.Equal(new byte[] { 0x01, 0x14, 0x01, 0x01 }, stream.Requests.Last()[..4]);

        Assert.False(await device.SetHeaterStateAsync(false));
        Assert.Equal(new byte[] { 0x01, 0x14, 0x01, 0x00 }, stream.Requests.Last()[..4]);
    }

    [Fact]
    public async Task SetsTheHeaterPower()
    {
        var (device, stream) = await ConnectAsync(request => Response(0x15, request[3]));
        await using var __ = device;

        Assert.Equal(75, await device.SetHeaterPowerAsync(75));
        Assert.Equal(new byte[] { 0x01, 0x15, 0x01, 75 }, stream.Requests.Last()[..4]);
    }

    [Fact]
    public async Task RejectsHeaterPowerAboveOneHundredPercent()
    {
        var (device, stream) = await ConnectAsync(request => Response(0x15, request[3]));
        await using var __ = device;

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => device.SetHeaterPowerAsync(101));
        Assert.Empty(stream.Requests);
    }

    [Fact]
    public async Task ReadsTheHeaterPower()
    {
        var (device, _) = await ConnectAsync(_ => Response(0x15, 50));
        await using var __ = device;

        Assert.Equal(50, await device.GetHeaterPowerAsync());
    }

    [Fact]
    public async Task ReadsTheChipTemperature()
    {
        // 2345 hundredths of a degree Celsius is 23.45 degrees Celsius.
        var (device, _) = await ConnectAsync(_ => Response(0x1B, 0x29, 0x09));
        await using var __ = device;

        Assert.Equal(23.45, await device.ForceTemperatureUpdateAsync(), 6);
    }

    [Fact]
    public async Task ReadsANegativeChipTemperature()
    {
        var (device, _) = await ConnectAsync(_ => Response(0x1B, 0xD7, 0xF6));
        await using var __ = device;

        Assert.Equal(-23.45, await device.ForceTemperatureUpdateAsync(), 6);
    }

    [Fact]
    public async Task ReadsTheRawChipTemperatureAndItsCalibration()
    {
        var (device, _) = await ConnectAsync(request => request[1] switch
        {
            0x18 => Response(0x18, 0x64, 0x00),
            0x19 => Response(0x19, 0xC8, 0x00),
            _ => Response(0x1C, 0x34, 0x12)
        });
        await using var __ = device;

        Assert.Equal(100, await device.GetTemperatureScaleAsync());
        Assert.Equal(200, await device.GetTemperatureOffsetAsync());
        Assert.Equal(0x1234, await device.ForceRawTemperatureUpdateAsync());
    }

    [Theory]
    [InlineData(0x0B)] // Board hardware reset
    [InlineData(0x0C)] // Sensor hard reset
    [InlineData(0x0D)] // Sensor soft reset
    [InlineData(0x0E)] // Start flow sensor
    public async Task SendsCommandsThatCarryNoData(byte functionCode)
    {
        var (device, stream) = await ConnectAsync(request => Response(request[1]));
        await using var __ = device;

        await (functionCode switch
        {
            0x0B => device.BoardHardwareResetAsync(),
            0x0C => device.SensorHardResetAsync(),
            0x0D => device.SensorSoftResetAsync(),
            _ => device.StartFlowSensorAsync()
        });

        var request = Assert.Single(stream.Requests);
        Assert.Equal(4, request.Length);
        Assert.Equal(functionCode, request[1]);
        Assert.Equal(0x00, request[2]);
    }

    [Fact]
    public async Task SetsTheBaudRate()
    {
        var (device, stream) = await ConnectAsync(request => Response(0x22, request[3]));
        await using var __ = device;

        var accepted = await device.SetBaudRateAsync(BaudRateCode.Baud230400);

        Assert.Equal(BaudRateCode.Baud230400, accepted);
        Assert.Equal(new byte[] { 0x01, 0x22, 0x01, 10 }, Assert.Single(stream.Requests)[..4]);
    }

    [Fact]
    public async Task RejectsAnUnknownBaudRateCode()
    {
        var (device, stream) = await ConnectAsync(request => Response(0x22, request[3]));
        await using var __ = device;

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => device.SetBaudRateAsync((BaudRateCode)99));
        Assert.Empty(stream.Requests);
    }

    [Theory]
    [InlineData(BaudRateCode.Baud4800, 4800)]
    [InlineData(BaudRateCode.Baud115200, 115200)]
    [InlineData(BaudRateCode.Baud576000, 576000)]
    public void MapsBaudRateCodesToTheirNominalRates(BaudRateCode code, int expected)
    {
        Assert.Equal(expected, code.ToBaudRate());
    }

    [Fact]
    public async Task TheObsoleteGetValueStillReadsTheRawFlow()
    {
        var (device, _) = await ConnectAsync(_ => Response(0x11, 0x34, 0x12));
        await using var __ = device;

#pragma warning disable CS0618
        Assert.Equal(0x1234, await device.GetValue());
#pragma warning restore CS0618
    }
}
