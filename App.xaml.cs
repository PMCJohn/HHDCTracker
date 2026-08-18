using HHDCTracker.Data;
using HHDCTracker.Models;
using System.IO;
using System.Windows;

namespace HHDCTracker;

public partial class App : Application
{
    public static AppDbContext? Db { get; private set; }
    public static User? CurrentUser { get; set; }
    public static Location? CurrentLocation { get; set; }
    public static string DbPath { get; private set; } = string.Empty;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DbPath = GetDatabasePath();
        Db = new AppDbContext(DbPath);
        await DatabaseInitializer.InitializeAsync(Db);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Db?.Dispose();
        base.OnExit(e);
    }

    private static string GetDatabasePath()
    {
        var configPath = System.IO.Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "hhdc.config");
        if (File.Exists(configPath))
        {
            var configured = File.ReadAllText(configPath).Trim();
            if (!string.IsNullOrEmpty(configured)) return configured;
        }
        return System.IO.Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "hhdc_tracker.db");
    }
}
