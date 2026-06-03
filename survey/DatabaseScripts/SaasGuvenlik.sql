USE [Survey];
GO

IF OBJECT_ID('dbo.CalismaAlani', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.CalismaAlani
    (
        CalismaAlaniId INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        CalismaAlaniAdi NVARCHAR(150) NOT NULL,
        FirmaAdi NVARCHAR(150) NULL,
        SahipPersonelId INT NOT NULL,
        KrediBakiyesi INT NOT NULL CONSTRAINT DF_CalismaAlani_KrediBakiyesi DEFAULT (0),
        Pasif BIT NOT NULL CONSTRAINT DF_CalismaAlani_Pasif DEFAULT (0),
        KayitTarihi DATETIME NOT NULL CONSTRAINT DF_CalismaAlani_KayitTarihi DEFAULT (GETDATE()),
        CONSTRAINT FK_CalismaAlani_Personel FOREIGN KEY (SahipPersonelId) REFERENCES dbo.Personel(PersonelId)
    );
END;
GO

IF OBJECT_ID('dbo.CalismaAlaniUye', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.CalismaAlaniUye
    (
        CalismaAlaniUyeId INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        CalismaAlaniId INT NOT NULL,
        PersonelId INT NOT NULL,
        Rol NVARCHAR(30) NOT NULL CONSTRAINT DF_CalismaAlaniUye_Rol DEFAULT (N'Sahip'),
        Pasif BIT NOT NULL CONSTRAINT DF_CalismaAlaniUye_Pasif DEFAULT (0),
        KayitTarihi DATETIME NOT NULL CONSTRAINT DF_CalismaAlaniUye_KayitTarihi DEFAULT (GETDATE()),
        CONSTRAINT FK_CalismaAlaniUye_CalismaAlani FOREIGN KEY (CalismaAlaniId) REFERENCES dbo.CalismaAlani(CalismaAlaniId),
        CONSTRAINT FK_CalismaAlaniUye_Personel FOREIGN KEY (PersonelId) REFERENCES dbo.Personel(PersonelId),
        CONSTRAINT UQ_CalismaAlaniUye UNIQUE (CalismaAlaniId, PersonelId)
    );
END;
GO

IF COL_LENGTH('dbo.Personel', 'MailOnaylandi') IS NULL
    ALTER TABLE dbo.Personel ADD MailOnaylandi BIT NOT NULL CONSTRAINT DF_Personel_MailOnaylandi DEFAULT (1);
GO

IF COL_LENGTH('dbo.Personel', 'MailOnayKodu') IS NULL
    ALTER TABLE dbo.Personel ADD MailOnayKodu NVARCHAR(10) NULL;
GO

IF COL_LENGTH('dbo.Personel', 'MailOnayKoduTarihi') IS NULL
    ALTER TABLE dbo.Personel ADD MailOnayKoduTarihi DATETIME NULL;
GO

IF COL_LENGTH('dbo.Personel', 'GoogleKimlikId') IS NULL
    ALTER TABLE dbo.Personel ADD GoogleKimlikId NVARCHAR(100) NULL;
GO

IF COL_LENGTH('dbo.Personel', 'GirisKaynagi') IS NULL
    ALTER TABLE dbo.Personel ADD GirisKaynagi NVARCHAR(30) NULL;
GO

IF COL_LENGTH('dbo.Personel', 'SonGirisTarihi') IS NULL
    ALTER TABLE dbo.Personel ADD SonGirisTarihi DATETIME NULL;
GO

IF COL_LENGTH('dbo.Anket', 'CalismaAlaniId') IS NULL
    ALTER TABLE dbo.Anket ADD CalismaAlaniId INT NULL;
GO

IF COL_LENGTH('dbo.Anket', 'SahipPersonelId') IS NULL
    ALTER TABLE dbo.Anket ADD SahipPersonelId INT NULL;
GO

IF COL_LENGTH('dbo.Anket', 'YayinDurumu') IS NULL
    ALTER TABLE dbo.Anket ADD YayinDurumu NVARCHAR(30) NOT NULL CONSTRAINT DF_Anket_YayinDurumu DEFAULT (N'Taslak');
GO

IF COL_LENGTH('dbo.Anket', 'OlusturmaTarihi') IS NULL
    ALTER TABLE dbo.Anket ADD OlusturmaTarihi DATETIME NULL;
GO

IF COL_LENGTH('dbo.Anket', 'YayinTarihi') IS NULL
    ALTER TABLE dbo.Anket ADD YayinTarihi DATETIME NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Anket_CalismaAlaniId' AND object_id = OBJECT_ID('dbo.Anket'))
    CREATE INDEX IX_Anket_CalismaAlaniId ON dbo.Anket(CalismaAlaniId);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Personel_MailOnay' AND object_id = OBJECT_ID('dbo.Personel'))
    CREATE INDEX IX_Personel_MailOnay ON dbo.Personel(Mail, MailOnayKodu);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_Personel_GoogleKimlikId' AND object_id = OBJECT_ID('dbo.Personel'))
    CREATE UNIQUE INDEX UX_Personel_GoogleKimlikId ON dbo.Personel(GoogleKimlikId) WHERE GoogleKimlikId IS NOT NULL;
GO

DECLARE @SahipPersonelId INT;
SELECT @SahipPersonelId = PersonelId FROM dbo.Personel WHERE KullaniciAdi = N'fikircilesi';

IF @SahipPersonelId IS NOT NULL
BEGIN
    IF NOT EXISTS (SELECT 1 FROM dbo.CalismaAlani WHERE SahipPersonelId = @SahipPersonelId)
    BEGIN
        INSERT INTO dbo.CalismaAlani (CalismaAlaniAdi, FirmaAdi, SahipPersonelId, KrediBakiyesi, Pasif, KayitTarihi)
        VALUES (N'Fikircilesi Çalışma Alanı', N'İzgi Yazılım', @SahipPersonelId, 100, 0, GETDATE());
    END;

    DECLARE @CalismaAlaniId INT;
    SELECT TOP 1 @CalismaAlaniId = CalismaAlaniId
    FROM dbo.CalismaAlani
    WHERE SahipPersonelId = @SahipPersonelId
    ORDER BY CalismaAlaniId;

    IF NOT EXISTS (SELECT 1 FROM dbo.CalismaAlaniUye WHERE CalismaAlaniId = @CalismaAlaniId AND PersonelId = @SahipPersonelId)
    BEGIN
        INSERT INTO dbo.CalismaAlaniUye (CalismaAlaniId, PersonelId, Rol, Pasif, KayitTarihi)
        VALUES (@CalismaAlaniId, @SahipPersonelId, N'Sahip', 0, GETDATE());
    END;

    UPDATE dbo.Anket
    SET CalismaAlaniId = @CalismaAlaniId,
        SahipPersonelId = @SahipPersonelId,
        YayinDurumu = CASE WHEN Pasif = 1 THEN N'Taslak' ELSE N'Yayinda' END,
        OlusturmaTarihi = ISNULL(OlusturmaTarihi, GETDATE())
    WHERE CalismaAlaniId IS NULL;
END;
GO
