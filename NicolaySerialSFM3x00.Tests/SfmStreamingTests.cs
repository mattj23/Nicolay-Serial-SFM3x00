using Xunit;

using static NicolaySerialSFM3x00.Tests.SfmDeviceTests;

namespace NicolaySerialSFM3x00.Tests;

public class SfmStreamingTests
{
    private static byte[] PressureSensorInfoResponse(bool hasPressure) => hasPressure
        ? Response(0x06, 12, 0x38, 0xFF, 0xC8, 0x00, 0x66, 0x06, 0x99, 0x39)
        : Response(0x06, 0, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00);

    private static byte[] StreamPacket(int flow, ushort? pressure)
    {
        var packet = new List<byte>
        {
            (byte)flow, (byte)(flow >> 8), (byte)(flow >> 16), (byte)(flow >> 24)
        };

        if (pressure.HasValue)
        {
            packet.Add((byte)pressure.Value);
            packet.Add((byte)(pressure.Value >> 8));
        }

        packet.Add(0xFF);
        packet.Add(0x03);
        return packet.ToArray();
    }

    /// <summary>
    /// Connects to a fake device that emits <paramref name="count"/> packets when it receives the
    /// streaming command. The fake device emits no more data after those packets.
    /// </summary>
    private static async Task<(SfmDevice Device, FakeDeviceStream Stream)> ConnectStreamingAsync(
        bool hasPressure, int count)
    {
        FakeDeviceStream? stream = null;
        var (device, s) = await ConnectAsync(request =>
        {
            if (request[1] == 0x06 && request.Length > 4) return PressureSensorInfoResponse(hasPressure);
            if (request.Length == 4 && request[1] == 0x1E)
            {
                for (var i = 0; i < count; i++)
                {
                    stream!.PushToHost(StreamPacket(i, hasPressure ? (ushort)(1000 + i) : null));
                }
            }

            return null;
        });
        stream = s;
        return (device, s);
    }

    [Fact]
    public async Task StreamsFlowOnlyPackets()
    {
        var (device, _) = await ConnectStreamingAsync(hasPressure: false, count: 5);
        await using var __ = device;

        var samples = new List<StreamSample>();
        await foreach (var sample in device.StreamAsync())
        {
            samples.Add(sample);
            if (samples.Count == 5) break;
        }

        Assert.Equal([0, 1, 2, 3, 4], samples.Select(s => s.FlowMilliSlm));
        Assert.All(samples, s => Assert.Null(s.RawPressure));
    }

    [Fact]
    public async Task StreamsPacketsCarryingPressure()
    {
        var (device, _) = await ConnectStreamingAsync(hasPressure: true, count: 3);
        await using var __ = device;

        var samples = new List<StreamSample>();
        await foreach (var sample in device.StreamAsync())
        {
            samples.Add(sample);
            if (samples.Count == 3) break;
        }

        Assert.Equal([0, 1, 2], samples.Select(s => s.FlowMilliSlm));
        Assert.Equal<ushort?>([1000, 1001, 1002], samples.Select(s => s.RawPressure));
    }

    [Fact]
    public async Task ConvertsStreamedFlowToStandardLitres()
    {
        var (device, _) = await ConnectStreamingAsync(hasPressure: false, count: 1);
        await using var __ = device;

        await foreach (var sample in device.StreamAsync())
        {
            Assert.Equal(0, sample.FlowMilliSlm);
            Assert.True(sample.IsFlowReadable);
            break;
        }
    }

    [Fact]
    public async Task AsksTheDeviceToStream()
    {
        var (device, stream) = await ConnectStreamingAsync(hasPressure: false, count: 1);
        await using var __ = device;

        await foreach (var _ in device.StreamAsync()) break;

        // Query the pressure sensor first to determine the packet length, and then start streaming.
        Assert.Equal(0x06, stream.Requests.First()[1]);
        Assert.Contains(stream.Requests, r => r.Length == 4 && r[1] == 0x1E);
    }

    [Fact]
    public async Task StopsTheDeviceWhenTheLoopEnds()
    {
        var (device, stream) = await ConnectStreamingAsync(hasPressure: false, count: 3);
        await using var __ = device;

        await foreach (var _ in device.StreamAsync()) break;

        // Any byte stops the device, so send one byte when the caller finishes.
        Assert.Equal([0x00], stream.Requests.Last());
        Assert.False(device.IsStreaming);
    }

    [Fact]
    public async Task ReportsWhileStreaming()
    {
        var (device, _) = await ConnectStreamingAsync(hasPressure: false, count: 3);
        await using var __ = device;

        Assert.False(device.IsStreaming);

        await foreach (var _ in device.StreamAsync())
        {
            Assert.True(device.IsStreaming);
            break;
        }

        Assert.False(device.IsStreaming);
    }

    [Fact]
    public async Task StopsStreamingWhenCancelled()
    {
        var (device, stream) = await ConnectStreamingAsync(hasPressure: false, count: 2);
        await using var __ = device;

        using var cts = new CancellationTokenSource();
        var received = 0;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in device.StreamAsync(cts.Token))
            {
                received++;
                if (received == 2) cts.Cancel();
            }
        });

        Assert.Equal(2, received);
        Assert.False(device.IsStreaming);
        Assert.Equal([0x00], stream.Requests.Last());
    }

    [Fact]
    public async Task ReturnsToAnsweringRequestsAfterStreaming()
    {
        FakeDeviceStream? stream = null;
        var (device, s) = await ConnectAsync(request =>
        {
            if (request.Length == 1) return null; // This byte stops the stream.
            if (request[1] == 0x06) return PressureSensorInfoResponse(false);
            if (request[1] == 0x1E)
            {
                for (var i = 0; i < 3; i++) stream!.PushToHost(StreamPacket(i, null));
                return null;
            }

            return Response(0x11, 0x34, 0x12);
        });
        stream = s;
        await using var __ = device;

        await foreach (var _ in device.StreamAsync()) break;

        // Packets still in flight must not be mistaken for the next response.
        Assert.Equal(0x1234, await device.GetRawFlowAsync());
    }

    [Fact]
    public async Task RejectsStreamingBeforeConnecting()
    {
        var device = new SfmDevice(new FakeDeviceStream(_ => null), 0x01);

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (var _ in device.StreamAsync()) break;
        });
    }
}
