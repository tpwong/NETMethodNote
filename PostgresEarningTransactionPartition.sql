CREATE OR REPLACE FUNCTION create_bet_quarter_partitions()
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
        'CREATE TABLE IF NOT EXISTS bucket_earned_transactions_%s ' ||
        'PARTITION OF bucket_earned_transactions ' ||
        'FOR VALUES FROM (%L) TO (%L) ' ||
        'PARTITION BY HASH (acct_id)',
        quarter_name, quarter_start, quarter_end
    );
    
    -- 創建該季度的所有哈希子分區
    FOR hash_id IN 0..15 LOOP
        partition_name := 'bucket_earned_transactions_' || quarter_name || '_hash' || hash_id;
        
        EXECUTE format(
            'CREATE TABLE IF NOT EXISTS %I ' ||
            'PARTITION OF bucket_earned_transactions_%s ' ||
            'FOR VALUES WITH (MODULUS 16, REMAINDER %s)',
            partition_name, quarter_name, hash_id
        );
        
        -- 創建索引
        EXECUTE format(
            'CREATE INDEX IF NOT EXISTS idx_%s_acct_txn ON %I (acct_id, txn_date)',
            replace(partition_name, 'bucket_earned_transactions_', 'bet_'), 
            partition_name
        );
        
        EXECUTE format(
            'CREATE INDEX IF NOT EXISTS idx_%s_bucket ON %I (bucket_id)',
            replace(partition_name, 'bucket_earned_transactions_', 'bet_'), 
            partition_name
        );
    END LOOP;
    
    RAISE NOTICE 'Created partitions for quarter: %', quarter_name;
END;
$$ LANGUAGE plpgsql;

------------------------------------------------------------------------------------


CREATE OR REPLACE FUNCTION purge_old_bet_partitions()
RETURNS void AS $$
DECLARE
    purge_before DATE := CURRENT_DATE - interval '7 years';
    quarter_to_check RECORD;
    quarter_start DATE;
BEGIN
    -- 查找所有可能需要清理的季度分區
    FOR quarter_to_check IN 
        SELECT table_name 
        FROM information_schema.tables 
        WHERE table_name LIKE 'bucket_earned_transactions_20%Q%'
          AND table_name NOT LIKE '%hash%'  -- 只檢查季度主分區
        ORDER BY table_name
    LOOP
        -- 從分區名稱提取季度起始日期
        IF quarter_to_check.table_name ~ 'bucket_earned_transactions_([0-9]{4})Q([1-4])' THEN
            -- 提取年份和季度
            EXECUTE format(
                'SELECT to_date(substring(%L from 
                ''bucket_earned_transactions_([0-9]{4})Q([1-4])'', 1) || ''-'' || 
                ((substring(%L from ''bucket_earned_transactions_([0-9]{4})Q([1-4])'', 2)::int - 1) * 3 + 1) || ''-01'', 
                ''YYYY-MM-DD'')',
                quarter_to_check.table_name, quarter_to_check.table_name
            ) INTO quarter_start;
            
            -- 如果整個季度都在清理日期之前，則刪除
            IF quarter_start + interval '3 months' <= purge_before THEN
                EXECUTE format('DROP TABLE %I CASCADE', quarter_to_check.table_name);
                RAISE NOTICE 'Dropped partition: %', quarter_to_check.table_name;
            END IF;
        END IF;
    END LOOP;
END;
$$ LANGUAGE plpgsql;
