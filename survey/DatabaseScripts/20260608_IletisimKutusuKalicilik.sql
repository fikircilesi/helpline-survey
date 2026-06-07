SET XACT_ABORT ON;
GO

BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.IletisimKonusma', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.IletisimKonusma
    (
        IletisimKonusmaId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_IletisimKonusma PRIMARY KEY,
        KonusmaKodu NVARCHAR(40) NOT NULL,
        AdSoyad NVARCHAR(120) NULL,
        Eposta NVARCHAR(160) NOT NULL,
        OlusturmaTarihi DATETIME NOT NULL CONSTRAINT DF_IletisimKonusma_OlusturmaTarihi DEFAULT (GETDATE()),
        SonHareketTarihi DATETIME NOT NULL CONSTRAINT DF_IletisimKonusma_SonHareketTarihi DEFAULT (GETDATE()),
        SonMailKontrolTarihi DATETIME NULL,
        SonMailKontrolOzeti NVARCHAR(500) NULL,
        Pasif BIT NOT NULL CONSTRAINT DF_IletisimKonusma_Pasif DEFAULT (0)
    );
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_IletisimKonusma_KonusmaKodu' AND object_id = OBJECT_ID(N'dbo.IletisimKonusma'))
    CREATE UNIQUE INDEX UX_IletisimKonusma_KonusmaKodu ON dbo.IletisimKonusma(KonusmaKodu);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_IletisimKonusma_SonHareketTarihi' AND object_id = OBJECT_ID(N'dbo.IletisimKonusma'))
    CREATE INDEX IX_IletisimKonusma_SonHareketTarihi ON dbo.IletisimKonusma(Pasif, SonHareketTarihi DESC);

IF OBJECT_ID(N'dbo.IletisimMesaj', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.IletisimMesaj
    (
        IletisimMesajId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_IletisimMesaj PRIMARY KEY,
        IletisimKonusmaId INT NOT NULL,
        MesajSira INT NOT NULL,
        Kimden NVARCHAR(20) NOT NULL,
        MesajMetni NVARCHAR(MAX) NOT NULL,
        KayitTarihi DATETIME NOT NULL CONSTRAINT DF_IletisimMesaj_KayitTarihi DEFAULT (GETDATE()),
        MailAnahtari NVARCHAR(300) NULL,
        CONSTRAINT FK_IletisimMesaj_IletisimKonusma FOREIGN KEY (IletisimKonusmaId) REFERENCES dbo.IletisimKonusma(IletisimKonusmaId)
    );
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_IletisimMesaj_KonusmaSira' AND object_id = OBJECT_ID(N'dbo.IletisimMesaj'))
    CREATE UNIQUE INDEX UX_IletisimMesaj_KonusmaSira ON dbo.IletisimMesaj(IletisimKonusmaId, MesajSira);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_IletisimMesaj_MailAnahtari' AND object_id = OBJECT_ID(N'dbo.IletisimMesaj'))
    CREATE INDEX IX_IletisimMesaj_MailAnahtari ON dbo.IletisimMesaj(IletisimKonusmaId, MailAnahtari);

COMMIT TRANSACTION;
GO
