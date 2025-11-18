/// <summary>
/// 根據 CMP 系統的交易 ID (cmpTranId) 和賭場代碼 (casinoCode) 生成對應的 Hub 系統交易 ID。
/// 這個過程會從配置中查找一個前綴，並將其與原始 ID 拼接起來。
/// </summary>
/// <param name="cmpTranId">來源於 CMP 系統的交易 ID。不可為 null 或 0。</param>
/// <param name="casinoCode">用於查找 ID 前綴的賭場代碼。不可為 null 或空。</param>
/// <returns>
/// 如果成功生成，則返回一個新的 Hub 交易 ID (long)。
/// 如果輸入參數無效、找不到對應的前綴或最終生成的 ID 無法解析為長整數，則返回 null。
/// </returns>
private long? GetHubTranIdByCmpTranId(long? cmpTranId, string casinoCode)
{
    // =================================================================
    // 步驟 1: 參數驗證 (Guard Clauses)
    // 使用 "提前退出" 模式，讓主邏輯更清晰。
    // =================================================================
    if (cmpTranId is null || cmpTranId == 0 || string.IsNullOrEmpty(casinoCode))
    {
        // 參數無效，沒有繼續處理的意義，返回 null 表示失敗。
        return null;
    }

    // =================================================================
    // 步驟 2: 查找交易 ID 前綴
    // 假設 _hubTransIdPrefix 是一個 Dictionary<string, string>
    // =================================================================
    if (!_hubTransIdPrefix.TryGetValue(casinoCode, out string? hubTranIdPrefix) || string.IsNullOrEmpty(hubTranIdPrefix))
    {
        // 如果找不到對應 casinoCode 的前綴，或前綴為空，則無法生成 ID。
        // 返回 null 表示失敗。
        return null;
    }

    // =================================================================
    // 步驟 3: 組合字串並嘗試解析為 long
    // =================================================================
    string combinedIdString = hubTranIdPrefix + cmpTranId.Value; // .Value 是安全的，因為前面已檢查過 is null

    if (long.TryParse(combinedIdString, out long hubTranId))
    {
        // 解析成功，返回結果。
        return hubTranId;
    }
    else
    {
        // 如果拼接後的字串 (例如 "PREFIX12345") 不是一個有效的 long，
        // 這可能是一個配置錯誤或數據問題。返回 null 表示失敗。
        // 這裡也可以考慮記錄一個警告日誌。
        // _logger.LogWarning("無法將組合後的 ID '{CombinedId}' 解析為 long。", combinedIdString);
        return null;
    }
}