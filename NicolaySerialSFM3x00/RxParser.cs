using System;
using System.Collections.Concurrent;
using System.IO;

namespace NicolaySerialSFM3x00;

public class RxParser
{
    private static readonly (byte, byte)[] ResponseLengths =
    [
        (0x01, 7),
        (0x02, 6),
        (0x05, 6),
        (0x06, 13),
        (0x07, 6),
        (0x09, 10),
        (0x0A, 8),
        (0x0C, 4),
        (0x0D, 4),
        (0x0E, 4),
        (0x0F, 8),
        (0x10, 8),
        (0x11, 6),
        (0x12, 6),
        (0x13, 6),
        (0x14, 5),
        (0x15, 5),
        (0x18, 6),
        (0x19, 6),
        (0x1B, 6),
        (0x1C, 6),
        (0x1D, 0), // Bulk read, variable response 
        (0x1E, 0) // Stream send, streaming responses ending with 0xFF, 0x03
    ];
    
    private readonly ConcurrentQueue<ushort> _expected = new();

    private readonly byte[] _buffer = new byte[1024];
    private int _bufferIndex = 0;
    
    private readonly byte[] _responseLengths = new byte[256];
    
    private byte _expectedByte0 = 0;
    private byte _expectedByte1 = 0;
    private byte _expectedLength = 0;
    
    private readonly Action<byte[]> _onMessageComplete;
    

    public RxParser(Action<byte[]> onMessageComplete)
    {
        _onMessageComplete = onMessageComplete;
        // Quick build of response lengths map
        foreach (var (cmd, len) in ResponseLengths)
        {
            _responseLengths[cmd] = len;
        }
        
    }
    
    public void EnqueueExpected(ushort command)
    {
        _expected.Enqueue(command);
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
        
        if (_expectedByte1 == 0 && _expected.TryDequeue(out var cmd))
        {
            _expectedByte0 = (byte)(cmd >> 8);
            _expectedByte1 = (byte)(cmd & 0xFF);
            _expectedLength = _responseLengths[_expectedByte1];
        }

        if (_expectedByte1 == 0)
        {
            // We're just going to throw away whatever data we're picking up
            return;
        }
        
        // We're going to make sure that the first two bytes of the buffer are the expected command
        if (_bufferIndex == 0)
        {
            // This is the first byte, it should match the first byte of the expected command
            if (b != _expectedByte0)
            {
                return;
            }
        }

        if (_bufferIndex == 1)
        {
            // This is the second byte, it should match the second byte of the expected command
            if (b != _expectedByte1)
            {
                // Reset buffer index to 0 to start over
                _bufferIndex = 0;
                return;
            }
        }

        // Add byte to buffer
        _buffer[_bufferIndex++] = b;
        
        // Check if we have a complete message
        if (_expectedLength > 0 && _bufferIndex >= _expectedLength)
        {
            _onMessageComplete(_buffer[.._bufferIndex]);
            _bufferIndex = 0;
            _expectedByte0 = 0;
            _expectedByte1 = 0;
            _expectedLength = 0;
        }
    }
}