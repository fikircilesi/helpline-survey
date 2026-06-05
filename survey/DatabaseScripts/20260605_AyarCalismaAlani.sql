USE [Survey];
GO

DECLARE @VarsayilanCalismaAlaniId INT;

SELECT TOP 1 @VarsayilanCalismaAlaniId = CalismaAlaniId
FROM dbo.CalismaAlani
WHERE ISNULL(Pasif, 0) = 0
ORDER BY CalismaAlaniId;

IF COL_LENGTH('dbo.Unvan', 'CalismaAlaniId') IS NULL
    ALTER TABLE dbo.Unvan ADD CalismaAlaniId INT NULL;

IF COL_LENGTH('dbo.Departman', 'CalismaAlaniId') IS NULL
    ALTER TABLE dbo.Departman ADD CalismaAlaniId INT NULL;

IF COL_LENGTH('dbo.Bolge', 'CalismaAlaniId') IS NULL
    ALTER TABLE dbo.Bolge ADD CalismaAlaniId INT NULL;

IF COL_LENGTH('dbo.Sehir', 'CalismaAlaniId') IS NULL
    ALTER TABLE dbo.Sehir ADD CalismaAlaniId INT NULL;

IF COL_LENGTH('dbo.Sube', 'CalismaAlaniId') IS NULL
    ALTER TABLE dbo.Sube ADD CalismaAlaniId INT NULL;

IF COL_LENGTH('dbo.Bolum', 'CalismaAlaniId') IS NULL
    ALTER TABLE dbo.Bolum ADD CalismaAlaniId INT NULL;

IF COL_LENGTH('dbo.Yonetici', 'CalismaAlaniId') IS NULL
    ALTER TABLE dbo.Yonetici ADD CalismaAlaniId INT NULL;

IF COL_LENGTH('dbo.User', 'CalismaAlaniId') IS NULL
    ALTER TABLE dbo.[User] ADD CalismaAlaniId INT NULL;

IF COL_LENGTH('dbo.smtpayar', 'CalismaAlaniId') IS NULL
    ALTER TABLE dbo.smtpayar ADD CalismaAlaniId INT NULL;

IF OBJECT_ID(N'dbo.AiAyar', N'U') IS NOT NULL AND COL_LENGTH('dbo.AiAyar', 'CalismaAlaniId') IS NULL
    ALTER TABLE dbo.AiAyar ADD CalismaAlaniId INT NULL;

IF COL_LENGTH('dbo.SoruGrup', 'CalismaAlaniId') IS NULL
    ALTER TABLE dbo.SoruGrup ADD CalismaAlaniId INT NULL;

IF COL_LENGTH('dbo.Soru', 'CalismaAlaniId') IS NULL
    ALTER TABLE dbo.Soru ADD CalismaAlaniId INT NULL;

IF COL_LENGTH('dbo.CevapGrup', 'CalismaAlaniId') IS NULL
    ALTER TABLE dbo.CevapGrup ADD CalismaAlaniId INT NULL;

IF COL_LENGTH('dbo.Cevap', 'CalismaAlaniId') IS NULL
    ALTER TABLE dbo.Cevap ADD CalismaAlaniId INT NULL;

IF @VarsayilanCalismaAlaniId IS NOT NULL
BEGIN
    EXEC sp_executesql N'UPDATE dbo.Unvan SET CalismaAlaniId = @id WHERE CalismaAlaniId IS NULL', N'@id int', @VarsayilanCalismaAlaniId;
    EXEC sp_executesql N'UPDATE dbo.Departman SET CalismaAlaniId = @id WHERE CalismaAlaniId IS NULL', N'@id int', @VarsayilanCalismaAlaniId;
    EXEC sp_executesql N'UPDATE dbo.Bolge SET CalismaAlaniId = @id WHERE CalismaAlaniId IS NULL', N'@id int', @VarsayilanCalismaAlaniId;
    EXEC sp_executesql N'UPDATE dbo.Sehir SET CalismaAlaniId = @id WHERE CalismaAlaniId IS NULL', N'@id int', @VarsayilanCalismaAlaniId;
    EXEC sp_executesql N'UPDATE dbo.Sube SET CalismaAlaniId = @id WHERE CalismaAlaniId IS NULL', N'@id int', @VarsayilanCalismaAlaniId;
    EXEC sp_executesql N'UPDATE dbo.Bolum SET CalismaAlaniId = @id WHERE CalismaAlaniId IS NULL', N'@id int', @VarsayilanCalismaAlaniId;
    EXEC sp_executesql N'UPDATE dbo.Yonetici SET CalismaAlaniId = @id WHERE CalismaAlaniId IS NULL', N'@id int', @VarsayilanCalismaAlaniId;
    EXEC sp_executesql N'UPDATE dbo.[User] SET CalismaAlaniId = @id WHERE CalismaAlaniId IS NULL', N'@id int', @VarsayilanCalismaAlaniId;
    EXEC sp_executesql N'UPDATE dbo.smtpayar SET CalismaAlaniId = @id WHERE CalismaAlaniId IS NULL', N'@id int', @VarsayilanCalismaAlaniId;

    IF OBJECT_ID(N'dbo.AiAyar', N'U') IS NOT NULL
        EXEC sp_executesql N'UPDATE dbo.AiAyar SET CalismaAlaniId = @id WHERE CalismaAlaniId IS NULL', N'@id int', @VarsayilanCalismaAlaniId;

    UPDATE sg
    SET CalismaAlaniId = kaynak.CalismaAlaniId
    FROM dbo.SoruGrup sg
    INNER JOIN (
        SELECT ag.SoruGrupId, MIN(a.CalismaAlaniId) AS CalismaAlaniId
        FROM dbo.AnketGrup ag
        INNER JOIN dbo.Anket a ON a.AnketId = ag.AnketId
        WHERE a.CalismaAlaniId IS NOT NULL
        GROUP BY ag.SoruGrupId
    ) kaynak ON kaynak.SoruGrupId = sg.SoruGrupId
    WHERE sg.CalismaAlaniId IS NULL;

    UPDATE s
    SET CalismaAlaniId = sg.CalismaAlaniId
    FROM dbo.Soru s
    INNER JOIN dbo.SoruGrup sg ON sg.SoruGrupId = s.SoruGrupId
    WHERE s.CalismaAlaniId IS NULL
      AND sg.CalismaAlaniId IS NOT NULL;

    UPDATE cg
    SET CalismaAlaniId = kaynak.CalismaAlaniId
    FROM dbo.CevapGrup cg
    INNER JOIN (
        SELECT s.CevapGrupId, MIN(s.CalismaAlaniId) AS CalismaAlaniId
        FROM dbo.Soru s
        WHERE s.CevapGrupId IS NOT NULL
          AND s.CalismaAlaniId IS NOT NULL
        GROUP BY s.CevapGrupId
    ) kaynak ON kaynak.CevapGrupId = cg.CevapGrupId
    WHERE cg.CalismaAlaniId IS NULL;

    UPDATE c
    SET CalismaAlaniId = cg.CalismaAlaniId
    FROM dbo.Cevap c
    INNER JOIN dbo.CevapGrup cg ON cg.CevapGrupId = c.CevapGrupId
    WHERE c.CalismaAlaniId IS NULL
      AND cg.CalismaAlaniId IS NOT NULL;

    EXEC sp_executesql N'UPDATE dbo.SoruGrup SET CalismaAlaniId = @id WHERE CalismaAlaniId IS NULL', N'@id int', @VarsayilanCalismaAlaniId;
    EXEC sp_executesql N'UPDATE dbo.Soru SET CalismaAlaniId = @id WHERE CalismaAlaniId IS NULL', N'@id int', @VarsayilanCalismaAlaniId;
    EXEC sp_executesql N'UPDATE dbo.CevapGrup SET CalismaAlaniId = @id WHERE CalismaAlaniId IS NULL', N'@id int', @VarsayilanCalismaAlaniId;
    EXEC sp_executesql N'UPDATE dbo.Cevap SET CalismaAlaniId = @id WHERE CalismaAlaniId IS NULL', N'@id int', @VarsayilanCalismaAlaniId;
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Unvan_CalismaAlaniId' AND object_id = OBJECT_ID(N'dbo.Unvan'))
    CREATE INDEX IX_Unvan_CalismaAlaniId ON dbo.Unvan(CalismaAlaniId);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Departman_CalismaAlaniId' AND object_id = OBJECT_ID(N'dbo.Departman'))
    CREATE INDEX IX_Departman_CalismaAlaniId ON dbo.Departman(CalismaAlaniId);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Bolge_CalismaAlaniId' AND object_id = OBJECT_ID(N'dbo.Bolge'))
    CREATE INDEX IX_Bolge_CalismaAlaniId ON dbo.Bolge(CalismaAlaniId);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Sehir_CalismaAlaniId' AND object_id = OBJECT_ID(N'dbo.Sehir'))
    CREATE INDEX IX_Sehir_CalismaAlaniId ON dbo.Sehir(CalismaAlaniId);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Sube_CalismaAlaniId' AND object_id = OBJECT_ID(N'dbo.Sube'))
    CREATE INDEX IX_Sube_CalismaAlaniId ON dbo.Sube(CalismaAlaniId);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Bolum_CalismaAlaniId' AND object_id = OBJECT_ID(N'dbo.Bolum'))
    CREATE INDEX IX_Bolum_CalismaAlaniId ON dbo.Bolum(CalismaAlaniId);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Yonetici_CalismaAlaniId' AND object_id = OBJECT_ID(N'dbo.Yonetici'))
    CREATE INDEX IX_Yonetici_CalismaAlaniId ON dbo.Yonetici(CalismaAlaniId);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_User_CalismaAlaniId' AND object_id = OBJECT_ID(N'dbo.[User]'))
    CREATE INDEX IX_User_CalismaAlaniId ON dbo.[User](CalismaAlaniId);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_smtpayar_CalismaAlaniId' AND object_id = OBJECT_ID(N'dbo.smtpayar'))
    CREATE INDEX IX_smtpayar_CalismaAlaniId ON dbo.smtpayar(CalismaAlaniId);

IF OBJECT_ID(N'dbo.AiAyar', N'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_AiAyar_CalismaAlaniId' AND object_id = OBJECT_ID(N'dbo.AiAyar'))
    CREATE INDEX IX_AiAyar_CalismaAlaniId ON dbo.AiAyar(CalismaAlaniId);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_SoruGrup_CalismaAlaniId' AND object_id = OBJECT_ID(N'dbo.SoruGrup'))
    CREATE INDEX IX_SoruGrup_CalismaAlaniId ON dbo.SoruGrup(CalismaAlaniId);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Soru_CalismaAlaniId' AND object_id = OBJECT_ID(N'dbo.Soru'))
    CREATE INDEX IX_Soru_CalismaAlaniId ON dbo.Soru(CalismaAlaniId);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_CevapGrup_CalismaAlaniId' AND object_id = OBJECT_ID(N'dbo.CevapGrup'))
    CREATE INDEX IX_CevapGrup_CalismaAlaniId ON dbo.CevapGrup(CalismaAlaniId);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Cevap_CalismaAlaniId' AND object_id = OBJECT_ID(N'dbo.Cevap'))
    CREATE INDEX IX_Cevap_CalismaAlaniId ON dbo.Cevap(CalismaAlaniId);
GO
