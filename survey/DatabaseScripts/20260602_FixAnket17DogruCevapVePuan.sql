BEGIN TRANSACTION;

DECLARE @AnketId INT = 17;

DECLARE @DogruCevaplar TABLE (CevapId INT PRIMARY KEY);
INSERT INTO @DogruCevaplar (CevapId)
VALUES (234), (237), (243);

UPDATE c
SET
    c.Dogru = CASE WHEN d.CevapId IS NULL THEN 0 ELSE 1 END,
    c.CevapPuan = CASE WHEN d.CevapId IS NULL THEN 0 ELSE ISNULL(s.SoruPuan, 0) END
FROM dbo.AnketGrup ag
JOIN dbo.Soru s ON s.SoruGrupId = ag.SoruGrupId
JOIN dbo.Cevap c ON c.CevapGrupId = s.CevapGrupId
LEFT JOIN @DogruCevaplar d ON d.CevapId = c.CevapId
WHERE ag.AnketId = @AnketId;

UPDATE h
SET
    h.SoruPuan = ISNULL(s.SoruPuan, h.SoruPuan),
    h.CevapPuan = CASE WHEN c.Dogru = 1 THEN ISNULL(s.SoruPuan, h.SoruPuan) ELSE 0 END
FROM dbo.Havuz h
JOIN dbo.Soru s ON s.SoruId = h.SoruID
JOIN dbo.Cevap c ON c.CevapId = h.CevapId
WHERE h.AnketId = @AnketId;

COMMIT TRANSACTION;
