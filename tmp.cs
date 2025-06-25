是的，在 YARP 中，您可以透過設定 `destinationPrefix` (baseUrl) 和自定義路徑來達成您的需求。有兩種主要方式可以實現：

### 方法一：使用 SendAsync 直接轉發

```csharp
public async Task ProcessRequest(HttpContext context)
{
    // 1. 基礎 URL
    string baseUrl = "https://api.example.com";
    
    // 2. 函數的特定端點
    string functionEndpoint = "/api/function";
    
    // 合併成完整的目標 URL
    string destinationUrl = baseUrl + functionEndpoint;
    
    // 建立 HttpClient
    var httpClient = new HttpMessageInvoker(new SocketsHttpHandler()
    {
        UseProxy = false,
        AllowAutoRedirect = false,
        AutomaticDecompression = DecompressionMethods.None,
        UseCookies = false
    });
    
    // 配置轉發請求
    var requestConfig = new ForwarderRequestConfig { Timeout = TimeSpan.FromSeconds(100) };
    
    // 建立自定義轉換器，用於設定目標 URL
    var transformer = new CustomTransformer(functionEndpoint);
    
    // 執行轉發
    await context.ForwardAsync(destinationUrl, httpClient, requestConfig, transformer);
}

// 自定義轉換器
public class CustomTransformer : HttpTransformer
{
    private readonly string _functionEndpoint;
    
    public CustomTransformer(string functionEndpoint)
    {
        _functionEndpoint = functionEndpoint;
    }
    
    public override ValueTask TransformRequestAsync(HttpContext httpContext, HttpRequestMessage proxyRequest, string destinationPrefix)
    {
        // 將請求直接指向目標 URL，忽略原始路徑
        proxyRequest.RequestUri = new Uri(destinationPrefix);
        
        return base.TransformRequestAsync(httpContext, proxyRequest, destinationPrefix);
    }
}
```

### 方法二：使用 YARP 配置方式

在 `Program.cs` 或 `Startup.cs` 中：

```csharp
public void ConfigureServices(IServiceCollection services)
{
    // 添加 YARP 服務
    services.AddReverseProxy()
        .LoadFromConfig(Configuration.GetSection("ReverseProxy"));
}

public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
{
    // 其他中間件配置...
    
    // 使用 YARP
    app.UseEndpoints(endpoints =>
    {
        endpoints.MapReverseProxy(proxyPipeline =>
        {
            // 自定義轉發邏輯
            proxyPipeline.Use((context, next) =>
            {
                // 覆蓋路由配置
                var routeConfig = context.Features.Get<IReverseProxyFeature>().Route.Config;
                
                // 使用自定義的 transformer
                context.Features.Set<IHttpTransformer>(new CustomTransformer("/api/function"));
                
                return next();
            });
        });
    });
}
```

然後在 `appsettings.json` 中：

```json
{
  "ReverseProxy": {
    "Routes": {
      "route1": {
        "ClusterId": "cluster1",
        "Match": {
          "Path": "{**catch-all}"
        }
      }
    },
    "Clusters": {
      "cluster1": {
        "Destinations": {
          "destination1": {
            "Address": "https://api.example.com"
          }
        }
      }
    }
  }
}
```

這兩種方法都能讓您：
1. 設定基礎 URL (`baseUrl`)
2. 指定函數端點 (`functionEndpoint`)

然後將請求直接轉發到完整的目標 URL，而不考慮原始請求的路徑。

您可以根據您的具體需求選擇最適合的方式。