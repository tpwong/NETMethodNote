CREATE TABLE dbo.tPlayerCard (
    -- 主鍵
    PlayerId INT NOT NULL PRIMARY KEY,
    
    -- 會員卡相關資訊
    Acct NVARCHAR(50) NOT NULL,
    LastLocnId INT NULL,
    IsCardIn BIT NOT NULL,
    CardId INT NULL,
    CardCount INT NULL,
    IsInactive BIT NOT NULL,
    
    -- 磁條資料
    Track1Data NVARCHAR(255) NULL,
    Track2Data NVARCHAR(255) NULL,
    Track3Data NVARCHAR(255) NULL,
    
    -- 審計欄位
    CreatedBy INT NOT NULL,
    CreatedCasinoId INT NOT NULL,
    CreatedComputerName NVARCHAR(15) NOT NULL,
    ModifiedBy INT NOT NULL,
    ModifiedCasinoId INT NULL,
    ModifiedComputerName NVARCHAR(24) NULL,
    CreatedDtm DATETIMEOFFSET(2) NOT NULL,
    ModifiedDtm DATETIMEOFFSET(2) NOT NULL,
    DataRowVersion INT NULL,
    
    -- 計算欄位與額外資訊
    Acct_int AS CAST(Acct AS BIGINT) PERSISTED, -- 計算欄位
    PlayStatus VARCHAR(100) NULL,
    LastGamingLocn VARCHAR(100) NULL,
    CMSAccountId NVARCHAR(50) NULL,
    CMSID NVARCHAR(100) NULL,
    CardSequenceNum INT NULL
);