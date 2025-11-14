SELECT
    t.tran_id,
    t.bucket_type,
    t.main_id,
    t.earning_rule_id,
    t.gaming_dt,
    b.bucket_name,
    t.hub_is_synced,
    -- 使用 CASE 表达式根据 hub_is_synced 的值来设定 priority
    CASE
        WHEN t.hub_is_synced = 'NotSync' THEN 1
        WHEN t.hub_is_synced = 'Pending' THEN 2
    END AS priority
FROM
    bucket_earned_transactions t
INNER JOIN
    earning_bucket b ON t.main_id = b.main_id AND b.is_latest
INNER JOIN
    earning_rule er ON er.id = t.earning_rule_id
WHERE
    -- 1. 这是两个查询共有的过滤条件
    t.gaming_dt >= CURRENT_DATE - INTERVAL '14 days'
    AND NOT t.is_void
    -- 2. 确保只选择这两种状态的数据
    AND t.hub_is_synced IN ('NotSync', 'Pending')
    -- 3. 这是两个查询中不同的核心逻辑，使用 OR 连接
    AND (
        (t.hub_is_synced = 'NotSync' AND t.last_modified_date < current_timestamp - INTERVAL '2 hour')
        OR
        (t.hub_is_synced = 'Pending' AND t.last_modified_date < CURRENT_DATE - INTERVAL '6 hour')
    )
ORDER BY
    priority,          -- 首先按我们设定的优先级排序
    gaming_dt ASC,
    tran_id DESC
LIMIT 150;