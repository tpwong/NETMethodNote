private async Task<IOrderedEnumerable<BucketBalance>> GetAvailableBucketBalance(
    string acct, string bucketType, string category, DateOnly gamingDt)
{
    // -----------------------------------------------------------------
    // 定義區域函式：負責從 DB 撈取並過濾資料 (避免重複寫兩次邏輯)
    // -----------------------------------------------------------------
    async Task<IOrderedEnumerable<BucketBalance>> FetchAndFilterAsync()
    {
        var balances = await bucketEarningDb.PlayerBucketTotal(acct, gamingDt);

        // 根據 Category 過濾
        if (string.Equals(category, "Base", StringComparison.OrdinalIgnoreCase))
        {
            balances = balances.Where(c => c.ExpiryDate is null);
        }
        else if (string.Equals(category, "Promotional", StringComparison.OrdinalIgnoreCase))
        {
            balances = balances.Where(c => c.ExpiryDate is not null);
        }

        // 排序與篩選 BucketType
        return balances
            .Where(x => x.BucketType == bucketType)
            .OrderBy(x => x.ExpiryDate ?? DateOnly.MaxValue)
            .ThenByDescending(x => x.Total);
    }

    // =================================================================
    // 步驟 1: 第一次嘗試獲取 (First Attempt)
    // =================================================================
    var availables = await FetchAndFilterAsync();

    if (!availables.IsNullOrEmpty())
    {
        return availables; // 如果有資料，直接回傳
    }

    // =================================================================
    // 步驟 2: 沒資料，準備建立初始資料 (Create Initial)
    // =================================================================
    var initialBalance = new BucketBalance
    {
        Acct = acct,
        BucketName = GetDefaultBucketName(bucketType, category),
        BucketType = bucketType,
        ExpiryDate = await GetBucketExpiryDate(bucketType, category),
        Total = 0m
    };

    // 寫入資料庫
    await bucketEarningDb.InitialBucket(initialBalance);

    // =================================================================
    // 步驟 3: 建立後，再查一次 (Second Attempt / Retry)
    // =================================================================
    availables = await FetchAndFilterAsync();

    if (!availables.IsNullOrEmpty())
    {
        return availables; // 如果這次查到了 (DB寫入成功且讀取成功)，回傳 DB 的資料
    }

    // =================================================================
    // 步驟 4: 還是查不到 (可能是 DB 延遲或寫入問題)，直接回傳剛剛建立的物件
    // =================================================================
    // 將單一物件包裝成 IOrderedEnumerable 回傳
    return new[] { initialBalance }.OrderBy(x => x.ExpiryDate);
}