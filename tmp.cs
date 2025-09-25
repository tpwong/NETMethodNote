-- 為 4 月份分區添加約束的範例
ALTER TABLE earning.bucket_earned_import_migration_new_1 
ADD CONSTRAINT bucket_earned_import_migration_new_1_gaming_dt_check 
CHECK (gaming_dt >= '2025-04-01' AND gaming_dt < '2025-05-01');
