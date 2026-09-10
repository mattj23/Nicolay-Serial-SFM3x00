using System;
using System.IO;
using System.IO.Ports;
using System.Threading;
using System.Threading.Tasks;

namespace NicolaySerialSFM3x00
{
    /// <summary>
    /// Communicates with a Nicolay flow meter through a serial port.
    /// </summary>
    /// <remarks>
    /// The device processes one request at a time, so requests are serialized. A request that goes
    /// unanswered within its timeout resynchronizes the parser and throws
    /// <see cref="SfmTimeoutException"/> rather than waiting indefinitely.
    /// </remarks>
    public partial class SfmDevice : IDisposable, IAsyncDisposable
    {
        /// <summary>The baud rate the device uses after power on.</summary>
        public const int DefaultBaudRate = 115200;

        private readonly string? _portName;
        private readonly byte _address;
        private readonly RxParser _rxParser;

        private readonly CancellationTokenSource _cts = new();

        // The device serves one request at a time, so one pending-request slot is sufficient.
        private readonly SemaphoreSlim _requestLock = new(1, 1);
        private TaskCompletionSource<byte[]>? _pending;
        private byte _pendingCommand;

        private SerialPort? _serialPort;
        private Stream? _stream;
        private Task? _readTask;

        public SfmDevice(string portName, byte address = 0x01)
        {
            _portName = portName;
            _address = address;
            _rxParser = new RxParser(OnParsed);
        }

        /// <summary>
        /// Creates a device that communicates through an open stream for tests that do not use
        /// hardware.
        /// </summary>
        internal SfmDevice(Stream stream, byte address = 0x01)
        {
            _portName = null;
            _address = address;
            _stream = stream;
            _rxParser = new RxParser(OnParsed);
        }

        /// <summary>
        /// The time to wait for a response, in milliseconds. Individual requests can override
        /// this value.
        /// </summary>
        public int DefaultTimeoutMs { get; set; } = 250;

        /// <summary>The device address on the bus.</summary>
        public byte Address => _address;

        /// <summary>Whether the port is open and the read loop is running.</summary>
        public bool IsConnected => _readTask != null;

        /// <summary>
        /// Raised when the read loop stops because of an unexpected error, such as the port being
        /// removed. Any request in flight fails with the same error.
        /// </summary>
        public event EventHandler<Exception>? ReadFault;

        /// <summary>
        /// Opens the configured serial port, if necessary, and begins reading from the device.
        /// </summary>
        public Task Connect()
        {
            if (IsConnected)
            {
                throw new InvalidOperationException("Device is already connected");
            }

            if (_portName != null)
            {
                _serialPort = new SerialPort(_portName, DefaultBaudRate, Parity.None, 8, StopBits.One);
                _serialPort.Open();
                _stream = _serialPort.BaseStream;
            }

            _readTask = Task.Factory
                .StartNew(ReadLoop, TaskCreationOptions.LongRunning)
                .Unwrap();

            return Task.CompletedTask;
        }

        /// <summary>
        /// Runs the device's test command (0x05) and checks whether it returns the expected pattern.
        /// </summary>
        public async Task<bool> Check(int? timeoutMs = null, CancellationToken cancellationToken = default)
        {
            var payload = await ExecuteAsync(0x05, [], timeoutMs, cancellationToken).ConfigureAwait(false);
            return payload.Length == 2 && payload[0] == 0x55 && payload[1] == 0xAA;
        }

        /// <summary>
        /// Sends a request and waits for the device's response. Returns the response data bytes
        /// without the address, function code, count, or CRC.
        /// </summary>
        /// <exception cref="SfmDeviceException">The device rejected the request.</exception>
        /// <exception cref="SfmTimeoutException">The device did not respond in time.</exception>
        /// <exception cref="SfmCrcException">The response failed its checksum.</exception>
        private async Task<byte[]> ExecuteAsync(byte command, byte[] data, int? timeoutMs,
            CancellationToken cancellationToken)
        {
            if (!IsConnected)
            {
                throw new InvalidOperationException("Device is not connected");
            }

            var timeout = timeoutMs ?? DefaultTimeoutMs;

            await _requestLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var pending = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
                _pendingCommand = command;
                _pending = pending;

                var frame = Protocol.BuildFrame(_address, command, data);
                _rxParser.EnqueueExpected((ushort)((_address << 8) | command));
                _stream!.Write(frame, 0, frame.Length);

                using var timeoutCts =
                    CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _cts.Token);
                var delay = Task.Delay(timeout, timeoutCts.Token);

                if (await Task.WhenAny(pending.Task, delay).ConfigureAwait(false) == pending.Task)
                {
                    timeoutCts.Cancel(); // Stop the timer early.
                    return await pending.Task.ConfigureAwait(false);
                }

                cancellationToken.ThrowIfCancellationRequested();

                // The response did not arrive. Abandon all data that is still in flight so the
                // parser can frame the next response correctly.
                _rxParser.Reset();

                if (_cts.IsCancellationRequested)
                {
                    throw new SfmException("The device was disconnected while awaiting a response");
                }

                throw new SfmTimeoutException(command, timeout);
            }
            finally
            {
                _pending = null;
                _requestLock.Release();
            }
        }

        private void OnParsed(byte[] message)
        {
            var pending = _pending;
            if (pending == null) return;
            if (message.Length < 4 || message[0] != _address) return;

            if (!Protocol.HasValidCrc(message))
            {
                pending.TrySetException(new SfmCrcException(_pendingCommand));
                return;
            }

            // An exception frame sets the function code's high bit and contains the reason that
            // the device refused the request.
            if (message[1] == (byte)(_pendingCommand | 0x80))
            {
                pending.TrySetException(new SfmDeviceException(_pendingCommand, message[3]));
                return;
            }

            if (message[1] != _pendingCommand) return;

            var payload = new byte[message.Length - 4];
            Array.Copy(message, 3, payload, 0, payload.Length);
            pending.TrySetResult(payload);
        }

        private async Task ReadLoop()
        {
            try
            {
                var buffer = new byte[1024];
                while (!_cts.Token.IsCancellationRequested)
                {
                    var bytesRead = await _stream!.ReadAsync(buffer, 0, buffer.Length, _cts.Token)
                        .ConfigureAwait(false);
                    if (bytesRead > 0)
                    {
                        _rxParser.AddBytes(buffer.AsSpan(0, bytesRead));
                    }
                }
            }
            catch (Exception e) when (e is OperationCanceledException or ObjectDisposedException
                                          or IOException && _cts.IsCancellationRequested)
            {
                // The read operation stopped during shutdown, so do not report a fault.
                _streamChannel?.Writer.TryComplete();
            }
            catch (Exception e)
            {
                _pending?.TrySetException(e);
                _streamChannel?.Writer.TryComplete(e);
                ReadFault?.Invoke(this, e);
            }
        }

        private async Task StopReading()
        {
            if (_readTask == null) return;

            _cts.Cancel();

            // Closing the port unblocks a read that the cancellation token could not interrupt.
            _serialPort?.Close();

            try
            {
                await _readTask.ConfigureAwait(false);
            }
            catch (Exception)
            {
                // The read loop reports faults through ReadFault.
            }

            _readTask = null;
        }

        public void Dispose()
        {
            _cts.Cancel();
            _pending?.TrySetException(new SfmException("The device was disposed"));
            _serialPort?.Close();
            _serialPort?.Dispose();
            _serialPort = null;
            _stream = null;
            _readTask = null;
            _cts.Dispose();
            _requestLock.Dispose();
        }

        public async ValueTask DisposeAsync()
        {
            _pending?.TrySetException(new SfmException("The device was disposed"));
            await StopReading().ConfigureAwait(false);
            _serialPort?.Dispose();
            _serialPort = null;
            _stream = null;
            _cts.Dispose();
            _requestLock.Dispose();
        }
    }
}
