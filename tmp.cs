INSERT INTO OPENQUERY(CO_GEGCRM, 'SELECT acct, gaming_dt, bucket_name, bucket_type, amount, post_dtm, related_id, remark, is_void FROM earning.bucket_adjust_transactions WHERE 1=0')
VALUES
('21008455', '2025-07-05', 'PyDollar_Base', 'Dollar', 22.096600000, '2025-07-05 04:14:43', '7123912355', 'Initial migrate from hub', 0)