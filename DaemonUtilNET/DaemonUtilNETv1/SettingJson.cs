namespace DaemonUtilNETv1
{
    internal class SettingJson
    {
        public class SettingItem
        {
            public required string taskName { get; set; }
            public required string workFolder { get; set; }
            public required string programName { get; set; }
            public required string runParameter { get; set; }
        }
    }
}
