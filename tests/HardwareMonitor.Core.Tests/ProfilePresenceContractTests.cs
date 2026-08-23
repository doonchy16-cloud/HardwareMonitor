using System.Reflection;
using TheSpark.HardwareMonitor.Core.Profiles;

namespace TheSpark.HardwareMonitor.Core.Tests;

public sealed class ProfilePresenceContractTests
{
    private static readonly Assembly CoreAssembly = typeof(ProfileContract).Assembly;
    private const string Namespace = "TheSpark.HardwareMonitor.Core.Profiles.Presence";

    [Fact]
    public void Phase5_presence_contract_surface_exists()
    {
        var expectedTypes = new[]
        {
            $"{Namespace}.ProfileConnectivityState",
            $"{Namespace}.ProfileTelemetryPresentation",
            $"{Namespace}.ProfilePresenceSnapshot",
            $"{Namespace}.ProfilePresenceEvaluator",
        };

        foreach (var typeName in expectedTypes)
        {
            Assert.NotNull(CoreAssembly.GetType(typeName));
        }
    }

    [Fact]
    public void Presence_evaluator_exposes_one_pure_evaluate_entry_point()
    {
        var evaluator = CoreAssembly.GetType($"{Namespace}.ProfilePresenceEvaluator");
        Assert.NotNull(evaluator);

        var methods = evaluator!
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(method => method.DeclaringType == evaluator)
            .ToArray();

        var evaluate = Assert.Single(methods);
        Assert.Equal("Evaluate", evaluate.Name);
        Assert.Equal(2, evaluate.GetParameters().Length);
    }
}
