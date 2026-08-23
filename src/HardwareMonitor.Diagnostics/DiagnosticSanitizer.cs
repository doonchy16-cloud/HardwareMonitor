namespace TheSpark.HardwareMonitor.Diagnostics;

public static class DiagnosticSanitizer
{
    public static string SanitizeValue(string? value, int maxLength = 512)
    {
        if (maxLength <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxLength));
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var cleaned = value
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Replace('\t', ' ');

        while (cleaned.Contains("  ", StringComparison.Ordinal))
        {
            cleaned = cleaned.Replace("  ", " ", StringComparison.Ordinal);
        }

        cleaned = cleaned.Trim();
        return cleaned.Length <= maxLength ? cleaned : cleaned[..maxLength];
    }
}
