WITH Player AS (
    SELECT pc.PlayerId, pc.Acct 
    FROM dbo.tPlayerCard pc (NOLOCK) 
    WHERE TRY_CAST(pc.Acct AS bigint) % @chunkSize = @acctModValue
),
FilteredAwards AS (
    SELECT a.PlayerId, a.TranCodeId, a.TranId, a.AuthAward, a.CasinoId
    FROM dbo.tAwards a (NOLOCK)
    INNER JOIN Player p ON a.PlayerId = p.PlayerId
    WHERE a.TranCodeId IN (17, 252) 
      AND a.IsVoid = 0 
      AND a.IsOpenItem = 0
      AND a.ModifiedDtm BETWEEN @startPostDtm AND @endPostDtm
),
RelatedTrans AS (
    SELECT DISTINCT TranId
    FROM FilteredAwards
    WHERE TranCodeId = 252
),
ExcludedTrans AS (
    SELECT DISTINCT b.TranId
    FROM dbo.tAwards b (NOLOCK)
    INNER JOIN RelatedTrans rt ON b.TranId = rt.TranId
    WHERE b.TranCodeId = 252 
      AND b.IsVoid = 0 
      AND b.ModifiedDtm BETWEEN @startPostDtm AND @endPostDtm
      AND NOT EXISTS (
          SELECT 1 FROM RelatedTrans rt2 
          WHERE rt2.TranId = b.TranId
      )
),
SettleTransactions AS (
    SELECT fa.PlayerId, 
           ISNULL(SUM(ISNULL(-fa.AuthAward, 0)), 0) AS mgmtComp
    FROM FilteredAwards fa
    WHERE fa.TranCodeId = 17
      AND NOT EXISTS (SELECT 1 FROM ExcludedTrans et WHERE et.TranId = fa.TranId)
    GROUP BY fa.PlayerId
),
AwardTransactions AS (
    SELECT fa.PlayerId,
           SUM(fa.AuthAward) AS mgmtComp  
    FROM FilteredAwards fa
    WHERE fa.TranCodeId = 252
      AND NOT EXISTS (SELECT 1 FROM ExcludedTrans et WHERE et.TranId = fa.TranId)
    GROUP BY fa.PlayerId
),
mgmtBal AS (
    SELECT p.PlayerId, p.Acct,
           COALESCE(st.mgmtComp, 0) + COALESCE(at.mgmtComp, 0) AS mgmtComp
    FROM Player p
    LEFT JOIN SettleTransactions st ON p.PlayerId = st.PlayerId
    LEFT JOIN AwardTransactions at ON p.PlayerId = at.PlayerId
    WHERE (st.mgmtComp IS NOT NULL OR at.mgmtComp IS NOT NULL)
),
eachMComp AS (
    SELECT DISTINCT p.Acct AS Acct, 
           @gamingDay AS GamingDpt, 
           @mCompBucketName AS BucketName, 
           'MComp' AS BucketType,
           ISNULL(mb.mgmtComp, 0) AS Amount, 
           CURRENT_TIMESTAMP AS PostDtm, 
           'Initial migrate from hub' AS Remark
    FROM Player p 
    LEFT JOIN mgmtBal mb ON p.Acct = mb.Acct
    LEFT JOIN dbo.tCasino c (NOLOCK) ON c.IsInactive = 0 
    WHERE mb.mgmtComp IS NOT NULL
)
SELECT * FROM eachMComp;
