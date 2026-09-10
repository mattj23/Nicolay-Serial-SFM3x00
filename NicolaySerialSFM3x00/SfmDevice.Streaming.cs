using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace NicolaySerialSFM3x00
{
    public partial class SfmDevice
    {
        /// <summary>
        /// The delay, in milliseconds, that lets the final packets arrive after the stop command
        /// before the parser resumes framing responses.
        /// </summary>
        private const int StreamStopDrainMs = 50;

        private Channel<StreamSample>? _streamChannel;

        /// <summary>Whether the device is currently streaming measurements.</summary>
        public bool IsStreaming => _rxParser.IsStreaming;

        /// <summary>
        /// Starts the device's continuous measurement stream (0x1E) and yields each packet as it
        /// arrives. Streaming is considerably faster than polling for individual measurements.
        /// </summary>
        /// <remarks>
        /// The device stops streaming as soon as it receives any byte, so no other command can be
        /// sent while the stream is running; calls to other methods wait until it ends. Ending the
        /// loop, canceling <paramref name="cancellationToken"/>, or disposing the enumerator stops
        /// the stream and returns the device to processing requests.
        ///
        /// Packets are buffered until they are read, so a consumer that cannot keep up will cause
        /// the buffer to grow. Streaming carries no checksum, so packets are framed on their length
        /// and terminator alone; the datasheet recommends it only for RS232 or onboard USB.
        /// </remarks>
        public async IAsyncEnumerable<StreamSample> StreamAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            if (!IsConnected)
            {
                throw new InvalidOperationException("Device is not connected");
            }

            // Packets are longer when a pressure sensor is installed. Determining whether the
            // sensor is present requires a request, so query it before the stream starts.
            var info = await GetPressureSensorInfoAsync(false, null, cancellationToken).ConfigureAwait(false);
            var hasPressure = info.IsPresent;

            await _requestLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var channel = Channel.CreateUnbounded<StreamSample>(
                    new UnboundedChannelOptions { SingleWriter = true, SingleReader = true });
                _streamChannel = channel;

                _rxParser.EnterStreamMode(hasPressure,
                    packet => channel.Writer.TryWrite(DecodeStreamPacket(packet, hasPressure)));

                var frame = Protocol.BuildFrame(_address, 0x1E, []);
                _stream!.Write(frame, 0, frame.Length);

                using var linked =
                    CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _cts.Token);

                while (await channel.Reader.WaitToReadAsync(linked.Token).ConfigureAwait(false))
                {
                    while (channel.Reader.TryRead(out var sample))
                    {
                        yield return sample;
                    }
                }
            }
            finally
            {
                _streamChannel = null;
                await StopStreamingAsync().ConfigureAwait(false);
                _requestLock.Release();
            }
        }

        private static StreamSample DecodeStreamPacket(byte[] packet, bool hasPressure) =>
            new(Protocol.I32(packet, 0), hasPressure ? Protocol.U16(packet, 4) : null);

        private async Task StopStreamingAsync()
        {
            try
            {
                // Any byte stops the device streaming. A single byte cannot form a complete frame,
                // so the device discards it once the gap between bytes exceeds its frame timeout.
                _stream?.Write([0x00], 0, 1);
            }
            catch (Exception)
            {
                // The port can already be unavailable, in which case there is nothing to stop.
            }

            // Let packets already on the wire arrive and be discarded as a partial packet. This
            // prevents the parser from treating them as the start of the next response.
            await Task.Delay(StreamStopDrainMs).ConfigureAwait(false);
            _rxParser.ExitStreamMode();
        }
    }
}
