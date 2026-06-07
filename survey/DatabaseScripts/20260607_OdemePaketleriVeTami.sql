USE [Survey];
GO

IF OBJECT_ID(N'dbo.OdemePaketi', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.OdemePaketi
    (
        OdemePaketiId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_OdemePaketi PRIMARY KEY,
        PaketKodu NVARCHAR(40) NOT NULL,
        PaketAdi NVARCHAR(120) NOT NULL,
        Aciklama NVARCHAR(500) NULL,
        Tutar DECIMAL(18,2) NOT NULL CONSTRAINT DF_OdemePaketi_Tutar DEFAULT (0),
        ParaBirimi NVARCHAR(10) NOT NULL CONSTRAINT DF_OdemePaketi_ParaBirimi DEFAULT (N'TRY'),
        SureGun INT NOT NULL CONSTRAINT DF_OdemePaketi_SureGun DEFAULT (30),
        KullaniciLimiti INT NOT NULL CONSTRAINT DF_OdemePaketi_KullaniciLimiti DEFAULT (2),
        AktifAnketLimiti INT NOT NULL CONSTRAINT DF_OdemePaketi_AktifAnketLimiti DEFAULT (3),
        AylikYanitLimiti INT NOT NULL CONSTRAINT DF_OdemePaketi_AylikYanitLimiti DEFAULT (250),
        MarkaIziGoster BIT NOT NULL CONSTRAINT DF_OdemePaketi_MarkaIziGoster DEFAULT (1),
        PdfRaporAktif BIT NOT NULL CONSTRAINT DF_OdemePaketi_PdfRaporAktif DEFAULT (1),
        GelismisRaporAktif BIT NOT NULL CONSTRAINT DF_OdemePaketi_GelismisRaporAktif DEFAULT (0),
        DisaAktarmaAktif BIT NOT NULL CONSTRAINT DF_OdemePaketi_DisaAktarmaAktif DEFAULT (0),
        YapayZekaOzetAktif BIT NOT NULL CONSTRAINT DF_OdemePaketi_YapayZekaOzetAktif DEFAULT (0),
        SiraNo INT NOT NULL CONSTRAINT DF_OdemePaketi_SiraNo DEFAULT (100),
        Pasif BIT NOT NULL CONSTRAINT DF_OdemePaketi_Pasif DEFAULT (0),
        KayitTarihi DATETIME NOT NULL CONSTRAINT DF_OdemePaketi_KayitTarihi DEFAULT (GETDATE())
    );
END;
GO

IF OBJECT_ID(N'dbo.AbonelikDurumu', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.AbonelikDurumu
    (
        AbonelikDurumuId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_AbonelikDurumu PRIMARY KEY,
        CalismaAlaniId INT NOT NULL,
        OdemePaketiId INT NOT NULL,
        AbonelikDurumu NVARCHAR(30) NOT NULL CONSTRAINT DF_AbonelikDurumu_Durum DEFAULT (N'Aktif'),
        BaslangicTarihi DATETIME NOT NULL CONSTRAINT DF_AbonelikDurumu_Baslangic DEFAULT (GETDATE()),
        BitisTarihi DATETIME NULL,
        SonOdemeIslemiId INT NULL,
        KayitTarihi DATETIME NOT NULL CONSTRAINT DF_AbonelikDurumu_Kayit DEFAULT (GETDATE()),
        GuncellemeTarihi DATETIME NOT NULL CONSTRAINT DF_AbonelikDurumu_Guncelleme DEFAULT (GETDATE()),
        CONSTRAINT FK_AbonelikDurumu_CalismaAlani FOREIGN KEY (CalismaAlaniId) REFERENCES dbo.CalismaAlani(CalismaAlaniId),
        CONSTRAINT FK_AbonelikDurumu_OdemePaketi FOREIGN KEY (OdemePaketiId) REFERENCES dbo.OdemePaketi(OdemePaketiId)
    );
END;
GO

IF OBJECT_ID(N'dbo.OdemeIslemi', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.OdemeIslemi
    (
        OdemeIslemiId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_OdemeIslemi PRIMARY KEY,
        CalismaAlaniId INT NOT NULL,
        PersonelId INT NOT NULL,
        OdemePaketiId INT NOT NULL,
        SiparisNo NVARCHAR(50) NOT NULL,
        Tutar DECIMAL(18,2) NOT NULL,
        ParaBirimi NVARCHAR(10) NOT NULL CONSTRAINT DF_OdemeIslemi_ParaBirimi DEFAULT (N'TRY'),
        OdemeDurumu NVARCHAR(30) NOT NULL CONSTRAINT DF_OdemeIslemi_Durum DEFAULT (N'Bekliyor'),
        TamiJeton NVARCHAR(500) NULL,
        TamiOdemeSayfasi NVARCHAR(1000) NULL,
        TamiHataKodu NVARCHAR(80) NULL,
        TamiHataMesaji NVARCHAR(1000) NULL,
        TamiOdemeDurumu NVARCHAR(80) NULL,
        TamiIslemDurumu NVARCHAR(80) NULL,
        TamiHamYanit NVARCHAR(MAX) NULL,
        KayitTarihi DATETIME NOT NULL CONSTRAINT DF_OdemeIslemi_Kayit DEFAULT (GETDATE()),
        TamamlanmaTarihi DATETIME NULL,
        CONSTRAINT FK_OdemeIslemi_CalismaAlani FOREIGN KEY (CalismaAlaniId) REFERENCES dbo.CalismaAlani(CalismaAlaniId),
        CONSTRAINT FK_OdemeIslemi_Personel FOREIGN KEY (PersonelId) REFERENCES dbo.Personel(PersonelId),
        CONSTRAINT FK_OdemeIslemi_OdemePaketi FOREIGN KEY (OdemePaketiId) REFERENCES dbo.OdemePaketi(OdemePaketiId)
    );
END;
GO

IF OBJECT_ID(N'dbo.UcretsizDenemeKaydi', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.UcretsizDenemeKaydi
    (
        UcretsizDenemeKaydiId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_UcretsizDenemeKaydi PRIMARY KEY,
        Eposta NVARCHAR(250) NOT NULL,
        EpostaAnahtari NVARCHAR(250) NOT NULL,
        PersonelId INT NOT NULL,
        CalismaAlaniId INT NOT NULL,
        KayitTarihi DATETIME NOT NULL CONSTRAINT DF_UcretsizDenemeKaydi_Kayit DEFAULT (GETDATE()),
        CONSTRAINT FK_UcretsizDenemeKaydi_Personel FOREIGN KEY (PersonelId) REFERENCES dbo.Personel(PersonelId),
        CONSTRAINT FK_UcretsizDenemeKaydi_CalismaAlani FOREIGN KEY (CalismaAlaniId) REFERENCES dbo.CalismaAlani(CalismaAlaniId)
    );
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_OdemePaketi_PaketKodu' AND object_id = OBJECT_ID(N'dbo.OdemePaketi'))
    CREATE UNIQUE INDEX UX_OdemePaketi_PaketKodu ON dbo.OdemePaketi(PaketKodu);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_AbonelikDurumu_CalismaAlaniId' AND object_id = OBJECT_ID(N'dbo.AbonelikDurumu'))
    CREATE UNIQUE INDEX UX_AbonelikDurumu_CalismaAlaniId ON dbo.AbonelikDurumu(CalismaAlaniId);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_OdemeIslemi_SiparisNo' AND object_id = OBJECT_ID(N'dbo.OdemeIslemi'))
    CREATE UNIQUE INDEX UX_OdemeIslemi_SiparisNo ON dbo.OdemeIslemi(SiparisNo);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_OdemeIslemi_CalismaAlaniId' AND object_id = OBJECT_ID(N'dbo.OdemeIslemi'))
    CREATE INDEX IX_OdemeIslemi_CalismaAlaniId ON dbo.OdemeIslemi(CalismaAlaniId, KayitTarihi DESC);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_UcretsizDenemeKaydi_EpostaAnahtari' AND object_id = OBJECT_ID(N'dbo.UcretsizDenemeKaydi'))
    CREATE UNIQUE INDEX UX_UcretsizDenemeKaydi_EpostaAnahtari ON dbo.UcretsizDenemeKaydi(EpostaAnahtari);
GO

MERGE dbo.OdemePaketi AS hedef
USING (VALUES
    (N'UCRETSIZ', N'Ücretsiz', N'Küçük işletmelerin temel anket ihtiyacını gerçekten çözer. Survey by Aslana Teknoloji marka izi görünür.', 0.00, N'TRY', 365, 2, 3, 250, 1, 1, 0, 0, 0, 10, 0),
    (N'BASLANGIC', N'Başlangıç', N'Düzenli anket kullanan küçük ekipler için daha fazla kapasite ve marka izi kaldırma.', 499.00, N'TRY', 30, 5, 10, 2500, 0, 1, 0, 1, 0, 20, 0),
    (N'PROFESYONEL', N'Profesyonel', N'Raporlama, dışa aktarma ve büyüyen ekip kullanımı için ana paket.', 1499.00, N'TRY', 30, 15, 40, 15000, 0, 1, 1, 1, 1, 30, 0),
    (N'KURUMSAL', N'Kurumsal', N'Yüksek hacimli kurumlar, özel destek ve yıllık kullanım için.', 14999.00, N'TRY', 365, 50, 250, 250000, 0, 1, 1, 1, 1, 40, 0)
) AS kaynak
(
    PaketKodu,
    PaketAdi,
    Aciklama,
    Tutar,
    ParaBirimi,
    SureGun,
    KullaniciLimiti,
    AktifAnketLimiti,
    AylikYanitLimiti,
    MarkaIziGoster,
    PdfRaporAktif,
    GelismisRaporAktif,
    DisaAktarmaAktif,
    YapayZekaOzetAktif,
    SiraNo,
    Pasif
)
ON hedef.PaketKodu = kaynak.PaketKodu
WHEN MATCHED THEN
    UPDATE SET PaketAdi = kaynak.PaketAdi,
               Aciklama = kaynak.Aciklama,
               Tutar = kaynak.Tutar,
               ParaBirimi = kaynak.ParaBirimi,
               SureGun = kaynak.SureGun,
               KullaniciLimiti = kaynak.KullaniciLimiti,
               AktifAnketLimiti = kaynak.AktifAnketLimiti,
               AylikYanitLimiti = kaynak.AylikYanitLimiti,
               MarkaIziGoster = kaynak.MarkaIziGoster,
               PdfRaporAktif = kaynak.PdfRaporAktif,
               GelismisRaporAktif = kaynak.GelismisRaporAktif,
               DisaAktarmaAktif = kaynak.DisaAktarmaAktif,
               YapayZekaOzetAktif = kaynak.YapayZekaOzetAktif,
               SiraNo = kaynak.SiraNo,
               Pasif = kaynak.Pasif
WHEN NOT MATCHED THEN
    INSERT
    (
        PaketKodu,
        PaketAdi,
        Aciklama,
        Tutar,
        ParaBirimi,
        SureGun,
        KullaniciLimiti,
        AktifAnketLimiti,
        AylikYanitLimiti,
        MarkaIziGoster,
        PdfRaporAktif,
        GelismisRaporAktif,
        DisaAktarmaAktif,
        YapayZekaOzetAktif,
        SiraNo,
        Pasif
    )
    VALUES
    (
        kaynak.PaketKodu,
        kaynak.PaketAdi,
        kaynak.Aciklama,
        kaynak.Tutar,
        kaynak.ParaBirimi,
        kaynak.SureGun,
        kaynak.KullaniciLimiti,
        kaynak.AktifAnketLimiti,
        kaynak.AylikYanitLimiti,
        kaynak.MarkaIziGoster,
        kaynak.PdfRaporAktif,
        kaynak.GelismisRaporAktif,
        kaynak.DisaAktarmaAktif,
        kaynak.YapayZekaOzetAktif,
        kaynak.SiraNo,
        kaynak.Pasif
    );
GO

DECLARE @UcretsizPaketId INT;
SELECT @UcretsizPaketId = OdemePaketiId
FROM dbo.OdemePaketi
WHERE PaketKodu = N'UCRETSIZ';

IF @UcretsizPaketId IS NOT NULL
BEGIN
    INSERT INTO dbo.AbonelikDurumu
        (CalismaAlaniId, OdemePaketiId, AbonelikDurumu, BaslangicTarihi, BitisTarihi, KayitTarihi, GuncellemeTarihi)
    SELECT ca.CalismaAlaniId,
           @UcretsizPaketId,
           N'Aktif',
           GETDATE(),
           DATEADD(DAY, 365, GETDATE()),
           GETDATE(),
           GETDATE()
    FROM dbo.CalismaAlani ca
    WHERE NOT EXISTS
    (
        SELECT 1
        FROM dbo.AbonelikDurumu ad
        WHERE ad.CalismaAlaniId = ca.CalismaAlaniId
    );

    ;WITH Sahipler AS
    (
        SELECT ca.CalismaAlaniId,
               p.PersonelId,
               p.Mail AS Eposta,
               LOWER(LTRIM(RTRIM(p.Mail))) AS EpostaAnahtari,
               ROW_NUMBER() OVER (PARTITION BY LOWER(LTRIM(RTRIM(p.Mail))) ORDER BY ca.CalismaAlaniId) AS SiraNo
        FROM dbo.CalismaAlani ca
        INNER JOIN dbo.Personel p ON p.PersonelId = ca.SahipPersonelId
        WHERE p.Mail IS NOT NULL
          AND LTRIM(RTRIM(p.Mail)) <> N''
    )
    INSERT INTO dbo.UcretsizDenemeKaydi
        (Eposta, EpostaAnahtari, PersonelId, CalismaAlaniId, KayitTarihi)
    SELECT Eposta,
           EpostaAnahtari,
           PersonelId,
           CalismaAlaniId,
           GETDATE()
    FROM Sahipler s
    WHERE s.SiraNo = 1
      AND NOT EXISTS
      (
          SELECT 1
          FROM dbo.UcretsizDenemeKaydi u
          WHERE u.EpostaAnahtari = s.EpostaAnahtari
      );
END;
GO
