using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Management;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using System.Threading;

namespace dmxEasyDancing
{
    [SupportedOSPlatform("windows")]
    public class EnttecOpenDmx
    {
        public const int DMX_BUFFER_SIZE = 513;

        private static byte[] dmxBuffer = new byte[DMX_BUFFER_SIZE];
        private static IntPtr ftHandle = IntPtr.Zero;
        private static bool dmxThreadRunning = false;
        private static Thread? dmxThread;

        /// <summary>
        /// Controls whether debug information is printed during operations.
        /// </summary>
        public static bool EnableDebugLogging { get; set; } = true;

        /// <summary>
        /// Custom logger action. Defaults to Console.WriteLine.
        /// </summary>
        public static Action<string> Logger { get; set; } = Console.WriteLine;

        #region FTDI D2XX Driver Imports
        public enum FT_STATUS : uint
        {
            FT_OK = 0,
            FT_INVALID_HANDLE,
            FT_DEVICE_NOT_FOUND,
            FT_DEVICE_NOT_OPENED,
            FT_IO_ERROR,
            FT_INSUFFICIENT_RESOURCES,
            FT_INVALID_PARAMETER,
            FT_INVALID_BAUD_RATE,
            FT_DEVICE_NOT_OPENED_FOR_ERASE,
            FT_DEVICE_NOT_OPENED_FOR_WRITE,
            FT_FAILED_TO_WRITE_DEVICE,
            FT_EEPROM_READ_FAILED,
            FT_EEPROM_WRITE_FAILED,
            FT_EEPROM_ERASE_FAILED,
            FT_EEPROM_NOT_PRESENT,
            FT_EEPROM_NOT_PROGRAMMED,
            FT_INVALID_ARGS,
            FT_NOT_SUPPORTED,
            FT_OTHER_ERROR,
            FT_DEVICE_LIST_NOT_READY
        }

        public const byte BITS_8 = 8;
        public const byte STOP_BITS_2 = 2;
        public const byte PARITY_NONE = 0;
        public const ushort FLOW_NONE = 0;
        public const byte PURGE_RX = 1;
        public const byte PURGE_TX = 2;

        [DllImport("FTD2XX.dll")]
        private static extern FT_STATUS FT_CreateDeviceInfoList(ref uint numDevs);

        [DllImport("FTD2XX.dll")]
        private static extern FT_STATUS FT_GetDeviceInfoDetail(
            uint index,
            ref uint flags,
            ref uint type,
            ref uint id,
            ref uint locId,
            byte[] serialNumber,
            byte[] description,
            ref IntPtr handle);

        [DllImport("FTD2XX.dll")]
        private static extern FT_STATUS FT_Open(uint deviceNumber, ref IntPtr handle);

        [DllImport("FTD2XX.dll")]
        private static extern FT_STATUS FT_OpenEx(string pvArg1, uint dwFlags, ref IntPtr handle);

        [DllImport("FTD2XX.dll")]
        private static extern FT_STATUS FT_Close(IntPtr handle);

        [DllImport("FTD2XX.dll")]
        private static extern FT_STATUS FT_ResetDevice(IntPtr handle);

        [DllImport("FTD2XX.dll")]
        private static extern FT_STATUS FT_SetDivisor(IntPtr handle, char usDivisor);

        [DllImport("FTD2XX.dll")]
        private static extern FT_STATUS FT_SetDataCharacteristics(IntPtr handle, byte uWordLength, byte uStopBits, byte uParity);

        [DllImport("FTD2XX.dll")]
        private static extern FT_STATUS FT_SetFlowControl(IntPtr handle, char usFlowControl, byte uXon, byte uXoff);

        [DllImport("FTD2XX.dll")]
        private static extern FT_STATUS FT_ClrRts(IntPtr handle);

        [DllImport("FTD2XX.dll")]
        private static extern FT_STATUS FT_Purge(IntPtr handle, uint dwMask);

        [DllImport("FTD2XX.dll")]
        private static extern FT_STATUS FT_SetBreakOn(IntPtr handle);

        [DllImport("FTD2XX.dll")]
        private static extern FT_STATUS FT_SetBreakOff(IntPtr handle);

        [DllImport("FTD2XX.dll")]
        private static extern FT_STATUS FT_Write(IntPtr handle, IntPtr lpBuffer, uint dwBytesToWrite, ref uint lpdwBytesWritten);
        #endregion

        private static void Log(string message)
        {
            if (EnableDebugLogging && Logger != null)
            {
                Logger($"[EnttecOpenDmx DEBUG] {message}");
            }
        }

        public class DeviceInfo
        {
            public string Name { get; set; } = "";
            public string DeviceID { get; set; } = "";
            public string PNPDeviceID { get; set; } = "";
            public string Description { get; set; } = "";
            public string Caption { get; set; } = "";
            public string Status { get; set; } = "";
        }

        public static List<DeviceInfo> ListUsbDevices()
        {
            var devices = new List<DeviceInfo>();

            try
            {
                using var searcher = new ManagementObjectSearcher(
                    "SELECT DeviceID, PNPDeviceID, Name, Description, Caption, Status FROM Win32_PnPEntity " +
                    "WHERE DeviceID LIKE '%USB%' OR DeviceID LIKE '%FTDI%' OR Name LIKE '%FTDI%' OR Name LIKE '%Serial%' OR Name LIKE '%DMX%'");

                foreach (ManagementObject obj in searcher.Get())
                {
                    var dev = new DeviceInfo
                    {
                        Name = obj["Name"]?.ToString() ?? "Unknown",
                        DeviceID = obj["DeviceID"]?.ToString() ?? "",
                        PNPDeviceID = obj["PNPDeviceID"]?.ToString() ?? "",
                        Description = obj["Description"]?.ToString() ?? "",
                        Caption = obj["Caption"]?.ToString() ?? "",
                        Status = obj["Status"]?.ToString() ?? "Unknown"
                    };
                    devices.Add(dev);
                }
            }
            catch (Exception ex)
            {
                Log($"Error querying Win32_PnPEntity via WMI: {ex.Message}");
            }

            return devices;
        }

        private static void EnumerateD2xxDevices()
        {
            try
            {
                uint numDevs = 0;
                FT_STATUS status = FT_CreateDeviceInfoList(ref numDevs);
                Log($"FTDI D2XX Driver reports {numDevs} connected device(s) (Status: {status}):");
                for (uint i = 0; i < numDevs; i++)
                {
                    uint flags = 0, type = 0, id = 0, locId = 0;
                    byte[] serial = new byte[16];
                    byte[] desc = new byte[64];
                    IntPtr dummyHandle = IntPtr.Zero;
                    FT_STATUS detailStatus = FT_GetDeviceInfoDetail(i, ref flags, ref type, ref id, ref locId, serial, desc, ref dummyHandle);
                    string sn = System.Text.Encoding.ASCII.GetString(serial).TrimEnd('\0');
                    string description = System.Text.Encoding.ASCII.GetString(desc).TrimEnd('\0');
                    Log($"  D2XX Device [{i}]: Serial='{sn}', Desc='{description}', ID=0x{id:X}, Status={detailStatus}");
                }
            }
            catch (Exception ex)
            {
                Log($"Error enumerating D2XX devices: {ex.Message}");
            }
        }

        public static bool Start()
        {
            Log("==================================================");
            Log("Starting EnttecOpenDmx initialization using native FTDI D2XX driver...");

            // 1. Log WMI USB Devices
            var devices = ListUsbDevices();
            Log($"Found {devices.Count} USB/Serial device(s) in Windows PnP:");
            foreach (var d in devices)
            {
                Log($"  [PnP] Name: \"{d.Name}\" | ID: {d.DeviceID}");
            }

            // 2. Log FTDI D2XX Devices
            EnumerateD2xxDevices();

            // 3. Open FTDI Device
            Log("Attempting to open FTDI device via D2XX FT_Open(0)...");
            FT_STATUS status = FT_Open(0, ref ftHandle);
            if (status != FT_STATUS.FT_OK || ftHandle == IntPtr.Zero)
            {
                Log($"FT_Open(0) failed with status: {status}. Trying FT_OpenEx by description...");
                status = FT_OpenEx("USB Serial Converter", 2 /* OPEN_BY_DESCRIPTION */, ref ftHandle);
            }

            if (status != FT_STATUS.FT_OK || ftHandle == IntPtr.Zero)
            {
                Log($"Failed to open FTDI D2XX device. Final status: {status}");
                Log("==================================================");
                return false;
            }

            Log($"Successfully opened FTDI D2XX device! Handle: 0x{ftHandle.ToInt64():X}");

            // 4. Initialise OpenDMX FTDI chip configuration
            Log("Configuring FTDI chip for DMX512 (250,000 baud, 8N2, no flow control)...");
            status = FT_ResetDevice(ftHandle);
            Log($"  FT_ResetDevice: {status}");

            // Baud rate 250,000: FTDI divisor is 12 (3,000,000 / 250,000 = 12)
            status = FT_SetDivisor(ftHandle, (char)12);
            Log($"  FT_SetDivisor(12) [250 kBaud]: {status}");

            status = FT_SetDataCharacteristics(ftHandle, BITS_8, STOP_BITS_2, PARITY_NONE);
            Log($"  FT_SetDataCharacteristics(8, Stop2, ParityNone): {status}");

            status = FT_SetFlowControl(ftHandle, (char)FLOW_NONE, 0, 0);
            Log($"  FT_SetFlowControl(FLOW_NONE): {status}");

            status = FT_ClrRts(ftHandle);
            Log($"  FT_ClrRts: {status}");

            status = FT_Purge(ftHandle, PURGE_TX);
            status = FT_Purge(ftHandle, PURGE_RX);
            Log("  FT_Purge(TX/RX) complete.");

            // 5. Start continuous DMX transmission thread
            Log("Starting continuous DMX output thread...");
            dmxThread = new Thread(DmxThread)
            {
                IsBackground = true,
                Name = "EnttecDmxSender"
            };
            dmxThreadRunning = true;
            dmxThread.Start();

            Log("Enttec OpenDMX transmission thread active (~40 Hz).");
            Log("EnttecOpenDmx started successfully.");
            Log("==================================================");
            return true;
        }

        public static void Stop()
        {
            Log("Stopping EnttecOpenDmx...");
            dmxThreadRunning = false;
            if (dmxThread != null && dmxThread.IsAlive)
            {
                dmxThread.Join();
                Log("DMX thread joined.");
            }

            if (ftHandle != IntPtr.Zero)
            {
                FT_Close(ftHandle);
                ftHandle = IntPtr.Zero;
                Log("FTDI D2XX device handle closed.");
            }
            Log("EnttecOpenDmx stopped.");
        }

        public static void SetDmxValue(int channel, byte value)
        {
            if (channel > 0 && channel < DMX_BUFFER_SIZE)
            {
                dmxBuffer[channel] = value;
            }
        }

        private static void DmxThread()
        {
            IntPtr unmanagedBuffer = Marshal.AllocHGlobal(DMX_BUFFER_SIZE);
            try
            {
                while (dmxThreadRunning)
                {
                    if (ftHandle != IntPtr.Zero)
                    {
                        // 1. Send DMX Break & MAB
                        FT_SetBreakOn(ftHandle);
                        FT_SetBreakOff(ftHandle);

                        // 2. Start Code = 0
                        dmxBuffer[0] = 0;

                        // 3. Write DMX Packet
                        Marshal.Copy(dmxBuffer, 0, unmanagedBuffer, DMX_BUFFER_SIZE);
                        uint bytesWritten = 0;
                        FT_Write(ftHandle, unmanagedBuffer, (uint)DMX_BUFFER_SIZE, ref bytesWritten);
                    }
                    Thread.Sleep(25); // ~40Hz refresh rate
                }
            }
            finally
            {
                Marshal.FreeHGlobal(unmanagedBuffer);
            }
        }
    }
}
