CREATE OR REPLACE FUNCTION create_quarter_partitions()
RETURNS void AS $$
DECLARE
    hash_id INTEGER;
    next_quarter DATE := date_trunc('quarter', CURRENT_DATE + interval '3 month');
    quarter_name TEXT;
    quarter_start TEXT;
    quarter_end TEXT;
    partition_name TEXT;
BEGIN
    -- 生成季度名稱 (如：2023Q2)
    quarter_name := to_char(next_quarter, 'YYYY') || 'Q' || 
                    to_char(date_part('quarter', next_quarter), '9');
    quarter_start := to_char(next_quarter, 'YYYY-MM-DD');
    quarter_end := to_char(next_quarter + interval '3 month', 'YYYY-MM-DD');
    
    -- 創建季度分區
    EXECUTE format(
        'CREATE TABLE IF NOT EXISTS transactions_%s PARTITION OF transactions ' ||
        'FOR VALUES FROM (%L) TO (%L) ' ||
        'PARTITION BY HASH (acct_id)',
        quarter_name, quarter_start, quarter_end
    );
    
    -- 創建該季度的所有哈希分區
    FOR hash_id IN 0..15 LOOP
        partition_name := 'transactions_' || quarter_name || '_hash' || hash_id;
        
        EXECUTE format(
            'CREATE TABLE IF NOT EXISTS %I PARTITION OF transactions_%s ' ||
            'FOR VALUES WITH (MODULUS 16, REMAINDER %s)',
            partition_name, quarter_name, hash_id
        );
    END LOOP;
END;
$$ LANGUAGE plpgsql;

------------------------------------------------------------------------------------


CREATE OR REPLACE FUNCTION purge_old_partitions()
RETURNS void AS $$
DECLARE
    purge_before DATE := CURRENT_DATE - interval '7 years';
    purge_quarter TEXT;
    partition_exists BOOLEAN;
BEGIN
    purge_quarter := to_char(date_trunc('quarter', purge_before), 'YYYY') || 'Q' || 
                     to_char(date_part('quarter', date_trunc('quarter', purge_before)), '9');
    
    -- 檢查分區是否存在
    SELECT EXISTS (
        SELECT 1 FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace 
        WHERE c.relname = 'transactions_' || purge_quarter AND n.nspname = 'public'
    ) INTO partition_exists;
    
    -- 如果存在就刪除
    IF partition_exists THEN
        EXECUTE format('DROP TABLE transactions_%s CASCADE', purge_quarter);
        RAISE NOTICE 'Dropped partition for quarter: %', purge_quarter;
    END IF;
END;
$$ LANGUAGE plpgsql;

