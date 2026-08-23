using System.Text.Json;
using System.Text.Json.Serialization;
using TheSpark.HardwareMonitor.Core.Models;

namespace TheSpark.HardwareMonitor.Core.Profiles;

public sealed class JsonProfileRepository : IProfileRepository
{
    private const string SchemaVersion = "1.0";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _path;

    public JsonProfileRepository(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Profile cache path must not be empty.", nameof(path));
        }

        _path = Path.GetFullPath(path);
    }

    public async Task<ProfileRepositoryLoadResult> LoadAsync()
    {
        if (!File.Exists(_path))
        {
            return ProfileRepositoryLoadResult.Loaded(ProfileRegistrySnapshot.Empty);
        }

        try
        {
            var json = await File.ReadAllTextAsync(_path).ConfigureAwait(false);
            var document = JsonSerializer.Deserialize<RegistryDocument>(json, JsonOptions)
                ?? throw new InvalidDataException("Profile cache document is empty.");

            if (!string.Equals(document.SchemaVersion, SchemaVersion, StringComparison.Ordinal))
            {
                throw new InvalidDataException($"Unsupported profile cache schema '{document.SchemaVersion}'.");
            }

            if (document.Revision < 0)
            {
                throw new InvalidDataException("Profile registry revision must not be negative.");
            }

            var profiles = (document.Profiles ?? Array.Empty<ProfileDocument>())
                .Select(ToDomain)
                .ToArray();

            if (profiles.Select(profile => profile.ProfileId).Distinct().Count() != profiles.Length)
            {
                throw new InvalidDataException("Profile cache contains duplicate profile IDs.");
            }

            return ProfileRepositoryLoadResult.Loaded(
                new ProfileRegistrySnapshot(document.Revision, profiles));
        }
        catch (Exception ex) when (
            ex is JsonException or IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
        {
            return ProfileRepositoryLoadResult.Failed(
                $"Profile cache could not be loaded: {ex.Message}");
        }
    }

    public async Task SaveAsync(ProfileRegistrySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var directory = Path.GetDirectoryName(_path)
            ?? throw new InvalidOperationException("Profile cache path has no parent directory.");
        Directory.CreateDirectory(directory);

        var document = new RegistryDocument
        {
            SchemaVersion = SchemaVersion,
            Revision = snapshot.Revision,
            Profiles = snapshot.Profiles.Select(ToDocument).ToArray()
        };

        var json = JsonSerializer.Serialize(document, JsonOptions) + Environment.NewLine;
        var tempPath = Path.Combine(
            directory,
            $"{Path.GetFileName(_path)}.{Guid.NewGuid():N}.tmp");

        try
        {
            await File.WriteAllTextAsync(tempPath, json).ConfigureAwait(false);

            await using (var stream = new FileStream(
                tempPath,
                FileMode.Open,
                FileAccess.ReadWrite,
                FileShare.Read,
                bufferSize: 4096,
                FileOptions.WriteThrough))
            {
                stream.Flush(flushToDisk: true);
            }

            File.Move(tempPath, _path, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                try
                {
                    File.Delete(tempPath);
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }
    }

    private static HardwareProfile ToDomain(ProfileDocument document)
    {
        if (document.ProfileId == Guid.Empty)
        {
            throw new InvalidDataException("Profile ID must not be empty.");
        }

        var freshness = document.Freshness
            ?? throw new InvalidDataException("Profile freshness policy is missing.");

        return new HardwareProfile(
            document.ProfileId,
            document.Name ?? string.Empty,
            document.DeviceId,
            new HashSet<ProfileCapability>(document.Capabilities ?? Array.Empty<ProfileCapability>()),
            document.ViewerScope,
            new HashSet<Guid>(document.VisibleProfileIds ?? Array.Empty<Guid>()),
            new FreshnessPolicy(
                TimeSpan.FromSeconds(freshness.StaleAfterSeconds),
                TimeSpan.FromSeconds(freshness.OfflineAfterSeconds)),
            document.Enabled,
            document.Revision,
            new SensorVisibilityPolicy(
                new HashSet<SensorKind>(document.VisibleSensorKinds ?? Array.Empty<SensorKind>())));
    }

    private static ProfileDocument ToDocument(HardwareProfile profile) =>
        new()
        {
            ProfileId = profile.ProfileId,
            Name = profile.Name,
            DeviceId = profile.DeviceId,
            Capabilities = profile.Capabilities.OrderBy(value => value).ToArray(),
            ViewerScope = profile.ViewerScope,
            VisibleProfileIds = profile.VisibleProfileIds.OrderBy(value => value).ToArray(),
            Freshness = new FreshnessDocument
            {
                StaleAfterSeconds = profile.FreshnessPolicy.StaleAfter.TotalSeconds,
                OfflineAfterSeconds = profile.FreshnessPolicy.OfflineAfter.TotalSeconds
            },
            VisibleSensorKinds = profile.SensorVisibilityPolicy.VisibleKinds.OrderBy(value => value).ToArray(),
            Enabled = profile.Enabled,
            Revision = profile.Revision
        };

    private sealed class RegistryDocument
    {
        public string? SchemaVersion { get; set; }
        public long Revision { get; set; }
        public ProfileDocument[]? Profiles { get; set; }
    }

    private sealed class ProfileDocument
    {
        public Guid ProfileId { get; set; }
        public string? Name { get; set; }
        public Guid? DeviceId { get; set; }
        public ProfileCapability[]? Capabilities { get; set; }
        public ViewerScope ViewerScope { get; set; }
        public Guid[]? VisibleProfileIds { get; set; }
        public FreshnessDocument? Freshness { get; set; }
        public SensorKind[]? VisibleSensorKinds { get; set; }
        public bool Enabled { get; set; }
        public long Revision { get; set; }
    }

    private sealed class FreshnessDocument
    {
        public double StaleAfterSeconds { get; set; }
        public double OfflineAfterSeconds { get; set; }
    }
}
