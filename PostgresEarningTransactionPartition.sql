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
    
    -- 創建該季度的所有哈希分區並添加索引
    FOR hash_id IN 0..15 LOOP
        partition_name := 'transactions_' || quarter_name || '_hash' || hash_id;
        
        EXECUTE format(
            'CREATE TABLE IF NOT EXISTS %I PARTITION OF transactions_%s ' ||
            'FOR VALUES WITH (MODULUS 16, REMAINDER %s)',
            partition_name, quarter_name, hash_id
        );
        
        -- 創建索引
        PERFORM create_partition_indexes(partition_name);
    END LOOP;
END;
$$ LANGUAGE plpgsql;

------------------------------------------------------------------------------------


CREATE OR REPLACE FUNCTION purge_old_partitions()
RETURNS void AS $$
DECLARE
    purge_before DATE := CURRENT_DATE - interval '7 years';
    quarters_to_check RECORD;
    partition_earliest_date DATE;
    partition_name TEXT;
BEGIN
    -- 查找所有可能需要清理的季度分區
    FOR quarters_to_check IN 
        SELECT table_name 
        FROM information_schema.tables 
        WHERE table_name LIKE 'transactions_20%Q_'
        ORDER BY table_name
    LOOP
        partition_name := quarters_to_check.table_name;
        
        -- 獲取分區中的最新日期
        EXECUTE format(
            'SELECT COALESCE(MAX(last_modified_date), ''9999-12-31''::date) FROM %I',
            partition_name
        ) INTO partition_earliest_date;
        
        -- 如果整個分區都是舊數據，則刪除
        IF partition_earliest_date < purge_before THEN
            EXECUTE format('DROP TABLE %I CASCADE', partition_name);
            RAISE NOTICE 'Dropped partition: %', partition_name;
        END IF;
    END LOOP;
END;
$$ LANGUAGE plpgsql;

