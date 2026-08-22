namespace TheSpark.HardwareMonitor.App.Services;

public sealed class AppSettings
{
    public string Theme { get; set; } = "Dark";
    public string Motion { get; set; } = "Full";
    public int PollIntervalMilliseconds { get; set; } = 1000;
    public string TemperatureUnit { get; set; } = "Celsius";
    public double WindowWidth { get; set; } = 1180;
    public double WindowHeight { get; set; } = 780;
    public double? WindowLeft { get; set; }
    public double? WindowTop { get; set; }
    public string LastPage { get; set; } = "Dashboard";
}
