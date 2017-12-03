using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using XzonControlPanel.Config;
using XzonControlPanel.Logging;
using XzonControlPanel.Security;

namespace XzonControlPanel.Rig
{
    public interface IServerCommand
    {
        string Command { get; set; }
        string Password { get; set; }

        string Channel { get; set; }
    }

    public class ServerCommand : IServerCommand
    {
        #region Static Variables
        public static readonly List<string> CustomCommands = new List<string>();
        private static readonly string CustomCommandDirectory = "CustomCommandDirectory".FromConfig() ?? "CustomCommands";
        private static readonly string CommandPassword = SecurityHelper.GetHashSha256("CommandPassword".FromConfig(true));
        private static readonly Regex WhitespaceRegex = new Regex(@"\s+");
        #endregion

        public string Channel { get; set; }
        public string Password { get; set; }
        public string Command { get; set; }

        public ServerCommand()
        {
        }

        public ServerCommand(string json)
        {
            try
            {
                var cmd = JObject.Parse(json);

                foreach (var kvp in cmd)
                {
                    var key = kvp.Key;
                    var value = (string)kvp.Value;

                    PropertyInfo pInfo = typeof(ServerCommand).GetProperty(key);
                    if (pInfo != null)
                    {
                        value = WhitespaceRegex.Replace(value, string.Empty);
                        pInfo.SetValue(this, value);
                    }
                    else
                    {
                        Log.Warning($"Received command with unknown property: {key} = {value}. Ignoring.");
                    }
                }
            }
            catch (Exception ex)
            {
                Command = null;
                Password = null;
                Channel = null;
                Log.Warning(ex);
                Log.Warning("Received a command in the improper format, ignoring.");
            }
        }

        static ServerCommand()
        {
            if (!Directory.Exists(CustomCommandDirectory))
                return;

            var commands = Directory.GetFiles(CustomCommandDirectory, "*.bat", SearchOption.AllDirectories);
            foreach (var cmd in commands)
            {
                var commandName = Path.GetFileNameWithoutExtension(cmd);

                if (string.IsNullOrEmpty(commandName))
                    continue;

                commandName = WhitespaceRegex.Replace(commandName, string.Empty);
                CustomCommands.Add(commandName);
            }
        }

        public void Execute(ChannelCollection channels)
        {
            if (Password == null || Channel == null || Command == null)
                return;

            if (Password == CommandPassword)
            {
                var channel = channels.FirstOrDefault(c => c.Name == Channel);
                if (channel == null)
                {
                    Log.Warning("Received a command from a channel you aren't in, ignoring.");
                    return;
                }

                if (CheckChannelPermissions(channel))
                {
                    if (Command.Equals("Resume"))
                    {
                        Log.Informational($"Executing Command: {Command}");
                        Program.Unpause();
                    }
                    else if (Command.Equals("Pause"))
                    {
                        Log.Informational($"Executing Command: {Command}");
                        Program.Pause();
                        Program.KillMiner();

                    }
                    else if (Command.Equals("Restart"))
                    {
                        Program.Pause();
                        Log.Informational($"Executing Command: {Command}");
                        Program.KillMiner();
                        Program.Unpause();
                    }
                    else
                    {
                        var cmd = CustomCommands.FirstOrDefault(c => WhitespaceRegex.Replace(c, string.Empty).Equals(Command));

                        if (cmd == null)
                        {
                            Log.Warning($"Received command {Command} but no CustomCommand matching that name exists, ignoring.");
                        }
                        else
                        {
                            Log.Informational($"Executing Command: {Command}");
                            Process.Start($"CustomCommands\\{cmd}.bat");
                        }
                    }
                }
                else
                {
                    Log.Warning($"Received command {Command} but the channel is not marked as Trusted, ignoring.");
                }
            }
            else
            {
                Log.Warning($"Received {Command} command, but the password provided was incorrect. Ignoring the command.");
            }
        }

        public bool CheckChannelPermissions(ChannelConfig channel)
        {
            return channel.IsTrustedChannel;
        }
    }
}
