using System.Xml.Linq;

namespace TheSpark.HardwareMonitor.Setup.Tests;

public sealed class AppInstallerPolicyTests
{
    [Fact]
    public void Stable_appinstaller_checks_every_launch_without_prompt_and_has_repair_fallback()
    {
        var templatePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "HardwareMonitor.appinstaller.template");
        var document = XDocument.Load(templatePath);
        var root = Assert.NotNull(document.Root);
        var ns = root.Name.Namespace;

        var onLaunch = Assert.Single(root.Descendants(ns + "OnLaunch"));
        Assert.Equal("0", (string?)onLaunch.Attribute("HoursBetweenUpdateChecks"));
        Assert.Equal("false", (string?)onLaunch.Attribute("ShowPrompt"));
        Assert.Equal("false", (string?)onLaunch.Attribute("UpdateBlocksActivation"));

        var repairUris = Assert.Single(root.Elements(ns + "RepairUris"));
        var repairUri = Assert.Single(repairUris.Elements(ns + "RepairUri"));
        Assert.Equal("__APPINSTALLER_URI__", repairUri.Value.Trim());
    }
}
