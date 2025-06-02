CREATE TABLE dbo.tPlayerPoints (
    -- 主鍵
    PlayerId INT NOT NULL PRIMARY KEY,
    TranId BIGINT NOT NULL,
    TranCodeId INT NOT NULL,
    
    -- 積分相關欄位
    GamePts INT NOT NULL,
    BasePts INT NOT NULL,
    BonusPts INT NOT NULL,
    AdjPtsCr INT NOT NULL,
    AdjPtsDr INT NOT NULL,
    RedeemPts INT NOT NULL,
    ExpirePts INT NOT NULL,
    PartialPts MONEY NOT NULL,
    PartialPts2 MONEY NOT NULL,
    PtsBal INT NOT NULL,
    OverPts INT NOT NULL,
    
    -- 審計欄位
    CreatedDtm DATETIMEOFFSET(2) NOT NULL,
    CreatedBy INT NOT NULL,
    ModifiedDtm DATETIMEOFFSET(2) NOT NULL,
    ModifiedBy INT NOT NULL,
    QualPts INT NOT NULL,
    
    -- 其他鍵和分組欄位
    BucketGroupId INT NOT NULL,
    GamingDt DATE NOT NULL,
    
    -- 其他欄位
    DataRowVersion INT NULL,
    ExpiryDate DATE NULL,
    PartialPt1Overflow BIT NOT NULL,
    PartialPt2Overflow BIT NOT NULL,

    -- 外鍵關係(從圖片中無法確定完整的外鍵關係，您可能需要根據實際情況添加)
    CONSTRAINT FK_tPlayerPoints_TranId FOREIGN KEY (TranId) REFERENCES [相關表](TranId),
    CONSTRAINT FK_tPlayerPoints_TranCodeId FOREIGN KEY (TranCodeId) REFERENCES [相關表](TranCodeId),
    CONSTRAINT FK_tPlayerPoints_BucketGroupId FOREIGN KEY (BucketGroupId) REFERENCES [相關表](BucketGroupId)
);