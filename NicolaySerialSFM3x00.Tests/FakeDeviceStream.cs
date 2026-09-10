using System.Collections.Concurrent;
using System.Threading.Channels;

namespace NicolaySerialSFM3x00.Tests;

/// <summary>
/// Simulates a serial port by responding to frames as a device would. The stream passes each
/// request to a responder, which returns the response bytes or null to leave the request
/// unanswered.
/// </summary>
internal sealed class FakeDeviceStream : Stream
{
    private readonly Func<byte[], byte[]?> _responder;
    private readonly Channel<byte> _toHost = Channel.CreateUnbounded<byte>();
    private readonly ConcurrentQueue<byte[]> _requests = new();

    public FakeDeviceStream(Func<byte[], byte[]?> responder)
    {
        _responder = responder;
    }

    /// <summary>All frames that the host has written, in order.</summary>
    public IReadOnlyCollection<byte[]> Requests => _requests;

    /// <summary>Sends bytes to the host without being asked, as the device does while streaming.</summary>
    public void PushToHost(ReadOnlySpan<byte> bytes)
    {
        foreach (var b in bytes)
        {
            _toHost.Writer.TryWrite(b);
        }
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        var request = buffer.AsSpan(offset, count).ToArray();
        _requests.Enqueue(request);

        var response = _responder(request);
        if (response != null)
        {
            PushToHost(response);
        }
    }

    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count,
        CancellationToken cancellationToken)
    {
        // Wait until the device has data, and then read all available bytes.
        var first = await _toHost.Reader.ReadAsync(cancellationToken);
        buffer[offset] = first;

        var read = 1;
        while (read < count && _toHost.Reader.TryRead(out var next))
        {
            buffer[offset + read] = next;
            read++;
        }

        return read;
    }

    public override int Read(byte[] buffer, int offset, int count) =>
        ReadAsync(buffer, offset, count, CancellationToken.None).GetAwaiter().GetResult();

    public override bool CanRead => true;
    public override bool CanWrite => true;
    public override bool CanSeek => false;
    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
}
