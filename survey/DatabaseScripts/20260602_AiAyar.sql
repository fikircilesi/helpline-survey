IF OBJECT_ID(N'dbo.AiAyar', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.AiAyar
    (
        AiAyarId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_AiAyar PRIMARY KEY,
        Provider NVARCHAR(40) NOT NULL CONSTRAINT DF_AiAyar_Provider DEFAULT(N'OpenAI'),
        Endpoint NVARCHAR(300) NOT NULL CONSTRAINT DF_AiAyar_Endpoint DEFAULT(N'https://api.openai.com/v1'),
        ChatModel NVARCHAR(100) NOT NULL CONSTRAINT DF_AiAyar_ChatModel DEFAULT(N'gpt-4o-mini'),
        EmbeddingModel NVARCHAR(100) NOT NULL CONSTRAINT DF_AiAyar_EmbeddingModel DEFAULT(N'text-embedding-3-small'),
        ApiKey NVARCHAR(MAX) NULL,
        Aktif BIT NOT NULL CONSTRAINT DF_AiAyar_Aktif DEFAULT(1),
        OlusturmaTarihi DATETIME2(0) NOT NULL CONSTRAINT DF_AiAyar_OlusturmaTarihi DEFAULT(SYSDATETIME()),
        GuncellemeTarihi DATETIME2(0) NOT NULL CONSTRAINT DF_AiAyar_GuncellemeTarihi DEFAULT(SYSDATETIME())
    );
END;
GO

IF NOT EXISTS (SELECT 1 FROM dbo.AiAyar)
BEGIN
    INSERT INTO dbo.AiAyar (Provider, Endpoint, ChatModel, EmbeddingModel, ApiKey, Aktif)
    VALUES
    (
        N'OpenAI',
        N'https://api.openai.com/v1',
        N'gpt-4o-mini',
        N'text-embedding-3-small',
        N'BURAYA_OPENAI_API_KEY',
        1
    );
END;
GO
