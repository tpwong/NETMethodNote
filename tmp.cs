-- 說明:
-- 這個優化版本使用多個 CTEs 來分解原始的複雜查詢，以提高可讀性和可維護性。
-- 1. Player: 與原始查詢相同，用於分塊選取玩家。
-- 2. TransactionsToExclude: 獨立出原始查詢中的 NOT IN 子查詢，使其更清晰。
--    這部分定義了哪些 TranCodeId = 17 的交易需要被排除。
-- 3. CombinedTransactions: 將原始查詢中的三個 UNION ALL 部分合併，並應用排除邏輯。
--    這裡正確處理了不同 TranCodeId 所對應的不同欄位 (AuthAward vs. AwardUsed)。
-- 4. mgmtBal: 執行最終的聚合計算。關鍵點是，SUM 的結果是在這裡才被取負值，
--    這與原始查詢的 SUM(ISNULL(-Val, 0)) 邏輯在效果上是等價的。
-- 5. Final Select: 最終的查詢結構與原始查詢保持一致。

WITH Player AS (
    -- 步驟 1: 根據分塊邏輯選取目標玩家
    SELECT 
        pc.PlayerId, 
        pc.Acct 
    FROM dbo.tPlayerCard pc (NOLOCK) 
    WHERE TRY_CAST(pc.Acct AS BIGINT) % @chunkSize = @acctModValue
),
TransactionsToExclude AS (
    -- 步驟 2: 找出所有需要被排除的關聯交易ID (對應原始查詢的 NOT IN 子句)
    -- 這些是 TranCodeId = 252 的交易中記錄的 RelatedTranId
    SELECT DISTINCT 
        a.RelatedTranId
    FROM dbo.tAwards a (NOLOCK)
    INNER JOIN Player p ON a.PlayerId = p.PlayerId
    WHERE a.TranCodeId = 252 
      AND a.IsVoid = 0 
      AND a.ModifiedDtm BETWEEN @startPostDtm AND @endPostDtm
),
CombinedTransactions AS (
    -- 步驟 3: 合併所有相關交易，並應用排除邏輯
    -- 這部分取代了原始查詢中的 `eachMComp` 子查詢

    -- 邏輯分支 1: 1st step Settle Transaction (IsOpenItem = 1)
    SELECT 
        a.PlayerId, 
        a.AuthAward AS Val -- 使用 AuthAward
    FROM dbo.tAwards a (NOLOCK)
    WHERE a.PlayerId IN (SELECT PlayerId FROM Player)
      AND a.TranCodeId = 17
      AND a.IsOpenItem = 1 
      AND a.IsVoid = 0
      AND NOT EXISTS ( -- 使用 NOT EXISTS 替代 NOT IN，通常性能更好
          SELECT 1 
          FROM TransactionsToExclude te 
          WHERE te.RelatedTranId = a.TranId
      )

    UNION ALL

    -- 邏輯分支 2: 2nd step Settle Transaction
    SELECT 
        a.PlayerId, 
        a.AwardUsed AS Val -- 使用 AwardUsed
    FROM dbo.tAwards a (NOLOCK)
    WHERE a.PlayerId IN (SELECT PlayerId FROM Player)
      AND a.TranCodeId = 252 
      AND a.IsVoid = 0 
      AND a.ModifiedDtm BETWEEN @startPostDtm AND @endPostDtm

    UNION ALL

    -- 邏輯分支 3: 1st step Settle Transaction (IsOpenItem = 0)
    SELECT 
        a.PlayerId, 
        a.AuthAward AS Val -- 使用 AuthAward
    FROM dbo.tAwards a (NOLOCK)
    WHERE a.PlayerId IN (SELECT PlayerId FROM Player)
      AND a.TranCodeId = 17
      AND a.IsOpenItem = 0 
      AND a.IsVoid = 0
      AND NOT EXISTS ( -- 再次應用排除邏輯
          SELECT 1 
          FROM TransactionsToExclude te 
          WHERE te.RelatedTranId = a.TranId
      )
),
mgmtBal AS (
    -- 步驟 4: 對合併後的交易進行分組聚合，並在此處應用負值邏輯
    SELECT 
        p.Acct, 
        ISNULL(SUM(ISNULL(ct.Val, 0)), 0) * -1 AS mgmtComp -- 先加總，再將總和變為負數
    FROM Player p
    INNER JOIN CombinedTransactions ct ON p.PlayerId = ct.PlayerId
    GROUP BY p.Acct
)
-- 最終查詢: 結構與原始查詢相同，只是 JOIN 的對象變成了 CTE
SELECT DISTINCT 
    p.Acct AS Acct, 
    @gamingDay AS GamingDt, 
    @mCompBucketName AS BucketName, 
    'MComp' AS BucketType,
    ISNULL(mb.mgmtComp, 0) AS Amount, 
    CURRENT_TIMESTAMP AS PostDtm, 
    'Initial migrate from hub' AS Remark
FROM Player p 
INNER JOIN dbo.tCasino c (NOLOCK) ON c.IsInactive = 0 
INNER JOIN mgmtBal mb ON p.Acct = mb.Acct;