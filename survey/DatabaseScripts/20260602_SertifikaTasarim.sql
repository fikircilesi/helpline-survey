USE [Survey];
GO

IF COL_LENGTH('dbo.Anket', 'SertifikaTema') IS NULL
BEGIN
    ALTER TABLE dbo.Anket
        ADD SertifikaTema NVARCHAR(30) NOT NULL
            CONSTRAINT DF_Anket_SertifikaTema DEFAULT (N'Modern');
END
GO

IF COL_LENGTH('dbo.Anket', 'SertifikaLogo') IS NULL
BEGIN
    ALTER TABLE dbo.Anket
        ADD SertifikaLogo NVARCHAR(300) NULL;
END
GO

IF COL_LENGTH('dbo.Anket', 'SertifikaVurguRengi') IS NULL
BEGIN
    ALTER TABLE dbo.Anket
        ADD SertifikaVurguRengi NVARCHAR(20) NOT NULL
            CONSTRAINT DF_Anket_SertifikaVurguRengi DEFAULT (N'#2563eb');
END
GO

IF COL_LENGTH('dbo.Anket', 'SertifikaCerceve') IS NULL
BEGIN
    ALTER TABLE dbo.Anket
        ADD SertifikaCerceve NVARCHAR(40) NOT NULL
            CONSTRAINT DF_Anket_SertifikaCerceve DEFAULT (N'Classic');
END
GO

IF COL_LENGTH('dbo.Anket', 'SertifikaFont') IS NULL
BEGIN
    ALTER TABLE dbo.Anket
        ADD SertifikaFont NVARCHAR(40) NOT NULL
            CONSTRAINT DF_Anket_SertifikaFont DEFAULT (N'Georgia');
END
GO

IF COL_LENGTH('dbo.Anket', 'SertifikaYaziPunto') IS NULL
BEGIN
    ALTER TABLE dbo.Anket
        ADD SertifikaYaziPunto INT NOT NULL
            CONSTRAINT DF_Anket_SertifikaYaziPunto DEFAULT (17);
END
GO

IF COL_LENGTH('dbo.Anket', 'SertifikaBaslikPunto') IS NULL
BEGIN
    ALTER TABLE dbo.Anket
        ADD SertifikaBaslikPunto INT NOT NULL
            CONSTRAINT DF_Anket_SertifikaBaslikPunto DEFAULT (44);
END
GO

UPDATE dbo.Anket
SET SertifikaTema = ISNULL(NULLIF(SertifikaTema, N''), N'Modern'),
    SertifikaVurguRengi = ISNULL(NULLIF(SertifikaVurguRengi, N''), N'#2563eb'),
    SertifikaCerceve = ISNULL(NULLIF(SertifikaCerceve, N''), N'Classic'),
    SertifikaFont = ISNULL(NULLIF(SertifikaFont, N''), N'Georgia'),
    SertifikaYaziPunto = CASE WHEN SertifikaYaziPunto BETWEEN 11 AND 28 THEN SertifikaYaziPunto ELSE 17 END,
    SertifikaBaslikPunto = CASE WHEN SertifikaBaslikPunto BETWEEN 24 AND 72 THEN SertifikaBaslikPunto ELSE 44 END;
GO
