INSERT INTO OPENQUERY(CO_GEGCRM, 'SELECT acct, gaming_dt, bucket_name, bucket_type, amount, post_dtm, related_id, remark, is_void FROM earning.bucket_adjust_transactions WHERE 1=0')
VALUES
('21008455', '2025-07-05', 'PyDollar_Base', 'Dollar', 22.096600000, '2025-07-05 04:14:43', '7123912355', 'Initial migrate from hub', 0)




-- 變數聲明
DECLARE @BatchSize INT = 20000           -- 每批處理的記錄數
DECLARE @TotalRecords INT                -- 總記錄數
DECLARE @ProcessedRecords INT = 0        -- 已處理的記錄數
DECLARE @BatchNumber INT = 0             -- 當前批次號
DECLARE @StartTime DATETIME              -- 開始時間
DECLARE @EndTime DATETIME                -- 結束時間
DECLARE @TotalTime INT                   -- 總耗時(秒)
DECLARE @ErrorMessage NVARCHAR(4000)     -- 錯誤訊息

-- 獲取總記錄數
SELECT @TotalRecords = COUNT(*) 
FROM YourSourceTable
WHERE YourConditions

-- 記錄開始時間
SET @StartTime = GETDATE()
PRINT '開始批量插入作業: ' + CONVERT(VARCHAR, @StartTime, 120)
PRINT '總記錄數: ' + CAST(@TotalRecords AS VARCHAR)

-- 創建臨時表來存儲批次處理的ID範圍
CREATE TABLE #BatchRanges (
    BatchID INT IDENTITY(1,1),
    StartID INT,
    EndID INT
)

-- 假設有一個ID欄位用於分批 (如果沒有，可以使用ROW_NUMBER())
-- 填充批次範圍表
;WITH Numbered AS (
    SELECT 
        ROW_NUMBER() OVER (ORDER BY ID) AS RowNum, -- 替換ID為實際的唯一標識符
        ID
    FROM 
        YourSourceTable
    WHERE 
        YourConditions
)
INSERT INTO #BatchRanges (StartID, EndID)
SELECT 
    MIN(ID) AS StartID,
    MAX(ID) AS EndID
FROM 
    Numbered
GROUP BY 
    (RowNum - 1) / @BatchSize

-- 獲取批次總數
DECLARE @TotalBatches INT
SELECT @TotalBatches = COUNT(*) FROM #BatchRanges

-- 批量處理循環
WHILE @BatchNumber < @TotalBatches
BEGIN
    BEGIN TRY
        SET @BatchNumber = @BatchNumber + 1
        
        DECLARE @StartID INT, @EndID INT
        SELECT @StartID = StartID, @EndID = EndID 
        FROM #BatchRanges 
        WHERE BatchID = @BatchNumber
        
        PRINT '處理批次 ' + CAST(@BatchNumber AS VARCHAR) + ' 共 ' + CAST(@TotalBatches AS VARCHAR) + 
              ' 批次 (ID 範圍: ' + CAST(@StartID AS VARCHAR) + ' - ' + CAST(@EndID AS VARCHAR) + ')'
        
        -- 執行批量插入
        INSERT INTO OPENQUERY(CO_GEGCRM, 
            'SELECT acct, gaming_dt, bucket_name, bucket_type, amount, post_dtm, related_id, remark, is_void 
             FROM earning.bucket_adjust_transactions WHERE 1=0')
        SELECT 
            account_number,
            CONVERT(VARCHAR(10), gaming_date, 120),
            bucket_name,
            bucket_type,
            amount,
            CONVERT(VARCHAR(19), post_datetime, 120),
            related_id,
            remark,
            CASE WHEN is_void = 1 THEN 1 ELSE 0 END
        FROM 
            YourSourceTable
        WHERE 
            ID BETWEEN @StartID AND @EndID
            AND YourConditions
        
        -- 更新進度
        SET @ProcessedRecords = @ProcessedRecords + 
            (SELECT COUNT(*) FROM YourSourceTable WHERE ID BETWEEN @StartID AND @EndID AND YourConditions)
        
        -- 顯示進度
        PRINT '完成批次 ' + CAST(@BatchNumber AS VARCHAR) + 
              ', 已處理: ' + CAST(@ProcessedRecords AS VARCHAR) + 
              ' 筆資料 (' + CAST(ROUND((@ProcessedRecords * 100.0 / @TotalRecords), 2) AS VARCHAR) + '%)'
        
        -- 可選: 添加小延遲，避免過度負載
        WAITFOR DELAY '00:00:00.5'
    END TRY
    BEGIN CATCH
        SET @ErrorMessage = 
            '批次 ' + CAST(@BatchNumber AS VARCHAR) + ' 處理錯誤: ' + 
            ERROR_MESSAGE() + ' (錯誤號: ' + CAST(ERROR_NUMBER() AS VARCHAR) + ')'
        
        PRINT @ErrorMessage
        
        -- 可選: 記錄錯誤到表中
        -- INSERT INTO ErrorLog (ErrorTime, ErrorMessage, BatchNumber)
        -- VALUES (GETDATE(), @ErrorMessage, @BatchNumber)
        
        -- 可選: 是否要繼續處理下一批？
        -- 如果想在錯誤時中止，取消註釋下面行
        -- BREAK
    END CATCH
END

-- 記錄結束時間及總時間
SET @EndTime = GETDATE()
SET @TotalTime = DATEDIFF(SECOND, @StartTime, @EndTime)

PRINT '批量插入作業完成: ' + CONVERT(VARCHAR, @EndTime, 120)
PRINT '總處理時間: ' + 
      CAST(@TotalTime / 3600 AS VARCHAR) + ' 小時 ' + 
      CAST((@TotalTime % 3600) / 60 AS VARCHAR) + ' 分鐘 ' + 
      CAST((@TotalTime % 60) AS VARCHAR) + ' 秒'
PRINT '總處理記錄數: ' + CAST(@ProcessedRecords AS VARCHAR) + ' / ' + CAST(@TotalRecords AS VARCHAR)

-- 清理臨時表
DROP TABLE #BatchRanges
