# 使用 Serilog 寫入普通字符串日誌

如果您只需要寫入普通的字符串日誌，不需要特殊格式或結構化數據，這裡是一個使用 Serilog 的簡潔實現：

## 首先，安裝基本的 Serilog 包

```
Install-Package Serilog
Install-Package Serilog.Sinks.File
```

## Program.cs 中的最簡實現

```csharp
using System;
using Serilog;

class Program
{
    static void Main(string[] args)
    {
        // 設置 Serilog 配置 - 簡單地輸出到文件
        Log.Logger = new LoggerConfiguration()
            .WriteTo.File("logs/app.log", 
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} {Message}{NewLine}")
            .CreateLogger();

        try
        {
            Log.Information("應用程序啟動");

            // 您的應用程序代碼...
            for (int i = 0; i < 5; i++)
            {
                // 寫入普通字符串日誌
                Log.Information($"這是一條普通日誌消息 #{i+1}");
                
                // 如果需要，也可以寫入錯誤日誌
                if (i == 3)
                {
                    Log.Error("這是一條錯誤日誌消息");
                }
                
                System.Threading.Thread.Sleep(500);
            }
        }
        catch (Exception ex)
        {
            // 記錄異常
            Log.Fatal(ex, "應用程序發生嚴重錯誤");
        }
        finally
        {
            Log.Information("應用程序關閉");
            Log.CloseAndFlush(); // 確保所有日誌都被寫入
        }
    }
}
```

## 如果您需要自定義格式

```csharp
using System;
using Serilog;

class Program
{
    static void Main(string[] args)
    {
        // 設置 Serilog 配置
        Log.Logger = new LoggerConfiguration()
            .WriteTo.File("logs/app.log", 
                rollingInterval: RollingInterval.Day, // 每天創建新文件
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} {Message}{NewLine}")
            .CreateLogger();

        try
        {
            Log.Information("應用程序啟動");

            // 串口操作模擬
            Log.Information("嘗試打開串口 COM3");
            System.Threading.Thread.Sleep(500);
            
            Log.Information("串口已打開");
            System.Threading.Thread.Sleep(500);
            
            // 發送數據
            byte[] data = new byte[] { 0x01, 0x02, 0x03, 0xFF };
            Log.Information($"發送數據: {BitConverter.ToString(data).Replace("-", " ")}");
            System.Threading.Thread.Sleep(1000);
            
            // 接收數據
            byte[] received = new byte[] { 0xAA, 0xBB, 0xCC };
            Log.Information($"接收數據: {BitConverter.ToString(received).Replace("-", " ")}");
            
            // 關閉串口
            Log.Information("串口已關閉");
        }
        catch (Exception ex)
        {
            // 記錄異常
            Log.Error(ex, "應用程序發生錯誤");
        }
        finally
        {
            Log.Information("應用程序關閉");
            Log.CloseAndFlush();
        }
    }
}
```

## 使用自定義文件名和路徑

```csharp
using System;
using System.IO;
using Serilog;

class Program
{
    static void Main(string[] args)
    {
        // 獲取應用程序目錄
        string appDirectory = AppDomain.CurrentDomain.BaseDirectory;
        
        // 創建日誌目錄
        string logDirectory = Path.Combine(appDirectory, "Logs");
        Directory.CreateDirectory(logDirectory);
        
        // 生成日誌文件名（包含日期）
        string logFileName = $"Serial_{DateTime.Now:yyyyMMdd}.log";
        string logFilePath = Path.Combine(logDirectory, logFileName);
        
        // 設置 Serilog 配置
        Log.Logger = new LoggerConfiguration()
            .WriteTo.File(logFilePath, 
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} {Message}{NewLine}")
            .CreateLogger();

        try
        {
            Log.Information("應用程序啟動");
            Log.Information($"日誌記錄到: {logFilePath}");

            // 您的應用程序代碼...
            for (int i = 0; i < 5; i++)
            {
                Log.Information($"操作 #{i+1} 執行中...");
                System.Threading.Thread.Sleep(500);
            }
        }
        catch (Exception ex)
        {
            Log.Error($"錯誤: {ex.Message}");
        }
        finally
        {
            Log.Information("應用程序關閉");
            Log.CloseAndFlush();
        }
    }
}
```

## 帶調試級別的簡單實現

如果您想要區分不同級別的日誌，但只在文件中顯示消息本身：

```csharp
using System;
using Serilog;
using Serilog.Events;

class Program
{
    static void Main(string[] args)
    {
        // 設置 Serilog 配置
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug() // 設置最小日誌級別
            .WriteTo.File("logs/app.log", 
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} {Level:u3}: {Message}{NewLine}")
            .CreateLogger();

        try
        {
            Log.Information("應用程序啟動");

            // 不同級別的日誌示例
            Log.Debug("這是調試信息");
            Log.Information("這是一般信息");
            Log.Warning("這是警告信息");
            Log.Error("這是錯誤信息");

            // 模擬串口通信
            SimulateSerialCommunication();
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "應用程序發生嚴重錯誤");
        }
        finally
        {
            Log.Information("應用程序關閉");
            Log.CloseAndFlush();
        }
    }

    static void SimulateSerialCommunication()
    {
        // 模擬串口通信
        Log.Debug("串口配置: COM3, 9600, N, 8, 1");
        Log.Information("正在打開串口...");
        System.Threading.Thread.Sleep(500);
        
        Log.Information("串口已打開");
        
        // 模擬發送數據
        string sentCommand = "AT+CSQ";
        Log.Debug($"發送命令: {sentCommand}");
        System.Threading.Thread.Sleep(500);
        
        // 模擬接收數據
        string response = "+CSQ: 21,99\r\nOK";
        Log.Debug($"收到響應: {response}");
        
        // 模擬解析響應
        Log.Information("信號強度: 21 dBm");
        
        // 模擬關閉
        Log.Information("關閉串口");
    }
}
```

## 帶控制台輸出的簡單實現

如果您希望同時在控制台和文件中看到日誌：

```csharp
using System;
using Serilog;

class Program
{
    static void Main(string[] args)
    {
        // 設置 Serilog 配置 - 同時輸出到控制台和文件
        Log.Logger = new LoggerConfiguration()
            .WriteTo.Console(outputTemplate: "{Timestamp:HH:mm:ss.fff} {Message}")
            .WriteTo.File("logs/app.log", 
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} {Message}{NewLine}")
            .CreateLogger();

        try
        {
            Log.Information("應用程序啟動");

            // 您的應用程序代碼...
            Log.Information("正在初始化串口...");
            System.Threading.Thread.Sleep(500);
            
            Log.Information("串口已初始化");
            System.Threading.Thread.Sleep(500);
            
            Log.Information("正在傳輸數據...");
            System.Threading.Thread.Sleep(1000);
            
            Log.Information("數據傳輸完成");
            
            // 如果發生錯誤
            if (DateTime.Now.Second % 2 == 0) // 隨機模擬錯誤
            {
                Log.Error("傳輸過程中發生錯誤");
            }
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
```

## 使用 AppSettings 配置 Serilog 的實現

如果您想要從配置文件中讀取 Serilog 設置：

```csharp
using System;
using Microsoft.Extensions.Configuration;
using Serilog;

class Program
{
    static void Main(string[] args)
    {
        // 讀取配置
        var configuration = new ConfigurationBuilder()
            .AddJsonFile("appsettings.json")
            .Build();

        // 配置 Serilog
        Log.Logger = new LoggerConfiguration()
            .ReadFrom.Configuration(configuration) // 從 appsettings.json 讀取配置
            .CreateLogger();

        try
        {
            Log.Information("應用程序啟動");
            
            // 您的應用程序代碼...
            Log.Information("這是一條簡單的日誌消息");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "應用程序錯誤");
        }
        finally
        {
            Log.Information("應用程序關閉");
            Log.CloseAndFlush();
        }
    }
}
```

對應的 `appsettings.json` 文件：

```json
{
  "Serilog": {
    "Using": [ "Serilog.Sinks.Console", "Serilog.Sinks.File" ],
    "MinimumLevel": "Debug",
    "WriteTo": [
      {
        "Name": "Console"
      },
      {
        "Name": "File",
        "Args": {
          "path": "Logs/app.log",
          "rollingInterval": "Day",
          "outputTemplate": "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} {Message}{NewLine}"
        }
      }
    ]
  }
}
```

## 總結

以上提供了多種使用 Serilog 寫入普通字符串日誌的方法，從最簡單的實現到帶有更多功能的版本。您可以根據需要選擇最適合的方案，所有這些都可以直接在 Program.cs 中實現，不需要創建額外的服務類。

Serilog 的好處是配置靈活，同時保持 API 的簡潔性。即使您只需要記錄普通字符串，使用 Serilog 也可以讓您在未來需要更多功能時輕鬆擴展。