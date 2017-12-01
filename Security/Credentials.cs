namespace XzonControlPanel.Security
{
    public class Credentials
    {
        public string ChannelName { get; set; }
        public string Password { get; set; }
        public bool IsRig { get; set; }
        public bool IsTrustedChannel { get; set; }
        public string Version { get; set; }
    }
}
