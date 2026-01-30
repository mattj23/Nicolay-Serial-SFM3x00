using System;
using System.IO.Ports;

namespace NicolaySerialSFM3x00
{
    public class SfmDevice(string portName, byte address = 0x01) : IDisposable
    {
        private SerialPort _serialPort;
        private byte[] _rxBuffer = new byte[256];
        private int _rxBufferIndex = 0;
        private byte[] _testResponse = [0x00, 0x05, 0x02, 0x55, 0xAA, 0x7D]; 

        public void Connect()
        {
            _testResponse[0] = address;
            _serialPort = new SerialPort(portName, 115200, Parity.None, 8, StopBits.One);
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

        private void OnRx(object sender, SerialDataReceivedEventArgs e)
        {
            int bytesToRead = _serialPort.BytesToRead;
            _serialPort.Read(_rxBuffer, _rxBufferIndex, bytesToRead);
            _rxBufferIndex += bytesToRead;
            
            // Check if we have a complete message, if so process it

            // Process received data
            Console.WriteLine("Received Data: " + BitConverter.ToString(_rxBuffer[.._rxBufferIndex]));
        }
        
        private void SendCommand(byte command, byte[] data)
        {
            int length = 3 + data.Length + 1; // Command + Length + Data + CRC
            byte[] packet = new byte[length];
            packet[0] = address;
            packet[1] = command;
            packet[2] = (byte)data.Length;
            Array.Copy(data, 0, packet, 3, data.Length);
            packet[length - 1] = Crc8(packet.AsSpan(0, length - 1));
            
            Console.WriteLine(BitConverter.ToString(packet));

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