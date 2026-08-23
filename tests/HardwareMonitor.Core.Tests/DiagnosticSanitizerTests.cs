using TheSpark.HardwareMonitor.Diagnostics;

namespace TheSpark.HardwareMonitor.Core.Tests;

public sealed class DiagnosticSanitizerTests
{
    [Fact]
    public void Sanitizer_removes_newline_injection()
    {
        Assert.Equal("GPU driver failed details", DiagnosticSanitizer.SanitizeValue("GPU driver failed\r\ndetails"));
    }

    [Fact]
    public void Sanitizer_caps_untrusted_field_length()
    {
        var result = DiagnosticSanitizer.SanitizeValue(new string('x', 900), 128);

        Assert.Equal(128, result.Length);
    }

    [Fact]
    public void Sanitizer_returns_empty_for_null()
    {
        Assert.Equal(string.Empty, DiagnosticSanitizer.SanitizeValue(null));
    }
}
