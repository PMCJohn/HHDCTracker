namespace HHDCTracker.Services;

public static class DbRetryService
{
    private const int MaxRetries = 3;
    private const int RetryDelayMs = 200;

    public static async Task<T> ExecuteAsync<T>(Func<Task<T>> operation)
    {
        int attempt = 0;
        while (true)
        {
            try { return await operation(); }
            catch (Exception ex) when (
                attempt < MaxRetries - 1 &&
                (ex.Message.Contains("database is locked") ||
                 ex.Message.Contains("SQLITE_BUSY") ||
                 ex.Message.Contains("disk I/O error")))
            {
                attempt++;
                await Task.Delay(RetryDelayMs * attempt);
            }
        }
    }

    public static async Task ExecuteAsync(Func<Task> operation)
    {
        int attempt = 0;
        while (true)
        {
            try { await operation(); return; }
            catch (Exception ex) when (
                attempt < MaxRetries - 1 &&
                (ex.Message.Contains("database is locked") ||
                 ex.Message.Contains("SQLITE_BUSY") ||
                 ex.Message.Contains("disk I/O error")))
            {
                attempt++;
                await Task.Delay(RetryDelayMs * attempt);
            }
        }
    }
}
