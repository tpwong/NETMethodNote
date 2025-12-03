BEGIN TRANSACTION ISOLATION LEVEL READ COMMITTED;

-- 先鎖住會被這次交易影響到的 bucket_balances
WITH summary AS (
    SELECT acct, t.bucket_name, t.bucket_type, expiry_date, SUM(earned) total
    FROM bucket_earned_transactions t
    JOIN earning_bucket e ON t.main_id = e.main_id AND e.is_latest
    WHERE tran_id = 50981524541
    GROUP BY acct, t.bucket_name, t.bucket_type, expiry_date
)
SELECT 1
FROM bucket_balances b
JOIN summary s
  ON  b.acct        = s.acct
  AND b.bucket_name = s.bucket_name
  AND b.bucket_type = s.bucket_type
  AND (
        (b.expiry_date IS NULL AND s.expiry_date IS NULL)
     OR (b.expiry_date = s.expiry_date)
      )
FOR UPDATE;

-- 再用同樣邏輯跑 MERGE（這是第二條語句，要再宣告一次 CTE）
WITH summary AS (
    SELECT acct, t.bucket_name, t.bucket_type, expiry_date, SUM(earned) total
    FROM bucket_earned_transactions t
    JOIN earning_bucket e ON t.main_id = e.main_id AND e.is_latest
    WHERE tran_id = 50981524541
    GROUP BY acct, t.bucket_name, t.bucket_type, expiry_date
)
MERGE INTO bucket_balances b
USING summary t
  ON  b.acct        = t.acct
  AND b.bucket_type = t.bucket_type
  AND (
        (b.expiry_date IS NULL AND t.expiry_date IS NULL)
     OR (b.expiry_date = t.expiry_date)
      )
  AND b.bucket_name = t.bucket_name
WHEN MATCHED THEN
  UPDATE SET total = b.total - t.total,
             last_modified_date = CURRENT_TIMESTAMP
WHEN NOT MATCHED THEN
  INSERT (acct, bucket_name, bucket_type, expiry_date, total)
  VALUES (t.acct, t.bucket_name, t.bucket_type, t.expiry_date, -t.total)
RETURNING clock_timestamp() AS ts, ...;

COMMIT;