using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using TheSpark.HardwareMonitor.Core.Models;
using TheSpark.HardwareMonitor.Core.Profiles;

namespace TheSpark.HardwareMonitor.App.ViewModels;

public sealed class ProfileEditorViewModel : INotifyPropertyChanged
{
    private readonly HashSet<ProfileCapability> _capabilities;
    private string _name;
    private Guid? _deviceId;
    private bool _enabled;
    private ViewerScope _viewerScope;
    private double _staleAfterSeconds;
    private double _offlineAfterSeconds;
    private string? _validationError;

    internal ProfileEditorViewModel(HardwareProfile? profile)
    {
        IsNew = profile is null;
        ProfileId = profile?.ProfileId ?? Guid.NewGuid();
        OriginalRevision = profile?.Revision ?? 0;
        _name = profile?.Name ?? string.Empty;
        _deviceId = profile?.DeviceId;
        _enabled = profile?.Enabled ?? true;
        _viewerScope = profile?.ViewerScope ?? ViewerScope.None;
        _staleAfterSeconds = profile?.FreshnessPolicy.StaleAfter.TotalSeconds ?? 5;
        _offlineAfterSeconds = profile?.FreshnessPolicy.OfflineAfter.TotalSeconds ?? 20;
        _capabilities = profile is null
            ? new HashSet<ProfileCapability>()
            : new HashSet<ProfileCapability>(profile.Capabilities);
        VisibleProfileIds = profile is null
            ? new ObservableCollection<Guid>()
            : new ObservableCollection<Guid>(profile.VisibleProfileIds);
        VisibleSensorKinds = profile is null
            ? new ObservableCollection<SensorKind>()
            : new ObservableCollection<SensorKind>(profile.SensorVisibilityPolicy.VisibleKinds);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public Guid ProfileId { get; }
    public bool IsNew { get; }
    internal long OriginalRevision { get; }

    public string Name
    {
        get => _name;
        set => SetField(ref _name, value ?? string.Empty);
    }

    public Guid? DeviceId
    {
        get => _deviceId;
        set => SetField(ref _deviceId, value);
    }

    public bool Enabled
    {
        get => _enabled;
        set => SetField(ref _enabled, value);
    }

    public ViewerScope ViewerScope
    {
        get => _viewerScope;
        set => SetField(ref _viewerScope, value);
    }

    public double StaleAfterSeconds
    {
        get => _staleAfterSeconds;
        set => SetField(ref _staleAfterSeconds, value);
    }

    public double OfflineAfterSeconds
    {
        get => _offlineAfterSeconds;
        set => SetField(ref _offlineAfterSeconds, value);
    }

    public ObservableCollection<Guid> VisibleProfileIds { get; }
    public ObservableCollection<SensorKind> VisibleSensorKinds { get; }
    public IReadOnlySet<ProfileCapability> Capabilities => _capabilities;

    public string? ValidationError
    {
        get => _validationError;
        private set => SetField(ref _validationError, value);
    }

    public void SetCapability(ProfileCapability capability, bool enabled)
    {
        var changed = enabled ? _capabilities.Add(capability) : _capabilities.Remove(capability);
        if (changed)
        {
            OnPropertyChanged(nameof(Capabilities));
        }
    }

    public bool HasCapability(ProfileCapability capability) => _capabilities.Contains(capability);

    public bool TryBuildProfile(out HardwareProfile? profile)
    {
        return TryBuildProfile(OriginalRevision, out profile);
    }

    internal bool TryBuildProfile(long revision, out HardwareProfile? profile)
    {
        try
        {
            var freshness = new FreshnessPolicy(
                TimeSpan.FromSeconds(StaleAfterSeconds),
                TimeSpan.FromSeconds(OfflineAfterSeconds));

            profile = new HardwareProfile(
                ProfileId,
                Name,
                DeviceId,
                new HashSet<ProfileCapability>(_capabilities),
                ViewerScope,
                new HashSet<Guid>(VisibleProfileIds),
                freshness,
                Enabled,
                revision,
                new SensorVisibilityPolicy(new HashSet<SensorKind>(VisibleSensorKinds)));
            ValidationError = null;
            return true;
        }
        catch (ArgumentOutOfRangeException ex)
        {
            profile = null;
            ValidationError = ex.ParamName?.Contains("offline", StringComparison.OrdinalIgnoreCase) == true
                ? "Offline threshold must be later than the stale threshold."
                : "Stale threshold must be positive and offline must be later.";
            return false;
        }
        catch (ArgumentException ex)
        {
            profile = null;
            ValidationError = ex.Message;
            return false;
        }
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
