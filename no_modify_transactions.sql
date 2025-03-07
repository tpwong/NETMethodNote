-- 創建一個觸發器函數，當有人嘗試修改記錄時拋出異常
CREATE OR REPLACE FUNCTION prevent_modify_point_transactions()
RETURNS TRIGGER AS $$
BEGIN
    -- 拋出異常，阻止修改操作，並顯示被嘗試修改的記錄ID
    RAISE EXCEPTION '不允許修改交易記錄! 交易ID: %', OLD.id;
END;
$$ LANGUAGE plpgsql;

-- 創建第一個觸發器，命名為"no_update_point_transactions"
-- 它會在UPDATE操作前觸發，應用於point_transactions表的每一行
CREATE TRIGGER no_update_point_transactions
BEFORE UPDATE ON point_transactions  -- 這裡的point_transactions是表名
FOR EACH ROW EXECUTE FUNCTION prevent_modify_point_transactions();

-- 創建第二個觸發器，命名為"no_delete_point_transactions"
-- 它會在DELETE操作前觸發，應用於point_transactions表的每一行
CREATE TRIGGER no_delete_point_transactions
BEFORE DELETE ON point_transactions  -- 這裡的point_transactions是表名
FOR EACH ROW EXECUTE FUNCTION prevent_modify_point_transactions();
