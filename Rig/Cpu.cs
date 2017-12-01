using System.Collections.Generic;

namespace XzonControlPanel.Rig
{
    public class Cpu
    {
        public string Name { get; set; }
        public string Id { get; set; }
        public int Temp { get; set; }
        public string Color { get; set; }
        public decimal Rate { get; set; }
        public List<Stats> Stats { get; set; }
    }
}
