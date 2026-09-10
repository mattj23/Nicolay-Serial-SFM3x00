using Xunit;

namespace NicolaySerialSFM3x00.Tests;

public class CrcTests
{
    [Fact]
    public void Crc8_OfEmptyData_IsZero()
    {
        Assert.Equal(0x00, Protocol.Crc8([]));
    }

    /// <summary>
    /// In datasheet section 7.5, the host request [0x01, 0x05, 0x00] has the checksum 0x31.
    /// </summary>
    [Fact]
    public void Crc8_MatchesDatasheetTestCommandRequest()
    {
        Assert.Equal(0x31, Protocol.Crc8([0x01, 0x05, 0x00]));
    }

    /// <summary>
    /// In datasheet section 7.5, the response [0x01, 0x05, 0x02, 0x55, 0xAA] has the checksum 0x7D.
    /// </summary>
    [Fact]
    public void Crc8_MatchesDatasheetTestCommandResponse()
    {
        Assert.Equal(0x7D, Protocol.Crc8([0x01, 0x05, 0x02, 0x55, 0xAA]));
    }

    [Fact]
    public void HasValidCrc_AcceptsWellFormedFrame()
    {
        Assert.True(Protocol.HasValidCrc([0x01, 0x05, 0x02, 0x55, 0xAA, 0x7D]));
    }

    [Fact]
    public void HasValidCrc_RejectsCorruptedFrame()
    {
        Assert.False(Protocol.HasValidCrc([0x01, 0x05, 0x02, 0x55, 0xAB, 0x7D]));
        Assert.False(Protocol.HasValidCrc([0x01, 0x05, 0x02, 0x55, 0xAA, 0x7E]));
    }

    [Fact]
    public void HasValidCrc_RejectsUndersizedFrame()
    {
        Assert.False(Protocol.HasValidCrc([]));
        Assert.False(Protocol.HasValidCrc([0x00]));
    }
}

public class FrameBuilderTests
{
    [Fact]
    public void BuildFrame_WithoutData_MatchesDatasheetTestCommand()
    {
        var frame = Protocol.BuildFrame(0x01, 0x05, []);
        Assert.Equal(new byte[] { 0x01, 0x05, 0x00, 0x31 }, frame);
    }

    [Fact]
    public void BuildFrame_WithData_PlacesLengthAndPayload()
    {
        // In datasheet section 7.6, the Get Pressure Sensor Value request contains two zero data bytes.
        var frame = Protocol.BuildFrame(0x01, 0x06, [0x00, 0x00]);

        Assert.Equal(6, frame.Length);
        Assert.Equal(0x01, frame[0]);
        Assert.Equal(0x06, frame[1]);
        Assert.Equal(0x02, frame[2]);
        Assert.Equal(0x00, frame[3]);
        Assert.Equal(0x00, frame[4]);
        Assert.True(Protocol.HasValidCrc(frame));
    }

    [Fact]
    public void BuildFrame_UsesGivenAddress()
    {
        var frame = Protocol.BuildFrame(0x2A, 0x11, []);

        Assert.Equal(0x2A, frame[0]);
        Assert.True(Protocol.HasValidCrc(frame));
    }

    [Fact]
    public void BuildFrame_IsAlwaysAtLeastFourBytes()
    {
        // The datasheet specifies a minimum master transmission length of four bytes.
        Assert.Equal(4, Protocol.BuildFrame(0x01, 0x0E, []).Length);
    }
}

public class DecodeTests
{
    [Fact]
    public void U16_ReadsLowByteFirst()
    {
        Assert.Equal(0xAA55, Protocol.U16([0x55, 0xAA], 0));
    }

    [Fact]
    public void I16_SignExtends()
    {
        // Datasheet 7.6: 0xFF38 is -200 and 0x00C8 is +200.
        Assert.Equal(-200, Protocol.I16([0x38, 0xFF], 0));
        Assert.Equal(200, Protocol.I16([0xC8, 0x00], 0));
    }

    [Fact]
    public void U32_ReadsLowByteFirst()
    {
        Assert.Equal(0xDDCCBBAAu, Protocol.U32([0xAA, 0xBB, 0xCC, 0xDD], 0));
    }

    [Fact]
    public void I32_ReadsSignedSentinel()
    {
        // 0x7FFFFFFF marks an unreadable flow sensor.
        Assert.Equal(int.MaxValue, Protocol.I32([0xFF, 0xFF, 0xFF, 0x7F], 0));
        Assert.Equal(-1, Protocol.I32([0xFF, 0xFF, 0xFF, 0xFF], 0));
    }

    [Fact]
    public void Decoders_RespectOffset()
    {
        ReadOnlySpan<byte> payload = [0x00, 0x00, 0x34, 0x12];
        Assert.Equal(0x1234, Protocol.U16(payload, 2));
    }
}

public class ResponseLengthTests
{
    [Theory]
    [InlineData(0x01, 7)]
    [InlineData(0x05, 6)]
    [InlineData(0x06, 13)]
    [InlineData(0x09, 10)]
    [InlineData(0x11, 6)]
    [InlineData(0x14, 5)]
    public void ResponseLength_MatchesDatasheet(byte command, byte expected)
    {
        Assert.Equal(expected, Protocol.ResponseLength(command));
    }

    [Fact]
    public void ResponseLength_IsZeroForUnknownCommands()
    {
        Assert.Equal(0, Protocol.ResponseLength(0x7F));
    }
}
