using System;
using System.Collections.Generic;
using System.Management;
using XzonControlPanel.Logging;

namespace XzonControlPanel.Rig
{
    public class Temperature : IComparable<Temperature>
    {
        public double CurrentValue { get; private set; }
        public string InstanceName { get; private set; }
        public string InstanceId { get; private set; }

        public int CompareTo(Temperature t)
        {
            return string.Compare(InstanceId, t.InstanceId, StringComparison.Ordinal);
        }

        public static List<Temperature> CpuTemperatures
        {
            get
            {
                try
                {
                    List<Temperature> result = new List<Temperature>();
                    ManagementObjectSearcher searcher = new ManagementObjectSearcher(@"root\OpenHardwareMonitor", "SELECT * FROM Sensor");
                    Dictionary<string, string> hardwareList = new Dictionary<string, string>();

                    foreach (var hw in new ManagementObjectSearcher(@"root\OpenHardwareMonitor", "SELECT * FROM Hardware").Get())
                    {
                        hardwareList.Add(hw["identifier"].ToString(), hw["Name"].ToString());
                    }

                    foreach (var obj in searcher.Get())
                    {
                        if (obj["SensorType"].ToString() == "Temperature" &&
                            obj["Name"].ToString().Contains("CPU"))
                        {
                            result.Add(new Temperature
                            {
                                CurrentValue = double.Parse(obj["Value"].ToString()),
                                InstanceName = $"{hardwareList[obj["Parent"].ToString()]} {obj["Name"]}",
                                InstanceId = obj["Parent"].ToString()
                            });
                        }
                    }

                    result.Sort();
                    return result.Count == 0 ? new List<Temperature>() : result;
                }
                catch (Exception ex)
                {
                    Log.Warning($"Error trying to retrieve CPU temperatures. Error: {ex.Message}");
                    
                    return new List<Temperature>();
                }
            }
        }
        public static List<Temperature> GpuTemperatures
        {
            get
            {
                try
                {
                    List<Temperature> result = new List<Temperature>();
                    ManagementObjectSearcher searcher = new ManagementObjectSearcher(@"root\OpenHardwareMonitor", "SELECT * FROM Sensor");
                    Dictionary<string, string> hardwareList = new Dictionary<string, string>();

                    foreach (var hw in new ManagementObjectSearcher(@"root\OpenHardwareMonitor", "SELECT * FROM Hardware").Get())
                    {
                        hardwareList.Add(hw["identifier"].ToString(), hw["Name"].ToString());
                    }

                    foreach (var obj in searcher.Get())
                    {
                        if (obj["SensorType"].ToString() == "Temperature" &&
                            obj["Name"].ToString().Contains("GPU"))
                        {
                            result.Add(new Temperature
                            {
                                CurrentValue = double.Parse(obj["Value"].ToString()),
                                InstanceName = $"{hardwareList[obj["Parent"].ToString()]} {obj["Name"]}",
                                InstanceId = obj["Parent"].ToString()
                            });
                        }
                    }

                    result.Sort();
                    return result.Count == 0 ? null : result;
                }
                catch (Exception ex)
                {
                    PrettyConsole.WriteLine("Error trying to retrieve GPU temperatures. Unsafe to mine, killing process.", ConsoleColor.Red);
                    PrettyConsole.WriteLine(ex);

                    Log.Error(ex);

                    return null;
                }
            }
        }
    }
}
