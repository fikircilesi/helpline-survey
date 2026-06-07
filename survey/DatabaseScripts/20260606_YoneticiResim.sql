IF OBJECT_ID(N'dbo.Yonetici_Yedek_20260606', N'U') IS NULL
BEGIN
    SELECT *
    INTO dbo.Yonetici_Yedek_20260606
    FROM dbo.Yonetici;
END;

IF COL_LENGTH(N'dbo.Yonetici', N'YoneticiResim') IS NULL
BEGIN
    ALTER TABLE dbo.Yonetici
        ADD YoneticiResim nvarchar(50) NULL;
END;
