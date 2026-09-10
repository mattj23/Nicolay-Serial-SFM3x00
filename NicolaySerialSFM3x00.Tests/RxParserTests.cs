using Xunit;

namespace NicolaySerialSFM3x00.Tests;

public class RxParserTests
{
    private const byte Address = 0x01;

    private static ushort Expected(byte command, byte address = Address) =>
        (ushort)((address << 8) | command);

    /// <summary>
    /// Creates a raw flow measurement response, which the datasheet defines as six bytes.
    /// </summary>
    private static byte[] RawFlowResponse(ushort raw, byte address = Address)
    {
        byte[] frame = [address, 0x11, 0x02, (byte)(raw & 0xFF), (byte)(raw >> 8), 0x00];
        frame[5] = Protocol.Crc8(frame.AsSpan(0, 5));
        return frame;
    }

    private static (RxParser Parser, List<byte[]> Messages) NewParser()
    {
        var messages = new List<byte[]>();
        return (new RxParser(messages.Add), messages);
    }

    [Fact]
    public void CompletesAResponseDeliveredInOneChunk()
    {
        var (parser, messages) = NewParser();
        parser.EnqueueExpected(Expected(0x11));

        parser.AddBytes(RawFlowResponse(0x1234));

        Assert.Equal(RawFlowResponse(0x1234), Assert.Single(messages));
    }

    [Fact]
    public void CompletesAResponseDeliveredOneByteAtATime()
    {
        var (parser, messages) = NewParser();
        parser.EnqueueExpected(Expected(0x11));

        foreach (var b in RawFlowResponse(0x1234))
        {
            parser.AddBytes([b]);
        }

        Assert.Equal(RawFlowResponse(0x1234), Assert.Single(messages));
    }

    [Fact]
    public void CompletesAResponseSplitAtEveryOffset()
    {
        var response = RawFlowResponse(0xBEEF);

        for (var split = 0; split <= response.Length; split++)
        {
            var (parser, messages) = NewParser();
            parser.EnqueueExpected(Expected(0x11));

            parser.AddBytes(response.AsSpan(0, split));
            parser.AddBytes(response.AsSpan(split));

            Assert.Equal(response, Assert.Single(messages));
        }
    }

    [Fact]
    public void DiscardsBytesWhenNothingIsExpected()
    {
        var (parser, messages) = NewParser();

        parser.AddBytes(RawFlowResponse(0x1234));

        Assert.Empty(messages);
    }

    [Fact]
    public void SkipsLeadingBytesThatCannotStartTheResponse()
    {
        var (parser, messages) = NewParser();
        parser.EnqueueExpected(Expected(0x11));

        parser.AddBytes([0x00, 0xFF, 0x7E]);
        parser.AddBytes(RawFlowResponse(0x1234));

        Assert.Equal(RawFlowResponse(0x1234), Assert.Single(messages));
    }

    [Fact]
    public void RecoversWhenTheAddressIsFollowedByTheWrongFunctionCode()
    {
        var (parser, messages) = NewParser();
        parser.EnqueueExpected(Expected(0x11));

        // The address appears, but the function code that follows is not the one requested.
        parser.AddBytes([Address, 0x10]);
        parser.AddBytes(RawFlowResponse(0x1234));

        Assert.Equal(RawFlowResponse(0x1234), Assert.Single(messages));
    }

    [Fact]
    public void RecoversWhenTheAddressRepeatsBeforeTheResponse()
    {
        var (parser, messages) = NewParser();
        parser.EnqueueExpected(Expected(0x11));

        // A stray address byte is followed immediately by the real response.
        parser.AddBytes([Address]);
        parser.AddBytes(RawFlowResponse(0x1234));

        Assert.Equal(RawFlowResponse(0x1234), Assert.Single(messages));
    }

    [Fact]
    public void CompletesQueuedResponsesInOrder()
    {
        var (parser, messages) = NewParser();
        parser.EnqueueExpected(Expected(0x11));
        parser.EnqueueExpected(Expected(0x11));

        parser.AddBytes(RawFlowResponse(0x0001));
        parser.AddBytes(RawFlowResponse(0x0002));

        Assert.Equal(2, messages.Count);
        Assert.Equal(RawFlowResponse(0x0001), messages[0]);
        Assert.Equal(RawFlowResponse(0x0002), messages[1]);
    }

    [Fact]
    public void IgnoresResponsesAddressedToAnotherDevice()
    {
        var (parser, messages) = NewParser();
        parser.EnqueueExpected(Expected(0x11));

        parser.AddBytes(RawFlowResponse(0x1234, address: 0x02));

        Assert.Empty(messages);
    }

    [Fact]
    public void CompletesAnExceptionFrame()
    {
        var (parser, messages) = NewParser();
        parser.EnqueueExpected(Expected(0x11));

        // An exception response sets bit 7 of the function code and contains one code byte.
        // The exception response is shorter than the function code's standard response.
        byte[] frame = [Address, 0x11 | 0x80, 0x01, 0x04, 0x00];
        frame[4] = Protocol.Crc8(frame.AsSpan(0, 4));
        parser.AddBytes(frame);

        Assert.Equal(frame, Assert.Single(messages));
    }

    [Fact]
    public void ResumesAfterAnExceptionFrame()
    {
        var (parser, messages) = NewParser();
        parser.EnqueueExpected(Expected(0x11));
        parser.EnqueueExpected(Expected(0x11));

        byte[] exception = [Address, 0x11 | 0x80, 0x01, 0x04, 0x00];
        exception[4] = Protocol.Crc8(exception.AsSpan(0, 4));
        parser.AddBytes(exception);
        parser.AddBytes(RawFlowResponse(0x1234));

        Assert.Equal(2, messages.Count);
        Assert.Equal(RawFlowResponse(0x1234), messages[1]);
    }

    [Fact]
    public void ResetDiscardsQueuedExpectationsAndPartialData()
    {
        var (parser, messages) = NewParser();
        parser.EnqueueExpected(Expected(0x11));
        parser.AddBytes(RawFlowResponse(0x1234).AsSpan(0, 3));

        parser.Reset();
        parser.AddBytes(RawFlowResponse(0x1234).AsSpan(3));

        Assert.Empty(messages);
    }

    [Fact]
    public void ResetAllowsTheNextRequestToFrameCleanly()
    {
        var (parser, messages) = NewParser();
        parser.EnqueueExpected(Expected(0x11));
        parser.AddBytes(RawFlowResponse(0x1234).AsSpan(0, 3));
        parser.Reset();

        parser.EnqueueExpected(Expected(0x11));
        parser.AddBytes(RawFlowResponse(0x4321));

        Assert.Equal(RawFlowResponse(0x4321), Assert.Single(messages));
    }

    [Fact]
    public void RejectsExpectationsWithNoFixedResponseLength()
    {
        var (parser, _) = NewParser();

        // Bulk read is not implemented, and stream send has no framed response.
        Assert.Throws<ArgumentException>(() => parser.EnqueueExpected(Expected(0x1D)));
        Assert.Throws<ArgumentException>(() => parser.EnqueueExpected(Expected(0x1E)));
    }
}

public class RxParserStreamingTests
{
    private static (RxParser Parser, List<byte[]> Packets) NewStreamingParser(bool hasPressure)
    {
        var packets = new List<byte[]>();
        var parser = new RxParser(_ => Assert.Fail("No response should be framed while streaming"));
        parser.EnterStreamMode(hasPressure, packets.Add);
        return (parser, packets);
    }

    private static byte[] StreamPacket(int flow, ushort? pressure)
    {
        var packet = new List<byte>
        {
            (byte)(flow & 0xFF), (byte)((flow >> 8) & 0xFF),
            (byte)((flow >> 16) & 0xFF), (byte)((flow >> 24) & 0xFF)
        };

        if (pressure.HasValue)
        {
            packet.Add((byte)(pressure.Value & 0xFF));
            packet.Add((byte)(pressure.Value >> 8));
        }

        packet.Add(0xFF);
        packet.Add(0x03);
        return packet.ToArray();
    }

    [Fact]
    public void FramesFlowOnlyPackets()
    {
        var (parser, packets) = NewStreamingParser(hasPressure: false);

        parser.AddBytes(StreamPacket(1234, null));

        var packet = Assert.Single(packets);
        Assert.Equal(4, packet.Length);
        Assert.Equal(1234, Protocol.I32(packet, 0));
    }

    [Fact]
    public void FramesPacketsCarryingPressure()
    {
        var (parser, packets) = NewStreamingParser(hasPressure: true);

        parser.AddBytes(StreamPacket(-5000, 0x1FFD));

        var packet = Assert.Single(packets);
        Assert.Equal(6, packet.Length);
        Assert.Equal(-5000, Protocol.I32(packet, 0));
        Assert.Equal(0x1FFD, Protocol.U16(packet, 4));
    }

    [Fact]
    public void FramesBackToBackPackets()
    {
        var (parser, packets) = NewStreamingParser(hasPressure: false);

        for (var i = 0; i < 5; i++)
        {
            parser.AddBytes(StreamPacket(i, null));
        }

        Assert.Equal(5, packets.Count);
        Assert.Equal([0, 1, 2, 3, 4], packets.Select(p => Protocol.I32(p, 0)));
    }

    [Fact]
    public void FramesPacketsDeliveredOneByteAtATime()
    {
        var (parser, packets) = NewStreamingParser(hasPressure: false);

        foreach (var b in StreamPacket(77, null))
        {
            parser.AddBytes([b]);
        }

        Assert.Equal(77, Protocol.I32(Assert.Single(packets), 0));
    }

    [Fact]
    public void ResynchronisesAfterJoiningTheStreamMidPacket()
    {
        var (parser, packets) = NewStreamingParser(hasPressure: false);

        // Streaming starts with no header, so the first bytes read can be the end of a packet.
        parser.AddBytes(StreamPacket(1, null).AsSpan(3));
        parser.AddBytes(StreamPacket(2, null));
        parser.AddBytes(StreamPacket(3, null));

        // The parser discards the partial packet and frames the subsequent packets.
        Assert.Equal([2, 3], packets.Select(p => Protocol.I32(p, 0)));
    }

    [Fact]
    public void FramesPacketsWhoseDataContainsTheEndOfTextMarker()
    {
        var (parser, packets) = NewStreamingParser(hasPressure: false);

        // A flow value of 0x0003FF00 puts the end-of-text marker inside the packet's data.
        parser.AddBytes(StreamPacket(0x0003FF00, null));
        parser.AddBytes(StreamPacket(42, null));

        Assert.Equal([0x0003FF00, 42], packets.Select(p => Protocol.I32(p, 0)));
    }

    [Fact]
    public void ExitStreamModeRestoresResponseFraming()
    {
        var messages = new List<byte[]>();
        var parser = new RxParser(messages.Add);
        parser.EnterStreamMode(hasPressure: false, _ => Assert.Fail("Streaming has ended"));
        parser.AddBytes(StreamPacket(1, null).AsSpan(0, 3));

        parser.ExitStreamMode();
        Assert.False(parser.IsStreaming);

        parser.EnqueueExpected((ushort)((0x01 << 8) | 0x11));
        byte[] response = [0x01, 0x11, 0x02, 0x34, 0x12, 0x00];
        response[5] = Protocol.Crc8(response.AsSpan(0, 5));
        parser.AddBytes(response);

        Assert.Equal(response, Assert.Single(messages));
    }
}
