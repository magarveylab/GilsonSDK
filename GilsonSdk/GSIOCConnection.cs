﻿using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GilsonSdk
{
    public class GSIOCConnection : IDisposable
    {
        #region Fields
        public const byte GSIOCLastBufferedCharacterBit = 1 << 7;
        private SerialPort _port;
        #endregion

        #region Properties

        /// <summary>
        /// Gets a value indicating whether the connection is open.
        /// </summary>
        /// <value>
        ///   <c>true</c> if this instance is open; otherwise, <c>false</c>.
        /// </value>
        public bool IsOpen => _port.IsOpen;

        /// <summary>
        /// Gets or sets a value indicating whether [RTS enabled].
        /// </summary>
        /// <value>
        ///   <c>true</c> if [RTS enabled]; otherwise, <c>false</c>.
        /// </value>
        public bool RtsEnabled
        {
            get => _port.RtsEnable;
            set => _port.RtsEnable = value;
        }

        /// <summary>
        /// Gets or sets a value indicating whether [DTR enabled].
        /// </summary>
        /// <value>
        ///   <c>true</c> if [DTR enabled]; otherwise, <c>false</c>.
        /// </value>
        public bool DtrEnabled
        {
            get => _port.DtrEnable;
            set => _port.DtrEnable = value;
        }
        #endregion

        #region Constructors
        /// <summary>
        /// Initializes a new instance of the <see cref="GSIOCConnection"/> class.
        /// </summary>
        /// <param name="port">The port.</param>
        public GSIOCConnection(SerialPort port)
        {
            _port = port;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="GSIOCConnection"/> class.
        /// </summary>
        /// <param name="portName">Name of the port.</param>
        /// <param name="baudRate">The baud rate.</param>
        public GSIOCConnection(string portName, int baudRate) : this (portName, baudRate, System.IO.Ports.Parity.Even, 8, System.IO.Ports.StopBits.One, true, true)
        {

        }
        /// <summary>
        /// Initializes a new instance of the <see cref="GilsonGSIOCDevice"/> class.
        /// </summary>
        /// <param name="portName">Name of the port.</param>
        /// <param name="baudRate">The baud rate.</param>
        /// <param name="parity">The parity.</param>
        /// <param name="dataBits">The data bits.</param>
        /// <param name="stopBits">The stop bits.</param>
        /// <param name="DtrEnable">if set to <c>true</c> [DTR enable].</param>
        /// <param name="RtsEnable">if set to <c>true</c> [RTS enable].</param>
        public GSIOCConnection(string portName, int baudRate, Parity parity, int dataBits, StopBits stopBits, bool DtrEnable = true, bool RtsEnable = true)
        {
            _port = new SerialPort(portName, baudRate, parity, dataBits, stopBits)
            {
                DtrEnable = DtrEnable,
                RtsEnable = RtsEnable,
            };
        }

        #endregion

        #region Methods
        /// <summary>
        /// Opens the connection
        /// </summary>
        public void Open() => _port.Open();

        /// <summary>
        /// Closes the connection
        /// </summary>
        public void Close()
        {
            if (_port != null)
            {
                if (_port.IsOpen)
                {
                    _port.Close();
                }
            }
        }

        /// <summary>
        /// Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
        /// </summary>
        public void Dispose() => Close();


        /// <summary>
        /// Ensures that the port is open.
        /// </summary>
        internal void EnsurePortOpen()
        {
            if (IsOpen == false)
                Open();
        }
        #endregion

        /// <summary>
        /// Sends disconnect message for all Gilson devices
        /// </summary>
        public async Task DisconnectDevicesAsync()
        {
            EnsurePortOpen();

            byte disconnect = 0xff;

            var writeOne = new byte[1];
            writeOne[0] = disconnect;

            _port.Write(writeOne, 0, 1);

            await Task.Delay(100);
        }

        /// <summary>
        /// Connects the specified device Id asynchronously
        /// </summary>
        /// <param name="deviceId">The device Id.</param>
        /// <returns></returns>
        public async Task<byte> ConnectAsync(byte deviceId)
        {
            EnsurePortOpen();

            var writeOneTwo = new byte[1];
            writeOneTwo[0] = (byte)(((deviceId) & 0x3F) | 0x80);

            _port.Write(writeOneTwo, 0, 1);

            await Task.Delay(50);

            if (_port.BytesToRead > 0)
            {
                var bytes = new byte[_port.BytesToRead];

                _port.Read(bytes, 0, _port.BytesToRead);

                byte firstChar = bytes[0];
                byte queryChar = 0xfe;

                if (firstChar.Equals(queryChar))
                {
                    _port.Write(writeOneTwo, 0, 1);

                    await Task.Delay(50);

                    if (_port.BytesToRead > 0)
                    {
                        var qbytes = new byte[_port.BytesToRead];

                        _port.Read(qbytes, 0, _port.BytesToRead);

                        byte idChar = qbytes[0];

                        if (idChar.Equals(writeOneTwo[0]))
                        {
                            return deviceId;
                        }
                    }
                }
                else if (firstChar.Equals(writeOneTwo[0]))
                {
                    return deviceId;
                }
            }

            return 0;
        }


        /// <summary>
        /// Executes an immediate instruction asynchronously
        /// </summary>
        /// <param name="command">The command.</param>
        /// <returns></returns>
        public async Task<(byte[] BinaryData, string StringValue)> ExecuteImmediateCommandAsync(char command)
        {
            EnsurePortOpen();

            var data = new List<byte>();

            var commandByte = Convert.ToByte(command);

            var writeBytes = new byte[1] { commandByte };
            var ackBytes = new byte[1] { 0x06 };

            _port.Write(writeBytes, 0, 1);

            var completed = false;

            while (completed == false)
            {
                await Task.Delay(20);

                if (_port.BytesToRead > 0)
                {
                    var bytes = new byte[_port.BytesToRead];

                    _port.Read(bytes, 0, _port.BytesToRead);

                    if (bytes.Length == 1)
                    {
                        var lastByte = bytes.Last();

                        if (lastByte > 127)
                        {
                            var rxCahrint = lastByte & ~GSIOCLastBufferedCharacterBit;

                            var rxChar = Convert.ToByte(rxCahrint);

                            data.Add(rxChar);

                            completed = true;

                            continue;
                        }
                    }

                    data.AddRange(bytes);


                    //send acknowledgement that the bytes have been recieved
                    _port.Write(ackBytes, 0, 1);
                }
                else
                {
                    var lastByte = data.Last();

                    if (lastByte > 127)
                    {
                        var rxCahrint = lastByte & ~GSIOCLastBufferedCharacterBit;

                        var rxChar = Convert.ToByte(rxCahrint);

                        data[data.Count - 1] = rxChar;
                    }

                    completed = true;
                }
            }

            var outputData = data.ToArray();
            var stringValue = string.Empty;

            if (outputData != null && outputData.Length > 0)
            {
                stringValue = _port.Encoding.GetString(outputData);
            }

            return (outputData, stringValue);

        }

        /// <summary>
        /// Executes a buffered command asynchronously
        /// </summary>
        /// <param name="command">The command to execute</param>
        /// <param name="parameters">The parameters.</param>
        public Task ExecuteBufferedCommandAsync(char command, string parameters = null) => ExecuteBufferedCommandAsync(Convert.ToByte(command), parameters);

        /// <summary>
        /// Executes a buffered command asynchronously
        /// </summary>
        /// <param name="command">The command to execute</param>
        /// <param name="parameters">The parameters.</param>
        public async Task ExecuteBufferedCommandAsync(byte command, string parameters = null)
        {
            EnsurePortOpen();

            var linebreak = new byte[1] { 0x0A };
            var carrigeReturn = new byte[1] { 0x0D };
            var commandBytes = new byte[1] { command };

            _port.Write(linebreak, 0, 1);

            await Task.Delay(10);

            if (_port.BytesToRead > 0)
            {
                var bytes = new byte[_port.BytesToRead];

                _port.Read(bytes, 0, _port.BytesToRead);

                await Task.Delay(20);

            }

            _port.Write(commandBytes, 0, 1);

            await Task.Delay(10);

            if (_port.BytesToRead > 0)
            {
                var bytes = new byte[_port.BytesToRead];

                _port.Read(bytes, 0, _port.BytesToRead);

                await Task.Delay(10);

            }

            if (!string.IsNullOrWhiteSpace(parameters))
            {
                var chars = parameters.ToCharArray();

                foreach (var aChar in chars)
                {
                    var paramBytes = new byte[1] { Convert.ToByte(aChar) };

                    _port.Write(paramBytes, 0, 1);

                    await Task.Delay(10);

                    if (_port.BytesToRead > 0)
                    {
                        var bytes = new byte[_port.BytesToRead];

                        _port.Read(bytes, 0, _port.BytesToRead);

                        await Task.Delay(10);

                    }
                }
            }


            _port.Write(carrigeReturn, 0, 1);

            await Task.Delay(10);

            if (_port.BytesToRead > 0)
            {
                var bytes = new byte[_port.BytesToRead];

                _port.Read(bytes, 0, _port.BytesToRead);

                await Task.Delay(10);

            }

            await Task.Delay(100);

        }

        /// <summary>
        /// Finds the first gilson device and returns its device id asynchronously
        /// </summary>
        /// <param name="startPos">The initial scan position between 0 and 63</param>
        /// <param name="scanProgressUpdater">Progress updater action for showing the scanning process</param>
        /// <returns></returns>
        public async Task<GSIOCDeviceInfo> FindFirstDeviceAsync(byte startPos = 0, Action<string> scanProgressUpdater = null)
        {
            EnsurePortOpen();

            for (byte idNumberOffset = startPos; idNumberOffset <= 64; idNumberOffset++)
            {
                byte tempId = (byte)((idNumberOffset) & 0x3F);

                scanProgressUpdater?.Invoke($"{tempId}");

                var result = await ConnectAsync(tempId);


                if (result > 0)
                {
                    await DisconnectDevicesAsync();

                    var moduleInfo = await GetModuleInfoAsync(result);

                    await DisconnectDevicesAsync();

                    return new GSIOCDeviceInfo(result, moduleInfo);
                }

                await Task.Delay(50);
            }

            return null;
        }

        /// <summary>
        /// Finds all GSIOC gilson devices asynchronously
        /// </summary>
        /// <param name="startPos">The initial scan position between 0 and 63</param>
        /// <param name="scanProgressUpdater">Progress updater action for showing the scanning process</param>
        /// <returns></returns>
        public async Task<List<GSIOCDeviceInfo>> FindAllDevicesAsync(byte startPos = 0, Action<string> scanProgressUpdater = null)
        {
            EnsurePortOpen();

            var devices = new List<GSIOCDeviceInfo>();

            for (byte idNumberOffset = startPos; idNumberOffset <= 64; idNumberOffset++)
            {
                byte tempId = (byte)((idNumberOffset) & 0x3F);

                scanProgressUpdater?.Invoke($"{tempId}");

                var result = await ConnectAsync(tempId);

                if (result > 0)
                {
                    var moduleInfo = await GetModuleInfoAsync(result);

                    await DisconnectDevicesAsync();

                    devices.Add(new GSIOCDeviceInfo(result, moduleInfo));
                }

                await Task.Delay(50);
            }

            await DisconnectDevicesAsync();

            return devices;
        }


        /// <summary>
        /// Gets the module information asynchronously
        /// </summary>
        /// <param name="deviceId">The device Id.</param>
        /// <returns></returns>
        public async Task<string> GetModuleInfoAsync(byte deviceId)
        {
            await ConnectAsync(deviceId);

            var result = await ExecuteImmediateCommandAsync('%');

            return result.StringValue;
        }

        /// <summary>
        /// Sends the master reset to the specified device asynchronously
        /// </summary>
        /// <param name="deviceId">The device Id.</param>
        /// <returns></returns>
        public async Task<string> ResetDeviceAsync(byte deviceId)
        {
            await ConnectAsync(deviceId);

            var result = await ExecuteImmediateCommandAsync('$');

            return result.StringValue;
        }
    }
}
