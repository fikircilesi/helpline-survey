using survey.Models;
using System.Data.Entity;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace survey.Controllers
{
    public class OdemeController : LegacyController
    {
        private readonly SurveyEntities db = new SurveyEntities();

        public ActionResult Giris()
        {
            return RedirectToAction("Index");
        }

        public ActionResult Index(string mesaj = null)
        {
            if (Session["id"] == null)
            {
                return RedirectToAction("Giris", "Home");
            }

            var personelId = AktifPersonelId();
            var calismaAlaniId = AktifCalismaAlaniId();
            if (!personelId.HasValue || !calismaAlaniId.HasValue)
            {
                return RedirectToAction("Indexgosterge", "Home", new { idi = Session["id"] });
            }

            var paketler = PaketleriGetir();
            var abonelik = AbonelikGetir(calismaAlaniId.Value);
            string otomatikMesaj = null;
            if (abonelik == null && paketler.Any(x => string.Equals(x.PaketKodu, "UCRETSIZ", StringComparison.OrdinalIgnoreCase)))
            {
                otomatikMesaj = UcretsizPlanBaslat(calismaAlaniId.Value, personelId.Value, otomatik: true);
                abonelik = AbonelikGetir(calismaAlaniId.Value);
            }

            var model = new OdemePaketleriSayfaModel
            {
                Paketler = paketler,
                Abonelik = abonelik,
                SonOdemeler = SonOdemeleriGetir(calismaAlaniId.Value),
                TamiHazir = TamiAyarlari().HostedOdemeHazirMi(),
                PaketYonetimiAktif = KurucuHesapMi(personelId, calismaAlaniId),
                Mesaj = mesaj ?? otomatikMesaj
            };

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult UcretsizBaslat()
        {
            if (Session["id"] == null)
            {
                return RedirectToAction("Giris", "Home");
            }

            var personelId = AktifPersonelId();
            var calismaAlaniId = AktifCalismaAlaniId();
            if (!personelId.HasValue || !calismaAlaniId.HasValue)
            {
                return RedirectToAction("Index", new { mesaj = "Çalışma alanı bulunamadı." });
            }

            var sonuc = UcretsizPlanBaslat(calismaAlaniId.Value, personelId.Value, otomatik: false);
            return RedirectToAction("Index", new { mesaj = sonuc });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult PaketGuncelle(
            int OdemePaketiId,
            string PaketAdi,
            string Aciklama,
            decimal Tutar,
            string ParaBirimi,
            int SureGun,
            int KullaniciLimiti,
            int AktifAnketLimiti,
            int AylikYanitLimiti,
            bool MarkaIziGoster = false,
            bool PdfRaporAktif = false,
            bool GelismisRaporAktif = false,
            bool DisaAktarmaAktif = false,
            bool YapayZekaOzetAktif = false,
            int SiraNo = 100)
        {
            if (Session["id"] == null)
            {
                return RedirectToAction("Giris", "Home");
            }

            var personelId = AktifPersonelId();
            var calismaAlaniId = AktifCalismaAlaniId();
            if (!KurucuHesapMi(personelId, calismaAlaniId))
            {
                return RedirectToAction("Index", new { mesaj = "Paket fiyatlarını yalnızca kurucu hesap yönetebilir." });
            }

            if (OdemePaketiId <= 0 || string.IsNullOrWhiteSpace(PaketAdi))
            {
                return RedirectToAction("Index", new { mesaj = "Paket adı zorunlu." });
            }

            Tutar = Math.Max(0, Tutar);
            SureGun = Math.Max(1, SureGun);
            KullaniciLimiti = Math.Max(0, KullaniciLimiti);
            AktifAnketLimiti = Math.Max(0, AktifAnketLimiti);
            AylikYanitLimiti = Math.Max(0, AylikYanitLimiti);
            SiraNo = Math.Max(1, SiraNo);
            ParaBirimi = string.IsNullOrWhiteSpace(ParaBirimi) ? "TRY" : ParaBirimi.Trim().ToUpperInvariant();
            if (ParaBirimi.Length > 10)
            {
                ParaBirimi = ParaBirimi.Substring(0, 10);
            }

            db.Database.ExecuteSqlCommand(
                @"UPDATE dbo.OdemePaketi
                  SET PaketAdi = @p1,
                      Aciklama = @p2,
                      Tutar = @p3,
                      ParaBirimi = @p4,
                      SureGun = @p5,
                      KullaniciLimiti = @p6,
                      AktifAnketLimiti = @p7,
                      AylikYanitLimiti = @p8,
                      MarkaIziGoster = @p9,
                      PdfRaporAktif = @p10,
                      GelismisRaporAktif = @p11,
                      DisaAktarmaAktif = @p12,
                      YapayZekaOzetAktif = @p13,
                      SiraNo = @p14
                  WHERE OdemePaketiId = @p0",
                OdemePaketiId,
                PaketAdi.Trim(),
                (Aciklama ?? string.Empty).Trim(),
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
                SiraNo);

            return RedirectToAction("Index", new { mesaj = "Paket fiyatı ve limitleri güncellendi." });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> PaketSec(int id)
        {
            if (Session["id"] == null)
            {
                return RedirectToAction("Giris", "Home");
            }

            var personelId = AktifPersonelId();
            var calismaAlaniId = AktifCalismaAlaniId();
            if (!personelId.HasValue || !calismaAlaniId.HasValue)
            {
                return RedirectToAction("Index", new { mesaj = "Çalışma alanı bulunamadı." });
            }

            var paket = PaketGetir(id);
            if (paket == null)
            {
                return RedirectToAction("Index", new { mesaj = "Seçilen paket bulunamadı." });
            }

            if (paket.Tutar <= 0)
            {
                var sonuc = UcretsizPlanBaslat(calismaAlaniId.Value, personelId.Value, otomatik: false);
                return RedirectToAction("Index", new { mesaj = sonuc });
            }

            var ayarlar = TamiAyarlari();
            if (!ayarlar.HostedOdemeHazirMi())
            {
                return RedirectToAction("Index", new { mesaj = "Tami ayarları eksik. Ödeme almadan önce Tami üye işyeri bilgileri girilmeli." });
            }

            var siparisNo = SiparisNoUret();
            var odemeIslemiId = OdemeIslemiOlustur(calismaAlaniId.Value, personelId.Value, paket, siparisNo);
            var donusUrl = DonusUrl(siparisNo);
            var client = new TamiOdemeClient(ayarlar);
            var tokenSonucu = await client.HostedTokenOlusturAsync(new TamiHostedTokenIstegi
            {
                CalismaAlaniId = calismaAlaniId.Value,
                OdemePaketiId = paket.OdemePaketiId,
                SiparisNo = siparisNo,
                Tutar = paket.Tutar,
                BasariliDonusUrl = donusUrl,
                BasarisizDonusUrl = donusUrl
            });

            if (!tokenSonucu.Basarili)
            {
                OdemeHatasiKaydet(odemeIslemiId, tokenSonucu.HataMesaji);
                return RedirectToAction("Index", new { mesaj = tokenSonucu.HataMesaji });
            }

            OdemeYonlendirmeKaydet(odemeIslemiId, tokenSonucu.Token, tokenSonucu.OdemeSayfasi);
            return Redirect(tokenSonucu.OdemeSayfasi);
        }

        [AcceptVerbs("GET", "POST")]
        public async Task<ActionResult> TamiDonus(string siparisNo)
        {
            if (string.IsNullOrWhiteSpace(siparisNo))
            {
                return RedirectToAction("Index", new { mesaj = "Tami dönüşünde sipariş numarası alınamadı." });
            }

            var odeme = OdemeIslemiGetir(siparisNo);
            if (odeme == null)
            {
                return RedirectToAction("Index", new { mesaj = "Ödeme kaydı bulunamadı." });
            }

            var ayarlar = TamiAyarlari();
            if (!ayarlar.SorgulamaHazirMi())
            {
                OdemeHatasiKaydet(odeme.OdemeIslemiId, "Tami sorgulama ayarları eksik olduğu için ödeme doğrulanamadı.");
                return RedirectToAction("Index", new { mesaj = "Ödeme dönüşü alındı, ancak Tami sorgulama ayarları eksik. Kid ve K değeri girilmeli." });
            }

            var sorgu = await new TamiOdemeClient(ayarlar).OdemeSorgulaAsync(siparisNo);
            if (sorgu.OdemeBasarili)
            {
                OdemeBasariliKaydet(odeme, sorgu);
                return RedirectToAction("Index", new { mesaj = "Ödeme doğrulandı ve paketiniz aktif edildi." });
            }

            OdemeSorguSonucuKaydet(odeme.OdemeIslemiId, sorgu, "Basarisiz");
            return RedirectToAction("Index", new { mesaj = string.IsNullOrWhiteSpace(sorgu.HataMesaji) ? "Ödeme henüz başarılı görünmüyor." : sorgu.HataMesaji });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> OdemeSorgula(int id)
        {
            if (Session["id"] == null)
            {
                return RedirectToAction("Giris", "Home");
            }

            var calismaAlaniId = AktifCalismaAlaniId();
            if (!calismaAlaniId.HasValue)
            {
                return RedirectToAction("Index", new { mesaj = "Çalışma alanı bulunamadı." });
            }

            var odeme = db.Database.SqlQuery<OdemeIslemiModel>(
                @"SELECT TOP 1 oi.OdemeIslemiId,
                         oi.CalismaAlaniId,
                         oi.PersonelId,
                         oi.OdemePaketiId,
                         op.PaketAdi,
                         oi.SiparisNo,
                         oi.Tutar,
                         oi.ParaBirimi,
                         oi.OdemeDurumu,
                         oi.TamiJeton,
                         oi.TamiOdemeSayfasi,
                         oi.TamiHataKodu,
                         oi.TamiHataMesaji,
                         oi.TamiOdemeDurumu,
                         oi.TamiIslemDurumu,
                         oi.KayitTarihi,
                         oi.TamamlanmaTarihi
                  FROM dbo.OdemeIslemi oi
                  INNER JOIN dbo.OdemePaketi op ON op.OdemePaketiId = oi.OdemePaketiId
                  WHERE oi.OdemeIslemiId = @p0
                    AND oi.CalismaAlaniId = @p1",
                id,
                calismaAlaniId.Value).FirstOrDefault();

            if (odeme == null)
            {
                return RedirectToAction("Index", new { mesaj = "Ödeme kaydı bulunamadı." });
            }

            var ayarlar = TamiAyarlari();
            if (!ayarlar.SorgulamaHazirMi())
            {
                return RedirectToAction("Index", new { mesaj = "Tami sorgulama ayarları eksik." });
            }

            var sorgu = await new TamiOdemeClient(ayarlar).OdemeSorgulaAsync(odeme.SiparisNo);
            if (sorgu.OdemeBasarili)
            {
                OdemeBasariliKaydet(odeme, sorgu);
                return RedirectToAction("Index", new { mesaj = "Ödeme doğrulandı ve paketiniz aktif edildi." });
            }

            OdemeSorguSonucuKaydet(odeme.OdemeIslemiId, sorgu, "Sorgulandi");
            return RedirectToAction("Index", new { mesaj = "Ödeme sorgulandı; başarılı ödeme bulunamadı." });
        }

        private string UcretsizPlanBaslat(int calismaAlaniId, int personelId, bool otomatik)
        {
            var personel = db.Personel.AsNoTracking().FirstOrDefault(x => x.PersonelId == personelId);
            var eposta = EpostaAnahtari(personel?.Mail);
            if (string.IsNullOrWhiteSpace(eposta))
            {
                return otomatik ? null : "Ücretsiz plan için hesap e-postası gerekli.";
            }

            var onceki = db.Database.SqlQuery<int?>(
                @"SELECT TOP 1 CalismaAlaniId
                  FROM dbo.UcretsizDenemeKaydi
                  WHERE EpostaAnahtari = @p0",
                eposta).FirstOrDefault();

            if (onceki.HasValue && onceki.Value != calismaAlaniId)
            {
                return "Bu e-posta adresiyle ücretsiz plan daha önce başlatılmış. Ücretli paket seçerek devam edebilirsiniz.";
            }

            if (onceki.HasValue && onceki.Value == calismaAlaniId)
            {
                var aktifUcretsizVar = db.Database.SqlQuery<int>(
                    @"SELECT COUNT(1)
                      FROM dbo.AbonelikDurumu ad
                      INNER JOIN dbo.OdemePaketi op ON op.OdemePaketiId = ad.OdemePaketiId
                      WHERE ad.CalismaAlaniId = @p0
                        AND op.PaketKodu = N'UCRETSIZ'
                        AND ad.AbonelikDurumu = N'Aktif'
                        AND (ad.BitisTarihi IS NULL OR ad.BitisTarihi >= GETDATE())",
                    calismaAlaniId).FirstOrDefault();

                return aktifUcretsizVar > 0
                    ? (otomatik ? null : "Ücretsiz plan zaten aktif.")
                    : "Ücretsiz kullanım hakkı bu e-posta için tamamlanmış. Ücretli paket seçerek devam edebilirsiniz.";
            }

            var paket = db.Database.SqlQuery<OdemePaketiModel>(
                @"SELECT TOP 1 *
                  FROM dbo.OdemePaketi
                  WHERE PaketKodu = N'UCRETSIZ'
                    AND ISNULL(Pasif, 0) = 0
                  ORDER BY SiraNo").FirstOrDefault();

            if (paket == null)
            {
                return otomatik ? null : "Ücretsiz paket tanımı bulunamadı.";
            }

            AbonelikAktifEt(calismaAlaniId, paket, null);

            db.Database.ExecuteSqlCommand(
                @"IF NOT EXISTS (SELECT 1 FROM dbo.UcretsizDenemeKaydi WHERE EpostaAnahtari = @p0)
                  BEGIN
                      INSERT INTO dbo.UcretsizDenemeKaydi
                          (Eposta, EpostaAnahtari, PersonelId, CalismaAlaniId, KayitTarihi)
                      VALUES
                          (@p1, @p0, @p2, @p3, GETDATE());
                  END",
                eposta,
                personel?.Mail,
                personelId,
                calismaAlaniId);

            return otomatik ? null : "Ücretsiz plan aktif edildi.";
        }

        private List<OdemePaketiModel> PaketleriGetir()
        {
            try
            {
                return db.Database.SqlQuery<OdemePaketiModel>(
                    @"SELECT OdemePaketiId,
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
                             SiraNo
                      FROM dbo.OdemePaketi
                      WHERE ISNULL(Pasif, 0) = 0
                      ORDER BY SiraNo").ToList();
            }
            catch
            {
                return new List<OdemePaketiModel>();
            }
        }

        private OdemePaketiModel PaketGetir(int id)
        {
            return db.Database.SqlQuery<OdemePaketiModel>(
                @"SELECT TOP 1 OdemePaketiId,
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
                         SiraNo
                  FROM dbo.OdemePaketi
                  WHERE OdemePaketiId = @p0
                    AND ISNULL(Pasif, 0) = 0",
                id).FirstOrDefault();
        }

        private AbonelikDurumuModel AbonelikGetir(int calismaAlaniId)
        {
            try
            {
                return db.Database.SqlQuery<AbonelikDurumuModel>(
                    @"SELECT TOP 1 ad.CalismaAlaniId,
                             ad.OdemePaketiId,
                             op.PaketKodu,
                             op.PaketAdi,
                             ad.AbonelikDurumu,
                             ad.BaslangicTarihi,
                             ad.BitisTarihi,
                             CASE
                                WHEN ad.BitisTarihi IS NULL THEN 0
                                WHEN DATEDIFF(DAY, GETDATE(), ad.BitisTarihi) < 0 THEN 0
                                ELSE DATEDIFF(DAY, GETDATE(), ad.BitisTarihi)
                             END AS KalanGun,
                             op.KullaniciLimiti,
                             op.AktifAnketLimiti,
                             op.AylikYanitLimiti,
                             op.MarkaIziGoster,
                             op.PdfRaporAktif,
                             op.GelismisRaporAktif,
                             op.DisaAktarmaAktif,
                             op.YapayZekaOzetAktif
                      FROM dbo.AbonelikDurumu ad
                      INNER JOIN dbo.OdemePaketi op ON op.OdemePaketiId = ad.OdemePaketiId
                      WHERE ad.CalismaAlaniId = @p0
                      ORDER BY ad.AbonelikDurumuId DESC",
                    calismaAlaniId).FirstOrDefault();
            }
            catch
            {
                return null;
            }
        }

        private List<OdemeIslemiModel> SonOdemeleriGetir(int calismaAlaniId)
        {
            try
            {
                return db.Database.SqlQuery<OdemeIslemiModel>(
                    @"SELECT TOP 10 oi.OdemeIslemiId,
                             oi.CalismaAlaniId,
                             oi.PersonelId,
                             oi.OdemePaketiId,
                             op.PaketAdi,
                             oi.SiparisNo,
                             oi.Tutar,
                             oi.ParaBirimi,
                             oi.OdemeDurumu,
                             oi.TamiJeton,
                             oi.TamiOdemeSayfasi,
                             oi.TamiHataKodu,
                             oi.TamiHataMesaji,
                             oi.TamiOdemeDurumu,
                             oi.TamiIslemDurumu,
                             oi.KayitTarihi,
                             oi.TamamlanmaTarihi
                      FROM dbo.OdemeIslemi oi
                      INNER JOIN dbo.OdemePaketi op ON op.OdemePaketiId = oi.OdemePaketiId
                      WHERE oi.CalismaAlaniId = @p0
                      ORDER BY oi.OdemeIslemiId DESC",
                    calismaAlaniId).ToList();
            }
            catch
            {
                return new List<OdemeIslemiModel>();
            }
        }

        private int OdemeIslemiOlustur(int calismaAlaniId, int personelId, OdemePaketiModel paket, string siparisNo)
        {
            return db.Database.SqlQuery<int>(
                @"INSERT INTO dbo.OdemeIslemi
                    (CalismaAlaniId, PersonelId, OdemePaketiId, SiparisNo, Tutar, ParaBirimi, OdemeDurumu, KayitTarihi)
                  VALUES
                    (@p0, @p1, @p2, @p3, @p4, @p5, N'Bekliyor', GETDATE());
                  SELECT CAST(SCOPE_IDENTITY() AS int);",
                calismaAlaniId,
                personelId,
                paket.OdemePaketiId,
                siparisNo,
                paket.Tutar,
                paket.ParaBirimi).First();
        }

        private OdemeIslemiModel OdemeIslemiGetir(string siparisNo)
        {
            return db.Database.SqlQuery<OdemeIslemiModel>(
                @"SELECT TOP 1 oi.OdemeIslemiId,
                         oi.CalismaAlaniId,
                         oi.PersonelId,
                         oi.OdemePaketiId,
                         op.PaketAdi,
                         oi.SiparisNo,
                         oi.Tutar,
                         oi.ParaBirimi,
                         oi.OdemeDurumu,
                         oi.TamiJeton,
                         oi.TamiOdemeSayfasi,
                         oi.TamiHataKodu,
                         oi.TamiHataMesaji,
                         oi.TamiOdemeDurumu,
                         oi.TamiIslemDurumu,
                         oi.KayitTarihi,
                         oi.TamamlanmaTarihi
                  FROM dbo.OdemeIslemi oi
                  INNER JOIN dbo.OdemePaketi op ON op.OdemePaketiId = oi.OdemePaketiId
                  WHERE oi.SiparisNo = @p0",
                siparisNo).FirstOrDefault();
        }

        private void OdemeYonlendirmeKaydet(int odemeIslemiId, string token, string odemeSayfasi)
        {
            db.Database.ExecuteSqlCommand(
                @"UPDATE dbo.OdemeIslemi
                  SET OdemeDurumu = N'Yonlendirildi',
                      TamiJeton = @p1,
                      TamiOdemeSayfasi = @p2
                  WHERE OdemeIslemiId = @p0",
                odemeIslemiId,
                token,
                odemeSayfasi);
        }

        private void OdemeHatasiKaydet(int odemeIslemiId, string hataMesaji)
        {
            db.Database.ExecuteSqlCommand(
                @"UPDATE dbo.OdemeIslemi
                  SET OdemeDurumu = N'Hata',
                      TamiHataMesaji = @p1
                  WHERE OdemeIslemiId = @p0",
                odemeIslemiId,
                hataMesaji);
        }

        private void OdemeBasariliKaydet(OdemeIslemiModel odeme, TamiOdemeSorguSonucu sorgu)
        {
            var paket = PaketGetir(odeme.OdemePaketiId);
            if (paket == null)
            {
                OdemeHatasiKaydet(odeme.OdemeIslemiId, "Paket kaydı bulunamadığı için abonelik aktif edilemedi.");
                return;
            }

            OdemeSorguSonucuKaydet(odeme.OdemeIslemiId, sorgu, "Odendi");
            AbonelikAktifEt(odeme.CalismaAlaniId, paket, odeme.OdemeIslemiId);
        }

        private void OdemeSorguSonucuKaydet(int odemeIslemiId, TamiOdemeSorguSonucu sorgu, string odemeDurumu)
        {
            db.Database.ExecuteSqlCommand(
                @"UPDATE dbo.OdemeIslemi
                  SET OdemeDurumu = @p1,
                      TamiHataKodu = @p2,
                      TamiHataMesaji = @p3,
                      TamiOdemeDurumu = @p4,
                      TamiIslemDurumu = @p5,
                      TamiHamYanit = @p6,
                      TamamlanmaTarihi = CASE WHEN @p1 = N'Odendi' THEN GETDATE() ELSE TamamlanmaTarihi END
                  WHERE OdemeIslemiId = @p0",
                odemeIslemiId,
                odemeDurumu,
                sorgu.HataKodu,
                sorgu.HataMesaji,
                sorgu.OdemeDurumu,
                sorgu.IslemDurumu,
                sorgu.HamYanit);
        }

        private void AbonelikAktifEt(int calismaAlaniId, OdemePaketiModel paket, int? odemeIslemiId)
        {
            db.Database.ExecuteSqlCommand(
                @"DECLARE @Bugun DATETIME = GETDATE();
                  DECLARE @Baslangic DATETIME = @Bugun;
                  DECLARE @MevcutBitis DATETIME;

                  SELECT TOP 1 @MevcutBitis = BitisTarihi
                  FROM dbo.AbonelikDurumu
                  WHERE CalismaAlaniId = @p0
                    AND AbonelikDurumu = N'Aktif'
                  ORDER BY AbonelikDurumuId DESC;

                  IF @MevcutBitis IS NOT NULL AND @MevcutBitis > @Bugun
                      SET @Baslangic = @MevcutBitis;

                  IF EXISTS (SELECT 1 FROM dbo.AbonelikDurumu WHERE CalismaAlaniId = @p0)
                  BEGIN
                      UPDATE dbo.AbonelikDurumu
                      SET OdemePaketiId = @p1,
                          AbonelikDurumu = N'Aktif',
                          BaslangicTarihi = CASE WHEN BaslangicTarihi IS NULL THEN @Bugun ELSE BaslangicTarihi END,
                          BitisTarihi = DATEADD(DAY, @p2, @Baslangic),
                          SonOdemeIslemiId = @p3,
                          GuncellemeTarihi = GETDATE()
                      WHERE CalismaAlaniId = @p0;
                  END
                  ELSE
                  BEGIN
                      INSERT INTO dbo.AbonelikDurumu
                          (CalismaAlaniId, OdemePaketiId, AbonelikDurumu, BaslangicTarihi, BitisTarihi, SonOdemeIslemiId, KayitTarihi, GuncellemeTarihi)
                      VALUES
                          (@p0, @p1, N'Aktif', @Bugun, DATEADD(DAY, @p2, @Baslangic), @p3, GETDATE(), GETDATE());
                  END",
                calismaAlaniId,
                paket.OdemePaketiId,
                paket.SureGun,
                odemeIslemiId);
        }

        private int? AktifPersonelId()
        {
            var deger = Convert.ToString(Session["id"]);
            return int.TryParse(deger, out var personelId) && personelId > 0 ? personelId : null;
        }

        private int? AktifCalismaAlaniId()
        {
            var deger = Convert.ToString(Session["CalismaAlaniId"]);
            if (int.TryParse(deger, out var calismaAlaniId) && calismaAlaniId > 0)
            {
                return calismaAlaniId;
            }

            var personelId = AktifPersonelId();
            if (!personelId.HasValue)
            {
                return null;
            }

            var mevcut = db.Database.SqlQuery<int?>(
                @"SELECT TOP 1 cau.CalismaAlaniId
                  FROM dbo.CalismaAlaniUye cau
                  INNER JOIN dbo.CalismaAlani ca ON ca.CalismaAlaniId = cau.CalismaAlaniId
                  WHERE cau.PersonelId = @p0
                    AND ISNULL(cau.Pasif, 0) = 0
                    AND ISNULL(ca.Pasif, 0) = 0
                  ORDER BY cau.CalismaAlaniUyeId",
                personelId.Value).FirstOrDefault();

            if (mevcut.HasValue && mevcut.Value > 0)
            {
                Session["CalismaAlaniId"] = mevcut.Value;
            }

            return mevcut;
        }

        private TamiOdemeAyarlari TamiAyarlari()
        {
            var configuration = HttpContext.RequestServices.GetRequiredService<IConfiguration>();
            var ayarlar = new TamiOdemeAyarlari
            {
                Aktif = configuration.GetValue<bool?>("Tami:Aktif") ?? false,
                TestModu = configuration.GetValue<bool?>("Tami:TestModu") ?? true,
                UyeIsyeriNumarasi = configuration["Tami:UyeIsyeriNumarasi"],
                TerminalNumarasi = configuration["Tami:TerminalNumarasi"],
                GuvenlikAnahtari = configuration["Tami:GuvenlikAnahtari"],
                KidDegeri = configuration["Tami:KidDegeri"],
                KDegeri = configuration["Tami:KDegeri"],
                MusteriTelefonu = configuration["Tami:MusteriTelefonu"],
                DonusUrlKoku = configuration["Tami:DonusUrlKoku"]
            };

            return ayarlar;
        }

        private string DonusUrl(string siparisNo)
        {
            var ayarlar = TamiAyarlari();
            var kok = string.IsNullOrWhiteSpace(ayarlar.DonusUrlKoku)
                ? $"{Request.Scheme}://{Request.Host}"
                : ayarlar.DonusUrlKoku.TrimEnd('/');

            return $"{kok}{Url.Action("TamiDonus", "Odeme", new { siparisNo })}";
        }

        private static string EpostaAnahtari(string eposta)
            => string.IsNullOrWhiteSpace(eposta) ? null : eposta.Trim().ToLowerInvariant();

        private static bool KurucuHesapMi(int? personelId, int? calismaAlaniId)
            => personelId == 1 || calismaAlaniId == 1;

        private static string SiparisNoUret()
            => $"SVY-{DateTime.UtcNow:yyyyMMddHHmmss}-{RandomNumberGenerator.GetInt32(1000, 9999)}";
    }
}
