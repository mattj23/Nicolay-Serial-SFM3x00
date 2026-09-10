using Xunit;

namespace NicolaySerialSFM3x00.Tests;

public class SfmDeviceTests
{
    private const byte Address = 0x01;

    /// <summary>
    /// Builds a well-formed response frame that contains the specified data bytes.
    /// </summary>
    internal static byte[] Response(byte functionCode, params byte[] data)
    {
        var frame = new byte[4 + data.Length];
        frame[0] = Address;
        frame[1] = functionCode;
        frame[2] = (byte)data.Length;
        data.CopyTo(frame, 3);
        frame[^1] = Protocol.Crc8(frame.AsSpan(0, frame.Length - 1));
        return frame;
    }

    /// <summary>
    /// Builds the frame the device sends when it refuses a request.
    /// </summary>
    private static byte[] ExceptionResponse(byte functionCode, byte code)
    {
        byte[] frame = [Address, (byte)(functionCode | 0x80), 0x01, code, 0x00];
        frame[4] = Protocol.Crc8(frame.AsSpan(0, 4));
        return frame;
    }

    /// <summary>
    /// Connects a device to a fake stream that passes every request to
    /// <paramref name="responder"/>.
    /// </summary>
    internal static async Task<(SfmDevice Device, FakeDeviceStream Stream)> ConnectAsync(
        Func<byte[], byte[]?> responder)
    {
        var stream = new FakeDeviceStream(responder);
        var device = new SfmDevice(stream, Address);
        await device.Connect();
        return (device, stream);
    }

    [Fact]
    public async Task SendsAWellFormedRequest()
    {
        var (device, stream) = await ConnectAsync(_ => Response(0x11, 0x34, 0x12));
        await using var _ = device;

        await device.GetRawFlowAsync();

        var request = Assert.Single(stream.Requests);
        Assert.Equal(new byte[] { Address, 0x11, 0x00, 0xDC }, request);
        Assert.True(Protocol.HasValidCrc(request));
    }

    [Fact]
    public async Task ReadsTheRawFlowValue()
    {
        var (device, _) = await ConnectAsync(_ => Response(0x11, 0x34, 0x12));
        await using var __ = device;

        Assert.Equal(0x1234, await device.GetRawFlowAsync());
    }

    [Fact]
    public async Task CheckPassesOnTheExpectedPattern()
    {
        var (device, _) = await ConnectAsync(_ => Response(0x05, 0x55, 0xAA));
        await using var __ = device;

        Assert.True(await device.Check());
    }

    [Fact]
    public async Task CheckFailsOnAnUnexpectedPattern()
    {
        var (device, _) = await ConnectAsync(_ => Response(0x05, 0x55, 0x55));
        await using var __ = device;

        Assert.False(await device.Check());
    }

    [Fact]
    public async Task ServesRequestsInSequence()
    {
        var value = 0;
        var (device, stream) = await ConnectAsync(_ => Response(0x11, (byte)++value, 0x00));
        await using var __ = device;

        var results = new List<int>();
        for (var i = 0; i < 5; i++)
        {
            results.Add(await device.GetRawFlowAsync());
        }

        Assert.Equal([1, 2, 3, 4, 5], results);
        Assert.Equal(5, stream.Requests.Count);
    }

    [Fact]
    public async Task ConcurrentRequestsDoNotInterleave()
    {
        var (device, stream) = await ConnectAsync(request =>
            request[1] == 0x11 ? Response(0x11, 0x01, 0x00) : Response(0x05, 0x55, 0xAA));
        await using var __ = device;

        // Overlapping callers previously raced over a single response slot.
        var results = await Task.WhenAll(
            Enumerable.Range(0, 10).Select(_ => device.GetRawFlowAsync()));

        Assert.All(results, r => Assert.Equal(1, r));
        Assert.Equal(10, stream.Requests.Count);
    }

    [Fact]
    public async Task ThrowsWhenTheDeviceRejectsTheRequest()
    {
        var (device, _) = await ConnectAsync(_ => ExceptionResponse(0x11, 4));
        await using var __ = device;

        var exception = await Assert.ThrowsAsync<SfmDeviceException>(() => device.GetRawFlowAsync());

        Assert.Equal(SfmExceptionCode.Busy, exception.Code);
        Assert.Equal(0x11, exception.FunctionCode);
    }

    [Fact]
    public async Task ReportsAnUnknownExceptionCodeAsReceived()
    {
        var (device, _) = await ConnectAsync(_ => ExceptionResponse(0x11, 99));
        await using var __ = device;

        var exception = await Assert.ThrowsAsync<SfmDeviceException>(() => device.GetRawFlowAsync());

        Assert.Equal(99, exception.RawCode);
    }

    [Fact]
    public async Task RecoversFromARejectedRequest()
    {
        var reject = true;
        var (device, _) = await ConnectAsync(_ =>
        {
            if (!reject) return Response(0x11, 0x34, 0x12);
            reject = false;
            return ExceptionResponse(0x11, 4);
        });
        await using var __ = device;

        await Assert.ThrowsAsync<SfmDeviceException>(() => device.GetRawFlowAsync());

        Assert.Equal(0x1234, await device.GetRawFlowAsync());
    }

    [Fact]
    public async Task ThrowsWhenTheResponseFailsItsChecksum()
    {
        var (device, _) = await ConnectAsync(_ =>
        {
            var frame = Response(0x11, 0x34, 0x12);
            frame[^1] ^= 0xFF;
            return frame;
        });
        await using var __ = device;

        await Assert.ThrowsAsync<SfmCrcException>(() => device.GetRawFlowAsync());
    }

    [Fact]
    public async Task TimesOutWhenTheDeviceDoesNotAnswer()
    {
        var (device, _) = await ConnectAsync(_ => null);
        await using var __ = device;
        device.DefaultTimeoutMs = 50;

        var exception = await Assert.ThrowsAsync<SfmTimeoutException>(() => device.GetRawFlowAsync());

        Assert.Equal(0x11, exception.FunctionCode);
        Assert.Equal(50, exception.TimeoutMs);
    }

    [Fact]
    public async Task RecoversFromATimeout()
    {
        var answer = false;
        var (device, _) = await ConnectAsync(_ => answer ? Response(0x11, 0x34, 0x12) : null);
        await using var __ = device;
        device.DefaultTimeoutMs = 50;

        await Assert.ThrowsAsync<SfmTimeoutException>(() => device.GetRawFlowAsync());

        // The abandoned request must not shift the framing of subsequent responses.
        answer = true;
        Assert.Equal(0x1234, await device.GetRawFlowAsync());
    }

    [Fact]
    public async Task ALateResponseDoesNotCorruptTheNextRequest()
    {
        var delayFirst = true;
        FakeDeviceStream? stream = null;
        var (device, s) = await ConnectAsync(_ =>
        {
            if (!delayFirst) return Response(0x11, 0x34, 0x12);
            delayFirst = false;
            // The device responds after the host has already stopped waiting.
            Task.Run(async () =>
            {
                await Task.Delay(150);
                stream!.PushToHost(Response(0x11, 0x99, 0x99));
            });
            return null;
        });
        stream = s;
        await using var __ = device;
        device.DefaultTimeoutMs = 50;

        await Assert.ThrowsAsync<SfmTimeoutException>(() => device.GetRawFlowAsync());
        await Task.Delay(200);

        Assert.Equal(0x1234, await device.GetRawFlowAsync());
    }

    [Fact]
    public async Task HonoursAPerRequestTimeout()
    {
        var (device, _) = await ConnectAsync(_ => null);
        await using var __ = device;
        device.DefaultTimeoutMs = 5000;

        var exception = await Assert.ThrowsAsync<SfmTimeoutException>(() => device.GetRawFlowAsync(timeoutMs: 50));

        Assert.Equal(50, exception.TimeoutMs);
    }

    [Fact]
    public async Task HonoursCancellation()
    {
        var (device, _) = await ConnectAsync(_ => null);
        await using var __ = device;
        device.DefaultTimeoutMs = 5000;

        using var cts = new CancellationTokenSource(50);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => device.GetRawFlowAsync(cancellationToken: cts.Token));
    }

    [Fact]
    public async Task IgnoresResponsesAddressedToAnotherDevice()
    {
        var (device, _) = await ConnectAsync(_ =>
        {
            byte[] frame = [0x02, 0x11, 0x02, 0x34, 0x12, 0x00];
            frame[5] = Protocol.Crc8(frame.AsSpan(0, 5));
            return frame;
        });
        await using var __ = device;
        device.DefaultTimeoutMs = 50;

        await Assert.ThrowsAsync<SfmTimeoutException>(() => device.GetRawFlowAsync());
    }

    [Fact]
    public async Task RejectsRequestsBeforeConnecting()
    {
        var device = new SfmDevice(new FakeDeviceStream(_ => null), Address);

        await Assert.ThrowsAsync<InvalidOperationException>(() => device.GetRawFlowAsync());
    }

    [Fact]
    public async Task RejectsASecondConnect()
    {
        var (device, _) = await ConnectAsync(_ => null);
        await using var __ = device;

        await Assert.ThrowsAsync<InvalidOperationException>(() => device.Connect());
    }
}
