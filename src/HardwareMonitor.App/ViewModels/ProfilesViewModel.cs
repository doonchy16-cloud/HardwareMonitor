using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using TheSpark.HardwareMonitor.Core.Profiles;

namespace TheSpark.HardwareMonitor.App.ViewModels;

public sealed class ProfilesViewModel : INotifyPropertyChanged
{
    private readonly IProfileRepository _repository;
    private long _registryRevision;
    private string? _errorMessage;

    public ProfilesViewModel(IProfileRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        Profiles = new ObservableCollection<HardwareProfile>();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<HardwareProfile> Profiles { get; }
    public bool IsEmpty => Profiles.Count == 0;

    public string? ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            if (string.Equals(_errorMessage, value, StringComparison.Ordinal))
            {
                return;
            }

            _errorMessage = value;
            OnPropertyChanged();
        }
    }

    public async Task LoadAsync()
    {
        var loaded = await _repository.LoadAsync().ConfigureAwait(false);
        if (!loaded.Success || loaded.Snapshot is null)
        {
            ErrorMessage = loaded.Error ?? "Profile repository could not be loaded.";
            throw new InvalidDataException(ErrorMessage);
        }

        ReplaceFromSnapshot(loaded.Snapshot);
        ErrorMessage = null;
    }

    public ProfileEditorViewModel CreateEditor() => new(null);

    public ProfileEditorViewModel EditProfile(Guid profileId)
    {
        var profile = Profiles.FirstOrDefault(item => item.ProfileId == profileId)
            ?? throw new KeyNotFoundException($"Profile '{profileId}' was not found.");
        return new ProfileEditorViewModel(profile);
    }

    public async Task SaveEditorAsync(ProfileEditorViewModel editor)
    {
        ArgumentNullException.ThrowIfNull(editor);

        var existingIndex = Profiles
            .Select((profile, index) => (profile, index))
            .Where(item => item.profile.ProfileId == editor.ProfileId)
            .Select(item => item.index)
            .DefaultIfEmpty(-1)
            .First();

        if (!editor.IsNew && existingIndex < 0)
        {
            throw new KeyNotFoundException($"Profile '{editor.ProfileId}' was not found.");
        }

        var nextProfileRevision = editor.IsNew ? 1 : editor.OriginalRevision + 1;
        if (!editor.TryBuildProfile(nextProfileRevision, out var profile) || profile is null)
        {
            throw new InvalidOperationException(editor.ValidationError ?? "Profile is invalid.");
        }

        var nextProfiles = Profiles.ToList();
        if (existingIndex < 0)
        {
            nextProfiles.Add(profile);
        }
        else
        {
            nextProfiles[existingIndex] = profile;
        }

        await PersistAsync(nextProfiles).ConfigureAwait(false);
    }

    public async Task DeleteProfileAsync(Guid profileId)
    {
        var nextProfiles = Profiles.Where(profile => profile.ProfileId != profileId).ToList();
        if (nextProfiles.Count == Profiles.Count)
        {
            throw new KeyNotFoundException($"Profile '{profileId}' was not found.");
        }

        await PersistAsync(nextProfiles).ConfigureAwait(false);
    }

    public async Task SetEnabledAsync(Guid profileId, bool enabled)
    {
        var current = Profiles.FirstOrDefault(profile => profile.ProfileId == profileId)
            ?? throw new KeyNotFoundException($"Profile '{profileId}' was not found.");
        if (current.Enabled == enabled)
        {
            return;
        }

        var replacement = new HardwareProfile(
            current.ProfileId,
            current.Name,
            current.DeviceId,
            new HashSet<ProfileCapability>(current.Capabilities),
            current.ViewerScope,
            new HashSet<Guid>(current.VisibleProfileIds),
            current.FreshnessPolicy,
            enabled,
            current.Revision + 1,
            current.SensorVisibilityPolicy);

        var nextProfiles = Profiles
            .Select(profile => profile.ProfileId == profileId ? replacement : profile)
            .ToList();
        await PersistAsync(nextProfiles).ConfigureAwait(false);
    }

    private async Task PersistAsync(IReadOnlyList<HardwareProfile> profiles)
    {
        var snapshot = new ProfileRegistrySnapshot(_registryRevision + 1, profiles);
        await _repository.SaveAsync(snapshot).ConfigureAwait(false);
        ReplaceFromSnapshot(snapshot);
        ErrorMessage = null;
    }

    private void ReplaceFromSnapshot(ProfileRegistrySnapshot snapshot)
    {
        Profiles.Clear();
        foreach (var profile in snapshot.Profiles)
        {
            Profiles.Add(profile);
        }

        _registryRevision = snapshot.Revision;
        OnPropertyChanged(nameof(Profiles));
        OnPropertyChanged(nameof(IsEmpty));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
