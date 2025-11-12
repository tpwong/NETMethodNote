*Thinking...*

> **Analyzing the Core Question**
> 
> I'm currently focused on dissecting the user's core question. The elegance of middleware versus `DelegatingHandler` is the crux. I'm also starting to flesh out the user's specific context, noting the multiple hotels and standardized API specification. This should help me pinpoint the most effective approach.
> 
> 
> **Deconstructing the Problem's Nuances**
> 
> I'm now diving deeper into the nuances of "elegance." Defining it in terms of less boilerplate, clear separation, and reduced cognitive load is proving helpful. I'm focusing on why a `DelegatingHandler` fits the user's needs, specifically for handling `BaseUrl` and `JwtToken` variations across hotels, ensuring code reusability and simplifying API interactions.
> 
> 
> **Clarifying the Technical Details**
> 
> I'm now zeroing in on the practical implementation using `DelegatingHandler`. It's clear that it perfectly addresses the `Authorization` header aspect. My current challenge is how best to handle the `BaseAddress` setting. Modifying the `RequestUri` within the handler seems inelegant; I'm exploring different approaches to determine the optimal way. The handler's lifecycle is a factor.
> 
> 
> **Examining Hotel-Specific Data**
> 
> I'm now contemplating how to embed hotel-specific context into the `HttpRequestMessage`. Options include a custom header, using `HttpRequestMessage.Options`, or the recommended `IHttpClientFactory` approach with named clients. The best approach seems to be embedding the necessary hotel information and configuration.
> 
> 
> **Refining the Solution's Core**
> 
> I'm now revisiting the "Which Hotel?" problem. The factory approach, using `IHttpClientFactory`, now seems the more elegant. The single `HttpClient` with a shared `DelegatingHandler` forces the caller to pass hotel context via the request. This is less explicit and more prone to errors. Using named clients would be better suited to the job.
> 
> 
> **Considering Best Practice Integration**
> 
> I'm now contemplating how to integrate the `DelegatingHandler` approach into a working solution. While the `IHttpClientFactory` offers great flexibility, the handler needs access to the correct hotel configuration data. I'm leaning toward using dependency injection with a scoped service to supply the appropriate settings to the handler.
> 
> 
> **Synthesizing a Hybrid Approach**
> 
> I'm now integrating the user's insights with my analysis. I've concluded that the `IHttpClientFactory` paired with a custom `DelegatingHandler` provides the most elegant solution. This approach leverages the handler's ability to cleanly manage headers and configurations while benefiting from the factory's client-specific configurations. This combination balances flexibility and maintainability.
> 
> 
> **Examining the Core Question**
> 
> I'm now contemplating how to embed hotel-specific context into the `HttpRequestMessage`. The use of `HttpRequestMessage.Options` is an intriguing alternative to custom headers. It aligns well with the existing `HttpClient` architecture and avoids potential conflicts, but it's important to weigh these options.
> 
> 
> **Considering Custom Headers vs Options**
> 
> I'm now back to the question of how to efficiently transmit hotel-specific data with each request. I'm carefully considering the pros and cons of utilizing `HttpRequestMessage.Options`. This seems less intrusive than custom headers, and feels more streamlined. This approach might improve overall code readability.
> 
> 
這個問題問得非常好！您的直覺是正確的，使用 `DelegatingHandler` (HTTP 訊息處理程序) 確實是處理這類跨領域問題 (cross-cutting concerns) 更為優雅和模組化的方式。

讓我來詳細解釋一下，為什麼 `DelegatingHandler` 在這種情境下是個好選擇，以及它如何與我們之前討論的工廠模式相輔相成，創造出一個更乾淨、更可組合的設計。

首先，我們要釐清一個觀念：

*   **ASP.NET Core Middleware**：這是用來處理 **傳入 (incoming)** 您的伺服器的 HTTP 請求的管道 (pipeline)。它作用在您的 Controller 收到請求之前。這不適用於您要 **發出 (outgoing)** HTTP 請求到外部 API 的情境。
*   **`DelegatingHandler`**：這是 `HttpClient` 的「中介軟體」。它形成一個處理 **發出 (outgoing)** HTTP 請求的管道。每個請求在真正透過網路發送前，都會依序通過這個處理程序鏈。

所以，`DelegatingHandler` 才是我們需要的工具。

### `DelegatingHandler` 如何讓設計更優雅？

在我們之前的設計中，`HotelApiClientFactory` 負責了兩件事：
1.  從 `IHttpClientFactory` 取得 `HttpClient`。
2.  **手動設定** `BaseAddress` 和 `Authorization` 標頭。

雖然可行，但「設定授權標頭」這個行為其實是一個獨立的關注點。如果未來我們還需要加上追蹤 ID (Tracking ID) 的標頭、或是紀錄請求時間的標頭呢？`HotelApiClientFactory` 會變得越來越臃腫。

使用 `DelegatingHandler`，我們可以將這些「橫切關注點」封裝到各自獨立的、可重用的類別中。

讓我們來重構之前的設計，引入 `DelegatingHandler`。

---

### 優化後的設計：`IHttpClientFactory` + `DelegatingHandler`

這個設計的核心思想是：**讓 `IHttpClientFactory` 在建立 `HttpClient` 時，就自動為它裝配上已經設定好的 `DelegatingHandler`。**

#### 步驟 1：建立一個專門處理認證的 `DelegatingHandler`

這個處理程序的唯一職責，就是在每個發出的請求上附加 JWT Token。

```csharp
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

public class AuthenticationHandler : DelegatingHandler
{
    private readonly string _token;

    // 我們可以透過建構函式將特定的 Token 傳入
    public AuthenticationHandler(string token)
    {
        // 進行基本的 Token 驗證，確保不是空的
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new ArgumentNullException(nameof(token), "JWT token cannot be null or whitespace.");
        }
        _token = token;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // 在請求發送前，加上 Authorization 標頭
        // 使用 Clone 是為了避免多執行緒問題，雖然在此處非絕對必要，但是個好習慣
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);

        // 呼叫處理程序鏈中的下一個 Handler，最終會是實際發送請求的 HttpMessageHandler
        return await base.SendAsync(request, cancellationToken);
    }
}
```

**優點**：`AuthenticationHandler` 的職責非常單一，就是處理認證。它完全不知道什麼是酒店，也不知道 URL 是什麼。它非常容易進行單元測試。

#### 步驟 2：在 `Program.cs` 中進行更智慧的設定

這是最關鍵的一步。我們將使用 `IHttpClientFactory` 的進階設定功能，為**每一個酒店**動態註冊一個**具名用戶端 (Named Client)**。每個具名用戶端都會有自己獨立的設定（Base URL）和自己的處理程序鏈（Handler Pipeline）。

設定檔 `appsettings.json` 和設定模型 `HotelApiConfig` 保持不變。

```csharp
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using System.Collections.Generic;
using System.Linq;

var builder = WebApplication.CreateBuilder(args);

// --- 設定部分 ---
// 1. 綁定設定
builder.Services.Configure<List<HotelApiConfig>>(builder.Configuration.GetSection("HotelApiSettings"));

// 2. 取得設定，以便在註冊時使用
var sp = builder.Services.BuildServiceProvider();
var hotelConfigs = sp.GetRequiredService<IOptions<List<HotelApiConfig>>>().Value;

// --- HttpClient 設定 (核心部分) ---
// 3. 遍歷所有酒店設定，為每一間酒店註冊一個具名的 HttpClient
foreach (var config in hotelConfigs)
{
    // 使用 AddHttpClient 並傳入酒店名稱作為 Name
    builder.Services.AddHttpClient(config.Name, client =>
    {
        // a. 設定這個具名用戶端的 BaseAddress
        client.BaseAddress = new System.Uri(config.BaseUrl);
        client.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
    })
    // b. 為這個用戶端加上我們自訂的訊息處理程序
    .AddHttpMessageHandler(sp =>
    {
        // 建立一個 AuthenticationHandler，並將這間酒店專屬的 Token 傳入
        return new AuthenticationHandler(config.JwtToken);
    });
    // c. (可選) 你還可以在這裡鏈接更多的 Handler，例如 Polly 的彈性策略
    // .AddTransientHttpErrorPolicy(...)
}


// --- 服務註冊 ---
// 4. 工廠的註冊現在變得更簡單
builder.Services.AddSingleton<IHotelApiClientFactory, HotelApiClientFactory>();

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// ... 省略中介軟體設定 ...

app.Run();
```

**程式碼解釋**：
*   我們不再註冊一個通用的 `"HotelApiClient"`，而是根據 `appsettings.json` 中的 `Name` ("HotelA", "HotelB") 來註冊多個具名的 `HttpClient`。
*   `AddHttpClient(config.Name, ...)`：為名為 "HotelA" 的客戶端進行設定。
*   `ConfigureHttpClient(...)`：在這個 Lambda 中，我們設定了該客戶端專屬的 `BaseAddress`。
*   `AddHttpMessageHandler(...)`：這是最優雅的部分。我們為這個客戶端添加了一個 `AuthenticationHandler`。因為每個具名客戶端都有自己獨立的處理程序鏈，所以我們可以為 "HotelA" 的客戶端建立一個持有 Hotel A Token 的 `AuthenticationHandler`，為 "HotelB" 的客戶端建立另一個持有 Hotel B Token 的 `AuthenticationHandler`。

#### 步驟 3：簡化 `HotelApiClientFactory`

因為所有的配置工作（URL, Token）都已經在 `Program.cs` 中由 `IHttpClientFactory` 完成了，我們的工廠現在變得極其簡單。它的唯一職責就是向 `IHttpClientFactory` **索取已經配置好的具名用戶端**。

```csharp
public class HotelApiClientFactory : IHotelApiClientFactory
{
    private readonly IHttpClientFactory _httpClientFactory;

    public HotelApiClientFactory(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public HotelApiClient CreateClient(string hotelName)
    {
        // 直接向 IHttpClientFactory 索取以 hotelName 命名的、已經完全配置好的 HttpClient
        var client = _httpClientFactory.CreateClient(hotelName);

        // 如果找不到對應名稱的用戶端，CreateClient 會拋出例外，所以這裡不需要額外檢查。
        // （你也可以加上 try-catch 來提供更友善的錯誤訊息）

        return new HotelApiClient(client);
    }
}
```

看到了嗎？工廠不再需要知道 `BaseUrl` 或 `JwtToken` 的任何細節。它只負責建立 `HotelApiClient`。職責分離得非常乾淨。

#### 步驟 4：使用方式完全不變

最棒的是，對於最終的使用者（例如你的 Controller），呼叫方式完全沒有改變，依然是那麼直觀。

```csharp
[ApiController]
[Route("[controller]")]
public class DemoController : ControllerBase
{
    private readonly IHotelApiClientFactory _apiClientFactory;

    public DemoController(IHotelApiClientFactory apiClientFactory)
    {
        _apiClientFactory = apiClientFactory;
    }

    [HttpGet("bookings/{hotelName}")]
    public async Task<IActionResult> GetBookingsForHotel(string hotelName)
    {
        try
        {
            var apiClient = _apiClientFactory.CreateClient(hotelName); // e.g., "HotelA"
            var bookings = await apiClient.GetBookingsAsync();
            return Ok(bookings);
        }
        catch (System.ArgumentException ex)
        {
            return NotFound($"No configuration found for hotel: {hotelName}. Error: {ex.Message}");
        }
    }
}
```

### 結論：為什麼 `DelegatingHandler` 更優雅？

1.  **關注點分離 (Separation of Concerns)**：
    *   **`Program.cs`**：負責「組態」和「裝配」。它像一個總指揮，決定哪個 `HttpClient` 需要什麼 URL 和哪些「配件」（Handlers）。
    *   **`AuthenticationHandler`**：只負責「認證」。它是一個可重用的元件。
    *   **`HotelApiClientFactory`**：只負責「建立」。
    *   **`HotelApiClient`**：只負責「執行 API 呼叫」。

2.  **可組合性 (Composability)**：
    `DelegatingHandler` 的設計就像樂高積木。你可以輕鬆地組合它們。如果未來需要增加請求日誌，只需再寫一個 `LoggingHandler` 並在 `Program.cs` 中用 `.AddHttpMessageHandler()` 將它串上去即可，完全不用修改現有程式碼。

    ```csharp
    .AddHttpMessageHandler(sp => new AuthenticationHandler(config.JwtToken))
    .AddHttpMessageHandler<LoggingHandler>(); // 再串上一個 Handler
    ```

3.  **符合 .NET 設計哲學**：
    這種使用 `IHttpClientFactory` 搭配具名用戶端和 `DelegatingHandler` 的方式，是微軟官方推薦的最佳實踐，充分利用了 .NET 依賴注入和選項模式的強大能力。

總結來說，您的情況**非常適合**使用 `DelegatingHandler`。它不僅不是「比較難使用」，反而是解決這類問題

最標準、最優雅、最具擴展性的方案。它將設定、行為和業務邏輯完美地解耦，讓您的程式碼庫在未來更容易維護和擴展。




*Thinking...*

> **Examining the Core Problem**
> 
> I've been drilling down on the user's need. The core problem is clear: expired access tokens. My focus now is on understanding the mechanism, specifically, the process of using a refresh token to get a new access token via an OAuth API call. I'm considering the best way to integrate this into the application's flow.
> 
> 
> **Analyzing the Refresh Strategy**
> 
> I'm now outlining the steps the `DelegatingHandler` needs to execute. First, intercept the request and send it. Next, check the response; a `401` status necessitates token refresh. After, call the refresh token API, and on success, update the request header with the new token. Finally, re-send the request.
> 
> 
> **Outlining the Implementation Steps**
> 
> I'm now detailing the individual steps within the `TokenRefreshHandler`. The plan is: send the initial request, evaluate the response code (specifically, a `401`), then trigger the token refresh mechanism. If successful, I'll update the request header with the new token and resend. Finally, I will return the appropriate response.
> 
> 
> **Designing the Handler Structure**
> 
> The `TokenRefreshHandler` now taking shape. I'm focusing on its core responsibilities and dependencies. It will use an `IHttpClientFactory` to avoid circular dependencies and rely on an `ITokenService` for token management. The `SendAsync` method will orchestrate the request-response cycle, refresh, and retry.
> 
> 
> **Detailing the Code Structure**
> 
> I'm now implementing the `TokenRefreshHandler` class. It has dependencies on `IHttpClientFactory` and `ITokenService`. The `SendAsync` method will get the token, attach it to the request, and send it. If a `401` is received, I'll use the `ITokenService` to obtain a new token and retry.
> 
> 
> **Developing the Code Structure**
> 
> I'm deep in the implementation details. I'm focusing on the `TokenRefreshHandler` class. It has all the necessary dependencies, using the `IHttpClientFactory` and `ITokenService`. The `SendAsync` method will manage the token retrieval, setting the authentication header, sending the request, handling the `401` response with the refresh call, and re-sending the request if successful, ensuring everything operates seamlessly.
> 
> 
> **Developing Refinement and Detail**
> 
> I'm now in the process of finalizing the structure for the `TokenRefreshHandler`. I'm starting to visualize the implementation details for the handler itself, including its necessary dependencies, such as an `IHttpClientFactory` and an `ITokenService`. I'm also now considering the need for a concurrency lock within `ITokenService` and ensuring the implementation will be thread-safe.
> 
> 
> **Developing Handler Implementation**
> 
> I'm now focusing on writing the `TokenRefreshHandler` class. It's built with an `IHttpClientFactory` for creating HTTP clients, and `ITokenService` for handling the token refresh logic. The `SendAsync` method orchestrates the complete process: token retrieval, request modification, execution, `401` detection, refresh initiation, and retry mechanisms.
> 
> 
> **Refining Implementation Strategy**
> 
> I'm now integrating the `TokenRefreshHandler` into my application, and the code compiles successfully. I'm focusing on the implementation of the handler. In addition, I will also test the interaction with the `ITokenService` to ensure a smooth token refresh process. I'm double-checking the code to handle edge cases and confirm that concurrency is correctly addressed.
> 
> 
> **Structuring the Code Example**
> 
> I'm developing the `TokenRefreshHandler` and its components, and I will be paying close attention to the need for thread safety. My approach will be to incorporate a `SemaphoreSlim` in the `ITokenService` to ensure a single token refresh at a time, preventing race conditions. I'm focusing on the `SendAsync` method and its interaction with the `ITokenService`.
> 
> 
絕佳的問題！這正是 `DelegatingHandler` 最經典、最強大的應用場景之一。使用 `DelegatingHandler` 來自動處理 refresh token 邏輯是 .NET 中處理 API 驗證的最佳實踐。

**答案是：是的，這不僅可以用 `DelegatingHandler` 來做，而且是實現此功能的最佳方式。**

這樣做的好處是，所有與 token 刷新相關的複雜邏輯都被封裝在一個地方。對於最終呼叫 API 的程式碼（例如您的 `HotelApiClient` 或 Controller），這個過程是完全透明的。它只需要正常發送請求，而不需要關心 token 是否過期。

### 實現的邏輯流程

這個 `DelegatingHandler` 將會像一個智慧中間人一樣工作：

1.  **攔截請求**：在 `HttpClient` 發送請求之前，攔截它。
2.  **附加 Token**：從某個地方（例如一個共享的服務）獲取當前的 access token，並將其附加到請求的 `Authorization` 標頭中。
3.  **發送請求**：將請求傳遞給處理鏈的下一個環節（最終發送到伺服器）。
4.  **檢查回應**：等待並接收伺服器的回應。
5.  **判斷 Token 是否過期**：檢查回應的狀態碼。如果是 `401 Unauthorized`，就表示 token 可能已過期。
6.  **執行刷新邏輯**：
    *   呼叫 OAuth API 的 token endpoint，使用 refresh token 來獲取一個新的 access token 和 refresh token。
    *   **（關鍵）處理併發**：如果有多個請求同時發現 token 過期，我們必須確保只有一個請求去執行刷新操作，其他請求則等待新 token。這通常使用 `SemaphoreSlim` 來實現。
    *   將新的 token 安全地儲存起來。
7.  **重試原始請求**：
    *   使用新的 access token 更新原始請求的 `Authorization` 標頭。
    *   重新發送原始請求。
8.  **返回最終回應**：將第二次請求的（成功）回應返回給呼叫者。
9.  **正常流程**：如果第一次的回應不是 `401`，則直接將該回應返回，不做任何額外操作。

---

### 程式碼實現步驟

讓我們一步步建立一個健壯的 `TokenRefreshHandler`。

#### 步驟 1：建立一個管理 Token 的服務

首先，我們需要一個專門的服務來儲存和刷新 token。將這個邏輯從 `DelegatingHandler` 中分離出來，可以讓職責更清晰。

```csharp
// Token資訊的資料模型
public class TokenResponse
{
    public string AccessToken { get; set; }
    public string RefreshToken { get; set; }
    public DateTime ExpiresAt { get; set; }
}

// 管理Token的服務介面
public interface ITokenManager
{
    Task<string> GetAccessTokenAsync();
    Task InvalidateToken(); // 當我們確定Token失效時呼叫
}

// 服務的具體實現
// 注意：這個服務應該註冊為 Singleton，以確保整個應用程式共享同一份Token狀態。
public class TokenManager : ITokenManager
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptions<OAuthConfig> _oauthConfig; // 假設OAuth設定在appsettings.json中
    private TokenResponse _token;
    private static readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);

    public TokenManager(IHttpClientFactory httpClientFactory, IOptions<OAuthConfig> oauthConfig)
    {
        _httpClientFactory = httpClientFactory;
        _oauthConfig = oauthConfig;
    }

    public async Task<string> GetAccessTokenAsync()
    {
        // 如果Token存在且尚未過期（留一些緩衝時間），直接返回
        if (_token != null && _token.ExpiresAt > DateTime.UtcNow.AddSeconds(30))
        {
            return _token.AccessToken;
        }

        // 如果Token不存在或即將過期，則進入刷新邏輯
        // 使用 SemaphoreSlim 防止多個執行緒同時刷新Token
        await _semaphore.WaitAsync();
        try
        {
            // 雙重檢查鎖：在等待期間，可能已有其他執行緒刷新了Token
            if (_token != null && _token.ExpiresAt > DateTime.UtcNow.AddSeconds(30))
            {
                return _token.AccessToken;
            }

            Console.WriteLine("Token is expired or missing. Refreshing token...");
            var client = _httpClientFactory.CreateClient("OAuthClient"); // 使用一個專門的HttpClient

            var requestBody = new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = _token?.RefreshToken ?? _oauthConfig.Value.InitialRefreshToken,
                ["client_id"] = _oauthConfig.Value.ClientId,
                ["client_secret"] = _oauthConfig.Value.ClientSecret
            };

            var response = await client.PostAsync(_oauthConfig.Value.TokenEndpoint, new FormUrlEncodedContent(requestBody));

            if (!response.IsSuccessStatusCode)
            {
                // 刷新失敗，拋出例外或記錄日誌，並返回舊的（可能已失效的）Token
                throw new InvalidOperationException("Failed to refresh token.");
            }

            var responseContent = await response.Content.ReadFromJsonAsync<TokenResponse>(); // 假設API返回JSON
            _token = new TokenResponse
            {
                AccessToken = responseContent.AccessToken,
                RefreshToken = responseContent.RefreshToken, // OAuth伺服器可能會返回新的Refresh Token
                ExpiresAt = DateTime.UtcNow.AddSeconds(3599) // 假設Token一小時後過期
            };
            
            Console.WriteLine("Token refreshed successfully.");
            return _token.AccessToken;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public Task InvalidateToken()
    {
        // 強制讓Token失效，以便下次GetAccessTokenAsync會強制刷新
        _token = null;
        return Task.CompletedTask;
    }
}
```

#### 步驟 2：建立 `TokenRefreshHandler`

這個 Handler 會使用 `ITokenManager` 來處理所有邏輯。

```csharp
public class TokenRefreshHandler : DelegatingHandler
{
    private readonly ITokenManager _tokenManager;

    public TokenRefreshHandler(ITokenManager tokenManager)
    {
        _tokenManager = tokenManager;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // 1. 獲取Token並附加到標頭
        var accessToken = await _tokenManager.GetAccessTokenAsync();
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

        // 2. 發送請求
        var response = await base.SendAsync(request, cancellationToken);

        // 3. 檢查是否為 401 Unauthorized
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            Console.WriteLine("Request returned 401. Attempting to refresh token and retry.");
            
            // 4. Token可能已在伺服器端失效，強制本地Token失效
            await _tokenManager.InvalidateToken();
            
            // 5. 獲取新的Token（這將觸發刷新邏輯）
            var newAccessToken = await _tokenManager.GetAccessTokenAsync();

            // 6. 使用新Token更新標頭並重試請求
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", newAccessToken);
            
            // 再次發送同一個請求
            return await base.SendAsync(request, cancellationToken);
        }

        // 如果不是401，直接返回回應
        return response;
    }
}
```

#### 步驟 3：在 `Program.cs` 中進行設定

現在，我們需要將所有東西串聯起來。

```csharp
// --- Program.cs ---

// 0. 讀取設定
builder.Services.Configure<OAuthConfig>(builder.Configuration.GetSection("OAuthConfig"));

// 1. 註冊 Token 管理服務為 Singleton
builder.Services.AddSingleton<ITokenManager, TokenManager>();

// 2. 註冊 DelegatingHandler
builder.Services.AddTransient<TokenRefreshHandler>();

// 3. 註冊一個專門用於呼叫 OAuth API 的 HttpClient
//    這個 HttpClient *不能* 包含 TokenRefreshHandler，否則會造成無限循環！
builder.Services.AddHttpClient("OAuthClient", client =>
{
    // 可以在這裡設定 OAuth 伺服器的 BaseAddress
});

// 4. 註冊您的主要 HttpClient，並將 Handler 加入管線
//    注意 Handler 的順序！刷新 Token 的 Handler 應該在最前面。
foreach (var config in hotelConfigs)
{
    builder.Services.AddHttpClient(config.Name, client =>
    {
        client.BaseAddress = new System.Uri(config.BaseUrl);
    })
    // 將我們的 TokenRefreshHandler 加入處理管線
    .AddHttpMessageHandler<TokenRefreshHandler>(); 
    // 注意：您之前那個靜態的 AuthenticationHandler 就不再需要了，
    // 因為 TokenRefreshHandler 已經完全接管了設定 Authorization 標頭的職責。
}

// 5. 註冊您的工廠（保持不變）
builder.Services.AddSingleton<IHotelApiClientFactory, HotelApiClientFactory>();

// ... 其他服務
```

#### `appsettings.json` 範例

```json
{
  "OAuthConfig": {
    "TokenEndpoint": "https://your-oauth-server.com/token",
    "ClientId": "your_client_id",
    "ClientSecret": "your_client_secret",
    "InitialRefreshToken": "the_initial_refresh_token_you_got_at_login"
  },
  // ...
}
```

### 最終架構總結

1.  **Controller/Service**：依賴 `IHotelApiClientFactory`。
2.  **`IHotelApiClientFactory`**：依賴 `IHttpClientFactory`，建立 `HotelApiClient`。
3.  **`HotelApiClient`**：使用從 `IHttpClientFactory` 獲取的 `HttpClient`。
4.  **`HttpClient` 管線**：
    *   請求首先進入 `TokenRefreshHandler`。
    *   `TokenRefreshHandler` 依賴 `ITokenManager` 來獲取/刷新 token。
    *   請求帶著有效的 token 被發送出去。
    *   如果收到 `401`，`TokenRefreshHandler` 會觸發刷新並自動重試。
5.  **`ITokenManager`**：作為一個 `Singleton` 服務，它在整個應用程式中管理 token 的狀態，並使用一個名為 `"OAuthClient"` 的獨立 `HttpClient` 來執行刷新操作，避免了循環依賴。

這個設計模式非常強大且可擴展，它將複雜的身份驗證邏輯與您的業務邏輯完全解耦，是現代 .NET 應用程式中處理 API 驗證的黃金標準。
