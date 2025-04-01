public class PayloadBasedRoutingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IConfiguration _configuration;
    private readonly ILogger<PayloadBasedRoutingMiddleware> _logger;

    public PayloadBasedRoutingMiddleware(
        RequestDelegate next,
        IConfiguration configuration,
        ILogger<PayloadBasedRoutingMiddleware> logger)
    {
        _next = next;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // 只處理 POST/PUT 等包含 body 的請求
        if (context.Request.Method == "POST" || context.Request.Method == "PUT")
        {
            // 保存原始請求流位置
            var originalPosition = context.Request.Body.Position;
            
            // 啟用請求 Body 的重複讀取
            context.Request.EnableBuffering();
            
            // 重置流位置到開始
            context.Request.Body.Position = 0;

            // 讀取請求 Body
            using var reader = new StreamReader(
                context.Request.Body,
                encoding: Encoding.UTF8,
                detectEncodingFromByteOrderMarks: false,
                leaveOpen: true);
                
            var requestBody = await reader.ReadToEndAsync();
            
            // 重置流位置以便後續中間件可以再次讀取
            context.Request.Body.Position = 0;

            try
            {
                // 解析 JSON
                var payload = JsonDocument.Parse(requestBody);
                
                // 檢查 acct 字段
                if (payload.RootElement.TryGetProperty("acct", out var acctElement) && 
                    acctElement.ValueKind == JsonValueKind.Number || 
                    acctElement.ValueKind == JsonValueKind.String)
                {
                    string acctValue = acctElement.ToString();
                    
                    // 特定目標 acct 檢查
                    if (acctValue == "732")
                    {
                        // 設置一個特殊的 Header，後續用於路由決策
                        context.Request.Headers["X-Route-Version"] = "1.9.0";
                        _logger.LogInformation("Detected acct=732, routing to version 1.9.0");
                    }
                    else
                    {
                        _logger.LogInformation($"Using default routing for acct={acctValue}");
                    }
                }
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Failed to parse request body as JSON");
            }
        }

        await _next(context);
    }
}

// 擴展方法，方便註冊中間件
public static class PayloadBasedRoutingMiddlewareExtensions
{
    public static IApplicationBuilder UsePayloadBasedRouting(
        this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<PayloadBasedRoutingMiddleware>();
    }
}












public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // 添加 YARP 服務
        builder.Services.AddReverseProxy()
            .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"))
            .AddTransforms(transforms =>
            {
                // 添加自定義轉換，根據 X-Route-Version 頭修改目標路由
                transforms.AddRequestTransform(async context =>
                {
                    if (context.HttpContext.Request.Headers.TryGetValue("X-Route-Version", out var versionValue))
                    {
                        if (versionValue == "1.9.0")
                        {
                            // 動態修改目標
                            var destinationConfig = context.DestinationConfig;
                            context.Route = context.ProxyHost.Cluster.Destinations["v1.9.0"].Model;
                            return ValueTask.CompletedTask;
                        }
                    }
                    
                    // 保持原有路由
                    return ValueTask.CompletedTask;
                });
            });

        var app = builder.Build();

        // 使用我們的自定義中間件，必須在 UseRouting 之前
        app.UsePayloadBasedRouting();

        app.UseRouting();
        app.MapReverseProxy();

        app.Run();
    }
}


















appsettings.json

{
  "ReverseProxy": {
    "Routes": {
      "app1-route": {
        "ClusterId": "app1-cluster",
        "Match": {
          "Path": "/app1/{**catch-all}"
        }
      },
      "app2-route": {
        "ClusterId": "app2-cluster",
        "Match": {
          "Path": "/app2/{**catch-all}"
        }
      },
      "app3-route": {
        "ClusterId": "app3-cluster",
        "Match": {
          "Path": "/app3/{**catch-all}"
        }
      },
      "app4-route": {
        "ClusterId": "app4-cluster",
        "Match": {
          "Path": "/app4/{**catch-all}"
        }
      },
      "app5-route": {
        "ClusterId": "app5-cluster",
        "Match": {
          "Path": "/app5/{**catch-all}"
        }
      },
      "app6-route": {
        "ClusterId": "app6-cluster",
        "Match": {
          "Path": "/app6/{**catch-all}"
        }
      }
    },
    "Clusters": {
      "app1-cluster": {
        "Destinations": {
          "default": {
            "Address": "http://api-server-v1.6.0/"
          },
          "v1.9.0": {
            "Address": "http://api-server-v1.9.0/"
          }
        }
      },
      "app2-cluster": {
        "Destinations": {
          "default": {
            "Address": "http://api-server-v1.7.0/"
          },
          "v1.9.0": {
            "Address": "http://api-server-v1.9.0/"
          }
        }
      },
      "app3-cluster": {
        "Destinations": {
          "default": {
            "Address": "http://api-server-v1.8.0/"
          },
          "v1.9.0": {
            "Address": "http://api-server-v1.9.0/"
          }
        }
      },
      "app4-cluster": {
        "Destinations": {
          "default": {
            "Address": "http://api-server-v1.6.0/"
          },
          "v1.9.0": {
            "Address": "http://api-server-v1.9.0/"
          }
        }
      },
      "app5-cluster": {
        "Destinations": {
          "default": {
            "Address": "http://api-server-v1.7.0/"
          },
          "v1.9.0": {
            "Address": "http://api-server-v1.9.0/"
          }
        }
      },
      "app6-cluster": {
        "Destinations": {
          "default": {
            "Address": "http://api-server-v1.8.0/"
          },
          "v1.9.0": {
            "Address": "http://api-server-v1.9.0/"
          }
        }
      }
    }
  }
}



