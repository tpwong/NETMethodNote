using System;
using Serilog;

class Program
{
    static void Main(string[] args)
    {
        // 設置 Serilog 配置 - 使用 rollingInterval: RollingInterval.Day
        Log.Logger = new LoggerConfiguration()
            .WriteTo.File("logs/app_.log", 
                rollingInterval: RollingInterval.Day,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} {Message}{NewLine}")
            .CreateLogger();

        try
        {
            Log.Information("應用程序啟動");
            
            // 您的應用程序代碼...
            Log.Information("這是一條簡單的日誌消息");
            Log.Information("日誌將按日期自動分割");
        }
        catch (Exception ex)
        {
            Log.Error($"應用程序錯誤: {ex.Message}");
        }
        finally
        {
            Log.Information("應用程序關閉");
            Log.CloseAndFlush();
        }
    }
}