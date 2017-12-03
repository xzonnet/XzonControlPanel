using System;
using System.Runtime.InteropServices;

namespace XzonControlPanel.Windows
{
    public static class NativeMethods
    {
        public delegate bool ConsoleEventDelegate(int eventType);
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool SetConsoleCtrlHandler(ConsoleEventDelegate callback, bool add);
        [DllImport("kernel32.dll")]
        public static extern ErrorModes SetErrorMode(ErrorModes uMode);

        [Flags]
        public enum ErrorModes : uint
        {
            SystemDefault = 0x0,
            SemFailcriticalerrors = 0x0001,
            SemNoalignmentfaultexcept = 0x0004,
            SemNogpfaulterrorbox = 0x0002,
            SemNoopenfileerrorbox = 0x8000
        }

        [Flags]
        public enum ThreadAccess
        {
            Terminate = (0x0001),
            SuspendResume = (0x0002),
            GetContext = (0x0008),
            SetContext = (0x0010),
            SetInformation = (0x0020),
            QueryInformation = (0x0040),
            SetThreadToken = (0x0080),
            Impersonate = (0x0100),
            DirectImpersonation = (0x0200)
        }

        [DllImport("kernel32.dll")]
        public static extern IntPtr OpenThread(ThreadAccess dwDesiredAccess, bool bInheritHandle, uint dwThreadId);
        [DllImport("kernel32.dll")]
        public static extern uint SuspendThread(IntPtr hThread);
        [DllImport("kernel32.dll")]
        public static extern int ResumeThread(IntPtr hThread);
        [DllImport("kernel32", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern bool CloseHandle(IntPtr handle);

        public enum CtrlTypes
        {
            CtrlCEvent = 0,
            CtrlBreakEvent,
            CtrlCloseEvent,
            CtrlLogoffEvent = 5,
            CtrlShutdownEvent
        }
    }
}
