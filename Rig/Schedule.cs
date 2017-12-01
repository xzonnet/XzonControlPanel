using System.Collections.Generic;

namespace XzonControlPanel.Rig
{
    public class Schedule
    {
        public int CurrentMinute { get; set; }
        public int TotalMinutesInSchedule { get; set; }
        public List<ScheduledConfig> ScheduledConfigs { get; set; }
    }
}
