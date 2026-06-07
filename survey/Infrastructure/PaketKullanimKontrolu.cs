using survey.Models;

namespace survey.Infrastructure;

public static class PaketKullanimKontrolu
{
    public static bool AktifAnketEklenebilirMi(SurveyEntities db, int? calismaAlaniId, out string mesaj, int? personelId = null)
    {
        mesaj = null;
        if (!calismaAlaniId.HasValue || KurucuHesapMi(calismaAlaniId, personelId))
        {
            return true;
        }

        try
        {
            var limit = AktifPaketLimiti(db, calismaAlaniId.Value, "AktifAnketLimiti");
            if (!limit.HasValue || limit.Value <= 0)
            {
                return true;
            }

            var kullanim = db.Database.SqlQuery<int>(
                @"SELECT COUNT(1)
                  FROM dbo.Anket
                  WHERE CalismaAlaniId = @p0
                    AND ISNULL(Pasif, 0) = 0",
                calismaAlaniId.Value).FirstOrDefault();

            if (kullanim < limit.Value)
            {
                return true;
            }

            mesaj = $"Aktif anket limitiniz doldu ({limit.Value}). Daha fazla çalışma için paketinizi yükseltin.";
            return false;
        }
        catch
        {
            return true;
        }
    }

    public static bool PanelKullanicisiEklenebilirMi(SurveyEntities db, int? calismaAlaniId, out string mesaj, int? personelId = null)
    {
        mesaj = null;
        if (!calismaAlaniId.HasValue || KurucuHesapMi(calismaAlaniId, personelId))
        {
            return true;
        }

        try
        {
            var limit = AktifPaketLimiti(db, calismaAlaniId.Value, "KullaniciLimiti");
            if (!limit.HasValue || limit.Value <= 0)
            {
                return true;
            }

            var kullanim = db.Database.SqlQuery<int>(
                @"SELECT COUNT(1)
                  FROM dbo.CalismaAlaniUye
                  WHERE CalismaAlaniId = @p0
                    AND ISNULL(Pasif, 0) = 0",
                calismaAlaniId.Value).FirstOrDefault();

            if (kullanim < limit.Value)
            {
                return true;
            }

            mesaj = $"Panel kullanıcısı limitiniz doldu ({limit.Value}). Ek kullanıcı için paketinizi yükseltin.";
            return false;
        }
        catch
        {
            return true;
        }
    }

    private static int? AktifPaketLimiti(SurveyEntities db, int calismaAlaniId, string kolonAdi)
    {
        var guvenliKolon = kolonAdi switch
        {
            "AktifAnketLimiti" => "op.AktifAnketLimiti",
            "KullaniciLimiti" => "op.KullaniciLimiti",
            _ => throw new ArgumentOutOfRangeException(nameof(kolonAdi), kolonAdi, null)
        };

        var sql = $@"
SELECT TOP 1 {guvenliKolon}
FROM dbo.AbonelikDurumu ad
INNER JOIN dbo.OdemePaketi op ON op.OdemePaketiId = ad.OdemePaketiId
WHERE ad.CalismaAlaniId = @p0
  AND ad.AbonelikDurumu = N'Aktif'
  AND (ad.BitisTarihi IS NULL OR ad.BitisTarihi >= GETDATE())
ORDER BY ad.AbonelikDurumuId DESC";

        return db.Database.SqlQuery<int?>(sql, calismaAlaniId).FirstOrDefault();
    }

    private static bool KurucuHesapMi(int? calismaAlaniId, int? personelId)
        => calismaAlaniId == 1 || personelId == 1;
}
