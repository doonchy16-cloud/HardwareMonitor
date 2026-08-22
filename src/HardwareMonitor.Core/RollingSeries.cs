namespace TheSpark.HardwareMonitor.Core;

public sealed class RollingSeries
{
    private readonly int _capacity;
    private readonly Queue<double> _values;

    public RollingSeries(int capacity)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), "Capacity must be greater than zero.");
        }

        _capacity = capacity;
        _values = new Queue<double>(capacity);
    }

    public IReadOnlyList<double> Values => _values.ToArray();

    public double Average => _values.Count == 0 ? 0 : _values.Average();

    public double Minimum => _values.Count == 0 ? 0 : _values.Min();

    public double Maximum => _values.Count == 0 ? 0 : _values.Max();

    public void Add(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return;
        }

        if (_values.Count == _capacity)
        {
            _values.Dequeue();
        }

        _values.Enqueue(value);
    }

    public void Clear() => _values.Clear();
}
