This is a very practical scenario. When adding a Timeout within a Handler, the key lies in **correctly merging** the "external Token (User Cancel)" with the "internal Timeout Token".

We use `CancellationTokenSource.CreateLinkedTokenSource` to achieve this.

Here is the complete implementation integrating **Keyed Services** + **SemaphoreSlim** + **30-second Timeout**:

### Complete Implementation (SharedConcurrencyHandler.cs)

```csharp
using System.Threading;
using Microsoft.Extensions.DependencyInjection;

public class SharedConcurrencyHandler : DelegatingHandler
{
    private readonly SemaphoreSlim _semaphore;

    // Inject Keyed Semaphore
    public SharedConcurrencyHandler([FromKeyedServices("GlobalHttpLock")] SemaphoreSlim semaphore)
    {
        _semaphore = semaphore;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // 1. Set up a CTS with a 30-second timeout
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        // 2. Merge the "external token" with the "timeout token"
        // This ensures cancellation triggers whether the user cancels OR the time runs out
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        try
        {
            // 3. Wait for the lock (using linkedToken)
            // If queuing takes longer than 30s, this throws OperationCanceledException
            await _semaphore.WaitAsync(linkedCts.Token);

            try
            {
                // 4. Execute the request (using linkedToken)
                // If the request takes too long and total time exceeds 30s, this throws
                return await base.SendAsync(request, linkedCts.Token);
            }
            finally
            {
                // 5. Always release the lock
                _semaphore.Release();
            }
        }
        catch (OperationCanceledException)
        {
            // Determine if it was a Timeout or a User Cancel
            if (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                // This was caused by the 30s timeout
                throw new TimeoutException("Request timed out after 30 seconds (Wait + Execution).");
            }
            
            // Otherwise, it was cancelled by the user; rethrow strictly
            throw;
        }
    }
}
```

### Key Logic Explanation

1.  **`CreateLinkedTokenSource` (Crucial)**:
    *   You cannot rely solely on `timeoutCts.Token`. If the user cancels the operation at the 2nd second, the application must stop immediately rather than waiting for the full 30 seconds.
    *   The `LinkedToken` ensures that **(User Cancel OR Timeout)** triggers the cancellation immediately if either event occurs.

2.  **Scope**:
    *   This Timeout covers **"Time spent waiting for the lock" + "Time spent on the actual HTTP request"**.
    *   If the Semaphore is full and queuing alone takes 30 seconds, the code throws an exception and aborts before sending the request.

3.  **`using` Statement**:
    *   `CancellationTokenSource` implements `IDisposable`. Always use `using` to ensure resources are released correctly.

4.  **Exception Handling**:
    *   Standard `HttpClient` timeouts usually throw a `TaskCanceledException` (a subclass of `OperationCanceledException`).
    *   I added logic in the `catch` block to distinguish between a "Real Timeout" and a "User Cancellation," which is helpful for logging and error handling.

### Program.cs (Remains Unchanged)

The registration part remains simple and does not need modification:

```csharp
builder.Services.AddKeyedSingleton("GlobalHttpLock", (sp, key) => new SemaphoreSlim(3, 3));
builder.Services.AddTransient<SharedConcurrencyHandler>();
builder.Services.AddHttpClient("MyClient")
    .AddHttpMessageHandler<SharedConcurrencyHandler>();
```






-----







沒問題！如果不想更動原本的呼叫程式碼（例如想繼續用標準的 `PostAsJsonAsync`），那我們就不能依賴「C# 類別名稱」來判斷，而是改用 **「URL 路徑 (RequestUri)」** 來判斷是否要記錄。

這是最常見的做法，完全不需要修改任何發送端的程式碼。

以下是修改後的 `RequestIdTimingHandler.cs`：

### 完整程式碼：`RequestIdTimingHandler.cs`

主要變更點：
1.  **LogOptions**：改為設定 `TrackedUrlKeywords` (URL 關鍵字)，而不是類別名稱。
2.  **Handler**：檢查 `request.RequestUri` 是否包含關鍵字。
3.  **移除 Extension**：不需要額外的擴充方法了。

```csharp
using System.Diagnostics;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;

namespace MyHttpClientApp.Handlers;

// 1. 設定與白名單
public static class LogOptions
{
    // 【白名單設定】：改用 URL 關鍵字來判斷
    // 只要請求的網址包含這些字串，就會被記錄 RequestId 與時間
    public static readonly HashSet<string> TrackedUrlKeywords = new()
    {
        "/api/orders",    // 例如：訂單相關 API
        "/api/payment",   // 例如：付款相關 API
        "create-user"     // 也可以是網址中的某個動作名稱
    };
}

// 2. Handler 本體
public class RequestIdTimingHandler : DelegatingHandler
{
    private readonly ILogger<RequestIdTimingHandler> _logger;

    public RequestIdTimingHandler(ILogger<RequestIdTimingHandler> logger)
    {
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // --- 步驟 A: 檢查 URL 是否在白名單內 ---
        
        // 取得當前請求的網址 (轉成字串並轉小寫以方便比對)
        string requestUrl = request.RequestUri?.ToString().ToLower() ?? "";
        
        // 檢查是否包含任何一個白名單關鍵字
        // (如果不介意效能，也可以用 Regex，但簡單的 Contains 最快)
        bool isTracked = LogOptions.TrackedUrlKeywords.Any(keyword => requestUrl.Contains(keyword.ToLower()));

        // 如果不在白名單，直接放行 (不讀 Body，不計時)
        if (!isTracked)
        {
            return await base.SendAsync(request, cancellationToken);
        }

        // --- 步驟 B: 需要記錄，開始計時邏輯 ---

        // 1. 嘗試撈取 RequestId (這會讀取 Body)
        string requestId = await TryGetRequestIdFromPayload(request, cancellationToken);
        
        // 2. 開始計時
        var stopwatch = Stopwatch.StartNew();

        try
        {
            // 3. 發送請求
            return await base.SendAsync(request, cancellationToken);
        }
        finally
        {
            // 4. 結束計時並 Log
            stopwatch.Stop();
            
            // 這裡改印出 URL Path 方便識別
            string path = request.RequestUri?.AbsolutePath ?? "Unknown-Path";

            _logger.LogInformation(
                "[{Path}] Request ID: {Id} | 耗時: {Elapsed} ms", 
                path, 
                requestId, 
                stopwatch.ElapsedMilliseconds);
        }
    }

    private async Task<string> TryGetRequestIdFromPayload(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // 只有 POST 或 PUT 才會有 Body，GET 通常沒有
        if (request.Content == null) return "N/A";

        try
        {
            // 確保 Stream 可以被重複讀取
            await request.Content.LoadIntoBufferAsync();
            
            // 讀取內容
            var jsonString = await request.Content.ReadAsStringAsync(cancellationToken);
            
            // 解析 JSON (假設 RequestId 在第一層)
            // 如果你的 JSON 很複雜，這裡可以用 TryParse
            var jsonNode = JsonNode.Parse(jsonString);
            return jsonNode?["RequestId"]?.ToString() ?? "N/A";
        }
        catch
        {
            return "Error-Parsing";
        }
    }
}
```

---

### 這樣做的好處

你原本的程式碼 **完全不用動**，依然可以使用標準的 `PostAsJsonAsync`：

```csharp
// 你的 Service 程式碼 (完全不用改)
public async Task CreateOrder(OrderRequest order)
{
    // 因為網址包含 "/api/orders"，所以 Handler 會自動攔截並記錄
    await _client.PostAsJsonAsync("https://example.com/api/orders/create", order);
}

public async Task CheckHealth()
{
    // 因為網址是 "/health"，不在 LogOptions 白名單內，Handler 會直接放行，不耗效能
    await _client.GetAsync("https://example.com/health");
}
```

### 總結邏輯
1.  **LogOptions** 定義了：「哪些 URL 是重要的」。
2.  **Handler** 攔截請求：「看 URL 是否命中關鍵字」。
3.  **命中** -> 讀取 JSON 找 RequestId -> 計時 -> Log。
4.  **未命中** -> 直接發送，無效能損耗。