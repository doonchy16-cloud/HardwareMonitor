using System.Text.Json;
using TheSpark.HardwareMonitor.Core.Remote;
using Xunit;

namespace TheSpark.HardwareMonitor.Core.Tests;

public sealed class RemoteEnvelopeTests
{
    [Fact]
    public void TelemetryEnvelopeSerializesExactGatewayContractWithoutAuthorityFields()
    {
        var envelope = new TelemetryEnvelope(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            DateTimeOffset.Parse("2026-08-23T07:30:00Z"),
            "Training",
            "Healthy",
            [new TelemetryMetricEnvelope("gpu.temperature", "GPU", 74, null, "°C", "Available")]);

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(envelope));
        var root = document.RootElement;
        var keys = root.EnumerateObject().Select(item => item.Name).OrderBy(value => value).ToArray();

        Assert.Equal(
            new[] { "activity", "captured_at", "device_id", "health", "metrics", "profile_id", "schema_version" },
            keys);
        Assert.Equal("1.0", root.GetProperty("schema_version").GetString());
        Assert.Equal("11111111-1111-1111-1111-111111111111", root.GetProperty("device_id").GetString());
        Assert.Equal("22222222-2222-2222-2222-222222222222", root.GetProperty("profile_id").GetString());
        Assert.Equal(74, root.GetProperty("metrics")[0].GetProperty("numeric_value").GetDouble());

        var json = root.GetRawText().ToLowerInvariant();
        Assert.DoesNotContain("token", json);
        Assert.DoesNotContain("password", json);
        Assert.DoesNotContain("credential", json);
        Assert.DoesNotContain("installation_id", json);
        Assert.DoesNotContain("authority", json);
    }

    [Fact]
    public void PresenceEnvelopeSerializesOnlySanitizedPresenceFields()
    {
        var envelope = new PresenceEnvelope(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            DateTimeOffset.Parse("2026-08-23T07:30:00Z"),
            "Windows",
            "1.0.0",
            "ONLINE");

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(envelope));
        var root = document.RootElement;
        var keys = root.EnumerateObject().Select(item => item.Name).OrderBy(value => value).ToArray();

        Assert.Equal(
            new[] { "agent_version", "captured_at", "device_id", "platform", "schema_version", "state" },
            keys);
        Assert.Equal("Windows", root.GetProperty("platform").GetString());
        Assert.Equal("ONLINE", root.GetProperty("state").GetString());
    }

    [Fact]
    public void ProfileRegistryEnvelopeIsRevisionedAndContainsNoCredentials()
    {
        var envelope = new ProfileRegistryEnvelope(
            7,
            [new ProfileRegistryItemEnvelope(
                Guid.Parse("22222222-2222-2222-2222-222222222222"),
                "User profile",
                true,
                3)]);

        var json = JsonSerializer.Serialize(envelope);
        using var document = JsonDocument.Parse(json);

        Assert.Equal(7, document.RootElement.GetProperty("registry_revision").GetInt64());
        Assert.Equal("User profile", document.RootElement.GetProperty("profiles")[0].GetProperty("name").GetString());
        Assert.DoesNotContain("token", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("credential", json, StringComparison.OrdinalIgnoreCase);
    }
}
