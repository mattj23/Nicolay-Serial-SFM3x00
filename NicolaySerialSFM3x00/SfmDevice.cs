using System;
using System.IO.Ports;
using System.Threading.Tasks;

namespace NicolaySerialSFM3x00
{
    public class SfmDevice : IDisposable
    {
        private readonly string _portName;
        private readonly byte _address;
        
        private SerialPort _serialPort;
        private readonly byte[] _rxBuffer = new byte[4086];
        private TaskCompletionSource<int> _measurementTcs;

        private readonly RxParser _rxParser;

        public SfmDevice(string portName, byte address = 0x01)
        {
            _portName = portName;
            _address = address;
            _rxParser = new RxParser(OnParsed);
        }

        public void Connect()
        {
            _serialPort = new SerialPort(_portName, 115200, Parity.None, 8, StopBits.One);
            _serialPort.DataReceived += OnRx;
            _serialPort.Open();
        }

        /// <summary>
        /// Run the device's test (0x05) command and check the response
        /// </summary>
        public void Check()
        {
            SendCommand(0x05, Array.Empty<byte>());
        }

        public async Task<int> GetValue()
        {
            _measurementTcs = new TaskCompletionSource<int>();
            SendCommand(0x11, Array.Empty<byte>());
            return await _measurementTcs.Task;
        }

        private void OnRx(object sender, SerialDataReceivedEventArgs e)
        {
            int bytesToRead = _serialPort.BytesToRead;
            _serialPort.Read(_rxBuffer, 0, bytesToRead);
            _rxParser.AddBytes(_rxBuffer[..bytesToRead]);
        }

        private void OnParsed(byte[] message)
        {
            if (message.Length < 4) return;
            // Console.WriteLine("Received Data: " + BitConverter.ToString(message));
            
            if (message[1] == 0x11 && message.Length == 6)
            {
                // Measurement response
                var rawValue = (message[4] << 8) | message[3];
                _measurementTcs?.SetResult(rawValue);
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

        private byte Crc8(ReadOnlySpan<byte> data)
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

        public void Dispose()
        {
            _serialPort?.DataReceived -= OnRx;
            _serialPort?.Dispose();
        }
    }
}