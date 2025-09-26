using System.Collections.Concurrent;
using System.Diagnostics;

Console.WriteLine("--- 模式一：直接併發處理 Dictionary 中的每個項目 ---");

// =================================================================
// 1. 準備資料
// =================================================================
// 模擬一個包含待處理訂單的字典
var ordersToProcess = new Dictionary<string, OrderData>
{
    { "ORD-001", new OrderData("CUST-A", 199.99m) },
    { "ORD-002", new OrderData("CUST-B", 49.50m) },
    { "ORD-003", new OrderData("CUST-A", 88.00m) },
    { "ORD-004", new OrderData("CUST-C", 120.00m) },
    { "ORD-005", new OrderData("CUST-B", 300.10m) },
    { "ORD-006", new OrderData("CUST-D", 75.25m) },
    { "ORD-007", new OrderData("CUST-A", 55.00m) },
    { "ORD-008", new OrderData("CUST-C", 450.75m) },
};

// 用於存放處理結果，使用 ConcurrentDictionary 以確保線程安全
var processedResults = new ConcurrentDictionary<string, string>();

// =================================================================
// 2. 設定併發並執行
// =================================================================
int concurrencyLevel = 4; // 希望最多有 4 個訂單同時被處理
Console.WriteLine($"準備處理 {ordersToProcess.Count} 筆訂單，最大併發數: {concurrencyLevel}\n");

var stopwatch = Stopwatch.StartNew();

var parallelOptions = new ParallelOptions
{
    MaxDegreeOfParallelism = concurrencyLevel
};

// 直接將字典傳遞給 Parallel.ForEachAsync
// 迭代的 'item' 將會是一個 KeyValuePair<string, OrderData>
await Parallel.ForEachAsync(ordersToProcess, parallelOptions, async (orderPair, cancellationToken) =>
{
    string orderId = orderPair.Key;
    OrderData orderData = orderPair.Value;

    Console.WriteLine($"[任務 {Task.CurrentId, -2}] >> 開始處理訂單: {orderId} (客戶: {orderData.CustomerId})...");
    
    // 模擬耗時的 I/O 操作
    await Task.Delay(1000, cancellationToken);

    string resultMessage = $"訂單 {orderId} 處理成功，金額: {orderData.Amount:C}";
    processedResults[orderId] = resultMessage;

    Console.WriteLine($"[任務 {Task.CurrentId, -2}] << 完成處理訂單: {orderId}。");
});

stopwatch.Stop();

// =================================================================
// 3. 輸出結果
// =================================================================
Console.WriteLine($"\n所有訂單處理完畢，總耗時: {stopwatch.ElapsedMilliseconds}ms");
Console.WriteLine("處理結果:");
foreach (var result in processedResults.OrderBy(kvp => kvp.Key))
{
    Console.WriteLine($"- {result.Key}: {result.Value}");
}


// 模擬的資料模型
public record OrderData(string CustomerId, decimal Amount);