-- 說明:
-- 這是修正後的版本，已將您指出的 `TranId = RelatedTranId` 條件加回。
-- 這個條件對於篩選出 TranCodeId = 17 的「主交易」至關重要。

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
    SELECT DISTINCT 
        a.RelatedTranId
    FROM dbo.tAwards a (NOLOCK)
    INNER JOIN Player p ON a.PlayerId = p.PlayerId
    WHERE a.TranCodeId = 252 
      AND a.IsVoid = 0 
      AND a.ModifiedDtm BETWEEN @startPostDtm AND @endPostDtm
),
CombinedTransactions AS (
    -- 步驟 3: 合併所有相關交易，並應用所有篩選邏輯

    -- 邏輯分支 1: 1st step Settle Transaction (IsOpenItem = 1)
    SELECT 
        a.PlayerId, 
        a.AuthAward AS Val
    FROM dbo.tAwards a (NOLOCK)
    WHERE a.PlayerId IN (SELECT PlayerId FROM Player)
      AND a.TranCodeId = 17
      AND a.TranId = a.RelatedTranId -- <<<<<<< 在此處補上遺漏的關鍵條件
      AND a.IsOpenItem = 1 
      AND a.IsVoid = 0
      AND NOT EXISTS ( -- 使用 NOT EXISTS 替代 NOT IN
          SELECT 1 
          FROM TransactionsToExclude te 
          WHERE te.RelatedTranId = a.TranId
      )

    UNION ALL

    -- 邏輯分支 2: 2nd step Settle Transaction
    SELECT 
        a.PlayerId, 
        a.AwardUsed AS Val -- 此處使用 AwardUsed
    FROM dbo.tAwards a (NOLOCK)
    WHERE a.PlayerId IN (SELECT PlayerId FROM Player)
      AND a.TranCodeId = 252 
      AND a.IsVoid = 0 
      AND a.ModifiedDtm BETWEEN @startPostDtm AND @endPostDtm

    UNION ALL

    -- 邏輯分支 3: 1st step Settle Transaction (IsOpenItem = 0)
    SELECT 
        a.PlayerId, 
        a.AuthAward AS Val
    FROM dbo.tAwards a (NOLOCK)
    WHERE a.PlayerId IN (SELECT PlayerId FROM Player)
      AND a.TranCodeId = 17
      AND a.TranId = a.RelatedTranId -- <<<<<<< 在此處也補上遺漏的關鍵條件
      AND a.IsOpenItem = 0 
      AND a.IsVoid = 0
      AND NOT EXISTS ( -- 再次應用排除邏輯
          SELECT 1 
          FROM TransactionsToExclude te 
          WHERE te.RelatedTranId = a.TranId
      )
),
mgmtBal AS (
    -- 步驟 4: 對合併後的交易進行分組聚合，並應用負值邏輯
    SELECT 
        p.Acct, 
        ISNULL(SUM(ISNULL(ct.Val, 0)), 0) * -1 AS mgmtComp
    FROM Player p
    INNER JOIN CombinedTransactions ct ON p.PlayerId = ct.PlayerId
    GROUP BY p.Acct
)
-- 最終查詢
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