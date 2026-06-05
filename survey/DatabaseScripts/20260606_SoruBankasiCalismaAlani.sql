USE [Survey];
GO

IF COL_LENGTH('dbo.SoruGrup', 'CalismaAlaniId') IS NULL
    ALTER TABLE dbo.SoruGrup ADD CalismaAlaniId INT NULL;
GO

IF COL_LENGTH('dbo.Soru', 'CalismaAlaniId') IS NULL
    ALTER TABLE dbo.Soru ADD CalismaAlaniId INT NULL;
GO

IF COL_LENGTH('dbo.CevapGrup', 'CalismaAlaniId') IS NULL
    ALTER TABLE dbo.CevapGrup ADD CalismaAlaniId INT NULL;
GO

IF COL_LENGTH('dbo.Cevap', 'CalismaAlaniId') IS NULL
    ALTER TABLE dbo.Cevap ADD CalismaAlaniId INT NULL;
GO

DECLARE @VarsayilanCalismaAlaniId INT;

SELECT TOP 1 @VarsayilanCalismaAlaniId = CalismaAlaniId
FROM dbo.CalismaAlani
WHERE ISNULL(Pasif, 0) = 0
ORDER BY CalismaAlaniId;

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

IF @VarsayilanCalismaAlaniId IS NOT NULL
BEGIN
    UPDATE dbo.SoruGrup SET CalismaAlaniId = @VarsayilanCalismaAlaniId WHERE CalismaAlaniId IS NULL;
    UPDATE dbo.Soru SET CalismaAlaniId = @VarsayilanCalismaAlaniId WHERE CalismaAlaniId IS NULL;
    UPDATE dbo.CevapGrup SET CalismaAlaniId = @VarsayilanCalismaAlaniId WHERE CalismaAlaniId IS NULL;
    UPDATE dbo.Cevap SET CalismaAlaniId = @VarsayilanCalismaAlaniId WHERE CalismaAlaniId IS NULL;
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_SoruGrup_CalismaAlaniId' AND object_id = OBJECT_ID(N'dbo.SoruGrup'))
    CREATE INDEX IX_SoruGrup_CalismaAlaniId ON dbo.SoruGrup(CalismaAlaniId);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Soru_CalismaAlaniId' AND object_id = OBJECT_ID(N'dbo.Soru'))
    CREATE INDEX IX_Soru_CalismaAlaniId ON dbo.Soru(CalismaAlaniId);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_CevapGrup_CalismaAlaniId' AND object_id = OBJECT_ID(N'dbo.CevapGrup'))
    CREATE INDEX IX_CevapGrup_CalismaAlaniId ON dbo.CevapGrup(CalismaAlaniId);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Cevap_CalismaAlaniId' AND object_id = OBJECT_ID(N'dbo.Cevap'))
    CREATE INDEX IX_Cevap_CalismaAlaniId ON dbo.Cevap(CalismaAlaniId);
GO
