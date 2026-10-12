using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace dmxEasyDancing
{
    public class EnttecOpenDmx
    {
        public const int DMX_BUFFER_SIZE = 513;

        private static byte[] dmxBuffer = new byte[DMX_BUFFER_SIZE];
        private static IntPtr handle = IntPtr.Zero;
        private static bool dmxThreadRunning = false;
        private static Thread? dmxThread;

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

        public static bool Start()
        {
            handle = CreateFile(
                @"\\.\FTDIBUS\VID_0403+PID_6001+A50285BI\0000",
                0x40000000, // GENERIC_WRITE
                0, IntPtr.Zero, 3, 0, IntPtr.Zero);

            if (handle == new IntPtr(-1))
            {
                handle = CreateFile(
                    @"\\.\ENTTEC OPEN DMX USB",
                    0x40000000, // GENERIC_WRITE
                    0, IntPtr.Zero, 3, 0, IntPtr.Zero);
            }

            if (handle == new IntPtr(-1))
            {
                return false;
            }

            DCB dcb = new DCB();
            if (!GetCommState(handle, ref dcb))
            {
                CloseHandle(handle);
                return false;
            }

            dcb.BaudRate = 250000;
            dcb.ByteSize = 8;
            dcb.Parity = 0;
            dcb.StopBits = 2;

            if (!SetCommState(handle, ref dcb))
            {
                CloseHandle(handle);
                return false;
            }

            dmxThread = new Thread(DmxThread);
            dmxThread.IsBackground = true;
            dmxThreadRunning = true;
            dmxThread.Start();

            return true;
        }

        public static void Stop()
        {
            dmxThreadRunning = false;
            if (dmxThread != null && dmxThread.IsAlive)
            {
                dmxThread.Join();
            }
            if (handle != IntPtr.Zero && handle != new IntPtr(-1))
            {
                CloseHandle(handle);
            }
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
