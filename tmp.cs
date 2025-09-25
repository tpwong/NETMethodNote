-- 建立一個 IMV，預先計算好每天每個帳號的 earned 總和
CREATE INCREMENTAL MATERIALIZED VIEW earning.daily_acct_earned_summary AS
SELECT
    gaming_dt,
    acct,
    sum(earned) AS daily_sum_earned
FROM
    earning.bucket_earned_import_migration_new
WHERE
    NOT is_void
GROUP BY
    gaming_dt, acct;




CREATE INDEX daily_acct_earned_summary_dt_acct_idx 
ON earning.daily_acct_earned_summary (gaming_dt, acct);



-- 新的查詢，直接在已經計算好的小結果集上操作
SELECT sum(daily_sum_earned) as agg
FROM earning.daily_acct_earned_summary
WHERE gaming_dt >= '2025-05-01' AND acct = '999593000';
