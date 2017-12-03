using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Quobject.SocketIoClientDotNet.Client;
using XzonControlPanel.Config;
using XzonControlPanel.Logging;
using XzonControlPanel.Rig;
using XzonControlPanel.Security;
using XzonControlPanel.Windows;

namespace XzonControlPanel
{
    class Program
    {
        //Xzon Version
        public const string Version = "1.0.0";

        //Configuration Variables
        private static readonly double MaxTemp = "MaxGpuTemperature".FromConfig().ToDouble() ?? 90.0;
        private static readonly double WarningTemp = "WarningGpuTemperature".FromConfig().ToDouble() ?? 80.0;
        private static readonly bool VerboseOutput = "VerboseOutput".FromConfig().ToBool() ?? false;
        private static readonly int RebootAfterPausedXMinutes = "RebootAfterPausedXMinutes".FromConfig().ToInt() ?? 0;

        //Wallet Regex
        private static readonly Regex WalletRegex = new Regex(@"\s(V|t|0x|C|D)(\w{10,})(\s|$|\.)(\S*)");
        private static string _walletPrefix;
        private static string _wallet;
        //private static string _workerName;

        //Display Variables
        private static readonly List<string> DisplayKeywords = new List<string> { "submit", "authorized worker", "unknown", "reject", "nonce", "not accepted", "connected", "accepted:", "yes!" };
        private static readonly List<string> ShareFoundKeywords = new List<string> { "submitting share", "solution found", "accepted:", "yes!", "] accepted", "result accepted by" };
        private static readonly List<string> RejectedShareKeywords = new List<string> { "reject", "not accepted" };
        private static readonly List<string> ErrorKeywords = new List<string> { "error", "exception", "fatal" };
        private static readonly List<string> ValidHashrateKeywords = new List<string> { "h", "kh", "mh", "sols", "ksols" };

        //Statistics Variables
        private static int _solutionsFound;
        private static int _rejectedSolutions;
        private static readonly ConcurrentQueue<decimal> RecentHashrates = new ConcurrentQueue<decimal>();
        private static readonly ConcurrentDictionary<int, ConcurrentQueue<decimal>> RecentGpuHashrates = new ConcurrentDictionary<int, ConcurrentQueue<decimal>>();
        private static readonly int TotalReadingsToAverage = 50;
        private static string _units = string.Empty;

        //Miner Configurations
        private static MinerCollection _miners;
        private static MinerConfig _currentMiningConfig;
        private static string MinerLocation => _currentMiningConfig?.ExeLocation;
        private static string MinerExe => Path.GetFileNameWithoutExtension(_currentMiningConfig?.ExeLocation);
        private static string MinerCommandLineParameters => _currentMiningConfig?.CommandLineParameters ?? string.Empty;
        private static readonly Regex PoolRegex = new Regex(@"(\S*)\.(org|com|net|web|io):(\d{1,5})");
        private static string MiningPool
            =>
            PoolRegex.Match(MinerCommandLineParameters).Length > 0
                ? PoolRegex.Match(MinerCommandLineParameters).Value
                : string.Empty;

        //Schedule Variables
        private static readonly int MinutesInRotation = "MinutesInRotation".FromConfig().ToInt() ?? 1440;
        const string MiningMinuteFile = "miningminute.txt";
        private static int _miningMinute;
        private static readonly List<ScheduledConfig> ScheduledConfigs = new List<ScheduledConfig>();

        //Monitoring Socket
        private static readonly string MonitoringServer = "MonitoringServer".FromConfig() ?? "https://xzon.net:1443";
        private static Socket _monitoringSocket;
        private static ChannelCollection _channels;
        private static DateTime _lastHandshake = DateTime.MinValue;

        //Handlers
        public static NativeMethods.ConsoleEventDelegate Handler;

        //Mining Sub Process
        private static Process _minerProcess;
        private static Process _commandLineProcess;
        private static bool _running = true;

        //Channel Variables
        private static readonly string RigName = "RigName".FromConfig() ?? FingerPrint.Value;

        //Program Control Variables
        private static bool _paused;
        private static DateTime? _pausedTime;

        static void Initialize()
        {
            ChannelsSection channelConfigSection = null;
            try
            {
                channelConfigSection = ConfigurationManager.GetSection("ChannelsSection") as ChannelsSection;
                if (channelConfigSection == null)
                    throw new Exception("Configuration Error");
            }
            catch (Exception ex)
            {
                Log.Error(ex);
                Log.Warning("XzonControlPanel.exe.config file is malformed. Please fix it or run the Configuration Tool to generate a valid config.");
                PrettyConsole.WriteLine("Press [ENTER] to exit.");
                Console.ReadLine();
                Environment.Exit(0);
            }
            _channels = channelConfigSection.Channels;


            MinersSection minerConfigSection = null;
            try
            {
                minerConfigSection = ConfigurationManager.GetSection("MinersSection") as MinersSection;

                if (minerConfigSection == null)
                    throw new Exception("Configuration Error");
            }
            catch (Exception ex)
            {
                Log.Error(ex);
                Log.Warning("XzonControlPanel.exe.config file is malformed. Please fix it or run the Configuration Tool to generate a valid config.");
                PrettyConsole.WriteLine("Press [ENTER] to exit.");
                Console.ReadLine();
                Environment.Exit(0);
            }

            _miners = minerConfigSection.Miners;

            PrettyConsole.WriteLine("===========================");
            PrettyConsole.WriteLine("Detected Configured Miners:");
            PrettyConsole.WriteLine("===========================");
            decimal total = 0;

            List<string> purge = new List<string>();
            foreach (MinerConfig item in _miners)
            {
                if (item.Ratio == 0)
                    purge.Add(item.Name);
                total += item.Ratio;
            }
            purge.ForEach(s => _miners.Remove(s));

            decimal scale = 100 / total;

            int startMinute = 0;
            foreach (MinerConfig item in _miners)
            {
                item.TrueRatio = item.Ratio * scale;
                item.StartMinute = startMinute;
                PrettyConsole.Write($"{item.Name} ");
                PrettyConsole.WriteLine($"({decimal.Round(item.TrueRatio, 2)}%)", ConsoleColor.Green);
                PrettyConsole.WriteLine($"\tStarts at minute {item.StartMinute}");
                PrettyConsole.WriteLine($"\t{Path.GetFileNameWithoutExtension(item.ExeLocation)} {item.CommandLineParameters}");
                PrettyConsole.WriteLine("---------------------------");
                startMinute += (int)decimal.Floor(item.TrueRatio / 100 * MinutesInRotation);
            }
            Console.WriteLine();

            foreach (var miner in _miners)
            {
                if (miner.Ratio == 0)
                    continue;

                var match = WalletRegex.Match(miner.CommandLineParameters);
                if (match.Groups.Count >= 2)
                    miner.Wallet = $"{match.Groups[1].Value}{match.Groups[2].Value}";

                ScheduledConfigs.Add(new ScheduledConfig
                {
                    Name = miner.Name,
                    MinerCommandLine = Path.GetFileName(miner.ExeLocation) + miner.CommandLineParameters,
                    StartTime = miner.StartMinute
                });
            }

            if (!File.Exists(MiningMinuteFile))
                File.WriteAllText(MiningMinuteFile, "-1");

            HandleAutoStartup();

            _paused = !("StartMinerOnStartup".FromConfig().ToBool() ?? false);
        }

        private static void HandleAutoStartup()
        {
            bool launchOnStartup = "LaunchXzonOnBoot".FromConfig().ToBool() ?? false;

            try
            {
                string batPath = $"{Environment.GetFolderPath(Environment.SpecialFolder.Startup)}\\{Path.GetFileNameWithoutExtension(AppDomain.CurrentDomain.FriendlyName)}.bat".Replace(".vshost","");
                
                if (File.Exists(batPath))
                {
                    File.Delete(batPath);
                }

                if (launchOnStartup)
                {
                    string exePath = System.Reflection.Assembly.GetEntryAssembly().Location;
                    int startDelay = 30;

                    using (StreamWriter w = new StreamWriter(batPath))
                    {
                        w.WriteLine($"TIMEOUT /T {startDelay}");
                        w.WriteLine($"CD /d {Path.GetDirectoryName(exePath)}");
                        w.WriteLine($"START \"\" \"{exePath}\"");
                        w.WriteLine("EXIT");
                        w.Close();
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"Exception encountered while trying to access Auto-Start Batch File: {ex.Message}");
            }
        }

        [STAThread]
        static void Main()
        {
            Initialize();

            #region Error Mode
            //Prevent error dialog popups, this prevents the miner sub process from hanging by a windows error popup during a crash
            NativeMethods.SetErrorMode(NativeMethods.ErrorModes.SemFailcriticalerrors | NativeMethods.ErrorModes.SemNogpfaulterrorbox);
            #endregion

            #region Console Hooks
            //Hook Console to catch Close Event
            Handler = ConsoleEventCallback;
            NativeMethods.SetConsoleCtrlHandler(Handler, true);

            Console.CancelKeyPress += delegate (object sender, ConsoleCancelEventArgs e)
            {
                e.Cancel = true;
                _running = false;
                PrettyConsole.WriteLine("Gracefully exiting...", ConsoleColor.Magenta);
            };
            #endregion


            Startup:
            try
            {
                ConnectToMonitoringSocket();

                //Every minute the miner is running, it updates this file with how many minutes it has mined % MinutesInRotation
                var text = "";
                try
                {
                    text = File.ReadAllText(MiningMinuteFile);
                }
                catch (Exception ex)
                {
                    Log.Warning(ex);
                }

                if (!int.TryParse(text, out _miningMinute))
                {
                    Log.Warning($"Unable to read Mining Minute, defaulting to 0. Text received from {MiningMinuteFile}: {text}");
                }

                int currentMinute = -1;

                var freshStart = new List<string> { "XzonControlPanel" };
                foreach (MinerConfig miner in _miners)
                {
                    var exeName = Path.GetFileNameWithoutExtension(miner.ExeLocation);
                    if (!freshStart.Contains(exeName))
                        freshStart.Add(exeName);
                }

                foreach (var p in freshStart)
                {
                    ProcessHelper.KillProcess(p);
                }
                Thread.Sleep(1000);

                PrettyConsole.Write("Max Allowed GPU Temperature is ");
                PrettyConsole.WriteLine($"{MaxTemp}°c", ConsoleColor.Red);
                PrettyConsole.WriteLine();

                Log.Warning("Miner Has Started");

                //Main Loop
                while (_running)
                {
                    Temperature.Refresh();

                    #region Scheduling
                    if (DateTime.Now.Hour * 60 + DateTime.Now.Minute != currentMinute && !_paused)
                    {
                        //Minute changed, increment
                        _miningMinute = (_miningMinute + 1) % MinutesInRotation;
                        UpdateMiningMinute(_miningMinute);
                        currentMinute = DateTime.Now.Hour * 60 + DateTime.Now.Minute;

                        PrettyConsole.WriteLine($"Current Minute: {_miningMinute}", ConsoleColor.DarkYellow);

                        //Check if it is time to use the next miner config in the schedule
                        MinerConfig newMinerConfig = _miners[0];
                        foreach (MinerConfig miner in _miners)
                        {
                            if (miner.StartMinute <= _miningMinute)
                                newMinerConfig = miner;
                            else
                            {
                                PrettyConsole.WriteLine($"Will start {miner.Name} at minute {miner.StartMinute}", ConsoleColor.DarkYellow);
                            }
                        }
                        if (!newMinerConfig.Equals(_currentMiningConfig))
                        {
                            SetActiveMiner(newMinerConfig);
                        }
                    }
                    #endregion

                    VerifyProcesses();
                    if (!_running)
                        break;

                    if (_paused)
                    {
                        Log.Informational("Miner is paused");

                        //If reboot after paused X minutes is set, reboot after X minutes
                        if (RebootAfterPausedXMinutes > 0 && _pausedTime != null)
                        {
                            if (DateTime.Now > _pausedTime.Value.AddMinutes(RebootAfterPausedXMinutes))
                            {
                                Log.Warning("Rebooting Machine");
                                Process.Start("shutdown.exe", "-r -t 30");
                                _running = false;
                            }
                            else
                            {
                                Log.Informational($"Rebooting machine in {_pausedTime.Value.AddMinutes(RebootAfterPausedXMinutes).Subtract(DateTime.Now).TotalSeconds} seconds", true);
                                PrettyConsole.Write("Rebooting machine in ");
                                PrettyConsole.Write($"{_pausedTime.Value.AddMinutes(RebootAfterPausedXMinutes).Subtract(DateTime.Now).TotalSeconds}", ConsoleColor.Yellow);
                                PrettyConsole.WriteLine(" seconds");
                            }
                        }
                    }

                    List<Gpu> gpus = new List<Gpu>();
                    List<Cpu> cpus = new List<Cpu>();

                    if (Temperature.GpuTemperatures == null)
                    {
                        Log.Warning("Unable to read GPU Temperatures, killing miner");
                        Pause();
                        KillMiner();
                        continue;
                    }

                    foreach (var temp in Temperature.CpuTemperatures)
                    {
                        PrettyConsole.Write($"{temp.InstanceName} ({temp.InstanceId}): ");
                        PrettyConsole.WriteLine($"{temp.CurrentValue}°c", ConsoleColor.Cyan);
                        cpus.Add(new Cpu {Color = "Cyan", Id = temp.InstanceId, Name = temp.InstanceName, Temp = (int)temp.CurrentValue});
                    }

                    foreach (var temp in Temperature.GpuTemperatures)
                    {
                        PrettyConsole.Write($"{temp.InstanceName} ({temp.InstanceId}): ");
                        var outputColor = ConsoleColor.Cyan;

                        if (temp.CurrentValue >= MaxTemp)
                        {
                            outputColor = ConsoleColor.Red;
                            PrettyConsole.Write($"{temp.CurrentValue}", outputColor);
                            PrettyConsole.Write(@"°c");
                        }
                        else if (temp.CurrentValue >= WarningTemp)
                        {
                            outputColor = ConsoleColor.Yellow;
                            PrettyConsole.Write($"{temp.CurrentValue}", outputColor);
                            PrettyConsole.Write(@"°c");
                        }
                        else if (Math.Abs(temp.CurrentValue) < 1)
                        {
                            //Check if GPU went offline, if it did its reported temperature is 0, check for <1 because floating point numbers doing their thing
                            outputColor = ConsoleColor.Red;
                            PrettyConsole.Write($"{temp.CurrentValue}", outputColor);
                            PrettyConsole.Write(@"°c");
                            Log.Error($"GPU {temp.InstanceName} ({temp.InstanceId}) Went Offline.");
                            KillMiner();
                            Pause();
                        }
                        else
                        {
                            PrettyConsole.Write($"{temp.CurrentValue}", outputColor);
                            PrettyConsole.Write(@"°c");
                        }

                        decimal gpuHr = -1;

                        try
                        {
                            int gpuId;
                            if (int.TryParse(temp.InstanceId.Substring(temp.InstanceId.LastIndexOf("/", StringComparison.Ordinal) + 1), out gpuId))
                            {
                                lock (RecentGpuHashrates)
                                {
                                    if (RecentGpuHashrates.ContainsKey(gpuId) && RecentGpuHashrates[gpuId].Count > 0)
                                    {
                                        gpuHr = decimal.Round(RecentGpuHashrates[gpuId].Average(), 2);
                                        PrettyConsole.Write($" ({gpuHr} {_units}/s)", ConsoleColor.Gray);
                                    }
                                }
                            }
                        }
                        catch (Exception)
                        {
                            //Do nothing
                        }
                        finally
                        {
                            PrettyConsole.WriteLine();
                        }


                        gpus.Add(new Gpu
                        {
                            Color = outputColor.ToString(),
                            Name = temp.InstanceName,
                            Id = temp.InstanceId,
                            Temp = (int)temp.CurrentValue,
                            Rate = gpuHr
                        });

                        if (temp.CurrentValue > MaxTemp)
                        {
                            Log.Error($"Killing miner, GPU Temperature is {temp.CurrentValue}°c but the max is set for {MaxTemp}°c");

                            //Hard shut down
                            //Exit(1);

                            //Soft shut down
                            KillMiner();
                            Pause();
                        }
                    }

                    //Check average hash rate
                    decimal averageHashrate = 0;
                    ConsoleColor hashrateColor = ConsoleColor.Red;
                    if (RecentHashrates.Count > 0)
                    {
                        Queue<decimal> localQueue = new Queue<decimal>(RecentHashrates);
                        averageHashrate = decimal.Round(localQueue.Average(), 3);

                        hashrateColor = ConsoleColor.Green;
                        if (averageHashrate < _currentMiningConfig.HashrateError)
                        {
                            hashrateColor = ConsoleColor.Red;
                            if (localQueue.Count == TotalReadingsToAverage)
                            {
                                var error = $"Average Hashrate {averageHashrate} dropped below configured error rate ({_currentMiningConfig.HashrateError}). Killing miner.";
                                Log.Error(error);
                                KillMiner();
                            }
                        }
                        else if (averageHashrate < _currentMiningConfig.HashrateWarning)
                        {
                            hashrateColor = ConsoleColor.Yellow;
                            if (localQueue.Count == TotalReadingsToAverage)
                            {
                                Log.Warning($"Average Hashrate {averageHashrate} dropped below configured warning rate {_currentMiningConfig.HashrateWarning}.");
                            }
                        }

                        if (localQueue.Count > 0)
                        {
                            PrettyConsole.Write(@"Current Average Hashrate: ");

                            PrettyConsole.Write($"{averageHashrate} {_units}/s", hashrateColor);

                            PrettyConsole.WriteLine();
                        }

                        PrettyConsole.Write(@"Solutions Found: ");
                        PrettyConsole.Write($"{_solutionsFound}", _solutionsFound > 0 ? ConsoleColor.Green : ConsoleColor.Yellow);

                        if (_rejectedSolutions > 0)
                        {
                            PrettyConsole.Write(@". Rejected: ");
                            PrettyConsole.Write($"{_rejectedSolutions}", ConsoleColor.Red);
                        }

                        PrettyConsole.WriteLine(@".");
                    }

                    UpdateMonitoringSockets(gpus, cpus, averageHashrate, hashrateColor);

                    //foreach (var config in _miners)
                    //{
                    //    PrettyConsole.WriteLine($"({config.Wallet}){WalletHelper.GetWalletBalance(config.CurrencyCode, config.Wallet)}", ConsoleColor.DarkGreen);
                    //}

                    Thread.Sleep(10000);

                    if (File.Exists("stop.txt"))
                    {
                        File.Delete("stop.txt");
                        _running = false;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex);

                if (_running)
                {
                    Log.Warning(ex);
                    Log.Informational("Attempting to restart from the beginning...");
                    goto Startup;
                }
            }

            KillMiner();
            Exit();
        }

        private static void RedirectStandardOutput(object sendingProcess, DataReceivedEventArgs outLine)
        {
            if (string.IsNullOrEmpty(outLine?.Data) || outLine.Data.Contains("Mining on"))
                return;

            //PrettyConsole.WriteLine(outLine.Data, ConsoleColor.DarkCyan);
            RedirectStandardError(sendingProcess, outLine);
        }

        private static void RedirectStandardError(object sendingProcess, DataReceivedEventArgs outLine)
        {
            if (string.IsNullOrEmpty(outLine?.Data))
                return;

            var info = outLine.Data.ToLower();

            bool output = false;
            ConsoleColor outputColor = ConsoleColor.White;
            foreach (var s in DisplayKeywords)
            {
                if (info.Contains(s))
                {
                    output = true;
                    outputColor = ConsoleColor.Green;
                    break;
                }
            }

            foreach (var s in ShareFoundKeywords)
            {
                if (info.Contains(s))
                {
                    _solutionsFound++;
                    outputColor = ConsoleColor.Green;
                    break;
                }
            }

            foreach (var s in RejectedShareKeywords)
            {
                if (info.Contains(s))
                {
                    _rejectedSolutions++;
                    outputColor = ConsoleColor.Yellow;
                    break;
                }
            }

            foreach (var s in ErrorKeywords)
            {
                if (info.Contains(s))
                {
                    PrettyConsole.WriteLine(outLine.Data, ConsoleColor.Red);
                    Log.Error(outLine.Data);
                    KillMiner();
                    return;
                }
            }

            if (VerboseOutput || output)
                PrettyConsole.WriteLine(outLine.Data, outputColor);

            UpdateAverageHashRate(outLine.Data);
        }

        private static readonly Regex HrUnitsRegex = new Regex(@"\s(\d+\.?\d*)\s?(\D{2,4})\/s");
        //private static readonly Regex IndividualGpuRegex = new Regex(@"GPU #\d{1,2}");
        private static readonly Regex IndividualGpuRegex = new Regex(@"gpu(\/|\s*)(\d{1,2})\s+(\d+\.?\d*)", RegexOptions.IgnoreCase);
        private static void UpdateAverageHashRate(string text)
        {
            if (!text.Contains("/s"))
                return;

            var matches = HrUnitsRegex.Matches(text);

            var indMatches = IndividualGpuRegex.Matches(text);
            if (indMatches.Count > 0)
            {
                foreach (Match match in indMatches)
                {
                    //Handle invidivual GPU hashrates
                    if (match.Groups.Count == 4)
                    {
                        var gpuId = int.Parse(match.Groups[2].Value);
                        var gpuHr = decimal.Parse(match.Groups[3].Value);

                        lock (RecentGpuHashrates)
                        {
                            if (gpuHr == 0 && (!RecentGpuHashrates.ContainsKey(gpuId) || RecentGpuHashrates[gpuId] == null || RecentGpuHashrates[gpuId].Count == 0))
                            {
                                //Don't do anything if we are starting up, 0 Hashrate should be ignored until we get at least 1 non-zero hashrate
                                continue;
                            }

                            if (!RecentGpuHashrates.ContainsKey(gpuId))
                                RecentGpuHashrates[gpuId] = new ConcurrentQueue<decimal>();

                            if (RecentGpuHashrates[gpuId].Count < TotalReadingsToAverage)
                            {
                                RecentGpuHashrates[gpuId].Enqueue(gpuHr);
                            }
                            else
                            {
                                decimal trash;
                                if (RecentGpuHashrates[gpuId].TryDequeue(out trash))
                                {
                                    RecentGpuHashrates[gpuId].Enqueue(gpuHr);
                                }
                            }
                        }
                    }
                }
            }


            try
            {
                foreach (Match match in matches)
                {
                    if (match.Groups.Count >= 3)
                    {
                        var hashRate = decimal.Parse(match.Groups[1].Value);
                        var hashrateUnitOfMeasurement = match.Groups[2].Value.Trim();

                        if (ValidHashrateKeywords.Contains(hashrateUnitOfMeasurement.ToLower()))
                        {
                            if (!string.IsNullOrEmpty(_units) && hashrateUnitOfMeasurement != _units)
                            {
                                //If the hashrate changes, eg: going from kh -> Mh at startup
                                ClearHashRates();
                            }

                            _units = hashrateUnitOfMeasurement;
                        }
                        else
                        {
                            continue;
                        }

                        if (hashRate == 0 && RecentHashrates.Count == 0)
                        {
                            //Don't do anything if we are starting up, 0 Hashrate should be ignored until we get at least 1 non-zero hashrate
                            continue;
                        }

                        lock (RecentHashrates)
                        {
                            if (RecentHashrates.Count < TotalReadingsToAverage)
                            {
                                RecentHashrates.Enqueue(hashRate);
                            }
                            else
                            {
                                decimal trash;
                                if (RecentHashrates.TryDequeue(out trash))
                                {
                                    RecentHashrates.Enqueue(hashRate);
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"Warning: Regex failed to match reported hashrates.{Environment.NewLine}\t{text}");
                Log.Error(ex);
            }
        }

        public static void Exit(int exitCode = 0)
        {
            KillMiner();
            
            UpdateMiningMinute(_miningMinute);

            Process.GetCurrentProcess().Kill();
            Environment.Exit(exitCode);
        }
        private static bool ConsoleEventCallback(int eventType)
        {
            if (eventType == (int)NativeMethods.CtrlTypes.CtrlCloseEvent ||
                eventType == (int)NativeMethods.CtrlTypes.CtrlCEvent ||
                eventType == (int)NativeMethods.CtrlTypes.CtrlLogoffEvent ||
                eventType == (int)NativeMethods.CtrlTypes.CtrlShutdownEvent ||
                eventType == (int)NativeMethods.CtrlTypes.CtrlBreakEvent)
            {
                PrettyConsole.WriteLine("Gracefully exiting...", ConsoleColor.Magenta);
                Exit();
            }

            return true;
        }

        private static void UpdateMiningMinute(int newMinute)
        {
            try
            {
                File.WriteAllText(MiningMinuteFile, newMinute.ToString());
            }
            catch (Exception ex)
            {
                Log.Warning("Unable to update current mining minute, schedule may not work properly");
                Log.Error(ex);
            }
        }

        public static void KillMiner()
        {
            ProcessHelper.GracefulShutdownMiner(_minerProcess, _commandLineProcess);
            _commandLineProcess = null;
            _minerProcess = null;

            ClearHashRates();
        }

        private static void ClearHashRates()
        {
            lock (RecentHashrates)
            {
                decimal trash;
                while (RecentHashrates.TryDequeue(out trash))
                {
                }
            }

            lock (RecentGpuHashrates)
            {
                foreach (var key in RecentGpuHashrates.Keys)
                {
                    decimal trash;
                    while (RecentGpuHashrates[key].TryDequeue(out trash))
                    {
                    }
                }
            }
        }

        public static void Pause()
        {
            if (!_paused)
            {
                _paused = true;
                _pausedTime = DateTime.Now;
            }

            
        }

        public static void Unpause()
        {
            _paused = false;
            _pausedTime = null;
        }

        private static void ProcessSocketCommand(string command)
        {
            var cmdObj = new ServerCommand(command);
            cmdObj.Execute(_channels);
        }

        private static void SetActiveMiner(MinerConfig newMinerConfig)
        {
            _currentMiningConfig = newMinerConfig;
            PrettyConsole.WriteLine($"Starting {_currentMiningConfig.Name} - {_currentMiningConfig.ExeLocation} {_currentMiningConfig.CommandLineParameters}");

            KillMiner();

            var match = WalletRegex.Match(newMinerConfig.CommandLineParameters);
            _walletPrefix = match.Groups[1].Value;
            _wallet = match.Groups[2].Value;
            //_workerName = string.IsNullOrEmpty(match.Groups[3].Value.Trim()) ? string.Empty : $"{match.Groups[4].Value}";
        }

        private static void UpdateMonitoringSockets(List<Gpu> gpus, List<Cpu> cpus, decimal averageHashrate, ConsoleColor hashrateColor)
        {
            //Update Monitoring Sockets
            var s = new Schedule
            {
                TotalMinutesInSchedule = MinutesInRotation,
                CurrentMinute = _miningMinute,
                ScheduledConfigs = ScheduledConfigs
            };

            //Verify we are still connected to the Monitoring Socket
            Handshake();

            foreach (var channel in _channels)
            {
                var r = new MiningRig
                {
                    Channels = new List<string> { channel.Name },
                    Address = channel.ShowWalletAddress ? $"{_walletPrefix}{_wallet}" : string.Empty,
                    Name = channel.ShowRigName ? RigName : FingerPrint.Value.Substring(0,10),
                    SecureHardwareId = FingerPrint.Value,
                    Cpus = cpus,
                    Gpus = gpus,
                    Stats = new List<Stats>
                    {
                        new Stats
                        {
                            Code =  _paused ? "_Paused" : (_currentMiningConfig?.CurrencyCode ?? "_Paused"),
                            Color = hashrateColor.ToString(),
                            Rate = averageHashrate,
                            Units = _units
                        }
                    },
                    Completed = _solutionsFound,
                    Failed = _rejectedSolutions,
                    Paused = _paused,
                    Schedule = channel.ShowSchedule ? s : null,
                    Pool = channel.ShowMiningPool ? MiningPool : string.Empty,
                    IsTrustedChannel = channel.IsTrustedChannel,
                    CustomCommands = ServerCommand.CustomCommands
                };

                var json = JsonConvert.SerializeObject(r);

                try
                {
                    _monitoringSocket.Emit("pulse", json);
                }
                catch (Exception ex)
                {
                    PrettyConsole.WriteLine("Unable to connect to monitoring socket, attempting to reconnect...");
                    try
                    {
                        ConnectToMonitoringSocket();
                        _monitoringSocket.Emit("pulse", json);
                    }
                    catch (Exception ex2)
                    {
                        Log.Warning(ex);
                        Log.Warning(ex2);
                    }
                }
            }
        }

        private static void Handshake()
        {
            if (DateTime.Now > _lastHandshake.AddMinutes(1))
            {
                Log.Warning("Haven't received a handshake back from the Monitoring Server in 1 minute, attempting to reconnect...");
                ConnectToMonitoringSocket();
                Thread.Sleep(3000);
            }

            if (DateTime.Now > _lastHandshake.AddSeconds(29))
            {
                _monitoringSocket.Emit("handshake", 1);
            }
        }

        private static void VerifyProcesses()
        {
            LoopStart:

            if (!_running || _paused)
                return;

            if (_minerProcess != null && (_minerProcess.HasExited || !_minerProcess.Responding))
            {
                PrettyConsole.WriteLine(
                    $"{MinerExe}.exe is not running or not responding. Attempting to restart...",
                    ConsoleColor.Yellow);
                KillMiner();
                Thread.Sleep(1000);
            }

            if (_minerProcess == null && !_paused)
            {
                PrettyConsole.WriteLine($"Starting {MinerExe}.exe now.", ConsoleColor.Yellow);

                //Start a new cmd window so that it doesn't intercept ctrl+c in windows 7 and crash drivers during a process kill
                string commandLine = $"/K START /B {MinerLocation} {MinerCommandLineParameters}";
                
                ProcessStartInfo pInfo = new ProcessStartInfo("cmd.exe", commandLine)
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    //RedirectStandardInput = true
                    //Todo: Redirect standard input for miners that accept key commands
                };

                _commandLineProcess = Process.Start(pInfo);

                if (_commandLineProcess != null)
                {
                    //Note: Redirecting Standard Output/Error will only work if the mining sub process flushes console buffers, some miners (eg: xmr-stak) provide a flag to force console buffer flushing
                    _commandLineProcess.EnableRaisingEvents = true;
                    _commandLineProcess.OutputDataReceived += RedirectStandardOutput;
                    _commandLineProcess.ErrorDataReceived += RedirectStandardError;
                    _commandLineProcess.BeginOutputReadLine();
                    _commandLineProcess.BeginErrorReadLine();
                }

                Thread.Sleep(5000);

                Console.Out.Flush();

                _minerProcess = Process.GetProcessesByName(MinerExe).FirstOrDefault();

                goto LoopStart;
            }
        }

        /*
        private static void Debug()
        {
            decimal currentMinute = .1M;
            Random rng = new Random();
            int mode = rng.Next(2);
            //0 = bad
            //1 = okay
            //2 = good
            List<Gpu> gpus = new List<Gpu>();
            var gpu = new Gpu
            {
                Color = mode == 0 ? "Red" : mode == 1 ? "Yellow" : "Cyan",
                Id = "/nvidia/gpu/0",
                Name = "GTX970",
                Temp = mode == 0 ? rng.Next(65, 70) : mode == 1 ? rng.Next(60, 65) : rng.Next(55, 60)
            };
            gpus.Add(gpu);

            var gpu2 = new Gpu
            {
                Color = mode == 0 ? "Red" : mode == 1 ? "Yellow" : "Cyan",
                Id = "/nvidia/gpu/1",
                Name = "GTX960",
                Temp = mode == 0 ? rng.Next(65, 70) : mode == 1 ? rng.Next(60, 65) : rng.Next(55, 60)
            };
            gpus.Add(gpu2);

            for (;;)
            {
                var r = new MiningRig
                {
                    WalletPrefix = "0x",
                    Wallet = "1337",
                    Name = "theRig",
                    Gpus = gpus,
                    Hr = mode == 0 ? rng.Next(40, 50) : mode == 1 ? rng.Next(100, 110) : rng.Next(115, 120),
                    Units = "MH",
                    HrColor = mode == 0 ? "Red" : mode == 1 ? "Yellow" : "Cyan",
                    SolutionsFound = rng.Next(1337),
                    SolutionsRejected = rng.Next(10),
                    Paused = false,
                    ActiveCurrency = "ETH",
                    Channels = new List<string> { "xzon" },
                    MiningPool = "ethermine.org:4444",
                    WorkerName = "the_beast",
                    Schedule = new Schedule {
                        CurrentMinute = (int)decimal.Floor(currentMinute),
                        ScheduledConfigs = new List<ScheduledConfig>
                        {
                            new ScheduledConfig()
                            {
                                MinerCommandLine = "ethminer.exe -a test -o test",
                                Name = "Ethereum",
                                StartTime = 0
                            }
                        },
                        TotalMinutesInSchedule = 1440
                    }
                };

                var json = JsonConvert.SerializeObject(r);

                try
                {
                    MonitoringSocket.Emit("pulse", json);
                    Console.WriteLine("pulse");
                }
                catch (Exception) { }



                r = new MiningRig
                {
                    WalletPrefix = "",
                    Wallet = "",
                    Name = "1234ABCD",
                    Gpus = gpus,
                    Hr = mode == 0 ? rng.Next(40, 50) : mode == 1 ? rng.Next(100, 110) : rng.Next(115, 120),
                    Units = "MH",
                    HrColor = mode == 0 ? "Red" : mode == 1 ? "Yellow" : "Cyan",
                    SolutionsFound = rng.Next(1337),
                    SolutionsRejected = rng.Next(10),
                    Paused = false,
                    ActiveCurrency = "ETH",
                    Channels = new List<string> { "xzon_public" },
                    MiningPool = "",
                    WorkerName = "",
                    Schedule = null
                };

                json = JsonConvert.SerializeObject(r);

                try
                {
                    MonitoringSocket.Emit("pulse", json);
                    Console.WriteLine("pulse");
                }
                catch (Exception) { }



                System.Threading.Thread.Sleep(5000);
            }
        }*/

        private static void ConnectToMonitoringSocket()
        {
            _monitoringSocket?.Disconnect();
            _monitoringSocket?.Close();

            //Open socket to Monitoring Server
            IO.Options options = new IO.Options
            {
                AutoConnect = true,
                Reconnection = true,
                //ReconnectionAttempts = 5,
                //ReconnectionDelay = 5,
                //Timeout = 20, //This causes the socket to never open for some reason
                Secure = true,
                ForceNew = true,
                //Multiplex = true,
                IgnoreServerCertificateValidation = true,
                ForceJsonp = true
            };

            _monitoringSocket = IO.Socket(MonitoringServer, options);

            _monitoringSocket.On(Socket.EVENT_ERROR, () =>
            {
                Log.Warning("Error encountered with Monitoring Socket");
                _monitoringSocket.Disconnect();
            });

            _monitoringSocket.On(Socket.EVENT_DISCONNECT, () =>
            {
                PrettyConsole.WriteLine($"Disconnected from {MonitoringServer} Monitoring Server");
            });
            _monitoringSocket.On(Socket.EVENT_CONNECT, () =>
            {
                PrettyConsole.WriteLine("Connected to Monitoring Socket", ConsoleColor.Green);

                foreach (var channel in _channels)
                {
                    var cr = new Credentials
                    {
                        ChannelName = channel.Name,
                        Password = channel.Password,
                        IsRig = true,
                        IsTrustedChannel = channel.IsTrustedChannel,
                        Version = $"Xzon{Version}"
                    };

                    var json = JsonConvert.SerializeObject(cr);

                    PrettyConsole.WriteLine($"Joining channel {channel.Name}", ConsoleColor.Green);
                    PrettyConsole.WriteLine($"\tTrusted Channel: {channel.IsTrustedChannel}", channel.IsTrustedChannel ? ConsoleColor.Green : ConsoleColor.Yellow);
                    PrettyConsole.WriteLine($"\tShow Rig Name: {channel.ShowRigName}");
                    PrettyConsole.WriteLine($"\tShow Wallet Address: {channel.ShowWalletAddress}");
                    PrettyConsole.WriteLine($"\tShow Mining Pool: {channel.ShowMiningPool}");
                    PrettyConsole.WriteLine($"\tShow Schedule: {channel.ShowSchedule}");

                    _monitoringSocket.Emit("auth", json);
                    Thread.Sleep(100);
                }

                _lastHandshake = DateTime.Now;
            });
            _monitoringSocket.On("executeCommand", (data) =>
            {
                PrettyConsole.WriteLine($"Command Requested: {data}", ConsoleColor.DarkMagenta);
                ProcessSocketCommand(data.ToString());
            });
            _monitoringSocket.On("authFailed", (data) =>
            {
                PrettyConsole.Write($"Authentication to channel {data}: ");
                PrettyConsole.WriteLine("FAILED", ConsoleColor.Red);
            });
            _monitoringSocket.On("authSuccess", (data) =>
            {
                PrettyConsole.Write($"Authentication to channel {data}: ");
                PrettyConsole.WriteLine("SUCCESS", ConsoleColor.Green);
            });
            _monitoringSocket.On("handshake", (data) =>
            {
                _lastHandshake = DateTime.Now;
                //Log.Informational("Handshake Successful");
            });
        }
    }
}
