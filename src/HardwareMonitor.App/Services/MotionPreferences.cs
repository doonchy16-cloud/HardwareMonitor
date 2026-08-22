namespace TheSpark.HardwareMonitor.App.Services;

public static class MotionPreferences
{
    public static string Level { get; set; } = "Full";

    public static bool Enabled => !Level.Equals("Off", StringComparison.OrdinalIgnoreCase);

    public static bool Full => Level.Equals("Full", StringComparison.OrdinalIgnoreCase);

    public static TimeSpan Duration(int fullMilliseconds, int reducedMilliseconds = 80) =>
        !Enabled ? TimeSpan.Zero : TimeSpan.FromMilliseconds(Full ? fullMilliseconds : reducedMilliseconds);
}
