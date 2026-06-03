IF COL_LENGTH('dbo.Anket', 'KatilimYontemi') IS NULL
BEGIN
    ALTER TABLE dbo.Anket
        ADD KatilimYontemi NVARCHAR(30) NOT NULL
            CONSTRAINT DF_Anket_KatilimYontemi DEFAULT (N'HerkeseAcik');
END;

UPDATE dbo.Anket
SET KatilimYontemi = N'HerkeseAcik'
WHERE KatilimYontemi IS NULL
   OR LTRIM(RTRIM(KatilimYontemi)) = N''
   OR KatilimYontemi NOT IN (N'HerkeseAcik', N'Kayitli', N'BilgiFormu', N'KisiyeOzel');
