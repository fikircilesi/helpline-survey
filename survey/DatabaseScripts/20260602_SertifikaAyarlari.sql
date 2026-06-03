USE [Survey];
GO

IF COL_LENGTH('dbo.Anket', 'SertifikaAktif') IS NULL
    ALTER TABLE dbo.Anket ADD SertifikaAktif BIT NULL;
GO

IF COL_LENGTH('dbo.Anket', 'SertifikaKatilimciErisimi') IS NULL
    ALTER TABLE dbo.Anket ADD SertifikaKatilimciErisimi BIT NOT NULL CONSTRAINT DF_Anket_SertifikaKatilimciErisimi DEFAULT (1);
GO

IF COL_LENGTH('dbo.Anket', 'SertifikaVerilisZamani') IS NULL
    ALTER TABLE dbo.Anket ADD SertifikaVerilisZamani NVARCHAR(30) NOT NULL CONSTRAINT DF_Anket_SertifikaVerilisZamani DEFAULT (N'SureBitince');
GO

IF COL_LENGTH('dbo.Anket', 'SertifikaBaslik') IS NULL
    ALTER TABLE dbo.Anket ADD SertifikaBaslik NVARCHAR(150) NULL;
GO

IF COL_LENGTH('dbo.Anket', 'SertifikaMetni') IS NULL
    ALTER TABLE dbo.Anket ADD SertifikaMetni NVARCHAR(600) NULL;
GO

EXEC(N'
UPDATE dbo.Anket
SET SertifikaAktif = ISNULL(SertifikaAktif, ISNULL(Sonuc, 0)),
    SertifikaVerilisZamani = ISNULL(NULLIF(SertifikaVerilisZamani, N''''), N''SureBitince''),
    SertifikaBaslik = ISNULL(NULLIF(SertifikaBaslik, N''''), N''Katılım Sertifikası'');
');
GO

UPDATE dbo.Anket
SET SertifikaBaslik = N'Katılım Sertifikası'
WHERE SertifikaBaslik = N'KatÄ±lÄ±m SertifikasÄ±';
GO
