using System;
using System.Text.RegularExpressions;

namespace XzonControlPanel.Logging
{
    public static class PrettyConsole
    {
        private static readonly object LockeLamora = new object();
        public static void Write(string text, ConsoleColor color = ConsoleColor.White)
        {
            text = StripConsoleColorsFromRawText(text);

            lock (LockeLamora)
            {
                Console.ForegroundColor = color;
                Console.Write(text);
            }
        }
        public static void WriteLine()
        {
            lock (LockeLamora)
            {
                Console.WriteLine();
            }
        }
        public static void WriteLine(string text, ConsoleColor color = ConsoleColor.White)
        {
            text = StripConsoleColorsFromRawText(text);

            lock (LockeLamora)
            {
                Console.ForegroundColor = color;
                Console.WriteLine(text);
            }
        }
        public static void WriteLine(Exception ex)
        {
            lock (LockeLamora)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine(ex);
            }
        }
        private static string StripConsoleColorsFromRawText(string text)
        {
            text = Regex.Replace(text, @"\u001b\[\d+.*?m", string.Empty);
            return text;
        }
    }
}
