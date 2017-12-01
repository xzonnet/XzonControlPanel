using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;
using XzonControlPanel.Config;

namespace XzonControlPanel.Logging
{
    public static class Log
    {
        private static readonly object FileLocke = new object();

        //Configuration Variables
        private static readonly bool EmailOnWarning = "EmailOnWarning".FromConfig().ToBool() ?? false;
        private static readonly bool EmailOnError = "EmailOnError".FromConfig().ToBool() ?? false;
        private static readonly string EmailAddressToAlert = "EmailAddressToAlert".FromConfig();
        private static readonly bool ScreenshotOnWarning = "ScreenshotOnWarning".FromConfig().ToBool() ?? false;
        private static readonly bool ScreenshotOnError = "ScreenshotOnError".FromConfig().ToBool() ?? false;
        private static readonly string ScreenshotDirectory = "ScreenshotDirectory".FromConfig() ?? "Screenshots";
        private static readonly string LogsDirectory = "LogsDirectory".FromConfig() ?? "Logs";

        static Log()
        {
            if (!Directory.Exists(LogsDirectory))
                Directory.CreateDirectory(LogsDirectory);
        }

        private static void LogToFile(string filePathAndName, string text)
        {
            lock (FileLocke)
            {
                File.AppendAllText(filePathAndName, text);
            }
        }

        public static void Error(string text, bool bypassEmail = false)
        {
            LogToFile(Path.Combine(LogsDirectory, "error.txt"), $"[{DateTime.Now}] {text}{Environment.NewLine}");
            PrettyConsole.WriteLine($"Error: {text}", ConsoleColor.Red);

            string errorScreenshot = null;
            if (ScreenshotOnError)
            {
                errorScreenshot = Screenshot("warning");
            }

            if (EmailOnError && !bypassEmail)
            {
                if (string.IsNullOrEmpty(errorScreenshot))
                {
                    EmailHelper.SendEmail(EmailAddressToAlert, text, $"{Environment.MachineName} Error");
                }
                else
                {
                    EmailHelper.SendEmailWithAttachment(EmailAddressToAlert, text, errorScreenshot, $"{Environment.MachineName} Error");
                }
            }
        }
        public static void Warning(string text, bool bypassEmail = false)
        {
            LogToFile(Path.Combine(LogsDirectory, "warning.txt"), $"[{DateTime.Now}] {text}{Environment.NewLine}");
            PrettyConsole.WriteLine($"Warning: {text}", ConsoleColor.Yellow);

            string warningScreenshot = null;
            if (ScreenshotOnWarning)
            {
                warningScreenshot = Screenshot("warning");
            }

            if (EmailOnWarning && !bypassEmail)
            {
                if (string.IsNullOrEmpty(warningScreenshot))
                {
                    EmailHelper.SendEmail(EmailAddressToAlert, text, $"{Environment.MachineName} Warning");
                }
                else
                {
                    EmailHelper.SendEmailWithAttachment(EmailAddressToAlert, text, warningScreenshot, $"{Environment.MachineName} Warning");
                }
            }
        }
        public static void Informational(string text, bool silent = false)
        {
            LogToFile(Path.Combine(LogsDirectory, "informational.txt"), $"[{DateTime.Now}] {text}{Environment.NewLine}");

            if(!silent)
                PrettyConsole.WriteLine($"Informational: {text}");
        }
        public static void Error(Exception ex, bool bypassEmail = false)
        {
            Error(ex.ToString(), bypassEmail);
        }

        public static void Warning(Exception ex, bool bypassEmail = false)
        {
            Warning(ex.ToString(), bypassEmail);
        }

        public static string Screenshot(string name)
        {
            var bounds = Screen.GetBounds(Point.Empty);
            using (var bitmap = new Bitmap(bounds.Width, bounds.Height))
            {
                using (var g = Graphics.FromImage(bitmap))
                {
                    g.CopyFromScreen(Point.Empty, Point.Empty, bounds.Size);
                }

                var fileName = $"{DateTime.Now.Ticks}_{name}.jpg";

                if (!string.IsNullOrEmpty(ScreenshotDirectory) && !Directory.Exists(ScreenshotDirectory))
                    Directory.CreateDirectory(ScreenshotDirectory);

                var finalPath = Path.Combine(ScreenshotDirectory, fileName);

                bitmap.Save(finalPath, ImageFormat.Jpeg);
                return finalPath;
            }
        }
    }
}
