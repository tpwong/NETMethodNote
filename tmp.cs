這是一個非常實務的問題。在 Handler 中加入 Timeout，關鍵在於**如何正確合併**「外部傳入的 Token (User Cancel)」與「內部設定的 Timeout Token」。

我們需要使用 `CancellationTokenSource.CreateLinkedTokenSource` 來達成。

以下是整合了 **Keyed Services** + **SemaphoreSlim** + **30秒 Timeout** 的完整程式碼：

### 完整實作 (SharedConcurrencyHandler.cs)

```csharp
using System.Threading;
using Microsoft.Extensions.DependencyInjection;

public class SharedConcurrencyHandler : DelegatingHandler
{
    private readonly SemaphoreSlim _semaphore;

    // 注入 Keyed Semaphore
    public SharedConcurrencyHandler([FromKeyedServices("GlobalHttpLock")] SemaphoreSlim semaphore)
    {
        _semaphore = semaphore;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // 1. 設定 30 秒 Timeout 的 CTS
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        // 2. 將「外部傳入的 Token」與「Timeout Token」合併
        // 這樣無論是使用者按取消，還是時間到了，都會觸發取消
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        try
        {
            // 3. 等待鎖 (使用 linkedToken)
            // 如果排隊超過 30 秒還沒拿到鎖，這裡就會拋出 OperationCanceledException
            await _semaphore.WaitAsync(linkedCts.Token);

            try
            {
                // 4. 執行請求 (使用 linkedToken)
                // 如果請求執行太久導致總時間超過 30 秒，這裡也會拋出異常
                return await base.SendAsync(request, linkedCts.Token);
            }
            finally
            {
                // 5. 絕對要釋放鎖
                _semaphore.Release();
            }
        }
        catch (OperationCanceledException)
        {
            // 這裡可以判斷是 Timeout 還是 User Cancel
            if (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                // 這是因為 30秒 Timeout 造成的
                throw new TimeoutException("Request timed out after 30 seconds (Wait + Execution).");
            }
            
            // 否則就是使用者自己取消的，直接往上拋
            throw;
        }
    }
}
```

### 重點邏輯解釋

1.  **`CreateLinkedTokenSource` (關鍵)**：
    *   你不能只用 `timeoutCts.Token`，因為如果使用者在第 2 秒就按了「取消」，程式必須馬上停止，不能傻傻等到 30 秒。
    *   `LinkedToken` 確保了 **(使用者取消 OR 時間到)** 只要發生其中一個，操作就會中斷。

2.  **Scope (範圍)**：
    *   這個 Timeout 包含了 **「排隊等待鎖的時間」 + 「實際 HTTP 請求的時間」**。
    *   如果 Semaphore 很滿，光是排隊就排了 30 秒，程式會直接拋出 Exception 放棄，不會發送請求。

3.  **`using` 語句**：
    *   `CancellationTokenSource` 是需要 Dispose 的資源，務必使用 `using` 確保記憶體釋放。

4.  **Exception 處理**：
    *   標準的 `HttpClient` 在 Timeout 時通常會拋出 `TaskCanceledException` (它是 `OperationCanceledException` 的子類)。
    *   我在 `catch` 區塊加了一段邏輯，讓你區分「是真的 Timeout」還是「使用者取消」，方便你做 Log 或錯誤處理。

### Program.cs (保持不變)

註冊部分完全不需要改動，依然維持簡潔：

```csharp
builder.Services.AddKeyedSingleton("GlobalHttpLock", (sp, key) => new SemaphoreSlim(3, 3));
builder.Services.AddTransient<SharedConcurrencyHandler>();
builder.Services.AddHttpClient("MyClient")
    .AddHttpMessageHandler<SharedConcurrencyHandler>();
```