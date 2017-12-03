using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Management;
using OpenHardwareMonitor.Hardware;
using XzonControlPanel.Logging;

namespace XzonControlPanel.Rig
{
    public class Temperature : IComparable<Temperature>
    {
        private static readonly Computer Computer;
        private static UpdateVisitor updateVisitor;
        public double CurrentValue { get; private set; }
        public string InstanceName { get; private set; }
        public string InstanceId { get; private set; }
        
        static Temperature()
        {
            Computer = new Computer
            {
                GPUEnabled = true,
                CPUEnabled = true
            };

            Computer.HardwareRemoved += RemovedHandler;

            Computer.Open();
            updateVisitor = new UpdateVisitor();
        }

        private static void RemovedHandler(IHardware hardware)
        {
            Console.WriteLine($"{hardware.Name} removed!");
            Debugger.Break();
        }

        public static void Refresh()
        {
            Computer.Accept(updateVisitor);
        }

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
                    List<Temperature> ret = new List<Temperature>();

                    var cpus = Computer.Hardware.Where(h => h.HardwareType == HardwareType.CPU);
                    foreach (var cpu in cpus)
                    {
                        var sensors = cpu.Sensors.Where(s => s.SensorType == SensorType.Temperature).ToList();
                        sensors.Sort((a, b) => a.Value < b.Value ? 1 : -1);

                        ret.Add(new Temperature
                        {
                            CurrentValue = Convert.ToDouble(sensors.FirstOrDefault()?.Value),
                            InstanceId = sensors.FirstOrDefault()?.Hardware.Identifier.ToString(),
                            InstanceName = sensors.FirstOrDefault()?.Hardware.Name
                        });
                    }

                    return ret;
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
                    List<Temperature> ret = new List<Temperature>();

                    var gpus = Computer.Hardware.Where(h => h.HardwareType == HardwareType.GpuAti || h.HardwareType == HardwareType.GpuNvidia);
                    foreach (var gpu in gpus)
                    {
                        var sensors = gpu.Sensors.Where(s => s.SensorType == SensorType.Temperature).ToList();
                        sensors.Sort((a, b) => a.Value < b.Value ? 1 : -1);

                        ret.Add(new Temperature
                        {
                            CurrentValue = Convert.ToDouble(sensors.FirstOrDefault()?.Value),
                            InstanceId = sensors.FirstOrDefault()?.Hardware.Identifier.ToString(),
                            InstanceName = sensors.FirstOrDefault()?.Hardware.Name
                        });
                    }

                    return ret;
                }
                catch (Exception ex)
                {
                    PrettyConsole.WriteLine($"Error trying to retrieve GPU temperatures. Error: {ex.Message}", ConsoleColor.Red);
                    Log.Error(ex);

                    return null;
                }
            }
        }
        private class UpdateVisitor : IVisitor
        {
            public void VisitComputer(IComputer computer)
            {
                computer.Traverse(this);
            }

            public void VisitHardware(IHardware hardware)
            {
                hardware.Update();
                foreach (IHardware subHardware in hardware.SubHardware)
                    subHardware.Accept(this);
            }

            public void VisitSensor(ISensor sensor) { }

            public void VisitParameter(IParameter parameter) { }
        }
    }
}
