*Thinking...*

> **Analyzing User Needs Now**
> 
> I'm currently focused on the user's primary objective: extracting the `Body` field. I'm taking the core goal and the implicit context from prior interactions, working to define a refined C# code implementation. The aim is to create the most efficient and straightforward solution for this specific request.
> 
> 
> **Defining the Code's Function**
> 
> My focus has shifted to coding the implementation. I'm exploring the `Elasticsearch` library's `SearchAsync` method for source filtering, which seems optimal. The goal now is to efficiently retrieve just the `Body` field, minimizing unnecessary data transfer and processing. I'm recalling (or will check) how to use the `.Source()` method within the search descriptor for this specific purpose.
> 
> 
> **Formulating the Implementation**
> 
> Now I'm deep into the coding. I've broken down the user's need, and I'm concentrating on the source filtering approach in the C# code using the `Elastic.Clients.Elasticsearch` library. I've just finalized the thought process on how to refine the data model, opting for a `LogBodyOnly` class to enhance efficiency, and I'm ready to write the main part of the function.
> 
> 
> **Finalizing Code Logic**
> 
> I'm presently building the C# code, adapting the prior code base for the specified requirements. I'm focusing on incorporating source filtering using the `.Source()` method and `.Includes()` within the search descriptor. The approach entails refining the data model with a `LogBodyOnly` class and rewriting the `QueryAndExtractBodyAsync` method, specifically targeting the efficient extraction of the `Body` field from the Elasticsearch documents, optimizing for minimal data transfer and a clean, concise implementation.
> 
> 
> **Optimizing the Implementation**
> 
> I'm presently refactoring the code, specifically addressing source filtering for efficiency. The core focus is the `.Source()` method and its `.Includes()` configuration in the search descriptor of the `Elastic.Clients.Elasticsearch` library. I've successfully refined the `LogBodyOnly` class and the structure for the `QueryAndExtractBodyAsync` method. I'm now ready to test the refined function.
> 
> 
> **Coding the Solution's Core**
> 
> I'm presently refactoring the code, specifically addressing source filtering for efficiency. The core focus is the `.Source()` method and its `.Includes()` configuration in the search descriptor of the `Elastic.Clients.Elasticsearch` library. I've successfully refined the `LogBodyOnly` class and the structure for the `QueryAndExtractBodyAsync` method. My thoughts have shifted to integrating the parsing of `Body` content.
> 
> 
> **Coding the Complete Solution**
> 
> I'm synthesizing the code, using the previous examples, focusing on the refined data model (`LogBodyOnly`) and the .Source() filtering. I'm adapting the method with the `SearchAsync` call, specifically incorporating the `.Includes()` method, ensuring it's strongly typed, utilizing a `LogBodyOnly` class, and ensuring the data processing loop aligns with the new data model. I will add an explanation on why this approach is superior.
> 
> 
當然可以！如果您的目標只是從符合條件的 Elasticsearch 文件中提取 `Body` 欄位的內容，我們可以優化查詢，讓 Elasticsearch 只回傳這個欄位。這樣做效率更高，因為它減少了網路傳輸的資料量和 C# 客戶端需要反序列化的資料。

這個技術稱為 **Source Filtering**。

### 步驟 1：簡化 C# 資料模型

既然我們只關心 `Body` 欄位，就不再需要那個包含所有欄位的龐大 `ApiAccessLog` 類別了。我們可以定義一個只包含 `Body` 屬性的新類別。

```csharp
// 一個只用來接收 Body 欄位的精簡類別
public class LogBodyOnly
{
    [JsonPropertyName("Body")]
    public string? Body { get; set; }
}
```

我們仍然需要之前定義的、用來解析 `Body` 內容的那些類別 (`RequestBody`, `RatingUpdatePayload` 等)，所以我們會保留它們。

### 步驟 2：修改查詢以使用 Source Filtering

我們將在 `SearchAsync` 請求中加入 `.Source()` 方法，並明確告訴 Elasticsearch 我們只想要 `Body` 這個欄位。

以下是完整的、修改後的 C# 程式碼。

```csharp
using Elastic.Clients.Elasticsearch;
using Elastic.Transport;
using System.Text.Json;
using System.Text.Json.Serialization;

// --- 1. 設定您的連線資訊 ---
const string EsUrl = "https://your-elasticsearch-url:9200"; 
const string ApiKey = "your_base64_encoded_api_key"; 

// 使用萬用字元 (*) 來匹配所有 api_access_log-* 的 index
const string IndexName = "api_access_log-*";

// --- 2. 主程式 ---
public class Program
{
    public static async Task Main(string[] args)
    {
        Console.WriteLine("正在建立 Elasticsearch 客戶端...");

        var settings = new ElasticsearchClientSettings(new Uri(EsUrl))
            .Authentication(new ApiKey(ApiKey))
            .ServerCertificateValidationCallback(CertificateValidations.AllowAll) // 開發時使用，正式環境請用正式憑證
            .DefaultIndex(IndexName);

        var client = new ElasticsearchClient(settings);

        Console.WriteLine("客戶端建立成功！");
        Console.WriteLine("----------------------------------------");

        // --- 3. 執行查詢並只提取 Body 欄位 ---
        await QueryAndExtractBodyAsync(client);
    }

    private static async Task QueryAndExtractBodyAsync(ElasticsearchClient client)
    {
        Console.WriteLine("執行查詢：Service='Rating' AND Endpoint='POST /api/rating/ratingUpdate'");
        Console.WriteLine("優化：只從 _source 提取 'Body' 欄位。");

        var response = await client.SearchAsync<LogBodyOnly>(s => s
            .Index(IndexName)
            .Size(10)
            .Sort(so => so.Field("CreatedDtm", new FieldSort { Order = SortOrder.Desc }))
            .Query(q => q
                .Bool(b => b
                    .Must(
                        m => m.Match(mt => mt.Field("Service").Query("Rating")),
                        m => m.Match(mt => mt.Field("Endpoint").Query("POST /api/rating/ratingUpdate"))
                    )
                )
            )
            // --- 關鍵修改：使用 Source Filtering ---
            // 告訴 Elasticsearch 在回傳的 _source 中只包含 "Body" 欄位
            .Source(src => src
                .Includes(i => i.Field(f => f.Body))
            )
        );

        if (response.IsValidResponse)
        {
            Console.WriteLine($"\n查詢成功！共找到 {response.Total} 筆符合條件的資料。");
            Console.WriteLine("以下是每筆資料的 Body 內容 (已解析)：");

            var jsonOptions = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            };

            int docIndex = 1;
            foreach (var log in response.Documents)
            {
                Console.WriteLine($"\n--- Document #{docIndex++} Body ---");

                if (!string.IsNullOrEmpty(log.Body))
                {
                    try
                    {
                        // 將 Body 字串反序列化為 RequestBody 物件
                        var parsedBody = JsonSerializer.Deserialize<RequestBody>(log.Body, jsonOptions);

                        if (parsedBody != null)
                        {
                            Console.WriteLine($"  Message ID: {parsedBody.Header?.MessageId}");
                            Console.WriteLine($"  Operation: {parsedBody.Header?.Operation}");
                            
                            if (parsedBody.RatingUpdate != null)
                            {
                                Console.WriteLine($"  Rating ID: {parsedBody.RatingUpdate.RatingId}");
                                Console.WriteLine($"  Account: {parsedBody.RatingUpdate.RatingDetail?.Account}");
                                Console.WriteLine($"  Casino: {parsedBody.RatingUpdate.RatingDetail?.CasinoCode}");
                                Console.WriteLine($"  Table: {parsedBody.RatingUpdate.RatingDetail?.TableName}");
                                Console.WriteLine($"  Total Bets: {parsedBody.RatingUpdate.Bets?.Count ?? 0}");
                            }
                        }
                    }
                    catch (JsonException ex)
                    {
                        Console.WriteLine($"  無法解析 Body JSON: {ex.Message}");
                        Console.WriteLine($"  原始 Body 字串: {log.Body}");
                    }
                }
                else
                {
                    Console.WriteLine("  (Body 欄位為空或不存在)");
                }
            }
        }
        else
        {
            Console.WriteLine("查詢失敗！請檢查連線資訊、Index名稱和查詢語法。");
            Console.WriteLine(response.DebugInformation);
        }
    }
}


// --- 4. 資料模型 (POCOs) ---

// 精簡版模型，只用來接收 Body
public class LogBodyOnly
{
    [JsonPropertyName("Body")]
    public string? Body { get; set; }
}

// --- 以下是解析 Body 內部 JSON 所需的模型 (與之前相同) ---

public class RequestBody
{
    [JsonPropertyName("header")]
    public RequestHeader? Header { get; set; }

    [JsonPropertyName("ratingUpdate")]
    public RatingUpdatePayload? RatingUpdate { get; set; }
}

public class RequestHeader
{
    [JsonPropertyName("messageID")]
    public string? MessageId { get; set; }

    [JsonPropertyName("operation")]
    public string? Operation { get; set; }

    [JsonPropertyName("timeStamp")]
    public string? TimeStamp { get; set; }
}

public class RatingUpdatePayload
{
    [JsonPropertyName("ratingID")]
    public long RatingId { get; set; }

    [JsonPropertyName("ratingDetail")]
    public RatingDetail? RatingDetail { get; set; }

    [JsonPropertyName("winLoss")]
    public List<WinLossItem>? WinLoss { get; set; }

    [JsonPropertyName("bets")]
    public List<BetItem>? Bets { get; set; }
}

public class RatingDetail
{
    [JsonPropertyName("acct")]
    public string? Account { get; set; }

    [JsonPropertyName("isManual")]
    public bool IsManual { get; set; }

    [JsonPropertyName("seatNum")]
    public int SeatNum { get; set; }

    [JsonPropertyName("supervisorEmpID")]
    public string? SupervisorEmpId { get; set; }

    [JsonPropertyName("dealerEmpID")]
    public string? DealerEmpId { get; set; }

    [JsonPropertyName("casinoCode")]
    public string? CasinoCode { get; set; }

    [JsonPropertyName("tableName")]
    public string? TableName { get; set; }
}

public class WinLossItem
{
    [JsonPropertyName("winLossCurrency")]
    public string? WinLossCurrency { get; set; }

    [JsonPropertyName("winLossAmt")]
    public decimal WinLossAmt { get; set; }

    [JsonPropertyName("winLossChipset")]
    public string? WinLossChipset { get; set; }
}

public class BetItem
{
    [JsonPropertyName("betChipset")]
    public string? BetChipset { get; set; }

    [JsonPropertyName("betAmount")]
    public decimal BetAmount { get; set; }

    [JsonPropertyName("betWinLoss")]
    public decimal BetWinLoss { get; set; }

    [JsonPropertyName("betTheoWin")]
    public decimal BetTheoWin { get; set; }
}
```

### 程式碼變更重點

1.  **新的 `LogBodyOnly` 類別**：我們用這個極簡的類別取代了原本龐大的 `ApiAccessLog` 類別，因為我們只關心 `Body` 欄位。

2.  **`SearchAsync<LogBodyOnly>`**：查詢的泛型參數改為我們新的 `LogBodyOnly` 類別，這樣 Elasticsearch .NET 客戶端就知道要把回傳的 `_source` 映射到這個精簡的物件上。

3.  **`.Source(src => src.Includes(i => i.Field(f => f.Body)))`**：這是最核心的變更。
    *   `.Source()` 方法讓我們可以設定來源欄位過濾。
    *   `.Includes()` 指定我們想要**包含**哪些欄位。
    *   `i.Field(f => f.Body)` 是一個強型別的寫法，它會告訴 Elasticsearch 我們要 `LogBodyOnly` 類別中的 `Body` 屬性所對應的欄位。

4.  **迴圈處理**：現在 `response.Documents` 是一個 `LogBodyOnly` 物件的集合。我們直接從 `log.Body` 取得字串並進行後續的 JSON 解析，程式碼流程更直接。

這個版本不僅完全符合您的需求，而且在效能上更勝一籌。