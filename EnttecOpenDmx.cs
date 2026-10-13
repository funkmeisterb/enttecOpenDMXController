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
        private static IntPtr handle = IntPtr.Zero;
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

        #region Unmanaged Dll Imports
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr CreateFile(
            string lpFileName,
            uint dwDesiredAccess,
            uint dwShareMode,
            IntPtr lpSecurityAttributes,
            uint dwCreationDisposition,
            uint dwFlagsAndAttributes,
            IntPtr hTemplateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetCommState(IntPtr hFile, ref DCB lpDCB);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetCommState(IntPtr hFile, ref DCB lpDCB);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool EscapeCommFunction(IntPtr hFile, uint dwFunc);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool WriteFile(
            IntPtr hFile,
            byte[] lpBuffer,
            uint nNumberOfBytesToWrite,
            out uint lpNumberOfBytesWritten,
            IntPtr lpOverlapped);

        [StructLayout(LayoutKind.Sequential)]
        private struct DCB
        {
            public uint DCBlength;
            public uint BaudRate;
            public uint fBinary;
            public uint fParity;
            public uint fOutxCtsFlow;
            public uint fOutxDsrFlow;
            public uint fDtrControl;
            public uint fDsrSensitivity;
            public uint fTXContinueOnXoff;
            public uint fOutX;
            public uint fInX;
            public uint fErrorChar;
            public uint fNull;
            public uint fRtsControl;
            public uint fAbortOnError;
            public uint fDummy2;
            public ushort wReserved;
            public ushort XonLim;
            public ushort XoffLim;
            public byte ByteSize;
            public byte Parity;
            public byte StopBits;
            public char XonChar;
            public char XoffChar;
            public char ErrorChar;
            public char EofChar;
            public char EvtChar;
            public ushort wReserved1;
        }
        #endregion

        private static List<string> BuildCandidatePaths(List<DeviceInfo> devices)
        {
            var candidatePaths = new List<string>();

            // 1. Look for FTDI bus devices and extracted COM ports
            foreach (var d in devices)
            {
                // If it's an FTDIBUS device, try direct bus path \\.\<DeviceID>
                if (d.DeviceID.StartsWith(@"FTDIBUS\", StringComparison.OrdinalIgnoreCase))
                {
                    string ftdiBusPath = @"\\.\" + d.DeviceID;
                    if (!candidatePaths.Contains(ftdiBusPath))
                    {
                        candidatePaths.Add(ftdiBusPath);
                    }
                }

                // Check for COM port in Name/Caption (e.g., "USB Serial Port (COM3)")
                var comMatch = Regex.Match(d.Name, @"\((COM\d+)\)", RegexOptions.IgnoreCase);
                if (comMatch.Success)
                {
                    string comPath = @"\\.\" + comMatch.Groups[1].Value.ToUpperInvariant();
                    if (!candidatePaths.Contains(comPath))
                    {
                        candidatePaths.Add(comPath);
                    }
                }
            }

            // 2. Known legacy and generic fallbacks
            string legacyOpenDmx = @"\\.\ENTTEC OPEN DMX USB";
            string legacySpecific = @"\\.\FTDIBUS\VID_0403+PID_6001+A50285BI\0000";
            if (!candidatePaths.Contains(legacyOpenDmx)) candidatePaths.Add(legacyOpenDmx);
            if (!candidatePaths.Contains(legacySpecific)) candidatePaths.Add(legacySpecific);

            return candidatePaths;
        }

        public static bool Start()
        {
            Log("==================================================");
            Log("Starting EnttecOpenDmx initialization...");
            Log("Enumerating connected USB and FTDI/Serial devices via WMI...");

            var devices = ListUsbDevices();
            if (devices.Count == 0)
            {
                Log("No matching USB or serial devices detected via WMI.");
            }
            else
            {
                Log($"Found {devices.Count} relevant device(s):");
                for (int i = 0; i < devices.Count; i++)
                {
                    var d = devices[i];
                    Log($"  [{i + 1}] Name: \"{d.Name}\" | Status: {d.Status}");
                    Log($"      DeviceID: {d.DeviceID}");
                }
            }

            var candidatePaths = BuildCandidatePaths(devices);
            Log("Attempting connection across candidate paths in order:");
            foreach (var path in candidatePaths)
            {
                Log($"  -> Trying path: {path}");
                handle = CreateFile(
                    path,
                    0x40000000, // GENERIC_WRITE
                    0,          // No sharing
                    IntPtr.Zero,
                    3,          // OPEN_EXISTING
                    0,
                    IntPtr.Zero);

                if (handle != new IntPtr(-1) && handle != IntPtr.Zero)
                {
                    Log($"Successfully opened device handle: 0x{handle.ToInt64():X} via \"{path}\"");
                    break;
                }
                else
                {
                    int lastError = Marshal.GetLastWin32Error();
                    string errorMsg = new Win32Exception(lastError).Message;
                    Log($"     Failed to open \"{path}\". Win32 Error {lastError}: {errorMsg}");
                }
            }

            if (handle == new IntPtr(-1) || handle == IntPtr.Zero)
            {
                Log("Failed to open any valid device handle. Start() aborted.");
                Log("==================================================");
                return false;
            }

            // Configure DCB (Comm State)
            Log("Querying current comm state (GetCommState)...");
            DCB dcb = new DCB();
            if (!GetCommState(handle, ref dcb))
            {
                int lastError = Marshal.GetLastWin32Error();
                string errorMsg = new Win32Exception(lastError).Message;
                Log($"GetCommState failed. Win32 Error {lastError}: {errorMsg}");
                CloseHandle(handle);
                handle = IntPtr.Zero;
                return false;
            }

            Log($"Current DCB: BaudRate={dcb.BaudRate}, ByteSize={dcb.ByteSize}, Parity={dcb.Parity}, StopBits={dcb.StopBits}");

            dcb.BaudRate = 250000;
            dcb.ByteSize = 8;
            dcb.Parity = 0;   // NOPARITY
            dcb.StopBits = 2; // TWOSTOPBITS

            Log("Applying DMX comm state settings (BaudRate=250000, 8 Data Bits, 2 Stop Bits, No Parity)...");
            if (!SetCommState(handle, ref dcb))
            {
                int lastError = Marshal.GetLastWin32Error();
                string errorMsg = new Win32Exception(lastError).Message;
                Log($"SetCommState failed. Win32 Error {lastError}: {errorMsg}");
                CloseHandle(handle);
                handle = IntPtr.Zero;
                return false;
            }

            Log("DCB configuration successful.");

            // Start DMX transmission thread
            Log("Starting background DMX output thread...");
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
            if (handle != IntPtr.Zero && handle != new IntPtr(-1))
            {
                CloseHandle(handle);
                handle = IntPtr.Zero;
                Log("Device handle closed.");
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
            while (dmxThreadRunning)
            {
                WriteDmx();
                Thread.Sleep(25); // ~40Hz refresh rate
            }
        }

        private static void WriteDmx()
        {
            if (handle == IntPtr.Zero || handle == new IntPtr(-1)) return;

            // Break
            EscapeCommFunction(handle, 5); // SETBREAK
            Thread.Sleep(1);
            EscapeCommFunction(handle, 6); // CLRBREAK

            // Data
            dmxBuffer[0] = 0; // DMX Start Code
            WriteFile(handle, dmxBuffer, DMX_BUFFER_SIZE, out _, IntPtr.Zero);
        }
    }
}
