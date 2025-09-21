-- 建議將所有操作包在一個交易中
BEGIN;

-- 步驟 1: 將所有既有的 NULL 值更新為當前時間
-- 這是必要的步驟，否則下一步設定 NOT NULL 會失敗
UPDATE your_table_name
SET your_column_name = now()
WHERE your_column_name IS NULL;

-- 步驟 2: 修改欄位屬性，設定 NOT NULL 和 DEFAULT 值
-- 您可以將多個 ALTER COLUMN 操作合併在一個 ALTER TABLE 指令中
ALTER TABLE your_table_name
    ALTER COLUMN your_column_name SET NOT NULL,
    ALTER COLUMN your_column_name SET DEFAULT now();

-- 確認無誤後，提交交易
COMMIT;