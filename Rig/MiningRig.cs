using System.Collections.Generic;

namespace XzonControlPanel.Rig
{
    public class MiningRig
    {
        public string Name { get; set; }
        public List<string> Channels { get; set; }
        public string Address { get; set; }
        public List<Gpu> Gpus { get; set; }
        public List<Cpu> Cpus { get; set; }
        public List<Stats> Stats { get; set; }
        public int Completed { get; set; }
        public int Failed { get; set; }
        public bool Paused { get; set; }
        public Schedule Schedule { get; set; }
        public string Pool { get; set; }
        public bool IsTrustedChannel { get; set; }
        public List<string> CustomCommands { get; set; }
    }
}
