using System.Runtime.InteropServices;

namespace Yetta.Core;

/// <summary>Read-only runtime data; does not query other processes.</summary>
public static class SystemSnapshot
{
    public static string Os => RuntimeInformation.OSDescription;
    public static string Architecture => RuntimeInformation.OSArchitecture.ToString();
    public static string Runtime => RuntimeInformation.FrameworkDescription;
    public static int LogicalProcessors => Environment.ProcessorCount;
    public static string User => Environment.UserName;
    public static string Machine => Environment.MachineName;
    public static TimeSpan Uptime => TimeSpan.FromMilliseconds(Environment.TickCount64);
}
