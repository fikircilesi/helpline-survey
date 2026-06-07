using survey.Models;
using System.Data.Entity;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
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

        public ActionResult PlatformRaporu(int gun = 30)
        {
            if (Session["id"] == null)
            {
                return RedirectToAction("Giris", "Home");
            }

            var personelId = AktifPersonelId();
            if (!PlatformYoneticisiMi(personelId))
            {
                return RedirectToAction("Index", new { mesaj = "Platform raporunu yalnizca kurucu hesap gorebilir." });
            }

            gun = RaporGunDegeri(gun);
            var donemBaslangic = RaporBaslangicTarihi(gun);
            var model = new PlatformOdemeRaporuModel
            {
                Gun = gun,
                DonemBaslangicTarihi = donemBaslangic,
                DonemBitisTarihi = DateTime.Today,
                Musteriler = PlatformMusterileriGetir(),
                SonOdemeler = PlatformSonOdemeleriGetir(donemBaslangic, 80),
                PaketDagilimi = PlatformPaketDagilimiGetir(),
                AylikGelir = PlatformAylikGelirGetir()
            };

            PlatformFirsatNotlariniHazirla(model.Musteriler);
            model.Ozet = PlatformOzetiHazirla(model.Musteriler, donemBaslangic);
            return View(model);
        }

        public ActionResult PlatformRaporuCsv(int gun = 0)
        {
            if (Session["id"] == null)
            {
                return RedirectToAction("Giris", "Home");
            }

            var personelId = AktifPersonelId();
            if (!PlatformYoneticisiMi(personelId))
            {
                return RedirectToAction("Index", new { mesaj = "Platform raporunu yalnizca kurucu hesap gorebilir." });
            }

            gun = RaporGunDegeri(gun);
            var musteriler = PlatformMusterileriGetir();
            PlatformFirsatNotlariniHazirla(musteriler);

            var csv = new StringBuilder();
            csv.AppendLine("CalismaAlaniId;Firma;CalismaAlani;Sahip;Eposta;Telefon;Paket;Durum;Baslangic;Bitis;KalanGun;PanelKullanicisi;Katilimci;Anket;YayindaAnket;SonOdeme;SonOdemeDurumu;FirsatNotu");
            foreach (var satir in musteriler)
            {
                csv.AppendLine(string.Join(";",
                    CsvDegeri(satir.CalismaAlaniId.ToString(CultureInfo.InvariantCulture)),
                    CsvDegeri(satir.FirmaAdi),
                    CsvDegeri(satir.CalismaAlaniAdi),
                    CsvDegeri(satir.SahipAdi),
                    CsvDegeri(satir.SahipEposta),
                    CsvDegeri(satir.SahipTelefon),
                    CsvDegeri(satir.PaketAdi),
                    CsvDegeri(satir.AbonelikDurumu),
                    CsvDegeri(TarihYaz(satir.BaslangicTarihi)),
                    CsvDegeri(TarihYaz(satir.BitisTarihi)),
                    CsvDegeri(satir.KalanGun.ToString(CultureInfo.InvariantCulture)),
                    CsvDegeri(satir.PanelKullanicisi.ToString(CultureInfo.InvariantCulture)),
                    CsvDegeri(satir.KatilimciSayisi.ToString(CultureInfo.InvariantCulture)),
                    CsvDegeri(satir.AnketSayisi.ToString(CultureInfo.InvariantCulture)),
                    CsvDegeri(satir.YayindaAnketSayisi.ToString(CultureInfo.InvariantCulture)),
                    CsvDegeri(satir.SonOdemeTutari.HasValue ? satir.SonOdemeTutari.Value.ToString("N2", CultureInfo.GetCultureInfo("tr-TR")) + " " + satir.SonOdemeParaBirimi : string.Empty),
                    CsvDegeri(satir.SonOdemeDurumu),
                    CsvDegeri(satir.FirsatNotu)));
            }

            var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true).GetBytes(csv.ToString());
            return File(bytes, "text/csv", $"survey-platform-raporu-{DateTime.Today:yyyyMMdd}.csv");
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
                return RedirectToAction("Index", new { mesaj = "Ödeme sistemi henüz aktif değil. Ücretli paketler için ödeme altyapısı tamamlanmalı." });
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
                return RedirectToAction("Index", new { mesaj = "Ödeme dönüşünde sipariş numarası alınamadı." });
            }

            var odeme = OdemeIslemiGetir(siparisNo);
            if (odeme == null)
            {
                return RedirectToAction("Index", new { mesaj = "Ödeme kaydı bulunamadı." });
            }

            var ayarlar = TamiAyarlari();
            if (!ayarlar.SorgulamaHazirMi())
            {
                OdemeHatasiKaydet(odeme.OdemeIslemiId, "Ödeme sorgulama ayarları eksik olduğu için ödeme doğrulanamadı.");
                return RedirectToAction("Index", new { mesaj = "Ödeme dönüşü alındı, ancak ödeme sorgulama ayarları eksik." });
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
                return RedirectToAction("Index", new { mesaj = "Ödeme sorgulama ayarları eksik." });
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

        private List<PlatformMusteriRaporSatiriModel> PlatformMusterileriGetir()
        {
            return db.Database.SqlQuery<PlatformMusteriRaporSatiriModel>(
                @"WITH SonAbonelik AS
                  (
                      SELECT ad.*,
                             ROW_NUMBER() OVER (PARTITION BY ad.CalismaAlaniId ORDER BY ad.AbonelikDurumuId DESC) AS Sira
                      FROM dbo.AbonelikDurumu ad
                  ),
                  PanelKullanimi AS
                  (
                      SELECT CalismaAlaniId, COUNT(1) AS PanelKullanicisi
                      FROM dbo.CalismaAlaniUye
                      WHERE ISNULL(Pasif, 0) = 0
                      GROUP BY CalismaAlaniId
                  ),
                  KatilimciKullanimi AS
                  (
                      SELECT CalismaAlaniId, COUNT(1) AS KatilimciSayisi
                      FROM dbo.[User]
                      WHERE ISNULL(Pasif, 0) = 0
                      GROUP BY CalismaAlaniId
                  ),
                  AnketKullanimi AS
                  (
                      SELECT CalismaAlaniId,
                             COUNT(1) AS AnketSayisi,
                             SUM(CASE WHEN YayinDurumu = N'Yayinda' THEN 1 ELSE 0 END) AS YayindaAnketSayisi
                      FROM dbo.Anket
                      WHERE ISNULL(Pasif, 0) = 0
                      GROUP BY CalismaAlaniId
                  ),
                  SonOdeme AS
                  (
                      SELECT oi.*,
                             ROW_NUMBER() OVER (PARTITION BY oi.CalismaAlaniId ORDER BY oi.OdemeIslemiId DESC) AS Sira
                      FROM dbo.OdemeIslemi oi
                  )
                  SELECT ca.CalismaAlaniId,
                         ca.CalismaAlaniAdi,
                         ca.FirmaAdi,
                         p.PersonelAdi AS SahipAdi,
                         p.Mail AS SahipEposta,
                         p.Telefon AS SahipTelefon,
                         ca.KayitTarihi,
                         op.PaketKodu,
                         op.PaketAdi,
                         sa.AbonelikDurumu,
                         sa.BaslangicTarihi,
                         sa.BitisTarihi,
                         CASE
                            WHEN sa.BitisTarihi IS NULL THEN 0
                            WHEN DATEDIFF(DAY, GETDATE(), sa.BitisTarihi) < 0 THEN 0
                            ELSE DATEDIFF(DAY, GETDATE(), sa.BitisTarihi)
                         END AS KalanGun,
                         CAST(CASE WHEN ISNULL(op.Tutar, 0) > 0 THEN 1 ELSE 0 END AS bit) AS UcretliMi,
                         ISNULL(op.KullaniciLimiti, 0) AS KullaniciLimiti,
                         ISNULL(op.AktifAnketLimiti, 0) AS AktifAnketLimiti,
                         ISNULL(op.AylikYanitLimiti, 0) AS AylikYanitLimiti,
                         ISNULL(pk.PanelKullanicisi, 0) AS PanelKullanicisi,
                         ISNULL(kk.KatilimciSayisi, 0) AS KatilimciSayisi,
                         ISNULL(ak.AnketSayisi, 0) AS AnketSayisi,
                         ISNULL(ak.YayindaAnketSayisi, 0) AS YayindaAnketSayisi,
                         so.Tutar AS SonOdemeTutari,
                         so.ParaBirimi AS SonOdemeParaBirimi,
                         so.OdemeDurumu AS SonOdemeDurumu,
                         so.KayitTarihi AS SonOdemeTarihi,
                         CAST(N'' AS nvarchar(200)) AS FirsatNotu
                  FROM dbo.CalismaAlani ca
                  LEFT JOIN dbo.Personel p ON p.PersonelId = ca.SahipPersonelId
                  LEFT JOIN SonAbonelik sa ON sa.CalismaAlaniId = ca.CalismaAlaniId AND sa.Sira = 1
                  LEFT JOIN dbo.OdemePaketi op ON op.OdemePaketiId = sa.OdemePaketiId
                  LEFT JOIN PanelKullanimi pk ON pk.CalismaAlaniId = ca.CalismaAlaniId
                  LEFT JOIN KatilimciKullanimi kk ON kk.CalismaAlaniId = ca.CalismaAlaniId
                  LEFT JOIN AnketKullanimi ak ON ak.CalismaAlaniId = ca.CalismaAlaniId
                  LEFT JOIN SonOdeme so ON so.CalismaAlaniId = ca.CalismaAlaniId AND so.Sira = 1
                  WHERE ISNULL(ca.Pasif, 0) = 0
                  ORDER BY ISNULL(so.KayitTarihi, ca.KayitTarihi) DESC, ca.CalismaAlaniId DESC").ToList();
        }

        private List<PlatformPaketDagilimModel> PlatformPaketDagilimiGetir()
        {
            return db.Database.SqlQuery<PlatformPaketDagilimModel>(
                @"WITH SonAbonelik AS
                  (
                      SELECT ad.*,
                             ROW_NUMBER() OVER (PARTITION BY ad.CalismaAlaniId ORDER BY ad.AbonelikDurumuId DESC) AS Sira
                      FROM dbo.AbonelikDurumu ad
                  )
                  SELECT op.OdemePaketiId,
                         op.PaketKodu,
                         op.PaketAdi,
                         COUNT(DISTINCT CASE WHEN sa.Sira = 1 AND sa.AbonelikDurumu = N'Aktif' THEN sa.CalismaAlaniId END) AS AktifMusteriSayisi,
                         COUNT(CASE WHEN oi.OdemeDurumu = N'Odendi' THEN 1 END) AS BasariliOdemeSayisi,
                         ISNULL(SUM(CASE WHEN oi.OdemeDurumu = N'Odendi' THEN oi.Tutar ELSE 0 END), 0) AS Gelir,
                         ISNULL(AVG(CASE WHEN oi.OdemeDurumu = N'Odendi' THEN CONVERT(decimal(18,2), oi.Tutar) END), 0) AS OrtalamaTutar
                  FROM dbo.OdemePaketi op
                  LEFT JOIN SonAbonelik sa ON sa.OdemePaketiId = op.OdemePaketiId AND sa.Sira = 1
                  LEFT JOIN dbo.OdemeIslemi oi ON oi.OdemePaketiId = op.OdemePaketiId
                  WHERE ISNULL(op.Pasif, 0) = 0
                  GROUP BY op.OdemePaketiId, op.PaketKodu, op.PaketAdi, op.SiraNo
                  ORDER BY op.SiraNo").ToList();
        }

        private List<PlatformAylikGelirModel> PlatformAylikGelirGetir()
        {
            return db.Database.SqlQuery<PlatformAylikGelirModel>(
                @"SELECT CONVERT(char(7), ISNULL(TamamlanmaTarihi, KayitTarihi), 120) AS AyEtiketi,
                         COUNT(1) AS OdemeSayisi,
                         ISNULL(SUM(Tutar), 0) AS Gelir
                  FROM dbo.OdemeIslemi
                  WHERE OdemeDurumu = N'Odendi'
                    AND ISNULL(TamamlanmaTarihi, KayitTarihi) >= DATEADD(MONTH, -5, DATEFROMPARTS(YEAR(GETDATE()), MONTH(GETDATE()), 1))
                  GROUP BY CONVERT(char(7), ISNULL(TamamlanmaTarihi, KayitTarihi), 120)
                  ORDER BY AyEtiketi").ToList();
        }

        private List<PlatformOdemeRaporSatiriModel> PlatformSonOdemeleriGetir(DateTime? donemBaslangic, int adet)
        {
            var baslangic = donemBaslangic ?? new DateTime(1900, 1, 1);
            adet = Math.Clamp(adet, 10, 250);
            return db.Database.SqlQuery<PlatformOdemeRaporSatiriModel>(
                $@"SELECT TOP ({adet}) oi.OdemeIslemiId,
                         oi.CalismaAlaniId,
                         ca.CalismaAlaniAdi,
                         ca.FirmaAdi,
                         op.PaketAdi,
                         oi.SiparisNo,
                         oi.Tutar,
                         oi.ParaBirimi,
                         oi.OdemeDurumu,
                         oi.TamiHataMesaji,
                         oi.KayitTarihi,
                         oi.TamamlanmaTarihi
                  FROM dbo.OdemeIslemi oi
                  INNER JOIN dbo.CalismaAlani ca ON ca.CalismaAlaniId = oi.CalismaAlaniId
                  INNER JOIN dbo.OdemePaketi op ON op.OdemePaketiId = oi.OdemePaketiId
                  WHERE oi.KayitTarihi >= @p0
                  ORDER BY oi.OdemeIslemiId DESC",
                baslangic).ToList();
        }

        private PlatformOdemeRaporuOzetModel PlatformOzetiHazirla(List<PlatformMusteriRaporSatiriModel> musteriler, DateTime? donemBaslangic)
        {
            var baslangic = donemBaslangic ?? new DateTime(1900, 1, 1);
            var ozet = db.Database.SqlQuery<PlatformOdemeRaporuOzetModel>(
                @"SELECT
                    (SELECT COUNT(1) FROM dbo.OdemeIslemi WHERE OdemeDurumu = N'Odendi') AS BasariliOdemeSayisi,
                    ISNULL((SELECT SUM(Tutar) FROM dbo.OdemeIslemi WHERE OdemeDurumu = N'Odendi'), 0) AS ToplamGelir,
                    (SELECT COUNT(1) FROM dbo.OdemeIslemi WHERE OdemeDurumu IN (N'Bekliyor', N'Yonlendirildi')) AS BekleyenOdemeSayisi,
                    (SELECT COUNT(1) FROM dbo.OdemeIslemi WHERE OdemeDurumu IN (N'Hata', N'Basarisiz')) AS HataOdemeSayisi,
                    (SELECT COUNT(1) FROM dbo.OdemeIslemi WHERE OdemeDurumu = N'Odendi' AND KayitTarihi >= @p0) AS DonemOdemeSayisi,
                    ISNULL((SELECT SUM(Tutar) FROM dbo.OdemeIslemi WHERE OdemeDurumu = N'Odendi' AND KayitTarihi >= @p0), 0) AS DonemGelir,
                    (SELECT COUNT(1) FROM dbo.CalismaAlani WHERE ISNULL(Pasif, 0) = 0 AND KayitTarihi >= @p0) AS DonemYeniCalismaAlani,
                    (SELECT COUNT(1) FROM dbo.UcretsizDenemeKaydi WHERE KayitTarihi >= @p0) AS DonemUcretsizBaslangic,
                    (SELECT COUNT(1) FROM dbo.UcretsizDenemeKaydi) AS UcretsizDenemeKaydi",
                baslangic).FirstOrDefault() ?? new PlatformOdemeRaporuOzetModel();

            ozet.ToplamCalismaAlani = musteriler.Count;
            ozet.AktifCalismaAlani = musteriler.Count(x => string.Equals(x.AbonelikDurumu, "Aktif", StringComparison.OrdinalIgnoreCase));
            ozet.UcretsizMusteri = musteriler.Count(x => string.Equals(x.PaketKodu, "UCRETSIZ", StringComparison.OrdinalIgnoreCase) && string.Equals(x.AbonelikDurumu, "Aktif", StringComparison.OrdinalIgnoreCase));
            ozet.UcretliMusteri = musteriler.Count(x => x.UcretliMi && string.Equals(x.AbonelikDurumu, "Aktif", StringComparison.OrdinalIgnoreCase));
            ozet.PaketsizMusteri = musteriler.Count(x => string.IsNullOrWhiteSpace(x.PaketAdi));
            ozet.ToplamPanelKullanicisi = musteriler.Sum(x => x.PanelKullanicisi);
            ozet.ToplamKatilimci = musteriler.Sum(x => x.KatilimciSayisi);
            ozet.ToplamAnket = musteriler.Sum(x => x.AnketSayisi);
            ozet.YayindaAnket = musteriler.Sum(x => x.YayindaAnketSayisi);
            ozet.DonusumOrani = ozet.AktifCalismaAlani == 0
                ? 0
                : Math.Round((decimal)ozet.UcretliMusteri / ozet.AktifCalismaAlani * 100, 1);

            return ozet;
        }

        private static void PlatformFirsatNotlariniHazirla(List<PlatformMusteriRaporSatiriModel> musteriler)
        {
            foreach (var musteri in musteriler)
            {
                if (string.IsNullOrWhiteSpace(musteri.PaketAdi))
                {
                    musteri.FirsatNotu = "Paket baslatilmamis";
                }
                else if (musteri.UcretliMi && musteri.KalanGun > 0 && musteri.KalanGun <= 30)
                {
                    musteri.FirsatNotu = "Yenileme yakin";
                }
                else if (!musteri.UcretliMi && musteri.AktifAnketLimiti > 0 && musteri.AnketSayisi >= Math.Max(1, musteri.AktifAnketLimiti - 1))
                {
                    musteri.FirsatNotu = "Anket limitine yaklasti";
                }
                else if (!musteri.UcretliMi && musteri.KullaniciLimiti > 0 && musteri.PanelKullanicisi >= musteri.KullaniciLimiti)
                {
                    musteri.FirsatNotu = "Ekip limiti dolu";
                }
                else if (!musteri.UcretliMi && musteri.KatilimciSayisi >= 100)
                {
                    musteri.FirsatNotu = "Katilimci tabani buyuyor";
                }
                else if (!string.IsNullOrWhiteSpace(musteri.SonOdemeDurumu) && !string.Equals(musteri.SonOdemeDurumu, "Odendi", StringComparison.OrdinalIgnoreCase))
                {
                    musteri.FirsatNotu = "Odeme takibi";
                }
                else
                {
                    musteri.FirsatNotu = "Normal";
                }
            }
        }

        private static int RaporGunDegeri(int gun)
        {
            return gun switch
            {
                0 => 0,
                7 => 7,
                30 => 30,
                90 => 90,
                365 => 365,
                _ => 30
            };
        }

        private static DateTime? RaporBaslangicTarihi(int gun)
            => gun <= 0 ? null : DateTime.Today.AddDays(-gun + 1);

        private static string TarihYaz(DateTime? tarih)
            => tarih.HasValue ? tarih.Value.ToString("dd.MM.yyyy", CultureInfo.GetCultureInfo("tr-TR")) : string.Empty;

        private static string CsvDegeri(string deger)
        {
            deger ??= string.Empty;
            return "\"" + deger.Replace("\"", "\"\"") + "\"";
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

        private static bool PlatformYoneticisiMi(int? personelId)
            => personelId == 1;

        private static string SiparisNoUret()
            => $"SVY-{DateTime.UtcNow:yyyyMMddHHmmss}-{RandomNumberGenerator.GetInt32(1000, 9999)}";
    }
}
