using TheSpark.HardwareMonitor.Core;
using TheSpark.HardwareMonitor.Core.Models;

namespace TheSpark.HardwareMonitor.App.ViewModels;

public sealed class SensorRowViewModel : ObservableObject
{
    private readonly RollingSeries _series = new(600);
    private string _displayValue = "Not exposed";
    private string _status = "Unavailable";
    private double _minimum;
    private double _maximum;
    private double _average;

    public SensorRowViewModel(string id, string hardwareName, string name, SensorKind kind, string unit)
    {
        Id = id;
        HardwareName = hardwareName;
        Name = name;
        Kind = kind;
        Unit = unit;
    }

    public string Id { get; }
    public string HardwareName { get; }
    public string Name { get; }
    public SensorKind Kind { get; }
    public string Unit { get; }
    public string DisplayValue { get => _displayValue; private set => SetProperty(ref _displayValue, value); }
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    public double Minimum { get => _minimum; private set => SetProperty(ref _minimum, value); }
    public double Maximum { get => _maximum; private set => SetProperty(ref _maximum, value); }
    public double Average { get => _average; private set => SetProperty(ref _average, value); }
    public IReadOnlyList<double> History => _series.Values;

    public void Update(SensorReading reading, DateTimeOffset now, string temperatureUnit)
    {
        if (reading.Value.HasValue && reading.Availability == SensorAvailability.Available)
        {
            _series.Add(reading.Value.Value);
            if (reading.Kind == SensorKind.Temperature && temperatureUnit.Equals("Fahrenheit", StringComparison.OrdinalIgnoreCase))
            {
                DisplayValue = $"{reading.Value.Value * 9d / 5d + 32d:0.##} °F";
            }
            else
            {
                DisplayValue = $"{reading.Value.Value:0.##} {reading.Unit}".Trim();
            }
            Minimum = _series.Minimum;
            Maximum = _series.Maximum;
            Average = _series.Average;
            Status = reading.IsStale(now, TimeSpan.FromSeconds(3)) ? "Stale" : "Live";
        }
        else
        {
            DisplayValue = reading.Availability == SensorAvailability.NotExposed ? "Not exposed" : "Unavailable";
            Status = reading.Availability.ToString();
        }

        OnPropertyChanged(nameof(History));
    }
}
