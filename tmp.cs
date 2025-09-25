-- 1. 刪除舊的 BRIN 索引
DROP INDEX earning.bucket_earned_transactions_hub_tran_idx;

-- 2. 建立新的 B-Tree 索引 (這是正確的選擇)
--    同樣使用部分索引 (Partial Index) 來縮小索引體積，只索引未作廢的記錄
CREATE INDEX bucket_earned_transactions_hub_tran_idx 
ON earning.bucket_earned_transactions (hub_tran_id) 
WHERE (NOT is_void);


