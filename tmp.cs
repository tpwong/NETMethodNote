這是一個非常經典的架構問題：**跨客戶端共享資源限制 (Shared Resource Limiting)**。

既然你要限制的是「兩個 HttpClient 加起來」的總量，單純在各自的 Builder 設定 Policy 是不行的，因為它們會各自計算。你需要一個**共享的閘道**。

最優雅且符合你「排隊無限久、執行限時」需求的方法，是自定義一個 **Singleton 的 DelegatingHandler**。

### 核心思路
1.  建立一個 **Handler**，裡面放一個 `static` 或 `Singleton` 的 `SemaphoreSlim(3)`。
2.  將這個 Handler 同時注入給 Client A 和 Client B。
3.  在 Handler 內部實作「先排隊 (Wait)，再計時 (Timeout)」的邏輯。

### 優雅的實作代碼

#### 1. 建立共享的 Handler
這個類別負責控制並發量以及執行時間的邏輯。

```csharp
using System.Threading;

public class SharedConcurrencyHandler : DelegatingHandler
{
    // 核心：這把鎖是共享的，限制總量為 3
    private readonly SemaphoreSlim _semaphore;
    private readonly TimeSpan _executionTimeout;

    public SharedConcurrencyHandler()
    {
        // 限制同時只能有 3 個請求通過
        _semaphore = new SemaphoreSlim(3, 3);
        // 設定「真正執行」時的超時時間 (例如 10 秒)
        _executionTimeout = TimeSpan.FromSeconds(10);
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // --- 階段 1: 排隊 (Waiting) ---
        // 這裡使用傳入的 cancellationToken (通常由外層控制，或者根本不取消)
        // 如果隊列滿了，會在這裡死等 (例如等 5 分鐘)，直到有人釋放
        await _semaphore.WaitAsync(cancellationToken);

        try
        {
            // --- 階段 2: 執行 (Execution) ---
            // 拿到通行證了！現在開始倒數計時 (例如 10 秒)
            // 我們創建一個新的 CTS，專門用於控制請求發送的時間
            using var timeoutCts = new CancellationTokenSource(_executionTimeout);
            
            // 連結原本的 Token (如果外層取消，這裡也要取消)
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            try 
            {
                // 發送請求，使用 linkedCts 的 Token
                return await base.SendAsync(request, linkedCts.Token);
            }
            catch (OperationCanceledException) when (timeoutCts.Token.IsCancellationRequested)
            {
                // 這裡是為了區分「排隊取消」還是「執行超時」
                // 如果是因為 timeoutCts 觸發的取消，拋出 TimeoutException 讓上層知道是執行太久
                throw new TimeoutException($"請求已獲取執行權，但在 {_executionTimeout.TotalSeconds} 秒內未完成。");
            }
        }
        finally
        {
            // --- 階段 3: 釋放 (Release) ---
            // 無論成功或失敗，都要把位置讓出來給另一個 Client
            _semaphore.Release();
        }
    }
}
```

#### 2. 在 Program.cs 中註冊
關鍵在於將這個 Handler 註冊為 **Singleton**，並讓兩個 Client 都使用它。

```csharp
var builder = WebApplication.CreateBuilder(args);

// 1. 註冊 Handler 為 Singleton (確保兩個 Client 共用同一個 Semaphore)
builder.Services.AddSingleton<SharedConcurrencyHandler>();

// 2. 設定 Client A
builder.Services.AddHttpClient("ClientA", client =>
{
    client.BaseAddress = new Uri("https://api-a.com/");
    client.Timeout = Timeout.InfiniteTimeSpan; // 廢除 Client 自帶的超時，完全交給 Handler 控制
})
.AddHttpMessageHandler<SharedConcurrencyHandler>(); // 加入共享閘道

// 3. 設定 Client B
builder.Services.AddHttpClient("ClientB", client =>
{
    client.BaseAddress = new Uri("https://api-b.com/");
    client.Timeout = Timeout.InfiniteTimeSpan; 
})
.AddHttpMessageHandler<SharedConcurrencyHandler>(); // 加入同一個共享閘道

var app = builder.Build();

app.MapGet("/test", async (IHttpClientFactory factory) =>
{
    // 模擬情境：
    // 假設 ClientA 已經佔用了 2 個名額
    // ClientB 佔用了 1 個名額
    // 下一個請求不管是 A 還是 B，都會在 Handler 的 WaitAsync 那裡排隊
    
    var clientA = factory.CreateClient("ClientA");
    var clientB = factory.CreateClient("ClientB");

    // 這裡可以隨意呼叫，底層會自動協調
    var task1 = clientA.GetAsync("/resource");
    var task2 = clientB.GetAsync("/resource");
    
    await Task.WhenAll(task1, task2);
    return "Done";
});

app.Run();
```

### 為什麼這樣做最優雅？

1.  **真正的共享狀態**：
    因為 `SharedConcurrencyHandler` 是 Singleton，它內部的 `SemaphoreSlim` 只有一個實例。無論你是呼叫 Client A 還是 Client B，它們都在搶**同一把鎖**的 3 個位置。

2.  **完美解決 Timeout 分離**：
    *   **排隊時**：使用的是 `WaitAsync(cancellationToken)`。只要你不傳入會取消的 Token，它就會一直等（滿足你可能等 5 分鐘的需求）。
    *   **執行時**：使用的是內部的 `timeoutCts`。一旦拿到鎖，必須在 10 秒內完成，否則拋出異常。

3.  **業務邏輯乾淨**：
    你的 Controller 或 Service 不需要知道這兩個 Client 有共享限制，也不需要寫 `Semaphore.Wait`。你只需要像平常一樣 `client.GetAsync()`，複雜的限流邏輯被封裝在底層 HTTP Pipeline 中了。