using System.Xml.Linq;

namespace TheSpark.HardwareMonitor.Setup.Tests;

public sealed class InstallerManifestTests
{
    [Fact]
    public void Bootstrap_runs_as_invoker_so_per_user_install_stays_in_the_calling_profile()
    {
        var manifestPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "app.manifest");
        var document = XDocument.Load(manifestPath);
        XNamespace asmV3 = "urn:schemas-microsoft-com:asm.v3";

        var requestedExecutionLevel = document
            .Descendants(asmV3 + "requestedExecutionLevel")
            .Single();

        Assert.Equal("asInvoker", (string?)requestedExecutionLevel.Attribute("level"));
        Assert.Equal("false", (string?)requestedExecutionLevel.Attribute("uiAccess"));
    }
}
