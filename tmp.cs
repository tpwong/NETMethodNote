using System;
using Serilog;

class Program
{
    static void Main(string[] args)
    {
        Log.Logger = new LoggerConfiguration()
            .WriteTo.Async(a => a.File(
                "logs/app_.log", 
                rollingInterval: RollingInterval.Day,
                shared: true,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} {Message}{NewLine}"))
            .CreateLogger();

        try
        {
            Log.Information("應用程序啟動");
            // 您的應用程序代碼...
        }
        catch (Exception ex)
        {
            Log.Error($"應用程序錯誤: {ex.Message}");
        }
        finally
        {
            // 確保緩衝的日誌被寫入
            Log.CloseAndFlush();
        }
    }
}