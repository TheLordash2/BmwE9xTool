using System.Text;

namespace BmwE9xTool.Core;

public sealed class AppLog
{
    private readonly object _gate = new();
    private readonly List<string> _lines = new();
    public event Action? Changed;

    public void Add(string message)
    {
        lock (_gate)
        {
            _lines.Add($"{DateTime.Now:HH:mm:ss.fff}  {message}");
            if (_lines.Count > 2000) _lines.RemoveRange(0, 250);
        }
        Changed?.Invoke();
    }

    public string Text
    {
        get { lock (_gate) return string.Join(Environment.NewLine, _lines); }
    }

    public async Task<string> SaveAsync(string directory)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"session_{DateTime.Now:yyyyMMdd_HHmmss}.log");
        string text;
        lock (_gate) text = string.Join(Environment.NewLine, _lines);
        await File.WriteAllTextAsync(path, text, Encoding.UTF8);
        return path;
    }
}
