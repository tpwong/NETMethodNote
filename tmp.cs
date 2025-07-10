# 簡單直接的串行日誌記錄實現

既然您希望直接在 Program.cs 中實現最小化的日誌記錄功能，不想建立額外的服務類，下面提供兩種直接嵌入到 Program.cs 的簡潔實現：

## 方案一：最簡單的靜態方法實現

```csharp
using System;
using System.IO;
using System.Text;

class Program
{
    // 設置日誌文件路徑（可根據需要調整）
    private static readonly string LogFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SerialLog.txt");
    private static readonly object LogLock = new object();

    static void Main(string[] args)
    {
        // 應用程序啟動記錄
        LogToFile("應用程序已啟動");

        try
        {
            // 模擬串口操作
            LogToFile("嘗試打開串口 COM3");
            
            // 模擬收到數據
            byte[] receivedData = new byte[] { 0x01, 0x02, 0x03, 0x04, 0xFF };
            LogHexToFile(receivedData, "收到數據");
            
            // 模擬發送數據
            byte[] sentData = new byte[] { 0xAA, 0xBB, 0xCC };
            LogHexToFile(sentData, "發送數據");
            
            // 模擬錯誤情況
            LogToFile("串口通信超時", isError: true);
        }
        catch (Exception ex)
        {
            LogToFile($"發生錯誤: {ex.Message}", isError: true);
        }
        
        LogToFile("應用程序已關閉");
        
        Console.WriteLine("日誌已寫入: " + LogFilePath);
        Console.ReadKey();
    }

    // 基本日誌記錄方法
    private static void LogToFile(string message, bool isError = false)
    {
        try
        {
            string prefix = isError ? "ERROR" : "INFO";
            string logMessage = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{prefix}] - {message}";
            
            lock (LogLock)
            {
                File.AppendAllText(LogFilePath, logMessage + Environment.NewLine);
            }
            
            // 可選：同時輸出到控制台
            Console.WriteLine(logMessage);
        }
        catch
        {
            // 發生錯誤時的最小處理 - 只輸出到控制台
            Console.WriteLine($"無法寫入日誌: {message}");
        }
    }

    // 十六進制數據日誌記錄方法
    private static void LogHexToFile(byte[] data, string prefix)
    {
        if (data == null || data.Length == 0)
            return;

        StringBuilder sb = new StringBuilder();
        sb.Append(prefix).Append(": ");
        
        foreach (byte b in data)
        {
            sb.Append(b.ToString("X2")).Append(' ');
        }

        LogToFile(sb.ToString());
    }
}
```

## 方案二：使用本地函數（更現代的 C# 風格）

```csharp
using System;
using System.IO;
using System.Text;

class Program
{
    static void Main(string[] args)
    {
        // 設置日誌文件路徑
        string logFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SerialLog.txt");
        object logLock = new object();

        // 本地日誌函數
        void LogToFile(string message, bool isError = false)
        {
            try
            {
                string prefix = isError ? "ERROR" : "INFO";
                string logMessage = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{prefix}] - {message}";
                
                lock (logLock)
                {
                    File.AppendAllText(logFilePath, logMessage + Environment.NewLine);
                }
                
                // 可選：同時輸出到控制台
                Console.WriteLine(logMessage);
            }
            catch
            {
                Console.WriteLine($"無法寫入日誌: {message}");
            }
        }

        // 本地十六進制日誌函數
        void LogHexToFile(byte[] data, string prefix)
        {
            if (data == null || data.Length == 0)
                return;

            StringBuilder sb = new StringBuilder();
            sb.Append(prefix).Append(": ");
            
            foreach (byte b in data)
            {
                sb.Append(b.ToString("X2")).Append(' ');
            }
            
            // 添加ASCII表示
            sb.Append(" | ");
            foreach (byte b in data)
            {
                sb.Append(b >= 32 && b <= 126 ? (char)b : '.');
            }

            LogToFile(sb.ToString());
        }

        // 應用程序啟動
        LogToFile("應用程序已啟動");

        try
        {
            // TODO: 添加您的串口代碼
            LogToFile("嘗試打開串口 COM3");
            
            // 模擬串口操作
            System.Threading.Thread.Sleep(500); // 模擬操作延遲
            
            // 模擬收到數據
            byte[] receivedData = new byte[] { 0x01, 0x02, 0x03, 0x04, 0xFF };
            LogHexToFile(receivedData, "收到數據");
            
            // 模擬發送數據
            byte[] sentData = new byte[] { 0xAA, 0xBB, 0xCC };
            LogHexToFile(sentData, "發送數據");
            
            // 模擬錯誤情況
            LogToFile("串口通信超時", isError: true);
        }
        catch (Exception ex)
        {
            LogToFile($"發生錯誤: {ex.Message}", isError: true);
        }
        
        LogToFile("應用程序已關閉");
        
        Console.WriteLine("日誌已寫入: " + logFilePath);
        Console.ReadKey();
    }
}
```

## 方案三：使用 Minimal API 風格的輕量日誌

如果您使用的是 ASP.NET Core 或想要 Minimal API 風格的實現：

```csharp
using System;
using System.IO;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

// 設置日誌文件路徑
string logFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SerialLog.txt");
object logLock = new object();

// 簡單日誌記錄功能
Action<string, bool> LogToFile = (message, isError) =>
{
    try
    {
        string prefix = isError ? "ERROR" : "INFO";
        string logMessage = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{prefix}] - {message}";
        
        lock (logLock)
        {
            File.AppendAllText(logFilePath, logMessage + Environment.NewLine);
        }
        
        // 使用內建日誌系統同時記錄
        if (isError)
            app.Logger.LogError(message);
        else
            app.Logger.LogInformation(message);
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, $"無法寫入日誌: {message}");
    }
};

// 十六進制日誌記錄功能
Action<byte[], string> LogHexToFile = (data, prefix) =>
{
    if (data == null || data.Length == 0)
        return;

    StringBuilder sb = new StringBuilder();
    sb.Append(prefix).Append(": ");
    
    foreach (byte b in data)
    {
        sb.Append(b.ToString("X2")).Append(' ');
    }

    LogToFile(sb.ToString(), false);
};

// 記錄應用程序啟動
LogToFile("應用程序已啟動", false);

// 定義一個測試端點
app.MapGet("/", () => "Hello World!");

// 定義一個可觸發日誌記錄的端點
app.MapGet("/log-test", () => {
    LogToFile("收到日誌測試請求", false);
    
    byte[] testData = new byte[] { 0x01, 0x02, 0x03, 0x04, 0xFF };
    LogHexToFile(testData, "測試數據");
    
    return "日誌已記錄";
});

// 應用關閉時記錄
app.Lifetime.ApplicationStopping.Register(() => {
    LogToFile("應用程序正在關閉", false);
});

app.Run();
```

## 方案四：只使用一個文件的最小實現

如果您真的想要最小化，只在一個文件中實現所有功能，不引入任何額外服務或類：

```csharp
using System;
using System.IO;
using System.IO.Ports;
using System.Text;
using System.Threading;

class Program
{
    static void Main(string[] args)
    {
        // 日誌配置
        string logFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SerialLog.txt");
        object logLock = new object();
        
        // 串口配置
        string portName = "COM3";
        int baudRate = 9600;
        SerialPort serialPort = null;
        
        // 日誌函數
        void Log(string message, bool isError = false)
        {
            string prefix = isError ? "ERROR" : "INFO";
            string logMessage = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{prefix}] - {message}";
            
            try
            {
                lock (logLock)
                {
                    File.AppendAllText(logFilePath, logMessage + Environment.NewLine);
                }
                Console.WriteLine(logMessage);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"無法寫入日誌: {ex.Message}");
            }
        }
        
        // 十六進制日誌函數
        void LogHex(byte[] data, string prefix)
        {
            if (data == null || data.Length == 0) return;
            
            StringBuilder sb = new StringBuilder();
            sb.Append(prefix).Append(": ");
            
            foreach (byte b in data)
            {
                sb.Append(b.ToString("X2")).Append(' ');
            }
            
            Log(sb.ToString());
        }
        
        // 處理串口接收
        void DataReceivedHandler(object sender, SerialDataReceivedEventArgs e)
        {
            if (serialPort == null) return;
            
            try
            {
                // 給接收緩衝區一些時間來接收完整數據
                Thread.Sleep(50);
                
                int bytesToRead = serialPort.BytesToRead;
                if (bytesToRead > 0)
                {
                    byte[] buffer = new byte[bytesToRead];
                    serialPort.Read(buffer, 0, bytesToRead);
                    
                    // 記錄接收到的數據
                    LogHex(buffer, "RX");
                }
            }
            catch (Exception ex)
            {
                Log($"讀取串口數據時發生錯誤: {ex.Message}", true);
            }
        }
        
        // 打開串口
        bool OpenSerialPort()
        {
            try
            {
                Log($"嘗試打開串口 {portName}, 波特率: {baudRate}");
                
                serialPort = new SerialPort
                {
                    PortName = portName,
                    BaudRate = baudRate,
                    DataBits = 8,
                    Parity = Parity.None,
                    StopBits = StopBits.One,
                    ReadTimeout = 1000,
                    WriteTimeout = 1000
                };
                
                serialPort.DataReceived += DataReceivedHandler;
                serialPort.Open();
                
                Log($"串口 {portName} 已成功打開");
                return true;
            }
            catch (Exception ex)
            {
                Log($"打開串口時發生錯誤: {ex.Message}", true);
                return false;
            }
        }
        
        // 發送數據
        void SendData(byte[] data)
        {
            if (serialPort == null || !serialPort.IsOpen || data == null || data.Length == 0)
                return;
                
            try
            {
                serialPort.Write(data, 0, data.Length);
                LogHex(data, "TX");
            }
            catch (Exception ex)
            {
                Log($"發送數據時發生錯誤: {ex.Message}", true);
            }
        }
        
        // 關閉串口
        void CloseSerialPort()
        {
            if (serialPort != null && serialPort.IsOpen)
            {
                try
                {
                    serialPort.DataReceived -= DataReceivedHandler;
                    serialPort.Close();
                    serialPort.Dispose();
                    Log($"串口 {portName} 已關閉");
                }
                catch (Exception ex)
                {
                    Log($"關閉串口時發生錯誤: {ex.Message}", true);
                }
                finally
                {
                    serialPort = null;
                }
            }
        }
        
        // 主程序
        Log("應用程序已啟動");
        
        try
        {
            if (OpenSerialPort())
            {
                // 發送一些測試數據
                byte[] testData = new byte[] { 0xAA, 0xBB, 0xCC, 0xDD };
                SendData(testData);
                
                // 等待一段時間接收回應
                Console.WriteLine("按任意鍵關閉應用程序...");
                Console.ReadKey();
                
                // 關閉串口
                CloseSerialPort();
            }
        }
        catch (Exception ex)
        {
            Log($"應用程序運行時發生錯誤: {ex.Message}", true);
        }
        finally
        {
            CloseSerialPort();
            Log("應用程序已關閉");
        }
    }
}
```

選擇哪種方案取決於您的具體需求和使用場景。方案四是最完整的獨立實現，包含了串口通信和日誌記錄的完整功能。如果您只需要日誌記錄功能，方案一或方案二會更簡潔。如果您是在 ASP.NET Core 中工作，方案三可能更適合。

這些方案都避免了創建額外的服務類，直接在 Program.cs 中實現所需功能。