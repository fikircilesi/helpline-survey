using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using survey.Models;
using System;
using System.Data.Entity;
using System.Threading;
using System.Threading.Tasks;

namespace survey.Infrastructure
{
    public class RaporIndexBakimService : BackgroundService
    {
        private static readonly TimeSpan IlkKontrolGecikmesi = TimeSpan.FromSeconds(15);
        private static readonly TimeSpan HataSonrasiBekleme = TimeSpan.FromHours(1);
        private readonly ILogger<RaporIndexBakimService> _logger;

        public RaporIndexBakimService(ILogger<RaporIndexBakimService> logger)
        {
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                await Task.Delay(IlkKontrolGecikmesi, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }

            var ilkCalisma = true;
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    KritikIndexleriTamamla();
                    if (ilkCalisma)
                    {
                        _logger.LogInformation("Rapor index kontrolu tamamlandi.");
                        ilkCalisma = false;
                    }
                    else
                    {
                        IstatistikBakimiYap();
                        _logger.LogInformation("Rapor index ve istatistik bakimi tamamlandi.");
                    }

                    await Task.Delay(BirSonrakiBakimGecikmesi(), stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Rapor index bakimi tamamlanamadi.");
                    await Task.Delay(HataSonrasiBekleme, stoppingToken);
                }
            }
        }

        private void KritikIndexleriTamamla()
        {
            using (var db = new SurveyEntities())
            {
                db.Database.CommandTimeout = 180;
                db.Database.ExecuteSqlCommand(KritikIndexSql);
            }
        }

        private void IstatistikBakimiYap()
        {
            using (var db = new SurveyEntities())
            {
                db.Database.CommandTimeout = 180;
                db.Database.ExecuteSqlCommand(IstatistikBakimSql);
            }
        }

        private static TimeSpan BirSonrakiBakimGecikmesi()
        {
            var simdi = DateTime.Now;
            var hedef = simdi.Date.AddDays(1).AddHours(3).AddMinutes(15);
            return hedef - simdi;
        }

        private const string KritikIndexSql = @"
SET NOCOUNT ON;
SET XACT_ABORT OFF;

DECLARE @kilitSonucu int;
EXEC @kilitSonucu = sp_getapplock
    @Resource = N'survey_rapor_index_bakim',
    @LockMode = N'Exclusive',
    @LockOwner = N'Session',
    @LockTimeout = 0;

IF @kilitSonucu < 0
    RETURN;

BEGIN TRY
    IF OBJECT_ID(N'dbo.Havuz', N'U') IS NOT NULL
    BEGIN
        IF COL_LENGTH(N'dbo.Havuz', N'AnketId') IS NOT NULL
           AND COL_LENGTH(N'dbo.Havuz', N'UserId') IS NOT NULL
           AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.Havuz') AND name = N'IX_Havuz_AnketId_UserId')
            CREATE INDEX IX_Havuz_AnketId_UserId ON dbo.Havuz(AnketId, UserId) INCLUDE (SoruID, SoruGrupId, CevapId, CevapPuan, KayitTar);

        IF COL_LENGTH(N'dbo.Havuz', N'AnketId') IS NOT NULL
           AND COL_LENGTH(N'dbo.Havuz', N'Isimsiz') IS NOT NULL
           AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.Havuz') AND name = N'IX_Havuz_AnketId_Isimsiz')
            CREATE INDEX IX_Havuz_AnketId_Isimsiz ON dbo.Havuz(AnketId, Isimsiz) INCLUDE (SoruID, SoruGrupId, CevapId, CevapPuan, KayitTar);

        IF COL_LENGTH(N'dbo.Havuz', N'AnketId') IS NOT NULL
           AND COL_LENGTH(N'dbo.Havuz', N'SoruID') IS NOT NULL
           AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.Havuz') AND name = N'IX_Havuz_AnketId_SoruID')
            CREATE INDEX IX_Havuz_AnketId_SoruID ON dbo.Havuz(AnketId, SoruID) INCLUDE (UserId, Isimsiz, SoruGrupId, CevapId, CevapPuan, KayitTar);

        IF COL_LENGTH(N'dbo.Havuz', N'AnketId') IS NOT NULL
           AND COL_LENGTH(N'dbo.Havuz', N'SoruGrupId') IS NOT NULL
           AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.Havuz') AND name = N'IX_Havuz_AnketId_SoruGrupId')
            CREATE INDEX IX_Havuz_AnketId_SoruGrupId ON dbo.Havuz(AnketId, SoruGrupId) INCLUDE (UserId, Isimsiz, SoruID, CevapId, CevapPuan, KayitTar);

        IF COL_LENGTH(N'dbo.Havuz', N'AnketId') IS NOT NULL
           AND COL_LENGTH(N'dbo.Havuz', N'KayitTar') IS NOT NULL
           AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.Havuz') AND name = N'IX_Havuz_AnketId_KayitTar')
            CREATE INDEX IX_Havuz_AnketId_KayitTar ON dbo.Havuz(AnketId, KayitTar) INCLUDE (UserId, Isimsiz, SoruID, SoruGrupId, CevapId, CevapPuan);
    END

    IF OBJECT_ID(N'dbo.AnketGrup', N'U') IS NOT NULL
       AND COL_LENGTH(N'dbo.AnketGrup', N'AnketId') IS NOT NULL
       AND COL_LENGTH(N'dbo.AnketGrup', N'SoruGrupId') IS NOT NULL
       AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.AnketGrup') AND name = N'IX_AnketGrup_AnketId_SoruGrupId')
        CREATE INDEX IX_AnketGrup_AnketId_SoruGrupId ON dbo.AnketGrup(AnketId, SoruGrupId);

    IF OBJECT_ID(N'dbo.Izledim', N'U') IS NOT NULL
    BEGIN
        IF COL_LENGTH(N'dbo.Izledim', N'AnketId') IS NOT NULL
           AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.Izledim') AND name = N'IX_Izledim_AnketId')
            CREATE INDEX IX_Izledim_AnketId ON dbo.Izledim(AnketId);

        IF COL_LENGTH(N'dbo.Izledim', N'UseId') IS NOT NULL
           AND COL_LENGTH(N'dbo.Izledim', N'AnketId') IS NOT NULL
           AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.Izledim') AND name = N'IX_Izledim_UseId_AnketId')
            CREATE INDEX IX_Izledim_UseId_AnketId ON dbo.Izledim(UseId, AnketId);
    END

    IF OBJECT_ID(N'dbo.Soru', N'U') IS NOT NULL
       AND COL_LENGTH(N'dbo.Soru', N'SoruGrupId') IS NOT NULL
       AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.Soru') AND name = N'IX_Soru_SoruGrupId')
        CREATE INDEX IX_Soru_SoruGrupId ON dbo.Soru(SoruGrupId);

    IF OBJECT_ID(N'dbo.[User]', N'U') IS NOT NULL
    BEGIN
        IF COL_LENGTH(N'dbo.[User]', N'UserDepartman') IS NOT NULL
           AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.[User]') AND name = N'IX_User_UserDepartman')
            CREATE INDEX IX_User_UserDepartman ON dbo.[User](UserDepartman);

        IF COL_LENGTH(N'dbo.[User]', N'UserCinsiyet') IS NOT NULL
           AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.[User]') AND name = N'IX_User_UserCinsiyet')
            CREATE INDEX IX_User_UserCinsiyet ON dbo.[User](UserCinsiyet);

        IF COL_LENGTH(N'dbo.[User]', N'UserEgitim') IS NOT NULL
           AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.[User]') AND name = N'IX_User_UserEgitim')
            CREATE INDEX IX_User_UserEgitim ON dbo.[User](UserEgitim);

        IF COL_LENGTH(N'dbo.[User]', N'UserSehir') IS NOT NULL
           AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.[User]') AND name = N'IX_User_UserSehir')
            CREATE INDEX IX_User_UserSehir ON dbo.[User](UserSehir);

        IF COL_LENGTH(N'dbo.[User]', N'UserSube') IS NOT NULL
           AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.[User]') AND name = N'IX_User_UserSube')
            CREATE INDEX IX_User_UserSube ON dbo.[User](UserSube);

        IF COL_LENGTH(N'dbo.[User]', N'UserUnvan') IS NOT NULL
           AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.[User]') AND name = N'IX_User_UserUnvan')
            CREATE INDEX IX_User_UserUnvan ON dbo.[User](UserUnvan);

        IF COL_LENGTH(N'dbo.[User]', N'UserYaka') IS NOT NULL
           AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.[User]') AND name = N'IX_User_UserYaka')
            CREATE INDEX IX_User_UserYaka ON dbo.[User](UserYaka);

        IF COL_LENGTH(N'dbo.[User]', N'UserYoneticisi') IS NOT NULL
           AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.[User]') AND name = N'IX_User_UserYoneticisi')
            CREATE INDEX IX_User_UserYoneticisi ON dbo.[User](UserYoneticisi);
    END
END TRY
BEGIN CATCH
    DECLARE @hata nvarchar(4000) = ERROR_MESSAGE();
    RAISERROR(@hata, 11, 1);
END CATCH;

EXEC sp_releaseapplock
    @Resource = N'survey_rapor_index_bakim',
    @LockOwner = N'Session';
";

        private const string IstatistikBakimSql = @"
SET NOCOUNT ON;

DECLARE @kilitSonucu int;
EXEC @kilitSonucu = sp_getapplock
    @Resource = N'survey_rapor_istatistik_bakim',
    @LockMode = N'Exclusive',
    @LockOwner = N'Session',
    @LockTimeout = 0;

IF @kilitSonucu < 0
    RETURN;

BEGIN TRY
    IF OBJECT_ID(N'dbo.Havuz', N'U') IS NOT NULL UPDATE STATISTICS dbo.Havuz WITH RESAMPLE;
    IF OBJECT_ID(N'dbo.AnketGrup', N'U') IS NOT NULL UPDATE STATISTICS dbo.AnketGrup WITH RESAMPLE;
    IF OBJECT_ID(N'dbo.Izledim', N'U') IS NOT NULL UPDATE STATISTICS dbo.Izledim WITH RESAMPLE;
    IF OBJECT_ID(N'dbo.Soru', N'U') IS NOT NULL UPDATE STATISTICS dbo.Soru WITH RESAMPLE;
    IF OBJECT_ID(N'dbo.[User]', N'U') IS NOT NULL UPDATE STATISTICS dbo.[User] WITH RESAMPLE;
END TRY
BEGIN CATCH
    DECLARE @hata nvarchar(4000) = ERROR_MESSAGE();
    RAISERROR(@hata, 11, 1);
END CATCH;

EXEC sp_releaseapplock
    @Resource = N'survey_rapor_istatistik_bakim',
    @LockOwner = N'Session';
";
    }
}
