USE [Survey];
GO

IF COL_LENGTH('dbo.Anket', 'KatilimToken') IS NULL
    ALTER TABLE dbo.Anket ADD KatilimToken NVARCHAR(96) NULL;
GO

IF COL_LENGTH('dbo.Anket', 'KatilimTokenTarihi') IS NULL
    ALTER TABLE dbo.Anket ADD KatilimTokenTarihi DATETIME2(0) NULL;
GO

IF COL_LENGTH('dbo.Anket', 'YayinBaslangicTarihi') IS NULL
    ALTER TABLE dbo.Anket ADD YayinBaslangicTarihi DATETIME2(0) NULL;
GO

IF COL_LENGTH('dbo.Anket', 'YayinBitisTarihi') IS NULL
    ALTER TABLE dbo.Anket ADD YayinBitisTarihi DATETIME2(0) NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_Anket_KatilimToken' AND object_id = OBJECT_ID('dbo.Anket'))
    CREATE UNIQUE INDEX UX_Anket_KatilimToken ON dbo.Anket(KatilimToken) WHERE KatilimToken IS NOT NULL;
GO

IF COL_LENGTH('dbo.Anket', 'YayinTarihi') IS NOT NULL
BEGIN
    EXEC(N'
        UPDATE dbo.Anket
        SET YayinBaslangicTarihi = ISNULL(YayinBaslangicTarihi, YayinTarihi)
        WHERE YayinBaslangicTarihi IS NULL;
    ');
END;
GO
