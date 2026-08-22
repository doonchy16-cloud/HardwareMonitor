using System.Management;
using System.Runtime.InteropServices;

namespace TheSpark.HardwareMonitor.Platform.Windows;

public sealed class SystemInventoryProvider
{
    public Task<SystemInventorySnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default) =>
        Task.Run(BuildSnapshot, cancellationToken);

    private static SystemInventorySnapshot BuildSnapshot()
    {
        var os = FirstValue("SELECT Caption, Version FROM Win32_OperatingSystem", row =>
            $"{Value(row, "Caption")} {Value(row, "Version")}".Trim());
        var computer = FirstRow("SELECT Manufacturer, Model, TotalPhysicalMemory FROM Win32_ComputerSystem");
        var bios = FirstValue("SELECT SMBIOSBIOSVersion FROM Win32_BIOS", row => Value(row, "SMBIOSBIOSVersion"));
        var board = FirstValue("SELECT Manufacturer, Product FROM Win32_BaseBoard", row =>
            JoinNonEmpty(Value(row, "Manufacturer"), Value(row, "Product")));
        var cpu = FirstRow("SELECT Name, NumberOfCores, NumberOfLogicalProcessors FROM Win32_Processor");

        var gpus = AllValues("SELECT Name FROM Win32_VideoController", row => Value(row, "Name"));
        var storage = AllValues("SELECT Model, InterfaceType, Size FROM Win32_DiskDrive", row => new StorageDeviceInfo(
            Value(row, "Model", "Unknown drive"),
            Value(row, "InterfaceType", "Unknown"),
            UInt64Value(row, "Size")));

        return new SystemInventorySnapshot(
            Environment.MachineName,
            string.IsNullOrWhiteSpace(os) ? RuntimeInformation.OSDescription : os,
            Value(computer, "Manufacturer", "Unknown"),
            Value(computer, "Model", "Unknown"),
            string.IsNullOrWhiteSpace(bios) ? "Unknown" : bios,
            string.IsNullOrWhiteSpace(board) ? "Unknown" : board,
            Value(cpu, "Name", RuntimeInformation.ProcessArchitecture.ToString()),
            Int32Value(cpu, "NumberOfCores", Environment.ProcessorCount),
            Int32Value(cpu, "NumberOfLogicalProcessors", Environment.ProcessorCount),
            UInt64Value(computer, "TotalPhysicalMemory"),
            gpus,
            storage,
            DateTimeOffset.UtcNow);
    }

    private static Dictionary<string, object?> FirstRow(string query)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(query);
            using var results = searcher.Get();
            foreach (ManagementObject item in results)
            {
                using (item)
                {
                    return item.Properties.Cast<PropertyData>()
                        .ToDictionary(property => property.Name, property => property.Value, StringComparer.OrdinalIgnoreCase);
                }
            }
        }
        catch (ManagementException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
    }

    private static string FirstValue(string query, Func<IReadOnlyDictionary<string, object?>, string> selector)
    {
        var row = FirstRow(query);
        return row.Count == 0 ? string.Empty : selector(row);
    }

    private static IReadOnlyList<T> AllValues<T>(string query, Func<IReadOnlyDictionary<string, object?>, T> selector)
    {
        var values = new List<T>();
        try
        {
            using var searcher = new ManagementObjectSearcher(query);
            using var results = searcher.Get();
            foreach (ManagementObject item in results)
            {
                using (item)
                {
                    var row = item.Properties.Cast<PropertyData>()
                        .ToDictionary(property => property.Name, property => property.Value, StringComparer.OrdinalIgnoreCase);
                    values.Add(selector(row));
                }
            }
        }
        catch (ManagementException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        return values;
    }

    private static string Value(IReadOnlyDictionary<string, object?> values, string key, string fallback = "") =>
        values.TryGetValue(key, out var value) && value is not null
            ? Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? fallback
            : fallback;

    private static ulong UInt64Value(IReadOnlyDictionary<string, object?> values, string key)
    {
        if (!values.TryGetValue(key, out var value) || value is null)
        {
            return 0;
        }

        try
        {
            return Convert.ToUInt64(value, System.Globalization.CultureInfo.InvariantCulture);
        }
        catch (FormatException)
        {
            return 0;
        }
        catch (InvalidCastException)
        {
            return 0;
        }
        catch (OverflowException)
        {
            return 0;
        }
    }

    private static int Int32Value(IReadOnlyDictionary<string, object?> values, string key, int fallback)
    {
        if (!values.TryGetValue(key, out var value) || value is null)
        {
            return fallback;
        }

        try
        {
            return Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture);
        }
        catch (FormatException)
        {
            return fallback;
        }
        catch (InvalidCastException)
        {
            return fallback;
        }
        catch (OverflowException)
        {
            return fallback;
        }
    }

    private static string JoinNonEmpty(params string[] values) =>
        string.Join(" ", values.Where(value => !string.IsNullOrWhiteSpace(value)));
}
