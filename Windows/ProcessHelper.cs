using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using XzonControlPanel.Logging;

namespace XzonControlPanel.Windows
{
    public static class ProcessHelper
    {
        public static void GracefulShutdownMiner(Process minerProcess, Process commandLineProcess)
        {
            if (minerProcess == null)
                return;

            PrettyConsole.WriteLine($"Freezing Miner Process ({minerProcess.Id})", ConsoleColor.Magenta);

            try
            {
                SuspendProcess(minerProcess.Id);
            }
            catch (Exception ex)
            {
                Log.Error(ex);
                PrettyConsole.WriteLine($"Miner Process Has Exited: {minerProcess.HasExited}");
            }
            finally
            {
                Thread.Sleep(5000);
            }

            KillProcess(minerProcess);

            KillProcess(commandLineProcess);
        }

        public static void SuspendProcess(int pid)
        {
            var process = Process.GetProcessById(pid);

            if (process.ProcessName == string.Empty)
                return;

            foreach (ProcessThread pT in process.Threads)
            {
                IntPtr pOpenThread = NativeMethods.OpenThread(NativeMethods.ThreadAccess.SuspendResume, false, (uint)pT.Id);

                if (pOpenThread == IntPtr.Zero)
                {
                    continue;
                }

                NativeMethods.SuspendThread(pOpenThread);

                NativeMethods.CloseHandle(pOpenThread);
            }
        }

        public static void ResumeProcess(int pid)
        {
            var process = Process.GetProcessById(pid);

            if (process.ProcessName == string.Empty)
                return;

            foreach (ProcessThread pT in process.Threads)
            {
                IntPtr pOpenThread = NativeMethods.OpenThread(NativeMethods.ThreadAccess.SuspendResume, false, (uint)pT.Id);

                if (pOpenThread == IntPtr.Zero)
                {
                    continue;
                }

                int suspendCount;
                do
                {
                    suspendCount = NativeMethods.ResumeThread(pOpenThread);
                } while (suspendCount > 0);

                NativeMethods.CloseHandle(pOpenThread);
            }
        }

        public static void KillProcess(string processName)
        {
            Process[] processes = Process.GetProcesses().Where(p => p.Id != Process.GetCurrentProcess().Id && p.ProcessName.ToLower() == processName.ToLower() && !p.HasExited).ToArray();

            foreach (var p in processes)
            {
                try
                {
                    PrettyConsole.WriteLine($"Killing Process {p.ProcessName} [{p.Id}].", ConsoleColor.Magenta);
                    p.Kill();
                    Thread.Sleep(1000);
                }
                catch (Exception ex)
                {
                    Log.Error(ex);
                }
            }

            Thread.Sleep(1000);
        }

        public static void KillProcess(Process process)
        {
            if (process == null || process.HasExited)
                return;

            try
            {
                PrettyConsole.WriteLine($"Killing Process {process.ProcessName} [{process.Id}].", ConsoleColor.Magenta);
                process.Kill();
            }
            catch (Exception ex)
            {
                Log.Error(ex);
            }

            Thread.Sleep(1000);
        }
    }
}
