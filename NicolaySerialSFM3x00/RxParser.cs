using System;
using System.Collections.Concurrent;

namespace NicolaySerialSFM3x00;

/// <summary>
/// Frames the incoming byte stream from the device into complete messages.
/// </summary>
/// <remarks>
/// Non-streaming responses have no start or stop delimiter. The parser frames these responses
/// against a queue that the caller populates as it sends requests. The parser matches each response
/// by its address and function code and gets its length from the protocol's response-length table.
/// A response with bit 7 of its function code set is a five-byte exception frame, regardless of
/// the standard response length for that function code.
///
/// While streaming, the device sends headerless fixed-length packets that carry no address or
/// function code, so the parser switches to a separate mode that frames on packet length and the
/// trailing end-of-text marker instead.
/// </remarks>
internal class RxParser
{
    /// <summary>Marks the end of a streaming packet.</summary>
    private const byte StreamEtx = 0x03;

    /// <summary>Precedes <see cref="StreamEtx"/> at the end of a streaming packet.</summary>
    private const byte StreamEtxPrefix = 0xFF;

    /// <summary>Number of bytes in an exception frame: address, function code, count, code, CRC.</summary>
    private const byte ExceptionFrameLength = 5;

    private readonly ConcurrentQueue<ushort> _expected = new();

    private readonly byte[] _buffer = new byte[1024];
    private int _bufferIndex = 0;

    private bool _hasExpected = false;
    private byte _expectedByte0 = 0;
    private byte _expectedByte1 = 0;
    private byte _expectedLength = 0;

    private bool _isStreaming = false;
    private int _streamPacketLength = 0;
    private Action<byte[]>? _onStreamPacket;

    private readonly Action<byte[]> _onMessageComplete;

    public RxParser(Action<byte[]> onMessageComplete)
    {
        _onMessageComplete = onMessageComplete;
    }

    /// <summary>
    /// Whether the parser is currently framing streaming packets rather than responses.
    /// </summary>
    public bool IsStreaming => _isStreaming;

    /// <summary>
    /// Queues the response expected for a request, encoded as the address in the high byte and the
    /// function code in the low byte.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// The encoded function code has no fixed response length, so its response cannot be framed.
    /// </exception>
    public void EnqueueExpected(ushort command)
    {
        var functionCode = (byte)(command & 0xFF);
        if (Protocol.ResponseLength(functionCode) == 0)
        {
            throw new ArgumentException(
                $"Function code 0x{functionCode:X2} has no fixed response length", nameof(command));
        }

        _expected.Enqueue(command);
    }

    /// <summary>
    /// Discards all queued expectations and any partially received message. Call this method to
    /// resynchronize response framing after a request times out.
    /// </summary>
    public void Reset()
    {
        while (_expected.TryDequeue(out _))
        {
        }

        ClearCurrentMessage();
    }

    /// <summary>
    /// Switches the parser to framing streaming packets, which are delivered to
    /// <paramref name="onStreamPacket"/> as their data bytes without the end-of-text marker.
    /// </summary>
    /// <param name="hasPressure">
    /// Whether the device carries a pressure sensor, which adds two bytes to every packet.
    /// </param>
    /// <param name="onStreamPacket">Receives each complete packet's data bytes.</param>
    public void EnterStreamMode(bool hasPressure, Action<byte[]> onStreamPacket)
    {
        Reset();
        _onStreamPacket = onStreamPacket;
        _streamPacketLength = Protocol.StreamPacketLength(hasPressure);
        _isStreaming = true;
    }

    /// <summary>
    /// Returns the parser to framing responses and discards any partially received packet.
    /// </summary>
    public void ExitStreamMode()
    {
        _isStreaming = false;
        _onStreamPacket = null;
        _streamPacketLength = 0;
        Reset();
    }

    public void AddBytes(ReadOnlySpan<byte> bytes)
    {
        foreach (var b in bytes)
        {
            AddByte(b);
        }
    }

    private void AddByte(byte b)
    {
        if (_isStreaming)
        {
            AddStreamByte(b);
            return;
        }

        if (!_hasExpected && _expected.TryDequeue(out var cmd))
        {
            _hasExpected = true;
            _expectedByte0 = (byte)(cmd >> 8);
            _expectedByte1 = (byte)(cmd & 0xFF);
            _expectedLength = Protocol.ResponseLength(_expectedByte1);
        }

        if (!_hasExpected)
        {
            // Nothing was requested, so there is nothing this data can belong to.
            return;
        }

        // The first byte of a response is the address of the responding device.
        if (_bufferIndex == 0 && b != _expectedByte0)
        {
            return;
        }

        // The second byte is the requested function code. Bit 7 is set when the device reports an
        // exception, which shortens the frame.
        if (_bufferIndex == 1)
        {
            if (b == (byte)(_expectedByte1 | 0x80))
            {
                _expectedLength = ExceptionFrameLength;
            }
            else if (b != _expectedByte1)
            {
                // Start over and treat this byte as a possible start of the response.
                _bufferIndex = 0;
                AddByte(b);
                return;
            }
        }

        _buffer[_bufferIndex++] = b;

        if (_bufferIndex >= _expectedLength)
        {
            var message = _buffer[.._bufferIndex];
            ClearCurrentMessage();
            _onMessageComplete(message);
        }
    }

    private void AddStreamByte(byte b)
    {
        _buffer[_bufferIndex++] = b;

        if (_bufferIndex < _streamPacketLength) return;

        // A packet ends with the end-of-text marker, which can also occur in the packet data. If a
        // packet does not end with the marker, the stream is out of step. Discard the oldest byte
        // and try again as subsequent bytes arrive.
        if (_buffer[_bufferIndex - 2] == StreamEtxPrefix && _buffer[_bufferIndex - 1] == StreamEtx)
        {
            var packet = _buffer[..(_bufferIndex - 2)];
            _bufferIndex = 0;
            _onStreamPacket?.Invoke(packet);
        }
        else
        {
            Array.Copy(_buffer, 1, _buffer, 0, _bufferIndex - 1);
            _bufferIndex--;
        }
    }

    private void ClearCurrentMessage()
    {
        _bufferIndex = 0;
        _hasExpected = false;
        _expectedByte0 = 0;
        _expectedByte1 = 0;
        _expectedLength = 0;
    }
}
