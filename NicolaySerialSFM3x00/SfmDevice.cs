using System;
using System.IO.Ports;
using System.Threading;
using System.Threading.Tasks;

namespace NicolaySerialSFM3x00
{
    public class SfmDevice : IDisposable, IAsyncDisposable
    {
        private readonly string _portName;
        private readonly byte _address;
        
        private SerialPort _serialPort;
        private readonly RxParser _rxParser;
        
        // Signals and tasks for starting/stopping the serial communication
        private readonly CancellationTokenSource _cts = new();
        private TaskCompletionSource<bool> _stopReadTcs;
        private TaskCompletionSource<bool> _startTcs;

        // Task completion sources for response requests
        private TaskCompletionSource<int> _measurementTcs;
        private TaskCompletionSource<bool> _checkTcs;

        public SfmDevice(string portName, byte address = 0x01)
        {
            _portName = portName;
            _address = address;
            _rxParser = new RxParser(OnParsed);
        }

        public Task Connect()
        {
            if (_startTcs != null)
            {
                throw new InvalidOperationException("Device is already connected");
            }
            _startTcs = new TaskCompletionSource<bool>();
            
            _serialPort = new SerialPort(_portName, 115200, Parity.None, 8, StopBits.One);
            _serialPort.Open();
            
            // Create long-running background task to read data
            Task.Factory.StartNew(ReadTask, TaskCreationOptions.LongRunning);
            return _startTcs.Task;
        }
        
        /// <summary>
        /// Run the device's test (0x05) command and check the response
        /// </summary>
        public Task<bool> Check()
        {
            _checkTcs = new TaskCompletionSource<bool>();
            SendCommand(0x05, Array.Empty<byte>());
            return _checkTcs.Task;
        }

        public async Task<int> GetValue()
        {
            _measurementTcs = new TaskCompletionSource<int>();
            SendCommand(0x11, Array.Empty<byte>());
            return await _measurementTcs.Task;
        }

        private void OnParsed(byte[] message)
        {
            if (message[0] != _address) return; // Not for us
            
            if (message.Length < 4) return;
            // Console.WriteLine("Received Data: " + BitConverter.ToString(message));
            
            if (message[1] == 0x11 && message.Length == 6)
            {
                // Measurement response
                var rawValue = (message[4] << 8) | message[3];
                _measurementTcs?.SetResult(rawValue);
                return;
            }
            
            if (message[1] == 0x05)
            {
                if (message.Length != 6) 
                {
                    _checkTcs?.SetResult(false);
                }
                else
                {
                    _checkTcs?.SetResult(message[2] == 0x02 && message[3] == 0x55 && message[4] == 0xAA && message[5] == 0x7D);
                }
                return;
            }
            
        }
        
        private void SendCommand(byte command, byte[] data)
        {
            int length = 3 + data.Length + 1; // Command + Length + Data + CRC
            byte[] packet = new byte[length];
            packet[0] = _address;
            packet[1] = command;
            packet[2] = (byte)data.Length;
            Array.Copy(data, 0, packet, 3, data.Length);
            packet[length - 1] = Crc8(packet.AsSpan(0, length - 1));
            
            var code = (ushort)((_address << 8) | command);
            _rxParser.EnqueueExpected(code);
            
            // Console.WriteLine(BitConverter.ToString(packet));

            _serialPort.Write(packet, 0, length);
        }
        
        private Task StopReading()
        {
            if (_stopReadTcs == null) return Task.CompletedTask;
            _cts.Cancel();
            return _stopReadTcs.Task;
        }

        private async void ReadTask()
        {
            _stopReadTcs = new TaskCompletionSource<bool>();
            _startTcs?.TrySetResult(true);
            
            try
            {
                var buffer = new byte[1024];
                while (!_cts.Token.IsCancellationRequested)
                {
                    var bytesRead = await _serialPort.BaseStream.ReadAsync(buffer, _cts.Token);
                    if (bytesRead > 0)
                    {
                        _rxParser.AddBytes(buffer.AsSpan(0, bytesRead));
                    }
                }
            }
            catch (Exception e)
            {
                throw; // TODO handle exception
            }
            finally
            {
                _stopReadTcs.SetResult(true);
                _stopReadTcs = null;
            }
        }
        
        private static byte Crc8(ReadOnlySpan<byte> data)
        {
            byte crc = 0x00;
            foreach (var b in data)
            {
                crc ^= b;
                for (int i = 0; i < 8; i++)
                {
                    if ((crc & 0x80) != 0)
                        crc = (byte)((crc << 1) ^ 0x31);
                    else
                        crc <<= 1;
                }
            }
            return crc;
        }


        public async void Dispose()
        {
            await StopReading();
            _serialPort?.Close();
            _serialPort?.Dispose();
        }
        
        public async ValueTask DisposeAsync()
        {
            await StopReading();
            _serialPort?.Close();
            _serialPort?.Dispose();
        }
        
    }
}