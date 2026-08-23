using TheSpark.HardwareMonitor.App.ViewModels;
using TheSpark.HardwareMonitor.Core.Profiles;
using Xunit;

namespace TheSpark.HardwareMonitor.App.Tests;

public sealed class ProfileThermalEditorTests
{
    [Fact]
    public void New_profile_editor_exposes_default_thermal_thresholds()
    {
        var editor = new ProfileEditorViewModelAccessor(null).Editor;

        Assert.Equal(70, editor.WarmCelsius);
        Assert.Equal(82, editor.HotCelsius);
        Assert.Equal(92, editor.CriticalCelsius);
    }

    [Fact]
    public void User_can_configure_valid_thermal_thresholds_in_profile_editor()
    {
        var editor = new ProfileEditorViewModelAccessor(null).Editor;
        editor.Name = "Custom training thresholds";
        editor.WarmCelsius = 76;
        editor.HotCelsius = 86;
        editor.CriticalCelsius = 94;

        Assert.True(editor.TryBuildProfile(out var profile));
        Assert.NotNull(profile);
        Assert.Equal(76, profile!.ThermalThresholdPolicy.WarmCelsius);
        Assert.Equal(86, profile.ThermalThresholdPolicy.HotCelsius);
        Assert.Equal(94, profile.ThermalThresholdPolicy.CriticalCelsius);
    }

    [Fact]
    public void Invalid_thermal_order_is_rejected_before_save()
    {
        var editor = new ProfileEditorViewModelAccessor(null).Editor;
        editor.Name = "Invalid until fixed";
        editor.WarmCelsius = 90;
        editor.HotCelsius = 80;
        editor.CriticalCelsius = 95;

        Assert.False(editor.TryBuildProfile(out _));
        Assert.NotNull(editor.ValidationError);
        Assert.Contains("thermal", editor.ValidationError!, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class ProfileEditorViewModelAccessor
    {
        public ProfileEditorViewModelAccessor(HardwareProfile? profile)
        {
            var constructor = typeof(ProfileEditorViewModel).GetConstructor(
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
                binder: null,
                [typeof(HardwareProfile)],
                modifiers: null)
                ?? throw new InvalidOperationException("Profile editor constructor not found.");
            Editor = (ProfileEditorViewModel)constructor.Invoke([profile]);
        }

        public ProfileEditorViewModel Editor { get; }
    }
}
