public class SoapRoutingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<SoapRoutingMiddleware> _logger;
    private readonly IHttpClientFactory _httpClientFactory;

    // 目標端點配置
    private readonly Dictionary<string, string> _qaEndpoints = new()
    {
        { "NameService", "http://172.18.51.181:6550/NameService.asmx" },
        { "ReservationService", "http://172.18.51.181:6550/ReservationService.asmx" },
        { "GamingService", "http://172.18.51.181:6550/GamingService.asmx" }
    };

    private readonly Dictionary<string, string> _prodEndpoints = new()
    {
        { "NameService", "http://10.88.64.81:8009/NameService.asmx" },
        { "ReservationService", "http://10.88.64.81:8009/ReservationService.asmx" },
        { "GamingService", "http://10.88.64.81:8009/GamingService.asmx" }
    };

    // 特殊路由規則 - 需要轉發到自定義服務A的SOAP操作清單
    private readonly HashSet<string> _specialRoutingOperations = new(StringComparer.OrdinalIgnoreCase)
    {
        "GetPlayerInfo",
        "CheckPlayerStatus",
        "UpdatePlayerProfile"
        // 添加其他需要特殊處理的SOAP操作
    };

    // 自定義服務A的端點
    private const string CustomServiceAEndpoint = "http://custom-service-a-endpoint/api/soap";

    public SoapRoutingMiddleware(RequestDelegate next, ILogger<SoapRoutingMiddleware> logger, IHttpClientFactory httpClientFactory)
    {
        _next = next;
        _logger = logger;
        _httpClientFactory = httpClientFactory;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // 僅處理POST請求且包含SOAP內容
        if (context.Request.Method == "POST" && 
            (context.Request.ContentType?.Contains("text/xml") == true || 
             context.Request.ContentType?.Contains("application/soap+xml") == true))
        {
            // 緩存請求體，以便可以多次讀取
            context.Request.EnableBuffering();
            
            // 讀取請求體
            string requestBody;
            using (var reader = new StreamReader(context.Request.Body, Encoding.UTF8, leaveOpen: true))
            {
                requestBody = await reader.ReadToEndAsync();
                // 重置流位置以允許後續讀取
                context.Request.Body.Position = 0;
            }

            // 解析SOAP請求以確定服務和操作
            var (serviceName, operationName) = ParseSoapRequest(requestBody);
            _logger.LogInformation($"SOAP Request: Service={serviceName}, Operation={operationName}");

            // 決定目標端點
            string targetEndpoint;
            
            if (_specialRoutingOperations.Contains(operationName))
            {
                // 特殊路由 - 發送到自定義服務A
                targetEndpoint = CustomServiceAEndpoint;
                _logger.LogInformation($"Routing to custom service A: {targetEndpoint}");
            }
            else
            {
                // 常規路由 - 基於URL路徑確定使用QA還是生產環境
                var pathSegments = context.Request.Path.Value?.Split('/', StringSplitOptions.RemoveEmptyEntries);
                bool useProduction = pathSegments?.Any(s => s.Equals("prod", StringComparison.OrdinalIgnoreCase)) == true;

                var endpoints = useProduction ? _prodEndpoints : _qaEndpoints;
                
                if (!endpoints.TryGetValue(serviceName, out targetEndpoint))
                {
                    // 默認使用第一個服務
                    targetEndpoint = endpoints.FirstOrDefault().Value ?? 
                        (useProduction ? _prodEndpoints.FirstOrDefault().Value : _qaEndpoints.FirstOrDefault().Value);
                }
                
                _logger.LogInformation($"Routing to {(useProduction ? "PROD" : "QA")} endpoint: {targetEndpoint}");
            }

            // 轉發請求到目標端點
            await ForwardRequestAsync(context, targetEndpoint, requestBody);
            return;
        }

        // 非SOAP請求，繼續處理管道
        await _next(context);
    }

    private (string ServiceName, string OperationName) ParseSoapRequest(string soapXml)
    {
        try
        {
            // 創建XML文檔
            var doc = new XmlDocument();
            doc.LoadXml(soapXml);

            // 定義命名空間管理器處理SOAP命名空間
            var nsManager = new XmlNamespaceManager(doc.NameTable);
            nsManager.AddNamespace("soap", "http://schemas.xmlsoap.org/soap/envelope/");
            nsManager.AddNamespace("soap12", "http://www.w3.org/2003/05/soap-envelope");

            // 嘗試查找SOAP Body
            XmlNode bodyNode = doc.SelectSingleNode("//soap:Body", nsManager) ?? 
                               doc.SelectSingleNode("//soap12:Body", nsManager) ?? 
                               doc.SelectSingleNode("//Body", nsManager);

            if (bodyNode == null || !bodyNode.HasChildNodes)
            {
                return ("Unknown", "Unknown");
            }

            // 獲取第一個子節點作為操作節點
            XmlNode operationNode = bodyNode.FirstChild;
            
            // 從URL或操作名稱推斷服務名稱
            string serviceName = "Unknown";
            
            // 從SOAP操作名稱中推斷服務
            string operationName = operationNode.LocalName;
            if (operationName.Contains("Name"))
                serviceName = "NameService";
            else if (operationName.Contains("Reservation") || operationName.Contains("Booking"))
                serviceName = "ReservationService";
            else if (operationName.Contains("Gaming") || operationName.Contains("Game") || operationName.Contains("Player"))
                serviceName = "GamingService";
            
            return (serviceName, operationName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error parsing SOAP request");
            return ("Unknown", "Unknown");
        }
    }

    private async Task ForwardRequestAsync(HttpContext context, string targetEndpoint, string requestBody)
    {
        try
        {
            // 創建HTTP客戶端
            var client = _httpClientFactory.CreateClient("SoapForwarder");
            
            // 準備請求
            var request = new HttpRequestMessage(HttpMethod.Post, targetEndpoint)
            {
                Content = new StringContent(requestBody, Encoding.UTF8, context.Request.ContentType)
            };

            // 複製原始請求頭
            foreach (var header in context.Request.Headers)
            {
                if (!header.Key.Equals("Host", StringComparison.OrdinalIgnoreCase) &&
                    !header.Key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase) &&
                    !header.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
                {
                    request.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
                }
            }

            // 添加SOAP Action如果存在
            if (context.Request.Headers.TryGetValue("SOAPAction", out var soapAction))
            {
                request.Headers.TryAddWithoutValidation("SOAPAction", soapAction.ToArray());
            }

            // 發送請求
            var response = await client.SendAsync(request);
            
            // 讀取響應
            var responseContent = await response.Content.ReadAsStringAsync();
            
            // 設置響應狀態碼
            context.Response.StatusCode = (int)response.StatusCode;
            
            // 複製響應頭
            foreach (var header in response.Headers)
            {
                context.Response.Headers[header.Key] = header.Value.ToArray();
            }

            foreach (var header in response.Content.Headers)
            {
                context.Response.Headers[header.Key] = header.Value.ToArray();
            }
            
            // 寫入響應體
            context.Response.ContentType = response.Content.Headers.ContentType?.ToString() ?? "text/xml";
            await context.Response.WriteAsync(responseContent);
            
            _logger.LogInformation($"Request forwarded successfully to {targetEndpoint}. Status: {response.StatusCode}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error forwarding request to {targetEndpoint}");
            context.Response.StatusCode = 500;
            await context.Response.WriteAsync($"Error processing SOAP request: {ex.Message}");
        }
    }
}

// 中間件擴展方法
public static class SoapRoutingMiddlewareExtensions
{
    public static IApplicationBuilder UseSoapRouting(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<SoapRoutingMiddleware>();
    }
}