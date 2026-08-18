using System.Windows;

namespace HHDCTracker.Services;

public static class ProgressService
{
    public static event Action<double, string>? ProgressChanged;
    public static event Action? ProgressCompleted;

    private static bool _isRunning = false;
    public static bool IsRunning => _isRunning;

    public static void Report(double percent, string description)
    {
        _isRunning = true;
        Application.Current?.Dispatcher.Invoke(() =>
            ProgressChanged?.Invoke(Math.Clamp(percent, 0, 100), description));
    }

    public static void Complete()
    {
        _isRunning = false;
        Application.Current?.Dispatcher.Invoke(() =>
            ProgressCompleted?.Invoke());
    }
}
