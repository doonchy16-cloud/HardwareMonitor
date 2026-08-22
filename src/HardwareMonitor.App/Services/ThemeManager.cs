using System.Windows;

namespace TheSpark.HardwareMonitor.App.Services;

public static class ThemeManager
{
    public static IReadOnlyList<string> Themes { get; } = ["Light", "Dark", "Forgey Core"];

    public static void Apply(string? theme)
    {
        var normalized = Themes.Contains(theme, StringComparer.OrdinalIgnoreCase) ? theme! : "Dark";
        var file = normalized.Equals("Light", StringComparison.OrdinalIgnoreCase)
            ? "Light.xaml"
            : normalized.Equals("Forgey Core", StringComparison.OrdinalIgnoreCase)
                ? "ForgeyCore.xaml"
                : "Dark.xaml";

        var resources = Application.Current.Resources.MergedDictionaries;
        resources.Clear();
        resources.Add(new ResourceDictionary
        {
            Source = new Uri($"Themes/{file}", UriKind.Relative)
        });
    }
}
