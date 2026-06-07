using survey.Models;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Mail;
using System.Net.Security;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Processing;
using System.Web.Security;

namespace survey.Controllers
{
    public class AyarController : LegacyController
    {
        readonly SurveyEntities db = new SurveyEntities();
        readonly EnvanterTakipLisansEntities dbl = new EnvanterTakipLisansEntities();

        private bool YoneticiYetkisiVarMi()
        {
            var adminDegeri = Convert.ToString(Session["admin"]);
            return string.Equals(adminDegeri, "True", StringComparison.OrdinalIgnoreCase)
                || string.Equals(adminDegeri, "1", StringComparison.OrdinalIgnoreCase);
        }

        private ActionResult YoneticiYetkisiYok()
        {
            if (Session["id"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }

            return RedirectToAction("Indexgosterge", "Home", new { idi = Session["id"] });
        }

        public ActionResult index()
        {
            if (Session["id"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }
            if (!YoneticiYetkisiVarMi())
            {
                return YoneticiYetkisiYok();
            }

            ViewBag.PlatformSahibiMi = PlatformSahibiMi();
            VarsayilanSecenekViewBagHazirla();
            return View();
        }

        private int? AktifPersonelId()
        {
            var deger = Convert.ToString(Session["id"]);
            if (int.TryParse(deger, out var personelId) && personelId > 0)
            {
                return personelId;
            }

            return null;
        }

        private bool PlatformSahibiMi()
        {
            if (!YoneticiYetkisiVarMi())
            {
                return false;
            }

            var personelId = AktifPersonelId();
            if (!personelId.HasValue)
            {
                return false;
            }

            var ownerIds = PlatformAyarListesi("Platform:OwnerPersonelIds", "Platform:OwnerIds", "ASLANA_PLATFORM_OWNER_IDS");
            if (ownerIds.Any(x => int.TryParse(x, out var id) && id == personelId.Value))
            {
                return true;
            }

            var personel = db.Personel.AsNoTracking().FirstOrDefault(x => x.PersonelId == personelId.Value);
            if (personel == null)
            {
                return false;
            }

            var ownerMails = PlatformAyarListesi("Platform:OwnerMails", "Platform:OwnerEmails", "ASLANA_PLATFORM_OWNER_EMAILS");
            if (!string.IsNullOrWhiteSpace(personel.Mail)
                && ownerMails.Any(x => string.Equals(x, personel.Mail, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            var ownerUserNames = PlatformAyarListesi("Platform:OwnerUserNames", "Platform:OwnerKullaniciAdlari", "ASLANA_PLATFORM_OWNER_USERNAMES");
            return !string.IsNullOrWhiteSpace(personel.KullaniciAdi)
                && ownerUserNames.Any(x => string.Equals(x, personel.KullaniciAdi, StringComparison.OrdinalIgnoreCase));
        }

        private List<string> PlatformAyarListesi(params string[] keys)
        {
            var values = new List<string>();
            var configuration = HttpContext?.RequestServices.GetService<IConfiguration>();

            foreach (var key in keys)
            {
                if (configuration != null)
                {
                    values.Add(configuration[key]);
                }

                values.Add(Environment.GetEnvironmentVariable(key));
                values.Add(Environment.GetEnvironmentVariable(key.Replace(":", "__")));
            }

            return values
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .SelectMany(x => x.Split(new[] { ',', ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries))
                .Select(x => x.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
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

            try
            {
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
                    return mevcut.Value;
                }
            }
            catch
            {
            }

            return null;
        }

        private static string AyarSqlTablo(string tablo)
        {
            return tablo switch
            {
                "Unvan" => "dbo.Unvan",
                "Departman" => "dbo.Departman",
                "Bolge" => "dbo.Bolge",
                "Sehir" => "dbo.Sehir",
                "Sube" => "dbo.Sube",
                "Bolum" => "dbo.Bolum",
                "Yonetici" => "dbo.Yonetici",
                "User" => "dbo.[User]",
                "Personel" => "dbo.Personel",
                "Anket" => "dbo.Anket",
                "smtpayar" => "dbo.smtpayar",
                _ => throw new ArgumentOutOfRangeException(nameof(tablo), tablo, null)
            };
        }

        private static string AyarSqlNesneAdi(string tablo)
        {
            return tablo switch
            {
                "Unvan" => "dbo.Unvan",
                "Departman" => "dbo.Departman",
                "Bolge" => "dbo.Bolge",
                "Sehir" => "dbo.Sehir",
                "Sube" => "dbo.Sube",
                "Bolum" => "dbo.Bolum",
                "Yonetici" => "dbo.Yonetici",
                "User" => "dbo.User",
                "Personel" => "dbo.Personel",
                "Anket" => "dbo.Anket",
                "smtpayar" => "dbo.smtpayar",
                _ => throw new ArgumentOutOfRangeException(nameof(tablo), tablo, null)
            };
        }

        private static string AyarSqlKolon(string kolon)
        {
            return kolon switch
            {
                "UnvanId" => "[UnvanId]",
                "UnvanAdi" => "[UnvanAdi]",
                "DepartmanId" => "[DepartmanId]",
                "DepartmanAdi" => "[DepartmanAdi]",
                "BolgeId" => "[BolgeId]",
                "BolgeAdi" => "[BolgeAdi]",
                "SehirId" => "[SehirId]",
                "SehiarAdi" => "[SehiarAdi]",
                "SubeId" => "[SubeId]",
                "SubeAdi" => "[SubeAdi]",
                "BolumId" => "[BolumId]",
                "BolumAdi" => "[BolumAdi]",
                "YoneticiId" => "[YoneticiId]",
                "YoneticiAdi" => "[YoneticiAdi]",
                "UserId" => "[UserId]",
                "UserAdi" => "[UserAdi]",
                "PersonelId" => "[PersonelId]",
                "PersonelAdi" => "[PersonelAdi]",
                "AnketId" => "[AnketId]",
                "MailId" => "[MailId]",
                "UserUnvan" => "[UserUnvan]",
                "UserDepartman" => "[UserDepartman]",
                "UserBolge" => "[UserBolge]",
                "UserSehir" => "[UserSehir]",
                "UserSube" => "[UserSube]",
                "UserBolumu" => "[UserBolumu]",
                "UserYoneticisi" => "[UserYoneticisi]",
                "Unvani" => "[Unvani]",
                _ => throw new ArgumentOutOfRangeException(nameof(kolon), kolon, null)
            };
        }

        private bool AyarTablosuCalismaAlaniKolonuVarMi(string tablo)
        {
            try
            {
                return db.Database.SqlQuery<int>(
                    "SELECT CASE WHEN COL_LENGTH(@p0, N'CalismaAlaniId') IS NULL THEN 0 ELSE 1 END",
                    AyarSqlNesneAdi(tablo)).FirstOrDefault() == 1;
            }
            catch
            {
                return false;
            }
        }

        private List<T> CalismaAlaniKayitlari<T>(string tablo, string siralamaKolonu) where T : class
        {
            var calismaAlaniId = AktifCalismaAlaniId();
            if (!calismaAlaniId.HasValue || !AyarTablosuCalismaAlaniKolonuVarMi(tablo))
            {
                return new List<T>();
            }

            var sql = $"SELECT * FROM {AyarSqlTablo(tablo)} WHERE CalismaAlaniId = @p0 ORDER BY {AyarSqlKolon(siralamaKolonu)}";
            return db.Set<T>().SqlQuery(sql, calismaAlaniId.Value).ToList();
        }

        private T CalismaAlaniKaydiGetir<T>(string tablo, string idKolonu, int id) where T : class
        {
            var calismaAlaniId = AktifCalismaAlaniId();
            if (!calismaAlaniId.HasValue || !AyarTablosuCalismaAlaniKolonuVarMi(tablo))
            {
                return null;
            }

            var sql = $"SELECT * FROM {AyarSqlTablo(tablo)} WHERE {AyarSqlKolon(idKolonu)} = @p0 AND CalismaAlaniId = @p1";
            return db.Set<T>().SqlQuery(sql, id, calismaAlaniId.Value).FirstOrDefault();
        }

        private bool CalismaAlaniKaydiVarMi(string tablo, string idKolonu, int? id)
        {
            var calismaAlaniId = AktifCalismaAlaniId();
            if (!id.HasValue || !calismaAlaniId.HasValue || !AyarTablosuCalismaAlaniKolonuVarMi(tablo))
            {
                return false;
            }

            var sql = $"SELECT COUNT(1) FROM {AyarSqlTablo(tablo)} WHERE {AyarSqlKolon(idKolonu)} = @p0 AND CalismaAlaniId = @p1";
            return db.Database.SqlQuery<int>(sql, id.Value, calismaAlaniId.Value).FirstOrDefault() > 0;
        }

        private void CalismaAlaniKaydinaBagla(string tablo, string idKolonu, int id)
        {
            var calismaAlaniId = AktifCalismaAlaniId();
            if (!calismaAlaniId.HasValue || !AyarTablosuCalismaAlaniKolonuVarMi(tablo))
            {
                return;
            }

            var sql = $"UPDATE {AyarSqlTablo(tablo)} SET CalismaAlaniId = @p0 WHERE {AyarSqlKolon(idKolonu)} = @p1";
            db.Database.ExecuteSqlCommand(sql, calismaAlaniId.Value, id);
        }

        private bool CalismaAlaniReferansVarMi(string tablo, string kolon, int? id)
        {
            var calismaAlaniId = AktifCalismaAlaniId();
            if (!id.HasValue || !calismaAlaniId.HasValue || !AyarTablosuCalismaAlaniKolonuVarMi(tablo))
            {
                return false;
            }

            var sql = $"SELECT COUNT(1) FROM {AyarSqlTablo(tablo)} WHERE {AyarSqlKolon(kolon)} = @p0 AND CalismaAlaniId = @p1";
            return db.Database.SqlQuery<int>(sql, id.Value, calismaAlaniId.Value).FirstOrDefault() > 0;
        }

        private bool PersonelReferansVarMi(string kolon, int? id)
        {
            var calismaAlaniId = AktifCalismaAlaniId();
            if (!id.HasValue || !calismaAlaniId.HasValue)
            {
                return false;
            }

            var sql = $@"SELECT COUNT(1)
                         FROM dbo.Personel p
                         INNER JOIN dbo.CalismaAlaniUye cau ON cau.PersonelId = p.PersonelId
                         WHERE p.{AyarSqlKolon(kolon)} = @p0
                           AND cau.CalismaAlaniId = @p1
                           AND ISNULL(cau.Pasif, 0) = 0";
            return db.Database.SqlQuery<int>(sql, id.Value, calismaAlaniId.Value).FirstOrDefault() > 0;
        }

        private bool AnketCalismaAlanindaMi(int? anketId)
        {
            var calismaAlaniId = AktifCalismaAlaniId();
            if (!anketId.HasValue || !calismaAlaniId.HasValue)
            {
                return false;
            }

            try
            {
                return db.Database.SqlQuery<int>(
                    @"SELECT COUNT(1)
                      FROM dbo.Anket
                      WHERE AnketId = @p0
                        AND CalismaAlaniId = @p1",
                    anketId.Value,
                    calismaAlaniId.Value).FirstOrDefault() > 0;
            }
            catch
            {
                return false;
            }
        }

        private Anket CalismaAlaniAnketGetir(int? anketId)
        {
            if (!AnketCalismaAlanindaMi(anketId))
            {
                return null;
            }

            return db.Anket.FirstOrDefault(x => x.AnketId == anketId.Value);
        }

        private List<Personel> CalismaAlaniPersonelleri()
        {
            var calismaAlaniId = AktifCalismaAlaniId();
            if (!calismaAlaniId.HasValue)
            {
                return new List<Personel>();
            }

            return db.Personel.SqlQuery(
                @"SELECT p.*
                  FROM dbo.Personel p
                  INNER JOIN dbo.CalismaAlaniUye cau ON cau.PersonelId = p.PersonelId
                  WHERE cau.CalismaAlaniId = @p0
                    AND ISNULL(cau.Pasif, 0) = 0
                  ORDER BY p.PersonelAdi",
                calismaAlaniId.Value).ToList();
        }

        private List<PersonelListeSatiri> CalismaAlaniPersonelListeSatirlari()
        {
            var calismaAlaniId = AktifCalismaAlaniId();
            if (!calismaAlaniId.HasValue)
            {
                return new List<PersonelListeSatiri>();
            }

            return db.Database.SqlQuery<PersonelListeSatiri>(
                @"SELECT p.PersonelId,
                         p.PersonelAdi,
                         p.Tc,
                         p.Unvani,
                         u.UnvanAdi,
                         p.Mail,
                         p.Resim,
                         p.Telefon,
                         p.Adres,
                         p.KullaniciAdi,
                         p.Pasif,
                         p.KayitTarihi,
                         p.Admin,
                         p.MailOnaylandi,
                         p.GoogleKimlikId,
                         p.GirisKaynagi,
                         p.SonGirisTarihi
                  FROM dbo.Personel p
                  INNER JOIN dbo.CalismaAlaniUye cau ON cau.PersonelId = p.PersonelId
                  LEFT JOIN dbo.Unvan u ON u.UnvanId = p.Unvani
                  WHERE cau.CalismaAlaniId = @p0
                    AND ISNULL(cau.Pasif, 0) = 0
                  ORDER BY ISNULL(p.Pasif, 0), p.PersonelAdi",
                calismaAlaniId.Value).ToList();
        }

        private PersonelPanelBilgisi PersonelPanelBilgisiGetir(int personelId)
        {
            try
            {
                return db.Database.SqlQuery<PersonelPanelBilgisi>(
                    @"SELECT PersonelId,
                             MailOnaylandi,
                             GoogleKimlikId,
                             GirisKaynagi,
                             MailOnayKoduTarihi,
                             SonGirisTarihi
                      FROM dbo.Personel
                      WHERE PersonelId = @p0",
                    personelId).FirstOrDefault() ?? new PersonelPanelBilgisi { PersonelId = personelId, MailOnaylandi = true };
            }
            catch
            {
                return new PersonelPanelBilgisi { PersonelId = personelId, MailOnaylandi = true };
            }
        }

        private PersonelYonetimForm PersonelFormModeli(Personel personel)
        {
            var panel = PersonelPanelBilgisiGetir(personel.PersonelId);
            return new PersonelYonetimForm
            {
                PersonelId = personel.PersonelId,
                PersonelAdi = personel.PersonelAdi,
                Tc = personel.Tc,
                Unvani = personel.Unvani,
                Mail = personel.Mail,
                Resim = personel.Resim,
                Telefon = personel.Telefon,
                Adres = personel.Adres,
                KullaniciAdi = personel.KullaniciAdi,
                Sifre = string.Empty,
                HesapAktif = personel.Pasif != true,
                KayitTarihi = personel.KayitTarihi,
                Admin = personel.Admin == true,
                MailOnaylandi = panel.MailOnaylandi != false,
                GoogleKimlikId = panel.GoogleKimlikId,
                GirisKaynagi = panel.GirisKaynagi,
                MailOnayKoduTarihi = panel.MailOnayKoduTarihi,
                SonGirisTarihi = panel.SonGirisTarihi
            };
        }

        private void PersonelAlanlariniUygula(Personel personel, PersonelYonetimForm model, bool yeniKayit)
        {
            personel.PersonelAdi = model.PersonelAdi;
            personel.Tc = model.Tc;
            personel.Unvani = model.Unvani;
            personel.Mail = model.Mail;
            personel.Resim = model.Resim;
            personel.Telefon = model.Telefon;
            personel.Adres = model.Adres;
            personel.KullaniciAdi = model.KullaniciAdi;
            personel.Pasif = !model.HesapAktif;
            personel.Admin = model.Admin;
            personel.KayitTarihi = model.KayitTarihi ?? personel.KayitTarihi ?? DateTime.Now;

            if (yeniKayit || !string.IsNullOrWhiteSpace(model.Sifre))
            {
                personel.Sifre = model.Sifre;
            }
        }

        private void PersonelPanelAlanlariniUygula(int personelId, PersonelYonetimForm model)
        {
            try
            {
                db.Database.ExecuteSqlCommand(
                    @"UPDATE dbo.Personel
                      SET MailOnaylandi = @p0,
                          GoogleKimlikId = NULLIF(@p1, N''),
                          GirisKaynagi = NULLIF(@p2, N''),
                          MailOnayKodu = CASE WHEN @p0 = 1 THEN NULL ELSE MailOnayKodu END,
                          MailOnayKoduTarihi = CASE WHEN @p0 = 1 THEN NULL ELSE MailOnayKoduTarihi END
                      WHERE PersonelId = @p3",
                    model.MailOnaylandi,
                    model.GoogleKimlikId ?? string.Empty,
                    model.GirisKaynagi ?? string.Empty,
                    personelId);
            }
            catch
            {
            }
        }

        private Personel CalismaAlaniPersonelGetir(int id)
        {
            var calismaAlaniId = AktifCalismaAlaniId();
            if (!calismaAlaniId.HasValue)
            {
                return null;
            }

            return db.Personel.SqlQuery(
                @"SELECT p.*
                  FROM dbo.Personel p
                  INNER JOIN dbo.CalismaAlaniUye cau ON cau.PersonelId = p.PersonelId
                  WHERE p.PersonelId = @p0
                    AND cau.CalismaAlaniId = @p1
                    AND ISNULL(cau.Pasif, 0) = 0",
                id,
                calismaAlaniId.Value).FirstOrDefault();
        }

        private bool PersonelCalismaAlanindaMi(int? id)
        {
            var calismaAlaniId = AktifCalismaAlaniId();
            if (!id.HasValue || !calismaAlaniId.HasValue)
            {
                return false;
            }

            return db.Database.SqlQuery<int>(
                @"SELECT COUNT(1)
                  FROM dbo.CalismaAlaniUye
                  WHERE PersonelId = @p0
                    AND CalismaAlaniId = @p1
                    AND ISNULL(Pasif, 0) = 0",
                id.Value,
                calismaAlaniId.Value).FirstOrDefault() > 0;
        }

        private void PersonelCalismaAlaninaEkle(int personelId)
        {
            var calismaAlaniId = AktifCalismaAlaniId();
            if (!calismaAlaniId.HasValue)
            {
                return;
            }

            db.Database.ExecuteSqlCommand(
                @"IF EXISTS (SELECT 1 FROM dbo.CalismaAlaniUye WHERE CalismaAlaniId = @p0 AND PersonelId = @p1)
                  BEGIN
                      UPDATE dbo.CalismaAlaniUye
                      SET Pasif = 0
                      WHERE CalismaAlaniId = @p0 AND PersonelId = @p1
                  END
                  ELSE
                  BEGIN
                      INSERT INTO dbo.CalismaAlaniUye (CalismaAlaniId, PersonelId, Rol, Pasif, KayitTarihi)
                      VALUES (@p0, @p1, N'Uye', 0, GETDATE())
                  END",
                calismaAlaniId.Value,
                personelId);
        }

        private void UserLookupHazirla()
        {
            ViewBag.KayitTar = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
            ViewBag.Unv = CalismaAlaniKayitlari<Unvan>("Unvan", "UnvanAdi")
                .Select(i => new SelectListItem { Text = i.UnvanAdi, Value = i.UnvanId.ToString() }).ToList();
            ViewBag.Cin = db.Cinsiyet.OrderBy(x => x.CinsiyetAdi).ToList()
                .Select(i => new SelectListItem { Text = i.CinsiyetAdi, Value = i.CinsiyetId.ToString() }).ToList();
            ViewBag.Bol = CalismaAlaniKayitlari<Bolum>("Bolum", "BolumAdi")
                .Select(i => new SelectListItem { Text = i.BolumAdi, Value = i.BolumId.ToString() }).ToList();
            ViewBag.Egi = db.Egitim.OrderBy(x => x.EgitimAdi).ToList()
                .Select(i => new SelectListItem { Text = i.EgitimAdi, Value = i.EgitimId.ToString() }).ToList();
            ViewBag.Blg = CalismaAlaniKayitlari<Bolge>("Bolge", "BolgeAdi")
                .Select(i => new SelectListItem { Text = i.BolgeAdi, Value = i.BolgeId.ToString() }).ToList();
            ViewBag.Seh = CalismaAlaniKayitlari<Sehir>("Sehir", "SehiarAdi")
                .Select(i => new SelectListItem { Text = i.SehiarAdi, Value = i.SehirId.ToString() }).ToList();
            ViewBag.Sub = CalismaAlaniKayitlari<Sube>("Sube", "SubeAdi")
                .Select(i => new SelectListItem { Text = i.SubeAdi, Value = i.SubeId.ToString() }).ToList();
            ViewBag.Dep = CalismaAlaniKayitlari<Departman>("Departman", "DepartmanAdi")
                .Select(i => new SelectListItem { Text = i.DepartmanAdi, Value = i.DepartmanId.ToString() }).ToList();
            ViewBag.Yon = CalismaAlaniKayitlari<Yonetici>("Yonetici", "YoneticiAdi")
                .Select(i => new SelectListItem { Text = i.YoneticiAdi, Value = i.YoneticiId.ToString() }).ToList();
            ViewBag.Yak = db.Yaka.OrderBy(x => x.YakaAdi).ToList()
                .Select(i => new SelectListItem { Text = i.YakaAdi, Value = i.YakaId.ToString() }).ToList();
        }

        private class HizliSecenekTanimi
        {
            public string Tablo { get; set; }
            public string IdKolonu { get; set; }
            public string AdKolonu { get; set; }
            public bool CalismaAlaniKapsamli { get; set; }
        }

        public class AyarSecenekSatiri
        {
            public int Id { get; set; }
            public string Ad { get; set; }
        }

        public class UserImportMevcutKatilimci
        {
            public int UserId { get; set; }
            public string UserAdi { get; set; }
            public string UserTc { get; set; }
            public bool? Pasif { get; set; }
        }

        public class UserImportOnizleme
        {
            public List<Dictionary<string, string>> Satirlar { get; set; } = new();
            public int ExcelSatirSayisi { get; set; }
            public int GecerliKayitSayisi { get; set; }
            public int AtlananSatirSayisi { get; set; }
            public int TekrarEdenTcSayisi { get; set; }
            public int KayitliToplamSayisi { get; set; }
            public int KayitliAktifSayisi { get; set; }
            public int YeniKayitSayisi { get; set; }
            public int GuncellenecekKayitSayisi { get; set; }
            public int ExceldeOlmayanAktifSayisi { get; set; }
            public List<string> ExceldeOlmayanAktifOrnekleri { get; set; } = new();
            public List<string> AtlananSatirOrnekleri { get; set; } = new();
        }

        public class UserImportSonuc
        {
            public int Eklenen { get; set; }
            public int Guncellenen { get; set; }
            public int Atlanan { get; set; }
            public int ResimAdiWebpYapilan { get; set; }
            public int PasifeAlinan { get; set; }
            public int MevcutSecenekEslesen { get; set; }
            public int BenzerSecenekEslesen { get; set; }
            public int YeniSecenekEklenen { get; set; }
            public List<string> AtlananSatirOrnekleri { get; set; } = new();
            public List<string> SecenekUyariOrnekleri { get; set; } = new();
        }

        public class VarsayilanSecenekGrubu
        {
            public string Tip { get; set; }
            public string Baslik { get; set; }
            public List<string> Secenekler { get; set; } = new();
        }

        private const string UserImportOnizlemeSessionKey = "UserImportOnizleme";

        private static string SecenekEslesmeAnahtari(string value)
        {
            var text = (value ?? string.Empty).Trim().ToLower(new CultureInfo("tr-TR"))
                .Replace("ı", "i")
                .Replace("ğ", "g")
                .Replace("ü", "u")
                .Replace("ş", "s")
                .Replace("ö", "o")
                .Replace("ç", "c");

            var normalized = text.Normalize(NormalizationForm.FormD);
            var builder = new StringBuilder();
            foreach (var ch in normalized)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
                {
                    builder.Append(ch);
                }
            }

            return Regex.Replace(builder.ToString().Normalize(NormalizationForm.FormC), "[^a-z0-9]", string.Empty);
        }

        private static int LevenshteinMesafesi(string left, string right)
        {
            left ??= string.Empty;
            right ??= string.Empty;
            if (left.Length == 0)
            {
                return right.Length;
            }
            if (right.Length == 0)
            {
                return left.Length;
            }

            var costs = new int[right.Length + 1];
            for (var j = 0; j <= right.Length; j++)
            {
                costs[j] = j;
            }

            for (var i = 1; i <= left.Length; i++)
            {
                var previous = costs[0];
                costs[0] = i;
                for (var j = 1; j <= right.Length; j++)
                {
                    var temp = costs[j];
                    costs[j] = Math.Min(
                        Math.Min(costs[j] + 1, costs[j - 1] + 1),
                        previous + (left[i - 1] == right[j - 1] ? 0 : 1));
                    previous = temp;
                }
            }

            return costs[right.Length];
        }

        private static bool SecenekBenzerMi(string mevcut, string gelen, out bool yakinEslesme)
        {
            yakinEslesme = false;
            var mevcutAnahtar = SecenekEslesmeAnahtari(mevcut);
            var gelenAnahtar = SecenekEslesmeAnahtari(gelen);

            if (string.IsNullOrWhiteSpace(mevcutAnahtar) || string.IsNullOrWhiteSpace(gelenAnahtar))
            {
                return false;
            }

            if (string.Equals(mevcutAnahtar, gelenAnahtar, StringComparison.Ordinal))
            {
                return true;
            }

            var uzunluk = Math.Max(mevcutAnahtar.Length, gelenAnahtar.Length);
            if (uzunluk < 3)
            {
                return false;
            }

            var mesafe = LevenshteinMesafesi(mevcutAnahtar, gelenAnahtar);
            if (uzunluk <= 4)
            {
                yakinEslesme = mevcutAnahtar[0] == gelenAnahtar[0] && mesafe <= 1;
                return yakinEslesme;
            }

            var izin = uzunluk >= 8 ? 2 : 1;
            yakinEslesme = mesafe <= izin;
            return yakinEslesme;
        }

        private static readonly string[] TurkiyeIlAdlari =
        {
            "Adana", "Adıyaman", "Afyonkarahisar", "Ağrı", "Amasya", "Ankara", "Antalya", "Artvin", "Aydın", "Balıkesir",
            "Bilecik", "Bingöl", "Bitlis", "Bolu", "Burdur", "Bursa", "Çanakkale", "Çankırı", "Çorum", "Denizli",
            "Diyarbakır", "Edirne", "Elazığ", "Erzincan", "Erzurum", "Eskişehir", "Gaziantep", "Giresun", "Gümüşhane", "Hakkari",
            "Hatay", "Isparta", "Mersin", "İstanbul", "İzmir", "Kars", "Kastamonu", "Kayseri", "Kırklareli", "Kırşehir",
            "Kocaeli", "Konya", "Kütahya", "Malatya", "Manisa", "Kahramanmaraş", "Mardin", "Muğla", "Muş", "Nevşehir",
            "Niğde", "Ordu", "Rize", "Sakarya", "Samsun", "Siirt", "Sinop", "Sivas", "Tekirdağ", "Tokat",
            "Trabzon", "Tunceli", "Şanlıurfa", "Uşak", "Van", "Yozgat", "Zonguldak", "Aksaray", "Bayburt", "Karaman",
            "Kırıkkale", "Batman", "Şırnak", "Bartın", "Ardahan", "Iğdır", "Yalova", "Karabük", "Kilis", "Osmaniye",
            "Düzce"
        };

        private static readonly Dictionary<string, string> TurkiyeIlTakmaAdlari = new(StringComparer.Ordinal)
        {
            ["afyon"] = "Afyonkarahisar",
            ["antep"] = "Gaziantep",
            ["gaziantep"] = "Gaziantep",
            ["icel"] = "Mersin",
            ["izmit"] = "Kocaeli",
            ["maras"] = "Kahramanmaraş",
            ["kahramanmaras"] = "Kahramanmaraş",
            ["sanliurfa"] = "Şanlıurfa",
            ["urfa"] = "Şanlıurfa",
            ["skarya"] = "Sakarya"
        };

        private static readonly string[] YayginUnvanAdlari =
        {
            "Stajyer", "Personel", "İşçi", "Operatör", "Teknisyen", "Tekniker",
            "Asistan", "Sorumlu", "Şef", "Takım Lideri", "Ekip Lideri",
            "Uzman Yardımcısı", "Uzman", "Kıdemli Uzman", "Analist", "Danışman",
            "Mühendis", "Kıdemli Mühendis", "Muhasebe Sorumlusu", "Muhasebe Uzmanı",
            "Satış Temsilcisi", "Satış Uzmanı", "Satış Müdürü",
            "Pazarlama Uzmanı", "Pazarlama Müdürü",
            "İnsan Kaynakları Uzmanı", "İnsan Kaynakları Müdürü",
            "Finans Uzmanı", "Finans Müdürü",
            "Müdür Yardımcısı", "Müdür", "Şube Müdürü", "Bölge Müdürü", "Departman Müdürü",
            "Koordinatör", "Yönetmen", "Direktör",
            "Genel Müdür Yardımcısı", "Genel Müdür", "Başkan Yardımcısı", "Başkan",
            "CEO", "CFO", "CTO", "COO"
        };

        private static readonly Dictionary<string, string> YayginUnvanTakmaAdlari = new(StringComparer.Ordinal)
        {
            ["mudur"] = "Müdür",
            ["mdr"] = "Müdür",
            ["sefh"] = "Şef",
            ["sef"] = "Şef",
            ["direktor"] = "Direktör",
            ["gm"] = "Genel Müdür",
            ["genelmudur"] = "Genel Müdür",
            ["genelmuduryardimcisi"] = "Genel Müdür Yardımcısı",
            ["ikuzmani"] = "İnsan Kaynakları Uzmanı",
            ["ikmuduru"] = "İnsan Kaynakları Müdürü",
            ["satisuzmani"] = "Satış Uzmanı",
            ["satismuduru"] = "Satış Müdürü"
        };

        private static readonly Dictionary<string, string[]> VarsayilanSecenekAdlari = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Cinsiyet"] = new[]
            {
                "Erkek", "Kadın", "Belirtmek İstemiyorum", "Diğer"
            },
            ["Egitim"] = new[]
            {
                "İlköğretim", "Ortaokul", "Lise", "Ön Lisans", "Lisans", "Üniversite", "Yüksek Lisans", "Doktora"
            },
            ["Departman"] = new[]
            {
                "İnsan Kaynakları", "Muhasebe", "Finans", "Satış", "Pazarlama", "Operasyon", "Üretim",
                "Kalite", "Lojistik", "Satın Alma", "Müşteri Hizmetleri", "IT", "Bilgi Teknolojileri",
                "Ar-Ge", "Yönetim", "İdari İşler", "Bakım", "Planlama", "Depo"
            },
            ["Bolge"] = new[]
            {
                "Marmara", "Ege", "İç Anadolu", "Akdeniz", "Karadeniz", "Doğu Anadolu", "Güneydoğu Anadolu"
            }
        };

        private static readonly Dictionary<string, Dictionary<string, string>> VarsayilanSecenekTakmaAdlari = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Cinsiyet"] = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["bay"] = "Erkek",
                ["erk"] = "Erkek",
                ["e"] = "Erkek",
                ["bayan"] = "Kadın",
                ["kadin"] = "Kadın",
                ["kadn"] = "Kadın",
                ["k"] = "Kadın",
                ["diger"] = "Diğer"
            },
            ["Egitim"] = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["ilkogretim"] = "İlköğretim",
                ["ilkögretim"] = "İlköğretim",
                ["ortaokul"] = "Ortaokul",
                ["ortaokl"] = "Ortaokul",
                ["onlisans"] = "Ön Lisans",
                ["lisans"] = "Lisans",
                ["universite"] = "Üniversite",
                ["yukseklisans"] = "Yüksek Lisans",
                ["doktora"] = "Doktora"
            },
            ["Departman"] = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["ik"] = "İnsan Kaynakları",
                ["insankaynaklari"] = "İnsan Kaynakları",
                ["muhasebe"] = "Muhasebe",
                ["muhesebe"] = "Muhasebe",
                ["bt"] = "Bilgi Teknolojileri",
                ["bilgiislem"] = "Bilgi Teknolojileri",
                ["arge"] = "Ar-Ge",
                ["satis"] = "Satış",
                ["satinalma"] = "Satın Alma"
            },
            ["Bolge"] = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["ege"] = "Ege",
                ["ego"] = "Ege",
                ["marmara"] = "Marmara",
                ["akdeniz"] = "Akdeniz",
                ["karadeniz"] = "Karadeniz",
                ["icanadolu"] = "İç Anadolu",
                ["doguanadolu"] = "Doğu Anadolu",
                ["guneydoguanadolu"] = "Güneydoğu Anadolu"
            }
        };

        private static bool TurkiyeIlAdiBul(string value, out string ilAdi, out bool yakinEslesme)
        {
            ilAdi = string.Empty;
            yakinEslesme = false;
            var anahtar = SecenekEslesmeAnahtari(value);
            if (string.IsNullOrWhiteSpace(anahtar))
            {
                return false;
            }

            if (TurkiyeIlTakmaAdlari.TryGetValue(anahtar, out var takmaAd))
            {
                ilAdi = takmaAd;
                yakinEslesme = !string.Equals(anahtar, SecenekEslesmeAnahtari(takmaAd), StringComparison.Ordinal);
                return true;
            }

            foreach (var il in TurkiyeIlAdlari)
            {
                if (string.Equals(anahtar, SecenekEslesmeAnahtari(il), StringComparison.Ordinal))
                {
                    ilAdi = il;
                    return true;
                }
            }

            if (anahtar.Length < 5)
            {
                return false;
            }

            string enYakinIl = null;
            var enDusukMesafe = int.MaxValue;
            foreach (var il in TurkiyeIlAdlari)
            {
                var ilAnahtar = SecenekEslesmeAnahtari(il);
                var mesafe = LevenshteinMesafesi(anahtar, ilAnahtar);
                if (mesafe < enDusukMesafe)
                {
                    enDusukMesafe = mesafe;
                    enYakinIl = il;
                }
            }

            var izin = anahtar.Length >= 8 ? 2 : 1;
            if (!string.IsNullOrWhiteSpace(enYakinIl) && enDusukMesafe <= izin)
            {
                ilAdi = enYakinIl;
                yakinEslesme = true;
                return true;
            }

            return false;
        }

        private static bool YayginUnvanAdiBul(string value, out string unvanAdi, out bool yakinEslesme)
        {
            unvanAdi = string.Empty;
            yakinEslesme = false;
            var anahtar = SecenekEslesmeAnahtari(value);
            if (string.IsNullOrWhiteSpace(anahtar))
            {
                return false;
            }

            if (YayginUnvanTakmaAdlari.TryGetValue(anahtar, out var takmaAd))
            {
                unvanAdi = takmaAd;
                yakinEslesme = !string.Equals(anahtar, SecenekEslesmeAnahtari(takmaAd), StringComparison.Ordinal);
                return true;
            }

            foreach (var unvan in YayginUnvanAdlari)
            {
                if (string.Equals(anahtar, SecenekEslesmeAnahtari(unvan), StringComparison.Ordinal))
                {
                    unvanAdi = unvan;
                    return true;
                }
            }

            if (anahtar.Length < 4)
            {
                return false;
            }

            string enYakinUnvan = null;
            var enDusukMesafe = int.MaxValue;
            foreach (var unvan in YayginUnvanAdlari)
            {
                var unvanAnahtar = SecenekEslesmeAnahtari(unvan);
                var mesafe = LevenshteinMesafesi(anahtar, unvanAnahtar);
                if (mesafe < enDusukMesafe)
                {
                    enDusukMesafe = mesafe;
                    enYakinUnvan = unvan;
                }
            }

            var izin = anahtar.Length >= 8 ? 2 : 1;
            if (!string.IsNullOrWhiteSpace(enYakinUnvan) && enDusukMesafe <= izin)
            {
                unvanAdi = enYakinUnvan;
                yakinEslesme = true;
                return true;
            }

            return false;
        }

        private static bool VarsayilanSecenekAdiBul(string tip, string value, out string secenekAdi, out bool yakinEslesme)
        {
            secenekAdi = string.Empty;
            yakinEslesme = false;
            if (!VarsayilanSecenekAdlari.TryGetValue(tip ?? string.Empty, out var liste))
            {
                return false;
            }

            var anahtar = SecenekEslesmeAnahtari(value);
            if (string.IsNullOrWhiteSpace(anahtar))
            {
                return false;
            }

            if (VarsayilanSecenekTakmaAdlari.TryGetValue(tip, out var takmaAdlar)
                && takmaAdlar.TryGetValue(anahtar, out var takmaAd))
            {
                secenekAdi = takmaAd;
                yakinEslesme = !string.Equals(anahtar, SecenekEslesmeAnahtari(takmaAd), StringComparison.Ordinal);
                return true;
            }

            foreach (var item in liste)
            {
                if (string.Equals(anahtar, SecenekEslesmeAnahtari(item), StringComparison.Ordinal))
                {
                    secenekAdi = item;
                    return true;
                }
            }

            if (anahtar.Length < 3)
            {
                return false;
            }

            string enYakinSecenek = null;
            var enDusukMesafe = int.MaxValue;
            foreach (var item in liste)
            {
                var itemAnahtar = SecenekEslesmeAnahtari(item);
                var mesafe = LevenshteinMesafesi(anahtar, itemAnahtar);
                if (mesafe < enDusukMesafe)
                {
                    enDusukMesafe = mesafe;
                    enYakinSecenek = item;
                }
            }

            var izin = anahtar.Length >= 8 ? 2 : 1;
            if (!string.IsNullOrWhiteSpace(enYakinSecenek) && enDusukMesafe <= izin)
            {
                secenekAdi = enYakinSecenek;
                yakinEslesme = true;
                return true;
            }

            return false;
        }

        private static bool SecenekKanonikAdiGuncellensin(string tip)
        {
            return string.Equals(tip, "Sehir", StringComparison.OrdinalIgnoreCase)
                || string.Equals(tip, "Unvan", StringComparison.OrdinalIgnoreCase)
                || string.Equals(tip, "Cinsiyet", StringComparison.OrdinalIgnoreCase)
                || string.Equals(tip, "Egitim", StringComparison.OrdinalIgnoreCase)
                || string.Equals(tip, "Departman", StringComparison.OrdinalIgnoreCase)
                || string.Equals(tip, "Bolge", StringComparison.OrdinalIgnoreCase);
        }

        private static HizliSecenekTanimi HizliSecenekTanimiGetir(string tip)
        {
            return (tip ?? string.Empty).Trim() switch
            {
                "Unvan" => new HizliSecenekTanimi { Tablo = "dbo.Unvan", IdKolonu = "UnvanId", AdKolonu = "UnvanAdi", CalismaAlaniKapsamli = true },
                "Bolum" => new HizliSecenekTanimi { Tablo = "dbo.Bolum", IdKolonu = "BolumId", AdKolonu = "BolumAdi", CalismaAlaniKapsamli = true },
                "Sube" => new HizliSecenekTanimi { Tablo = "dbo.Sube", IdKolonu = "SubeId", AdKolonu = "SubeAdi", CalismaAlaniKapsamli = true },
                "Bolge" => new HizliSecenekTanimi { Tablo = "dbo.Bolge", IdKolonu = "BolgeId", AdKolonu = "BolgeAdi", CalismaAlaniKapsamli = true },
                "Departman" => new HizliSecenekTanimi { Tablo = "dbo.Departman", IdKolonu = "DepartmanId", AdKolonu = "DepartmanAdi", CalismaAlaniKapsamli = true },
                "Yonetici" => new HizliSecenekTanimi { Tablo = "dbo.Yonetici", IdKolonu = "YoneticiId", AdKolonu = "YoneticiAdi", CalismaAlaniKapsamli = true },
                "Sehir" => new HizliSecenekTanimi { Tablo = "dbo.Sehir", IdKolonu = "SehirId", AdKolonu = "SehiarAdi", CalismaAlaniKapsamli = true },
                "Cinsiyet" => new HizliSecenekTanimi { Tablo = "dbo.Cinsiyet", IdKolonu = "CinsiyetId", AdKolonu = "CinsiyetAdi" },
                "Egitim" => new HizliSecenekTanimi { Tablo = "dbo.Egitim", IdKolonu = "EgitimId", AdKolonu = "EgitimAdi" },
                "Yaka" => new HizliSecenekTanimi { Tablo = "dbo.Yaka", IdKolonu = "YakaId", AdKolonu = "YakaAdi" },
                _ => null
            };
        }

        private int HizliSecenekIdAlVeyaOlustur(string tip, string ad)
        {
            return HizliSecenekIdAlVeyaOlustur(tip, ad, out _);
        }

        private int HizliSecenekIdAlVeyaOlustur(string tip, string ad, out string sonuc)
        {
            return HizliSecenekIdAlVeyaOlustur(tip, ad, out sonuc, out _);
        }

        private int HizliSecenekIdAlVeyaOlustur(string tip, string ad, out string sonuc, out string kayitAdi)
        {
            sonuc = string.Empty;
            kayitAdi = string.Empty;
            var tanim = HizliSecenekTanimiGetir(tip);
            if (tanim == null)
            {
                return 0;
            }

            ad = MetniKirp(ad, 80);
            if (string.Equals(tip, "Sehir", StringComparison.OrdinalIgnoreCase))
            {
                if (TurkiyeIlAdiBul(ad, out var ilAdi, out var yakinSehirEslesme))
                {
                    ad = ilAdi;
                    sonuc = yakinSehirEslesme ? "benzer" : string.Empty;
                }
            }
            else if (string.Equals(tip, "Unvan", StringComparison.OrdinalIgnoreCase)
                && YayginUnvanAdiBul(ad, out var unvanAdi, out var yakinUnvanEslesme))
            {
                ad = unvanAdi;
                sonuc = yakinUnvanEslesme ? "benzer" : string.Empty;
            }
            else if (VarsayilanSecenekAdiBul(tip, ad, out var varsayilanSecenekAdi, out var yakinVarsayilanEslesme))
            {
                ad = varsayilanSecenekAdi;
                sonuc = yakinVarsayilanEslesme ? "benzer" : string.Empty;
            }

            kayitAdi = ad;
            if (string.IsNullOrWhiteSpace(ad))
            {
                return 0;
            }

            var calismaAlaniId = tanim.CalismaAlaniKapsamli ? AktifCalismaAlaniId() : null;
            if (tanim.CalismaAlaniKapsamli && !calismaAlaniId.HasValue)
            {
                return 0;
            }

            List<AyarSecenekSatiri> mevcutlar;
            if (tanim.CalismaAlaniKapsamli)
            {
                mevcutlar = db.Database.SqlQuery<AyarSecenekSatiri>(
                    $@"SELECT [{tanim.IdKolonu}] AS Id, [{tanim.AdKolonu}] AS Ad
                       FROM {tanim.Tablo}
                       WHERE CalismaAlaniId = @p0
                       ORDER BY [{tanim.IdKolonu}]",
                    calismaAlaniId.Value).ToList();
            }
            else
            {
                mevcutlar = db.Database.SqlQuery<AyarSecenekSatiri>(
                    $@"SELECT [{tanim.IdKolonu}] AS Id, [{tanim.AdKolonu}] AS Ad
                       FROM {tanim.Tablo}
                       ORDER BY [{tanim.IdKolonu}]").ToList();
            }

            var adAnahtar = SecenekEslesmeAnahtari(ad);
            var tamEslesen = mevcutlar.FirstOrDefault(x =>
                string.Equals(SecenekEslesmeAnahtari(x.Ad), adAnahtar, StringComparison.Ordinal));
            if (tamEslesen != null)
            {
                sonuc = sonuc == "benzer" ? "benzer" : "mevcut";
                if (SecenekKanonikAdiGuncellensin(tip)
                    && !string.Equals((tamEslesen.Ad ?? string.Empty).Trim(), ad, StringComparison.Ordinal))
                {
                    db.Database.ExecuteSqlCommand(
                        $"UPDATE {tanim.Tablo} SET [{tanim.AdKolonu}] = @p0 WHERE [{tanim.IdKolonu}] = @p1",
                        ad,
                        tamEslesen.Id);
                }
                return tamEslesen.Id;
            }

            foreach (var mevcut in mevcutlar)
            {
                if (SecenekBenzerMi(mevcut.Ad, ad, out var yakinEslesme))
                {
                    sonuc = sonuc == "benzer" || yakinEslesme ? "benzer" : "mevcut";
                    if (SecenekKanonikAdiGuncellensin(tip)
                        && !string.Equals((mevcut.Ad ?? string.Empty).Trim(), ad, StringComparison.Ordinal))
                    {
                        db.Database.ExecuteSqlCommand(
                            $"UPDATE {tanim.Tablo} SET [{tanim.AdKolonu}] = @p0 WHERE [{tanim.IdKolonu}] = @p1",
                            ad,
                            mevcut.Id);
                    }
                    return mevcut.Id;
                }
            }

            sonuc = "yeni";
            return tanim.CalismaAlaniKapsamli
                ? db.Database.SqlQuery<int>(
                    $@"INSERT INTO {tanim.Tablo} ([{tanim.AdKolonu}], CalismaAlaniId)
                       VALUES (@p0, @p1);
                       SELECT CAST(SCOPE_IDENTITY() AS int);",
                    ad,
                    calismaAlaniId.Value).First()
                : db.Database.SqlQuery<int>(
                    $@"INSERT INTO {tanim.Tablo} ([{tanim.AdKolonu}])
                       VALUES (@p0);
                       SELECT CAST(SCOPE_IDENTITY() AS int);",
                    ad).First();
        }

        private void TurkiyeSehirleriniHazirla()
        {
            var calismaAlaniId = AktifCalismaAlaniId();
            if (!calismaAlaniId.HasValue || !AyarTablosuCalismaAlaniKolonuVarMi("Sehir"))
            {
                return;
            }

            var sessionKey = $"TurkiyeSehirleriHazirlandi_{calismaAlaniId.Value}";
            if (Session[sessionKey] != null)
            {
                return;
            }

            try
            {
                foreach (var il in TurkiyeIlAdlari)
                {
                    HizliSecenekIdAlVeyaOlustur("Sehir", il);
                }

                Session[sessionKey] = true;
            }
            catch
            {
            }
        }

        private void YayginUnvanlariHazirla()
        {
            var calismaAlaniId = AktifCalismaAlaniId();
            if (!calismaAlaniId.HasValue || !AyarTablosuCalismaAlaniKolonuVarMi("Unvan"))
            {
                return;
            }

            var sessionKey = $"YayginUnvanlarHazirlandi_{calismaAlaniId.Value}";
            if (Session[sessionKey] != null)
            {
                return;
            }

            try
            {
                foreach (var unvan in YayginUnvanAdlari)
                {
                    HizliSecenekIdAlVeyaOlustur("Unvan", unvan);
                }

                Session[sessionKey] = true;
            }
            catch
            {
            }
        }

        private void VarsayilanSecenekleriHazirla(params string[] tipler)
        {
            foreach (var tip in tipler ?? Array.Empty<string>())
            {
                if (!VarsayilanSecenekAdlari.TryGetValue(tip ?? string.Empty, out var liste))
                {
                    continue;
                }

                var tanim = HizliSecenekTanimiGetir(tip);
                if (tanim == null)
                {
                    continue;
                }

                var calismaAlaniId = tanim.CalismaAlaniKapsamli ? AktifCalismaAlaniId() : null;
                if (tanim.CalismaAlaniKapsamli
                    && (!calismaAlaniId.HasValue || !AyarTablosuCalismaAlaniKolonuVarMi(tip)))
                {
                    continue;
                }

                var sessionKey = tanim.CalismaAlaniKapsamli
                    ? $"VarsayilanSecenekHazirlandi_{tip}_{calismaAlaniId.Value}"
                    : $"VarsayilanSecenekHazirlandi_{tip}";
                if (Session[sessionKey] != null)
                {
                    continue;
                }

                try
                {
                    foreach (var item in liste)
                    {
                        HizliSecenekIdAlVeyaOlustur(tip, item);
                    }

                    Session[sessionKey] = true;
                }
                catch
                {
                }
            }
        }

        private static List<VarsayilanSecenekGrubu> VarsayilanSecenekGruplari()
        {
            return new List<VarsayilanSecenekGrubu>
            {
                new VarsayilanSecenekGrubu { Tip = "Unvan", Baslik = "Ünvan", Secenekler = YayginUnvanAdlari.ToList() },
                new VarsayilanSecenekGrubu { Tip = "Cinsiyet", Baslik = "Cinsiyet", Secenekler = VarsayilanSecenekAdlari["Cinsiyet"].ToList() },
                new VarsayilanSecenekGrubu { Tip = "Egitim", Baslik = "Eğitim", Secenekler = VarsayilanSecenekAdlari["Egitim"].ToList() },
                new VarsayilanSecenekGrubu { Tip = "Departman", Baslik = "Departman", Secenekler = VarsayilanSecenekAdlari["Departman"].ToList() },
                new VarsayilanSecenekGrubu { Tip = "Bolge", Baslik = "Bölge", Secenekler = VarsayilanSecenekAdlari["Bolge"].ToList() },
                new VarsayilanSecenekGrubu { Tip = "Sehir", Baslik = "Şehir", Secenekler = TurkiyeIlAdlari.ToList() }
            };
        }

        private void VarsayilanSecenekViewBagHazirla()
        {
            ViewBag.VarsayilanSecenekGruplari = VarsayilanSecenekGruplari();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult HizliSecenekEkle(string tip, string ad)
        {
            if (Session["id"] == null)
            {
                return Json(new { success = false, message = "Oturum suresi doldu. Lutfen tekrar giris yapin." });
            }

            var tanim = HizliSecenekTanimiGetir(tip);
            if (tanim == null)
            {
                return Json(new { success = false, message = "Liste tipi taninamadi." });
            }

            ad = (ad ?? string.Empty).Trim();
            if (ad.Length < 2)
            {
                return Json(new { success = false, message = "En az 2 karakter yazin." });
            }

            if (ad.Length > 80)
            {
                ad = ad.Substring(0, 80);
            }

            var calismaAlaniId = tanim.CalismaAlaniKapsamli ? AktifCalismaAlaniId() : null;
            if (tanim.CalismaAlaniKapsamli && !calismaAlaniId.HasValue)
            {
                return Json(new { success = false, message = "Calisma alani bulunamadi." });
            }

            try
            {
                var id = HizliSecenekIdAlVeyaOlustur(tip, ad, out var sonuc, out var kayitAdi);
                if (id <= 0 && sonuc == "gecersiz" && string.Equals(tip, "Sehir", StringComparison.OrdinalIgnoreCase))
                {
                    return Json(new { success = false, message = "Sehir adi 81 il icinde bulunamadi. Resmi il adini yazin." });
                }

                return Json(new { success = id > 0, id, text = string.IsNullOrWhiteSpace(kayitAdi) ? ad : kayitAdi, kind = sonuc, message = id > 0 ? "" : "Secenek eklenemedi." });
            }
            catch
            {
                return Json(new { success = false, message = "Secenek eklenemedi." });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult VarsayilanSecenekleriEkle(string[] secenekler, string donus = "UserIndex")
        {
            if (Session["id"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }

            var donusAction = string.Equals(donus, "Index", StringComparison.OrdinalIgnoreCase) ? "Index" : "UserIndex";
            var gruplar = VarsayilanSecenekGruplari();
            var izinliSecenekler = gruplar
                .SelectMany(g => g.Secenekler.Select(s => $"{g.Tip}|{s}"))
                .ToHashSet(StringComparer.Ordinal);
            var secilenler = (secenekler ?? Array.Empty<string>())
                .Where(x => !string.IsNullOrWhiteSpace(x) && izinliSecenekler.Contains(x))
                .Distinct(StringComparer.Ordinal)
                .ToList();

            if (!secilenler.Any())
            {
                TempData["UserImportMesaj"] = "Varsayılan liste için en az bir seçenek işaretleyin.";
                return RedirectToAction(donusAction);
            }

            var eklenen = 0;
            var eslesen = 0;
            var atlanan = 0;

            foreach (var secenek in secilenler)
            {
                var parcalar = secenek.Split('|', 2);
                if (parcalar.Length != 2)
                {
                    atlanan++;
                    continue;
                }

                var id = HizliSecenekIdAlVeyaOlustur(parcalar[0], parcalar[1], out var sonuc);
                if (id <= 0)
                {
                    atlanan++;
                }
                else if (sonuc == "yeni")
                {
                    eklenen++;
                }
                else
                {
                    eslesen++;
                }
            }

            TempData["UserImportMesaj"] = $"{eklenen} varsayılan seçenek eklendi, {eslesen} seçenek mevcut kayıtla eşleşti, {atlanan} seçenek atlandı.";
            return RedirectToAction(donusAction);
        }

        private int? ImportSecenekId(string tip, string ad, UserImportSonuc importSonuc = null)
        {
            var id = HizliSecenekIdAlVeyaOlustur(tip, ad, out var sonuc);
            if (importSonuc != null && sonuc == "gecersiz" && !string.IsNullOrWhiteSpace(ad))
            {
                if (importSonuc.SecenekUyariOrnekleri.Count < 8)
                {
                    importSonuc.SecenekUyariOrnekleri.Add($"{tip}: {ad}");
                }
                return null;
            }

            if (importSonuc != null && id > 0)
            {
                if (sonuc == "benzer")
                {
                    importSonuc.BenzerSecenekEslesen++;
                }
                else if (sonuc == "mevcut")
                {
                    importSonuc.MevcutSecenekEslesen++;
                }
                else if (sonuc == "yeni")
                {
                    importSonuc.YeniSecenekEklenen++;
                }
            }

            return id > 0 ? (int?)id : null;
        }

        private static object SqlDegeri(object value)
        {
            return value ?? DBNull.Value;
        }

        private const string KatilimciResimKlasoru = "~/Content/Katilimci/";
        private const string YoneticiResimKlasoru = "~/Content/Yonetici/";
        private const int ResimWebpKalite = 82;
        private const int ResimWebpMaksimumKenar = 768;

        private static string ResimDosyaAdiWebpYap(string dosyaAdi, string varsayilanAd)
        {
            var fileName = Path.GetFileName((dosyaAdi ?? string.Empty).Trim());
            if (string.IsNullOrWhiteSpace(fileName))
            {
                return string.Empty;
            }

            var name = Path.GetFileNameWithoutExtension(fileName);
            name = Regex.Replace(name, @"[^\p{L}\p{Nd}\-_ ]", "_");
            name = Regex.Replace(name, @"\s+", "-").Trim('-', '_');
            if (string.IsNullOrWhiteSpace(name))
            {
                name = varsayilanAd;
            }

            const string extension = ".webp";
            var maxNameLength = Math.Max(1, 50 - extension.Length);
            if (name.Length > maxNameLength)
            {
                name = name.Substring(0, maxNameLength);
            }

            return name + extension;
        }

        private static string KatilimciResimDosyaAdiTemizle(string dosyaAdi)
        {
            return ResimDosyaAdiWebpYap(dosyaAdi, "katilimci");
        }

        private void ResmiWebpOlarakKaydet(IFormFile resimDosyasi, string klasor, string dosyaAdi)
        {
            using var stream = resimDosyasi.OpenReadStream();
            ResmiWebpOlarakKaydet(stream, klasor, dosyaAdi);
        }

        private void ResmiWebpOlarakKaydet(Stream stream, string klasor, string dosyaAdi)
        {
            if (stream.CanSeek)
            {
                stream.Position = 0;
            }

            using var image = SixLabors.ImageSharp.Image.Load(stream);
            if (image.Width > ResimWebpMaksimumKenar || image.Height > ResimWebpMaksimumKenar)
            {
                image.Mutate(x => x.Resize(new ResizeOptions
                {
                    Mode = ResizeMode.Max,
                    Size = new SixLabors.ImageSharp.Size(ResimWebpMaksimumKenar, ResimWebpMaksimumKenar)
                }));
            }

            var hedefPath = MapPath(klasor + dosyaAdi);
            var hedefKlasor = Path.GetDirectoryName(hedefPath);
            if (!string.IsNullOrWhiteSpace(hedefKlasor) && !Directory.Exists(hedefKlasor))
            {
                Directory.CreateDirectory(hedefKlasor);
            }

            image.SaveAsWebp(hedefPath, new WebpEncoder { Quality = ResimWebpKalite });
        }

        private string KatilimciResimBenzersizDosyaAdi(string dosyaAdi)
        {
            var fileName = KatilimciResimDosyaAdiTemizle(dosyaAdi);
            if (string.IsNullOrWhiteSpace(fileName))
            {
                return string.Empty;
            }

            var path = MapPath(KatilimciResimKlasoru + fileName);
            if (!System.IO.File.Exists(path))
            {
                return fileName;
            }

            var extension = Path.GetExtension(fileName);
            var name = Path.GetFileNameWithoutExtension(fileName);
            var suffix = "-" + DateTime.Now.ToString("yyyyMMddHHmmss");
            var maxNameLength = Math.Max(1, 50 - extension.Length - suffix.Length);
            if (name.Length > maxNameLength)
            {
                name = name.Substring(0, maxNameLength);
            }

            return name + suffix + extension;
        }

        private string KatilimciResimKaydet(IFormFile resimDosyasi)
        {
            if (resimDosyasi == null || resimDosyasi.Length <= 0)
            {
                return string.Empty;
            }

            var dosyaAdi = KatilimciResimBenzersizDosyaAdi(resimDosyasi.FileName);
            if (string.IsNullOrWhiteSpace(dosyaAdi))
            {
                throw new InvalidOperationException("Katılımcı resmi jpg, jpeg, png, webp veya gif olmalı.");
            }

            try
            {
                ResmiWebpOlarakKaydet(resimDosyasi, KatilimciResimKlasoru, dosyaAdi);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Katılımcı resmi okunabilir bir görsel olmalı; sistem WebP olarak kaydeder.", ex);
            }

            return dosyaAdi;
        }

        private string KatilimciResimTopluKaydet(IFormFile resimDosyasi)
        {
            if (resimDosyasi == null || resimDosyasi.Length <= 0)
            {
                return string.Empty;
            }

            var dosyaAdi = KatilimciResimDosyaAdiTemizle(resimDosyasi.FileName);
            if (string.IsNullOrWhiteSpace(dosyaAdi))
            {
                return string.Empty;
            }

            try
            {
                ResmiWebpOlarakKaydet(resimDosyasi, KatilimciResimKlasoru, dosyaAdi);
                return dosyaAdi;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string YoneticiResimDosyaAdiTemizle(string dosyaAdi)
        {
            return ResimDosyaAdiWebpYap(dosyaAdi, "yonetici");
        }

        private string YoneticiResimBenzersizDosyaAdi(string dosyaAdi)
        {
            var fileName = YoneticiResimDosyaAdiTemizle(dosyaAdi);
            if (string.IsNullOrWhiteSpace(fileName))
            {
                return string.Empty;
            }

            var path = MapPath(YoneticiResimKlasoru + fileName);
            if (!System.IO.File.Exists(path))
            {
                return fileName;
            }

            var extension = Path.GetExtension(fileName);
            var name = Path.GetFileNameWithoutExtension(fileName);
            var suffix = "-" + DateTime.Now.ToString("yyyyMMddHHmmss");
            var maxNameLength = Math.Max(1, 50 - extension.Length - suffix.Length);
            if (name.Length > maxNameLength)
            {
                name = name.Substring(0, maxNameLength);
            }

            return name + suffix + extension;
        }

        private string YoneticiResimKaydet(IFormFile resimDosyasi)
        {
            if (resimDosyasi == null || resimDosyasi.Length <= 0)
            {
                return string.Empty;
            }

            var dosyaAdi = YoneticiResimBenzersizDosyaAdi(resimDosyasi.FileName);
            if (string.IsNullOrWhiteSpace(dosyaAdi))
            {
                throw new InvalidOperationException("Yönetici resmi jpg, jpeg, png, webp veya gif olmalı.");
            }

            try
            {
                ResmiWebpOlarakKaydet(resimDosyasi, YoneticiResimKlasoru, dosyaAdi);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Yönetici resmi okunabilir bir görsel olmalı; sistem WebP olarak kaydeder.", ex);
            }

            return dosyaAdi;
        }

        private string KirpilmisResimKaydet(string kirpilmisResim, string klasor, string dosyaOnEki)
        {
            if (string.IsNullOrWhiteSpace(kirpilmisResim))
            {
                return string.Empty;
            }

            var virgul = kirpilmisResim.IndexOf(',');
            if (virgul <= 0)
            {
                return string.Empty;
            }

            var meta = kirpilmisResim.Substring(0, virgul).ToLowerInvariant();
            if (!meta.StartsWith("data:image/"))
            {
                return string.Empty;
            }

            var dosyaAdi = dosyaOnEki + "-" + DateTime.Now.ToString("yyyyMMddHHmmssfff") + ".webp";
            var bytes = Convert.FromBase64String(kirpilmisResim.Substring(virgul + 1));
            if (bytes.Length > 4 * 1024 * 1024)
            {
                throw new InvalidOperationException("Kırpılmış resim 4 MB altında olmalı.");
            }

            using var stream = new MemoryStream(bytes);
            ResmiWebpOlarakKaydet(stream, klasor, dosyaAdi);
            return dosyaAdi;
        }

        private bool YoneticiResimAlaniVarMi()
        {
            try
            {
                return db.Database.SqlQuery<int>("SELECT CAST(CASE WHEN COL_LENGTH('dbo.Yonetici', 'YoneticiResim') IS NULL THEN 0 ELSE 1 END AS int)").FirstOrDefault() == 1;
            }
            catch
            {
                return false;
            }
        }

        private string YoneticiResimGetir(int yoneticiId)
        {
            if (!YoneticiResimAlaniVarMi())
            {
                return string.Empty;
            }

            return db.Database
                .SqlQuery<string>("SELECT YoneticiResim FROM dbo.Yonetici WHERE YoneticiId = @p0", yoneticiId)
                .FirstOrDefault() ?? string.Empty;
        }

        private void YoneticiResimleriniYukle(IEnumerable<Yonetici> yoneticiler)
        {
            var liste = (yoneticiler ?? Enumerable.Empty<Yonetici>()).ToList();
            if (!liste.Any() || !YoneticiResimAlaniVarMi())
            {
                return;
            }

            foreach (var yonetici in liste)
            {
                yonetici.YoneticiResim = db.Database
                    .SqlQuery<string>("SELECT YoneticiResim FROM dbo.Yonetici WHERE YoneticiId = @p0", yonetici.YoneticiId)
                    .FirstOrDefault() ?? string.Empty;
            }
        }

        private void YoneticiResimGuncelle(int yoneticiId, string resim)
        {
            if (!YoneticiResimAlaniVarMi())
            {
                throw new InvalidOperationException("YoneticiResim alanı bulunamadı. DatabaseScripts/20260606_YoneticiResim.sql scriptini çalıştırın.");
            }

            db.Database.ExecuteSqlCommand(
                "UPDATE dbo.Yonetici SET YoneticiResim = @p0 WHERE YoneticiId = @p1",
                SqlDegeri(string.IsNullOrWhiteSpace(resim) ? null : resim),
                yoneticiId);
        }

        private static readonly string[] KatilimciImportBasliklari =
        {
            "Katılımcı Adı",
            "TC",
            "Unvan",
            "Cinsiyet",
            "Eğitim",
            "Doğum Tarihi",
            "Bölüm",
            "Şube",
            "Bölge",
            "Departman",
            "Yönetici",
            "Yaka",
            "Şehir",
            "E-posta",
            "Resim Dosya Adı",
            "Telefon",
            "Adres",
            "İşe Giriş Tarihi",
            "Pasif",
            "Kayıt Tarihi"
        };

        private static string MetniKirp(string value, int maxLength)
        {
            var text = (value ?? string.Empty).Trim();
            return text.Length <= maxLength ? text : text.Substring(0, maxLength);
        }

        private static string BaslikAnahtari(string value)
        {
            var text = (value ?? string.Empty).Trim().ToLowerInvariant()
                .Replace("ı", "i")
                .Replace("ğ", "g")
                .Replace("ü", "u")
                .Replace("ş", "s")
                .Replace("ö", "o")
                .Replace("ç", "c");

            return Regex.Replace(text, "[^a-z0-9]", string.Empty);
        }

        private static string Hucre(Dictionary<string, string> row, params string[] keys)
        {
            foreach (var key in keys)
            {
                if (row.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
                {
                    return value.Trim();
                }
            }

            return string.Empty;
        }

        private static string UserImportTcDegeri(Dictionary<string, string> row)
        {
            var value = Hucre(row, "tc", "tckimlikno", "kimlikno", "usertc");
            value = Regex.Replace((value ?? string.Empty).Trim(), @"\s+", string.Empty);
            value = Regex.Replace(value, @"([,.]0+)$", string.Empty);
            return MetniKirp(value, 50);
        }

        private static string UserImportSatirEtiketi(int excelSatirNo, string katilimciAdi, string tc, string neden)
        {
            var ad = string.IsNullOrWhiteSpace(katilimciAdi) ? "adsiz" : katilimciAdi;
            var kimlik = string.IsNullOrWhiteSpace(tc) ? "TC yok" : tc;
            return $"{excelSatirNo}. satir: {ad} ({kimlik}) - {neden}";
        }

        private static HashSet<string> UserImportTumTcSeti(IEnumerable<Dictionary<string, string>> satirlar)
        {
            return (satirlar ?? Enumerable.Empty<Dictionary<string, string>>())
                .Select(UserImportTcDegeri)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        private static bool PasifDegeri(string value)
        {
            var text = BaslikAnahtari(value);
            return text == "1" || text == "true" || text == "evet" || text == "pasif" || text == "e";
        }

        private static DateTime? TarihDegeri(string value)
        {
            value = (value ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            var formats = new[] { "yyyy-MM-dd", "dd.MM.yyyy", "d.M.yyyy", "dd/MM/yyyy", "d/M/yyyy" };
            if (DateTime.TryParseExact(value, formats, new CultureInfo("tr-TR"), DateTimeStyles.None, out var exact))
            {
                return exact;
            }

            if (DateTime.TryParse(value, new CultureInfo("tr-TR"), DateTimeStyles.None, out var parsed))
            {
                return parsed;
            }

            if (double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var serial) && serial > 0)
            {
                try
                {
                    return DateTime.FromOADate(serial);
                }
                catch
                {
                }
            }

            return null;
        }

        private static string ExcelKolonAdi(int index)
        {
            var name = string.Empty;
            while (index > 0)
            {
                index--;
                name = (char)('A' + (index % 26)) + name;
                index /= 26;
            }

            return name;
        }

        private static int ExcelKolonIndex(string cellReference)
        {
            var letters = new string((cellReference ?? string.Empty).TakeWhile(char.IsLetter).ToArray());
            var index = 0;
            foreach (var letter in letters.ToUpperInvariant())
            {
                index = index * 26 + (letter - 'A' + 1);
            }

            return index;
        }

        private static void ZipYaz(ZipArchive archive, string path, string content)
        {
            var entry = archive.CreateEntry(path);
            using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
            writer.Write(content);
        }

        private static byte[] KatilimciSablonuOlustur()
        {
            XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            var satirlar = new[]
            {
                KatilimciImportBasliklari,
                new[]
                {
                    "Ali Can",
                    "34918219872",
                    "Uzman",
                    "Erkek",
                    "Üniversite",
                    "1974-01-01",
                    "Operasyon",
                    "Merkez",
                    "Marmara",
                    "İnsan Kaynakları",
                    "Ayşe Yılmaz",
                    "Beyaz",
                    "İstanbul",
                    "ali.can@ornek.com",
                    "ali-can.webp",
                    "05xx xxx xx xx",
                    "Örnek adres",
                    "2020-01-01",
                    "Hayır",
                    ""
                }
            };

            var sheetRows = satirlar.Select((row, rowIndex) =>
                new XElement(ns + "row",
                    new XAttribute("r", rowIndex + 1),
                    row.Select((value, colIndex) =>
                        new XElement(ns + "c",
                            new XAttribute("r", ExcelKolonAdi(colIndex + 1) + (rowIndex + 1)),
                            new XAttribute("t", "inlineStr"),
                            new XElement(ns + "is", new XElement(ns + "t", value ?? string.Empty))))));

            var worksheet = new XDocument(
                new XElement(ns + "worksheet",
                    new XElement(ns + "sheetData", sheetRows)));

            using var ms = new MemoryStream();
            using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, true))
            {
                ZipYaz(archive, "[Content_Types].xml",
                    @"<?xml version=""1.0"" encoding=""UTF-8""?>
<Types xmlns=""http://schemas.openxmlformats.org/package/2006/content-types"">
  <Default Extension=""rels"" ContentType=""application/vnd.openxmlformats-package.relationships+xml""/>
  <Default Extension=""xml"" ContentType=""application/xml""/>
  <Override PartName=""/xl/workbook.xml"" ContentType=""application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml""/>
  <Override PartName=""/xl/worksheets/sheet1.xml"" ContentType=""application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml""/>
</Types>");
                ZipYaz(archive, "_rels/.rels",
                    @"<?xml version=""1.0"" encoding=""UTF-8""?>
<Relationships xmlns=""http://schemas.openxmlformats.org/package/2006/relationships"">
  <Relationship Id=""rId1"" Type=""http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument"" Target=""xl/workbook.xml""/>
</Relationships>");
                ZipYaz(archive, "xl/workbook.xml",
                    @"<?xml version=""1.0"" encoding=""UTF-8""?>
<workbook xmlns=""http://schemas.openxmlformats.org/spreadsheetml/2006/main"" xmlns:r=""http://schemas.openxmlformats.org/officeDocument/2006/relationships"">
  <sheets>
    <sheet name=""Katilimcilar"" sheetId=""1"" r:id=""rId1""/>
  </sheets>
</workbook>");
                ZipYaz(archive, "xl/_rels/workbook.xml.rels",
                    @"<?xml version=""1.0"" encoding=""UTF-8""?>
<Relationships xmlns=""http://schemas.openxmlformats.org/package/2006/relationships"">
  <Relationship Id=""rId1"" Type=""http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet"" Target=""worksheets/sheet1.xml""/>
</Relationships>");
                ZipYaz(archive, "xl/worksheets/sheet1.xml", worksheet.ToString(SaveOptions.DisableFormatting));
            }

            return ms.ToArray();
        }

        private static List<string> PaylasilanMetinleriOku(ZipArchive archive)
        {
            var entry = archive.GetEntry("xl/sharedStrings.xml");
            if (entry == null)
            {
                return new List<string>();
            }

            XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            using var stream = entry.Open();
            var doc = XDocument.Load(stream);
            return doc.Descendants(ns + "si")
                .Select(si => string.Concat(si.Descendants(ns + "t").Select(t => t.Value)))
                .ToList();
        }

        private static string ExcelHucreDegeri(XElement cell, List<string> sharedStrings)
        {
            XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            var type = cell.Attribute("t")?.Value ?? string.Empty;
            if (type == "inlineStr")
            {
                return string.Concat(cell.Descendants(ns + "t").Select(x => x.Value)).Trim();
            }

            var value = cell.Element(ns + "v")?.Value ?? string.Empty;
            if (type == "s" && int.TryParse(value, out var sharedIndex) && sharedIndex >= 0 && sharedIndex < sharedStrings.Count)
            {
                return sharedStrings[sharedIndex].Trim();
            }

            return value.Trim();
        }

        private static List<Dictionary<string, string>> ExcelSatirlariniOku(Stream stream)
        {
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read, true);
            var sheet = archive.GetEntry("xl/worksheets/sheet1.xml")
                ?? archive.Entries.FirstOrDefault(x => x.FullName.StartsWith("xl/worksheets/sheet", StringComparison.OrdinalIgnoreCase)
                                                        && x.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase));
            if (sheet == null)
            {
                return new List<Dictionary<string, string>>();
            }

            XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            var sharedStrings = PaylasilanMetinleriOku(archive);
            using var sheetStream = sheet.Open();
            var doc = XDocument.Load(sheetStream);
            var headerMap = new Dictionary<int, string>();
            var rows = new List<Dictionary<string, string>>();

            foreach (var row in doc.Descendants(ns + "row"))
            {
                var values = new Dictionary<int, string>();
                var nextIndex = 1;
                foreach (var cell in row.Elements(ns + "c"))
                {
                    var index = ExcelKolonIndex(cell.Attribute("r")?.Value);
                    if (index <= 0)
                    {
                        index = nextIndex;
                    }

                    values[index] = ExcelHucreDegeri(cell, sharedStrings);
                    nextIndex = index + 1;
                }

                if (!values.Values.Any(x => !string.IsNullOrWhiteSpace(x)))
                {
                    continue;
                }

                if (!headerMap.Any())
                {
                    foreach (var item in values)
                    {
                        var key = BaslikAnahtari(item.Value);
                        if (!string.IsNullOrWhiteSpace(key))
                        {
                            headerMap[item.Key] = key;
                        }
                    }

                    continue;
                }

                var mapped = new Dictionary<string, string>();
                foreach (var item in values)
                {
                    if (headerMap.TryGetValue(item.Key, out var key))
                    {
                        mapped[key] = item.Value;
                    }
                }

                if (mapped.Values.Any(x => !string.IsNullOrWhiteSpace(x)))
                {
                    rows.Add(mapped);
                }
            }

            return rows;
        }

        private void PersonelLookupHazirla()
        {
            ViewBag.KayitTar = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
            ViewBag.Unv = CalismaAlaniKayitlari<Unvan>("Unvan", "UnvanAdi")
                .Select(i => new SelectListItem { Text = i.UnvanAdi, Value = i.UnvanId.ToString() }).ToList();
        }

        public ActionResult AiAyar()
        {
            if (Session["id"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }

            if (!PlatformSahibiMi())
            {
                return RedirectToAction("Index");
            }

            var model = AiAyarVarsayilan();
            model.TableReady = AiAyarTablosuVarMi();
            if (model.TableReady)
            {
                var row = db.Database.SqlQuery<AiAyarForm>(
                    @"SELECT TOP 1 AiAyarId, Provider, Endpoint, ChatModel, EmbeddingModel, ApiKey, Aktif, GuncellemeTarihi,
                             CAST(1 AS bit) AS TableReady
                      FROM dbo.AiAyar
                      ORDER BY AiAyarId").FirstOrDefault();

                if (row != null)
                {
                    model = row;
                    model.TableReady = true;
                    model.ApiKeyMasked = MaskeleApiAnahtari(row.ApiKey);
                    model.ApiKey = null;
                }
            }

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult AiAyar(AiAyarForm model)
        {
            if (Session["id"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }

            if (!PlatformSahibiMi())
            {
                return RedirectToAction("Index");
            }

            model ??= AiAyarVarsayilan();
            var tableReady = AiAyarTablosuVarMi();
            model.TableReady = tableReady;

            if (!tableReady)
            {
                ModelState.AddModelError("", "AI ayar tablosu bulunamadi. DatabaseScripts/20260602_AiAyar.sql scriptini calistirin.");
                model.ApiKeyMasked = string.Empty;
                return View(model);
            }

            var existing = db.Database.SqlQuery<AiAyarForm>(
                @"SELECT TOP 1 AiAyarId, Provider, Endpoint, ChatModel, EmbeddingModel, ApiKey, Aktif, GuncellemeTarihi,
                         CAST(1 AS bit) AS TableReady
                  FROM dbo.AiAyar
                  ORDER BY AiAyarId").FirstOrDefault();

            var apiKey = string.IsNullOrWhiteSpace(model.ApiKey)
                ? existing?.ApiKey
                : model.ApiKey.Trim();

            model.Provider = "OpenAI";
            model.Endpoint = NormalizeAiEndpoint(model.Endpoint);
            model.ChatModel = (model.ChatModel ?? string.Empty).Trim();
            model.EmbeddingModel = (model.EmbeddingModel ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(model.Endpoint)
                || !Uri.TryCreate(model.Endpoint, UriKind.Absolute, out var endpointUri)
                || (endpointUri.Scheme != Uri.UriSchemeHttp && endpointUri.Scheme != Uri.UriSchemeHttps))
            {
                ModelState.AddModelError("Endpoint", "Gecerli bir endpoint girin. Ornek: https://api.openai.com/v1");
            }

            if (string.IsNullOrWhiteSpace(model.ChatModel))
            {
                ModelState.AddModelError("ChatModel", "Soru uretimi icin model zorunlu.");
            }

            if (string.IsNullOrWhiteSpace(model.EmbeddingModel))
            {
                ModelState.AddModelError("EmbeddingModel", "Embedding modeli zorunlu.");
            }

            if (string.IsNullOrWhiteSpace(apiKey) || apiKey.Contains("BURAYA_OPENAI_API_KEY", StringComparison.OrdinalIgnoreCase))
            {
                ModelState.AddModelError("ApiKey", "OpenAI API anahtari zorunlu.");
            }

            if (!ModelState.IsValid)
            {
                model.ApiKeyMasked = MaskeleApiAnahtari(existing?.ApiKey);
                model.ApiKey = null;
                return View(model);
            }

            if (existing == null)
            {
                db.Database.ExecuteSqlCommand(
                    @"INSERT INTO dbo.AiAyar (Provider, Endpoint, ChatModel, EmbeddingModel, ApiKey, Aktif)
                      VALUES (@p0, @p1, @p2, @p3, @p4, @p5)",
                    model.Provider,
                    model.Endpoint,
                    model.ChatModel,
                    model.EmbeddingModel,
                    apiKey,
                    model.Aktif);
            }
            else
            {
                db.Database.ExecuteSqlCommand(
                    @"UPDATE dbo.AiAyar
                      SET Provider = @p0,
                          Endpoint = @p1,
                          ChatModel = @p2,
                          EmbeddingModel = @p3,
                          ApiKey = @p4,
                          Aktif = @p5,
                          GuncellemeTarihi = SYSDATETIME()
                      WHERE AiAyarId = @p6",
                    model.Provider,
                    model.Endpoint,
                    model.ChatModel,
                    model.EmbeddingModel,
                    apiKey,
                    model.Aktif,
                    existing.AiAyarId);
            }

            TempData["AiAyarMesaj"] = "Platform yapay zeka ayarlari kaydedildi.";
            return RedirectToAction("AiAyar");
        }

        private bool AiAyarTablosuVarMi()
        {
            try
            {
                return db.Database.SqlQuery<int>(
                    "SELECT CASE WHEN OBJECT_ID(N'dbo.AiAyar', N'U') IS NOT NULL THEN 1 ELSE 0 END")
                    .FirstOrDefault() == 1;
            }
            catch
            {
                return false;
            }
        }

        private static AiAyarForm AiAyarVarsayilan()
        {
            return new AiAyarForm
            {
                Provider = "OpenAI",
                Endpoint = "https://api.openai.com/v1",
                ChatModel = "gpt-4o-mini",
                EmbeddingModel = "text-embedding-3-small",
                Aktif = true
            };
        }

        private static string NormalizeAiEndpoint(string endpoint)
        {
            return (endpoint ?? "https://api.openai.com/v1").Trim().TrimEnd('/');
        }

        private static string MaskeleApiAnahtari(string apiKey)
        {
            if (string.IsNullOrWhiteSpace(apiKey) || apiKey.Contains("BURAYA_OPENAI_API_KEY", StringComparison.OrdinalIgnoreCase))
            {
                return string.Empty;
            }

            var value = apiKey.Trim();
            if (value.Length <= 10)
            {
                return new string('*', value.Length);
            }

            return value.Substring(0, 7) + "..." + value.Substring(value.Length - 4);
        }
        public ActionResult UnvanIndex()
        {
            if (Session["id"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }
            return View(CalismaAlaniKayitlari<Unvan>("Unvan", "UnvanAdi"));
        }
        public ActionResult UnvanCreate()
        {
            if (Session["id"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }
            return View();
        }

        [ValidateAntiForgeryToken()]
        [HttpPost]
        public ActionResult UnvanCreate(Unvan dgskn)
        {
            if (Session["id"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }

            try
            {
                if (YayginUnvanAdiBul(dgskn.UnvanAdi, out var unvanAdi, out _))
                {
                    dgskn.UnvanAdi = unvanAdi;
                }

                db.Unvan.Add(dgskn);
                db.SaveChanges();
                CalismaAlaniKaydinaBagla("Unvan", "UnvanId", dgskn.UnvanId);
                return RedirectToAction("UnvanIndex");

            }
            catch
            {
                return View();
            }
        }
        public ActionResult UnvanEdit(int id)
        {
            if (Session["id"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }

            var kayit = CalismaAlaniKaydiGetir<Unvan>("Unvan", "UnvanId", id);
            if (kayit == null)
            {
                return RedirectToAction("Hata1", "Ayar", null);
            }

            return View(kayit);
        }

        [ValidateAntiForgeryToken()]
        [HttpPost]
        public ActionResult UnvanEdit(Unvan dgskn)
        {
            if (Session["id"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }

            try
            {
                if (YayginUnvanAdiBul(dgskn.UnvanAdi, out var unvanAdi, out _))
                {
                    dgskn.UnvanAdi = unvanAdi;
                }

                if (!CalismaAlaniKaydiVarMi("Unvan", "UnvanId", dgskn.UnvanId))
                {
                    return RedirectToAction("Hata1", "Ayar", null);
                }

                {
                    db.Entry(dgskn).State = EntityState.Modified;
                    db.SaveChanges();
                }
                return RedirectToAction("UnvanIndex");

            }
            catch
            {
                return View();
            }
        }
        public ActionResult UnvanDelete(int id)
        {
            if (Session["id"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }
            var kayit = CalismaAlaniKaydiGetir<Unvan>("Unvan", "UnvanId", id);
            if (kayit == null)
            {
                return RedirectToAction("Hata1", "Ayar", null);
            }

            return View(kayit);
        }
        [ValidateAntiForgeryToken()]
        [HttpPost]
        public ActionResult UnvanDelete(int? id)
        {
            if (Session["id"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }

            if (ModelState.IsValid)
            {
                //envanter kaydı mevcutsa envanter tipi silinmez
                if (!CalismaAlaniKaydiVarMi("Unvan", "UnvanId", id))
                {
                    return RedirectToAction("Hata1", "Ayar", null);
                }
                if (PersonelReferansVarMi("Unvani", id))
                {
                    return RedirectToAction("Hata1", "Ayar", null);
                }
                if (CalismaAlaniReferansVarMi("User", "UserUnvan", id) || CalismaAlaniReferansVarMi("Anket", "UnvanId", id))
                {
                    return RedirectToAction("Hata1", "Ayar", null);
                }
            }

            try
            {
                Unvan unv = CalismaAlaniKaydiGetir<Unvan>("Unvan", "UnvanId", id.Value);
                db.Unvan.Remove(unv);
                db.SaveChanges();
                return RedirectToAction("UnvanIndex");

            }
            catch
            {
                return View();
            }
        }
        public ActionResult DepartmanIndex()
        {
            if (Session["id"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }
            return View(CalismaAlaniKayitlari<Departman>("Departman", "DepartmanAdi"));
        }
        public ActionResult DepartmanCreate()
        {
            if (Session["id"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }
            return View();
        }

        [ValidateAntiForgeryToken()]
        [HttpPost]
        public ActionResult DepartmanCreate(Departman dgskn)
        {
            if (Session["id"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }

            try
            {
                if (VarsayilanSecenekAdiBul("Departman", dgskn.DepartmanAdi, out var departmanAdi, out _))
                {
                    dgskn.DepartmanAdi = departmanAdi;
                }

                db.Departman.Add(dgskn);
                db.SaveChanges();
                CalismaAlaniKaydinaBagla("Departman", "DepartmanId", dgskn.DepartmanId);
                return RedirectToAction("DepartmanIndex");

            }
            catch
            {
                return View();
            }
        }
        public ActionResult DepartmanEdit(int id)
        {
            if (Session["id"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }

            var kayit = CalismaAlaniKaydiGetir<Departman>("Departman", "DepartmanId", id);
            if (kayit == null)
            {
                return RedirectToAction("Hata1", "Ayar", null);
            }

            return View(kayit);
        }

        [ValidateAntiForgeryToken()]
        [HttpPost]
        public ActionResult DepartmanEdit(Departman dgskn)
        {
            if (Session["id"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }

            try
            {
                if (VarsayilanSecenekAdiBul("Departman", dgskn.DepartmanAdi, out var departmanAdi, out _))
                {
                    dgskn.DepartmanAdi = departmanAdi;
                }

                if (!CalismaAlaniKaydiVarMi("Departman", "DepartmanId", dgskn.DepartmanId))
                {
                    return RedirectToAction("Hata1", "Ayar", null);
                }

                {
                    db.Entry(dgskn).State = EntityState.Modified;
                    db.SaveChanges();
                }
                return RedirectToAction("DepartmanIndex");

            }
            catch
            {
                return View();
            }
        }
        public ActionResult DepartmanDelete(int id)
        {
            if (Session["id"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }
            var kayit = CalismaAlaniKaydiGetir<Departman>("Departman", "DepartmanId", id);
            if (kayit == null)
            {
                return RedirectToAction("Hata1", "Ayar", null);
            }

            return View(kayit);
        }
        [ValidateAntiForgeryToken()]
        [HttpPost]
        public ActionResult DepartmanDelete(int? id)
        {
            if (Session["id"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }

            if (ModelState.IsValid)
            {
                if (!CalismaAlaniKaydiVarMi("Departman", "DepartmanId", id))
                {
                    return RedirectToAction("Hata1", "Ayar", null);
                }

                if (CalismaAlaniReferansVarMi("User", "UserDepartman", id) || CalismaAlaniReferansVarMi("Anket", "DepartmanId", id))
                {
                    return RedirectToAction("Hata1", "Ayar", null);
                }
            }

            try
            {
                Departman unv = CalismaAlaniKaydiGetir<Departman>("Departman", "DepartmanId", id.Value);
                db.Departman.Remove(unv);
                db.SaveChanges();
                return RedirectToAction("DepartmanIndex");

            }
            catch
            {
                return View();
            }
        }

        public ActionResult BolgeIndex()
        {
            if (Session["id"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }
            return View(CalismaAlaniKayitlari<Bolge>("Bolge", "BolgeAdi"));
        }
        public ActionResult BolgeCreate()
        {
            if (Session["id"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }
            return View();
        }

        [ValidateAntiForgeryToken()]
        [HttpPost]
        public ActionResult BolgeCreate(Bolge dgskn)
        {
            if (Session["id"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }

            try
            {
                if (VarsayilanSecenekAdiBul("Bolge", dgskn.BolgeAdi, out var bolgeAdi, out _))
                {
                    dgskn.BolgeAdi = bolgeAdi;
                }

                db.Bolge.Add(dgskn);
                db.SaveChanges();
                CalismaAlaniKaydinaBagla("Bolge", "BolgeId", dgskn.BolgeId);
                return RedirectToAction("BolgeIndex");

            }
            catch
            {
                return View();
            }
        }
        public ActionResult BolgeEdit(int id)
        {
            if (Session["id"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }

            var kayit = CalismaAlaniKaydiGetir<Bolge>("Bolge", "BolgeId", id);
            if (kayit == null)
            {
                return RedirectToAction("Hata1", "Ayar", null);
            }

            return View(kayit);
        }

        [ValidateAntiForgeryToken()]
        [HttpPost]
        public ActionResult BolgeEdit(Bolge dgskn)
        {
            if (Session["id"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }

            try
            {
                if (VarsayilanSecenekAdiBul("Bolge", dgskn.BolgeAdi, out var bolgeAdi, out _))
                {
                    dgskn.BolgeAdi = bolgeAdi;
                }

                if (!CalismaAlaniKaydiVarMi("Bolge", "BolgeId", dgskn.BolgeId))
                {
                    return RedirectToAction("Hata1", "Ayar", null);
                }

                {
                    db.Entry(dgskn).State = EntityState.Modified;
                    db.SaveChanges();
                }
                return RedirectToAction("BolgeIndex");

            }
            catch
            {
                return View();
            }
        }
        public ActionResult BolgeDelete(int id)
        {
            if (Session["id"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }
            var kayit = CalismaAlaniKaydiGetir<Bolge>("Bolge", "BolgeId", id);
            if (kayit == null)
            {
                return RedirectToAction("Hata1", "Ayar", null);
            }

            return View(kayit);
        }
        [ValidateAntiForgeryToken()]
        [HttpPost]
        public ActionResult BolgeDelete(int? id)
        {
            if (Session["id"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }

            if (ModelState.IsValid)
            {
                if (!CalismaAlaniKaydiVarMi("Bolge", "BolgeId", id))
                {
                    return RedirectToAction("Hata1", "Ayar", null);
                }

                if (CalismaAlaniReferansVarMi("User", "UserBolge", id))
                {
                    return RedirectToAction("Hata1", "Ayar", null);
                }
            }

            try
            {
                Bolge unv = CalismaAlaniKaydiGetir<Bolge>("Bolge", "BolgeId", id.Value);
                db.Bolge.Remove(unv);
                db.SaveChanges();
                return RedirectToAction("BolgeIndex");

            }
            catch
            {
                return View();
            }
        }


        public ActionResult SehirIndex()
        {
            if (Session["id"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }
            return View(CalismaAlaniKayitlari<Sehir>("Sehir", "SehiarAdi"));
        }
        public ActionResult SehirCreate()
        {
            if (Session["id"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }
            return View();
        }

        [ValidateAntiForgeryToken()]
        [HttpPost]
        public ActionResult SehirCreate(Sehir dgskn)
        {
            if (Session["id"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }

            try
            {
                if (TurkiyeIlAdiBul(dgskn.SehiarAdi, out var ilAdi, out _))
                {
                    dgskn.SehiarAdi = ilAdi;
                }

                db.Sehir.Add(dgskn);
                db.SaveChanges();
                CalismaAlaniKaydinaBagla("Sehir", "SehirId", dgskn.SehirId);
                return RedirectToAction("SehirIndex");

            }
            catch
            {
                return View();
            }
        }
        public ActionResult SehirEdit(int id)
        {
            if (Session["id"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }

            var kayit = CalismaAlaniKaydiGetir<Sehir>("Sehir", "SehirId", id);
            if (kayit == null)
            {
                return RedirectToAction("Hata1", "Ayar", null);
            }

            return View(kayit);
        }

        [ValidateAntiForgeryToken()]
        [HttpPost]
        public ActionResult SehirEdit(Sehir dgskn)
        {
            if (Session["id"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }

            try
            {
                if (TurkiyeIlAdiBul(dgskn.SehiarAdi, out var ilAdi, out _))
                {
                    dgskn.SehiarAdi = ilAdi;
                }

                if (!CalismaAlaniKaydiVarMi("Sehir", "SehirId", dgskn.SehirId))
                {
                    return RedirectToAction("Hata1", "Ayar", null);
                }

                {
                    db.Entry(dgskn).State = EntityState.Modified;
                    db.SaveChanges();
                }
                return RedirectToAction("SehirIndex");

            }
            catch
            {
                return View();
            }
        }
        public ActionResult SehirDelete(int id)
        {
            if (Session["id"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }
            var kayit = CalismaAlaniKaydiGetir<Sehir>("Sehir", "SehirId", id);
            if (kayit == null)
            {
                return RedirectToAction("Hata1", "Ayar", null);
            }

            return View(kayit);
        }
        [ValidateAntiForgeryToken()]
        [HttpPost]
        public ActionResult SehirDelete(int? id)
        {
            if (Session["id"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }

            if (ModelState.IsValid)
            {
                //envanter kaydı mevcutsa envanter tipi silinmez
                if (!CalismaAlaniKaydiVarMi("Sehir", "SehirId", id))
                {
                    return RedirectToAction("Hata1", "Ayar", null);
                }

                if (CalismaAlaniReferansVarMi("User", "UserSehir", id) || CalismaAlaniReferansVarMi("Anket", "SehirId", id))
                {
                    return RedirectToAction("Hata1", "Ayar", null);
                }
            }

            try
            {
                Sehir unv = CalismaAlaniKaydiGetir<Sehir>("Sehir", "SehirId", id.Value);
                db.Sehir.Remove(unv);
                db.SaveChanges();
                return RedirectToAction("SehirIndex");

            }
            catch
            {
                return View();
            }
        }

        public ActionResult SubeIndex()
        {
            if (Session["id"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }
            return View(CalismaAlaniKayitlari<Sube>("Sube", "SubeAdi"));
        }
        public ActionResult SubeCreate()
        {
            if (Session["id"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }
            return View();
        }

        [ValidateAntiForgeryToken()]
        [HttpPost]
        public ActionResult SubeCreate(Sube dgskn)
        {
            if (Session["id"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }

            try
            {
                db.Sube.Add(dgskn);
                db.SaveChanges();
                CalismaAlaniKaydinaBagla("Sube", "SubeId", dgskn.SubeId);
                return RedirectToAction("SubeIndex");

            }
            catch
            {
                return View();
            }
        }
        public ActionResult SubeEdit(int id)
        {
            if (Session["id"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }

            var kayit = CalismaAlaniKaydiGetir<Sube>("Sube", "SubeId", id);
            if (kayit == null)
            {
                return RedirectToAction("Hata1", "Ayar", null);
            }

            return View(kayit);
        }

        [ValidateAntiForgeryToken()]
        [HttpPost]
        public ActionResult SubeEdit(Sube dgskn)
        {
            if (Session["id"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }

            try
            {
                if (!CalismaAlaniKaydiVarMi("Sube", "SubeId", dgskn.SubeId))
                {
                    return RedirectToAction("Hata1", "Ayar", null);
                }

                {
                    db.Entry(dgskn).State = EntityState.Modified;
                    db.SaveChanges();
                }
                return RedirectToAction("SubeIndex");

            }
            catch
            {
                return View();
            }
        }
        public ActionResult SubeDelete(int id)
        {
            if (Session["id"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }
            var kayit = CalismaAlaniKaydiGetir<Sube>("Sube", "SubeId", id);
            if (kayit == null)
            {
                return RedirectToAction("Hata1", "Ayar", null);
            }

            return View(kayit);
        }
        [ValidateAntiForgeryToken()]
        [HttpPost]
        public ActionResult SubeDelete(int? id)
        {
            if (Session["id"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }

            if (ModelState.IsValid)
            {
                //envanter kaydı mevcutsa envanter tipi silinmez
                if (!CalismaAlaniKaydiVarMi("Sube", "SubeId", id))
                {
                    return RedirectToAction("Hata1", "Ayar", null);
                }

                if (CalismaAlaniReferansVarMi("User", "UserSube", id) || CalismaAlaniReferansVarMi("Anket", "SubeId", id))
                {
                    return RedirectToAction("Hata1", "Ayar", null);
                }
            }

            try
            {
                Sube unv = CalismaAlaniKaydiGetir<Sube>("Sube", "SubeId", id.Value);
                db.Sube.Remove(unv);
                db.SaveChanges();
                return RedirectToAction("SubeIndex");

            }
            catch
            {
                return View();
            }
        }

        public ActionResult BolumIndex()
        {
            if (Session["id"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }
            return View(CalismaAlaniKayitlari<Bolum>("Bolum", "BolumAdi"));
        }
        public ActionResult BolumCreate()
        {
            if (Session["id"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }
            return View();
        }

        [ValidateAntiForgeryToken()]
        [HttpPost]
        public ActionResult BolumCreate(Bolum dgskn)
        {
            if (Session["id"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }

            try
            {
                db.Bolum.Add(dgskn);
                db.SaveChanges();
                CalismaAlaniKaydinaBagla("Bolum", "BolumId", dgskn.BolumId);
                return RedirectToAction("BolumIndex");

            }
            catch
            {
                return View();
            }
        }
        public ActionResult BolumEdit(int id)
        {
            if (Session["id"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }

            var kayit = CalismaAlaniKaydiGetir<Bolum>("Bolum", "BolumId", id);
            if (kayit == null)
            {
                return RedirectToAction("Hata1", "Ayar", null);
            }

            return View(kayit);
        }

        [ValidateAntiForgeryToken()]
        [HttpPost]
        public ActionResult BolumEdit(Bolum dgskn)
        {
            if (Session["id"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }

            try
            {
                if (!CalismaAlaniKaydiVarMi("Bolum", "BolumId", dgskn.BolumId))
                {
                    return RedirectToAction("Hata1", "Ayar", null);
                }

                {
                    db.Entry(dgskn).State = EntityState.Modified;
                    db.SaveChanges();
                }
                return RedirectToAction("BolumIndex");

            }
            catch
            {
                return View();
            }
        }
        public ActionResult BolumDelete(int id)
        {
            if (Session["id"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }
            var kayit = CalismaAlaniKaydiGetir<Bolum>("Bolum", "BolumId", id);
            if (kayit == null)
            {
                return RedirectToAction("Hata1", "Ayar", null);
            }

            return View(kayit);
        }
        [ValidateAntiForgeryToken()]
        [HttpPost]
        public ActionResult BolumDelete(int? id)
        {
            if (Session["id"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }

            if (ModelState.IsValid)
            {
                //envanter kaydı mevcutsa envanter tipi silinmez
                if (!CalismaAlaniKaydiVarMi("Bolum", "BolumId", id))
                {
                    return RedirectToAction("Hata1", "Ayar", null);
                }

                if (CalismaAlaniReferansVarMi("User", "UserBolumu", id))
                {
                    return RedirectToAction("Hata1", "Ayar", null);
                }
            }

            try
            {
                Bolum unv = CalismaAlaniKaydiGetir<Bolum>("Bolum", "BolumId", id.Value);
                db.Bolum.Remove(unv);
                db.SaveChanges();
                return RedirectToAction("BolumIndex");

            }
            catch
            {
                return View();
            }
        }

        public ActionResult YoneticiIndex()
        {
            if (Session["id"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }

            var yoneticiler = CalismaAlaniKayitlari<Yonetici>("Yonetici", "YoneticiAdi").ToList();
            YoneticiResimleriniYukle(yoneticiler);
            return View(yoneticiler);
        }
        public ActionResult YoneticiCreate()
        {
            if (Session["id"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }
            return View();
        }

        [ValidateAntiForgeryToken()]
        [HttpPost]
        public ActionResult YoneticiCreate(Yonetici dgskn, IFormFile resimDosyasi, string kirpilmisResim)
        {
            if (Session["id"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }

            try
            {
                if (((resimDosyasi != null && resimDosyasi.Length > 0) || !string.IsNullOrWhiteSpace(kirpilmisResim)) && !YoneticiResimAlaniVarMi())
                {
                    ModelState.AddModelError("", "YoneticiResim alanı bulunamadı. DatabaseScripts/20260606_YoneticiResim.sql scriptini çalıştırın.");
                    return View(dgskn);
                }

                var yuklenenResim = KirpilmisResimKaydet(kirpilmisResim, YoneticiResimKlasoru, "yonetici");
                if (string.IsNullOrWhiteSpace(yuklenenResim))
                {
                    yuklenenResim = YoneticiResimKaydet(resimDosyasi);
                }
                if (!string.IsNullOrWhiteSpace(yuklenenResim))
                {
                    dgskn.YoneticiResim = yuklenenResim;
                }

                db.Yonetici.Add(dgskn);
                db.SaveChanges();
                CalismaAlaniKaydinaBagla("Yonetici", "YoneticiId", dgskn.YoneticiId);

                if (!string.IsNullOrWhiteSpace(dgskn.YoneticiResim))
                {
                    YoneticiResimGuncelle(dgskn.YoneticiId, dgskn.YoneticiResim);
                }

                return RedirectToAction("YoneticiIndex");

            }
            catch (Exception ex)
            {
                ModelState.AddModelError("", ex.Message);
                return View(dgskn);
            }
        }
        public ActionResult YoneticiEdit(int id)
        {
            if (Session["id"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }

            var kayit = CalismaAlaniKaydiGetir<Yonetici>("Yonetici", "YoneticiId", id);
            if (kayit == null)
            {
                return RedirectToAction("Hata1", "Ayar", null);
            }

            kayit.YoneticiResim = YoneticiResimGetir(id);
            return View(kayit);
        }

        [ValidateAntiForgeryToken()]
        [HttpPost]
        public ActionResult YoneticiEdit(Yonetici dgskn, IFormFile resimDosyasi, bool resimKaldir = false, string kirpilmisResim = null)
        {
            if (Session["id"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }

            try
            {
                if (!CalismaAlaniKaydiVarMi("Yonetici", "YoneticiId", dgskn.YoneticiId))
                {
                    return RedirectToAction("Hata1", "Ayar", null);
                }

                var resimDegisecek = resimKaldir || (resimDosyasi != null && resimDosyasi.Length > 0) || !string.IsNullOrWhiteSpace(kirpilmisResim);
                if (resimDegisecek && !YoneticiResimAlaniVarMi())
                {
                    ModelState.AddModelError("", "YoneticiResim alanı bulunamadı. DatabaseScripts/20260606_YoneticiResim.sql scriptini çalıştırın.");
                    return View(dgskn);
                }

                if (resimKaldir)
                {
                    dgskn.YoneticiResim = null;
                }
                else
                {
                    var yuklenenResim = KirpilmisResimKaydet(kirpilmisResim, YoneticiResimKlasoru, "yonetici");
                    if (string.IsNullOrWhiteSpace(yuklenenResim))
                    {
                        yuklenenResim = YoneticiResimKaydet(resimDosyasi);
                    }

                    if (!string.IsNullOrWhiteSpace(yuklenenResim))
                    {
                        dgskn.YoneticiResim = yuklenenResim;
                    }
                }

                {
                    db.Entry(dgskn).State = EntityState.Modified;
                    db.SaveChanges();
                }

                if (resimDegisecek)
                {
                    YoneticiResimGuncelle(dgskn.YoneticiId, dgskn.YoneticiResim);
                }

                return RedirectToAction("YoneticiIndex");

            }
            catch (Exception ex)
            {
                ModelState.AddModelError("", ex.Message);
                return View(dgskn);
            }
        }
        public ActionResult YoneticiDelete(int id)
        {
            if (Session["id"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }
            var kayit = CalismaAlaniKaydiGetir<Yonetici>("Yonetici", "YoneticiId", id);
            if (kayit == null)
            {
                return RedirectToAction("Hata1", "Ayar", null);
            }

            return View(kayit);
        }
        [ValidateAntiForgeryToken()]
        [HttpPost]
        public ActionResult YoneticiDelete(int? id)
        {
            if (Session["id"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }

            if (ModelState.IsValid)
            {
                //envanter kaydı mevcutsa envanter tipi silinmez
                if (!CalismaAlaniKaydiVarMi("Yonetici", "YoneticiId", id))
                {
                    return RedirectToAction("Hata1", "Ayar", null);
                }

                if (CalismaAlaniReferansVarMi("User", "UserYoneticisi", id))
                {
                    return RedirectToAction("Hata1", "Ayar", null);
                }
            }

            try
            {
                Yonetici unv = CalismaAlaniKaydiGetir<Yonetici>("Yonetici", "YoneticiId", id.Value);
                db.Yonetici.Remove(unv);
                db.SaveChanges();
                return RedirectToAction("YoneticiIndex");

            }
            catch
            {
                return View();
            }
        }

        private List<UserImportMevcutKatilimci> UserImportKayitliKatilimcilar(int calismaAlaniId)
        {
            return db.Database.SqlQuery<UserImportMevcutKatilimci>(
                @"SELECT UserId, UserAdi, UserTc, Pasif
                  FROM dbo.[User]
                  WHERE CalismaAlaniId = @p0
                    AND (UserAdres IS NULL OR CONVERT(varchar(max), UserAdres) NOT LIKE @p1)",
                calismaAlaniId,
                "BilgiFormu:%").ToList();
        }

        private static HashSet<string> UserImportGecerliTcSeti(
            IEnumerable<Dictionary<string, string>> satirlar,
            out int gecerli,
            out int atlanan,
            out int tekrarEden,
            out List<string> atlananSatirOrnekleri)
        {
            var tcSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            gecerli = 0;
            atlanan = 0;
            tekrarEden = 0;
            atlananSatirOrnekleri = new List<string>();

            var excelSatirNo = 1;
            foreach (var satir in satirlar ?? Enumerable.Empty<Dictionary<string, string>>())
            {
                excelSatirNo++;
                var katilimciAdi = MetniKirp(Hucre(satir, "katilimciadi", "adsoyad", "ad"), 50);
                var tc = UserImportTcDegeri(satir);
                if (string.IsNullOrWhiteSpace(katilimciAdi))
                {
                    atlanan++;
                    if (atlananSatirOrnekleri.Count < 8)
                    {
                        atlananSatirOrnekleri.Add(UserImportSatirEtiketi(excelSatirNo, katilimciAdi, tc, "katilimci adi bos"));
                    }
                    continue;
                }

                if (string.IsNullOrWhiteSpace(tc))
                {
                    atlanan++;
                    if (atlananSatirOrnekleri.Count < 8)
                    {
                        atlananSatirOrnekleri.Add(UserImportSatirEtiketi(excelSatirNo, katilimciAdi, tc, "TC bos"));
                    }
                    continue;
                }

                if (!tcSet.Add(tc))
                {
                    tekrarEden++;
                    atlanan++;
                    if (atlananSatirOrnekleri.Count < 8)
                    {
                        atlananSatirOrnekleri.Add(UserImportSatirEtiketi(excelSatirNo, katilimciAdi, tc, "Excel icinde tekrar TC"));
                    }
                    continue;
                }

                gecerli++;
            }

            return tcSet;
        }

        private UserImportOnizleme UserImportOnizlemeOlustur(List<Dictionary<string, string>> satirlar, int calismaAlaniId)
        {
            satirlar ??= new List<Dictionary<string, string>>();
            var tcSet = UserImportGecerliTcSeti(satirlar, out var gecerli, out var atlanan, out var tekrarEden, out var atlananOrnekleri);
            var excelTcSet = UserImportTumTcSeti(satirlar);
            var kayitli = UserImportKayitliKatilimcilar(calismaAlaniId);
            var kayitliTcSet = kayitli
                .Select(x => (x.UserTc ?? string.Empty).Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var exceldeOlmayanAktif = kayitli
                .Where(x => x.Pasif != true)
                .Where(x =>
                {
                    var tc = (x.UserTc ?? string.Empty).Trim();
                    return string.IsNullOrWhiteSpace(tc) || !excelTcSet.Contains(tc);
                })
                .OrderBy(x => x.UserAdi)
                .ToList();

            return new UserImportOnizleme
            {
                Satirlar = satirlar,
                ExcelSatirSayisi = satirlar.Count,
                GecerliKayitSayisi = gecerli,
                AtlananSatirSayisi = atlanan,
                TekrarEdenTcSayisi = tekrarEden,
                KayitliToplamSayisi = kayitli.Count,
                KayitliAktifSayisi = kayitli.Count(x => x.Pasif != true),
                YeniKayitSayisi = tcSet.Count(x => !kayitliTcSet.Contains(x)),
                GuncellenecekKayitSayisi = tcSet.Count(x => kayitliTcSet.Contains(x)),
                ExceldeOlmayanAktifSayisi = exceldeOlmayanAktif.Count,
                ExceldeOlmayanAktifOrnekleri = exceldeOlmayanAktif
                    .Take(8)
                    .Select(x => string.IsNullOrWhiteSpace(x.UserTc) ? x.UserAdi : $"{x.UserAdi} ({x.UserTc})")
                    .ToList(),
                AtlananSatirOrnekleri = atlananOrnekleri
            };
        }

        private UserImportOnizleme UserImportOnizlemeSessiondanOku()
        {
            var json = Convert.ToString(Session[UserImportOnizlemeSessionKey]);
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            try
            {
                return JsonSerializer.Deserialize<UserImportOnizleme>(json);
            }
            catch
            {
                Session[UserImportOnizlemeSessionKey] = null;
                return null;
            }
        }

        private void UserImportOnizlemeViewBagHazirla()
        {
            var onizleme = UserImportOnizlemeSessiondanOku();
            if (onizleme == null)
            {
                return;
            }

            ViewBag.UserImportOnizlemeVar = true;
            ViewBag.UserImportExcelSatirSayisi = onizleme.ExcelSatirSayisi;
            ViewBag.UserImportGecerliKayitSayisi = onizleme.GecerliKayitSayisi;
            ViewBag.UserImportAtlananSatirSayisi = onizleme.AtlananSatirSayisi;
            ViewBag.UserImportTekrarEdenTcSayisi = onizleme.TekrarEdenTcSayisi;
            ViewBag.UserImportKayitliToplamSayisi = onizleme.KayitliToplamSayisi;
            ViewBag.UserImportKayitliAktifSayisi = onizleme.KayitliAktifSayisi;
            ViewBag.UserImportYeniKayitSayisi = onizleme.YeniKayitSayisi;
            ViewBag.UserImportGuncellenecekKayitSayisi = onizleme.GuncellenecekKayitSayisi;
            ViewBag.UserImportExceldeOlmayanAktifSayisi = onizleme.ExceldeOlmayanAktifSayisi;
            ViewBag.UserImportExceldeOlmayanAktifOrnekleri = onizleme.ExceldeOlmayanAktifOrnekleri;
            ViewBag.UserImportAtlananSatirOrnekleri = onizleme.AtlananSatirOrnekleri;
        }

        public ActionResult UserImportTemplate()
        {
            if (Session["id"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }

            var bytes = KatilimciSablonuOlustur();
            return File(
                bytes,
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                "katilimci-import-sablonu.xlsx");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult UserImport(IFormFile dosya)
        {
            if (Session["id"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }

            var calismaAlaniId = AktifCalismaAlaniId();
            if (!calismaAlaniId.HasValue)
            {
                TempData["UserImportMesaj"] = "Çalışma alanı bulunamadı. Lütfen tekrar giriş yapın.";
                return RedirectToAction("UserIndex");
            }

            if (dosya == null || dosya.Length == 0)
            {
                TempData["UserImportMesaj"] = "Lütfen dolu bir Excel dosyası seçin.";
                return RedirectToAction("UserIndex");
            }

            try
            {
                using var dosyaStream = dosya.OpenReadStream();
                var satirlar = ExcelSatirlariniOku(dosyaStream);

                if (!satirlar.Any())
                {
                    TempData["UserImportMesaj"] = "Excel dosyasında aktarılacak satır bulunamadı. İlk satır başlık, ikinci satırdan itibaren katılımcı olmalı.";
                    return RedirectToAction("UserIndex");
                }

                var onizleme = UserImportOnizlemeOlustur(satirlar, calismaAlaniId.Value);
                if (onizleme.GecerliKayitSayisi <= 0)
                {
                    TempData["UserImportMesaj"] = "Excel dosyasında geçerli katılımcı bulunamadı. Katılımcı adı ve TC alanları dolu olmalı.";
                    return RedirectToAction("UserIndex");
                }

                Session[UserImportOnizlemeSessionKey] = JsonSerializer.Serialize(onizleme);
                TempData["UserImportMesaj"] =
                    $"Excel okundu: {onizleme.GecerliKayitSayisi} geçerli kayıt, {onizleme.YeniKayitSayisi} yeni, {onizleme.GuncellenecekKayitSayisi} mevcut kayıt. Devam etmek için aşağıdaki aktarım kararını onaylayın.";
            }
            catch (Exception ex)
            {
                TempData["UserImportMesaj"] = "Excel içeri aktarılamadı: " + ex.Message;
            }

            return RedirectToAction("UserIndex");
        }

        private UserImportSonuc UserImportCalistir(List<Dictionary<string, string>> satirlar, int calismaAlaniId, bool exceldeOlmayanlariPasifeAl)
        {
            var sonuc = new UserImportSonuc();
            var islenenTcSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var excelTcSet = UserImportTumTcSeti(satirlar);

            using var transaction = db.Database.BeginTransaction();
            try
            {
                var excelSatirNo = 1;
                foreach (var satir in satirlar ?? new List<Dictionary<string, string>>())
                {
                    excelSatirNo++;
                    var katilimciAdi = MetniKirp(Hucre(satir, "katilimciadi", "adsoyad", "ad"), 50);
                    var tc = UserImportTcDegeri(satir);

                    if (string.IsNullOrWhiteSpace(katilimciAdi))
                    {
                        sonuc.Atlanan++;
                        if (sonuc.AtlananSatirOrnekleri.Count < 8)
                        {
                            sonuc.AtlananSatirOrnekleri.Add(UserImportSatirEtiketi(excelSatirNo, katilimciAdi, tc, "katilimci adi bos"));
                        }
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(tc))
                    {
                        sonuc.Atlanan++;
                        if (sonuc.AtlananSatirOrnekleri.Count < 8)
                        {
                            sonuc.AtlananSatirOrnekleri.Add(UserImportSatirEtiketi(excelSatirNo, katilimciAdi, tc, "TC bos"));
                        }
                        continue;
                    }

                    if (!islenenTcSet.Add(tc))
                    {
                        sonuc.Atlanan++;
                        if (sonuc.AtlananSatirOrnekleri.Count < 8)
                        {
                            sonuc.AtlananSatirOrnekleri.Add(UserImportSatirEtiketi(excelSatirNo, katilimciAdi, tc, "Excel icinde tekrar TC"));
                        }
                        continue;
                    }

                    var mevcutId = db.Database.SqlQuery<int?>(
                        @"SELECT TOP 1 UserId
                          FROM dbo.[User]
                          WHERE LTRIM(RTRIM(UserTc)) = @p0
                            AND CalismaAlaniId = @p1
                            AND (UserAdres IS NULL OR CONVERT(varchar(max), UserAdres) NOT LIKE @p2)
                          ORDER BY UserId",
                        tc,
                        calismaAlaniId,
                        "BilgiFormu:%").FirstOrDefault();

                    var unvanId = ImportSecenekId("Unvan", Hucre(satir, "unvan", "unvani"), sonuc);
                    var cinsiyetId = ImportSecenekId("Cinsiyet", Hucre(satir, "cinsiyet"), sonuc);
                    var egitimId = ImportSecenekId("Egitim", Hucre(satir, "egitim"), sonuc);
                    var dogumTarihi = TarihDegeri(Hucre(satir, "dogumtarihi", "dogumtar", "yas", "yasi"));
                    var bolumId = ImportSecenekId("Bolum", Hucre(satir, "bolum", "bolumu"), sonuc);
                    var subeId = ImportSecenekId("Sube", Hucre(satir, "sube", "subesi", "subeadi"), sonuc);
                    var bolgeId = ImportSecenekId("Bolge", Hucre(satir, "bolge", "bolgesi", "bolgeadi"), sonuc);
                    var departmanId = ImportSecenekId("Departman", Hucre(satir, "departman", "departmanadi"), sonuc);
                    var yoneticiId = ImportSecenekId("Yonetici", Hucre(satir, "yonetici", "yoneticisi", "yoneticiadi"), sonuc);
                    var yakaId = ImportSecenekId("Yaka", Hucre(satir, "yaka", "yakaadi"), sonuc);
                    var sehirId = ImportSecenekId("Sehir", Hucre(satir, "sehir", "sehri", "sehiradi"), sonuc);
                    var eposta = MetniKirp(Hucre(satir, "eposta", "email", "mail", "usermail"), 50);
                    var resimHucre = Hucre(satir, "resimdosyaadi", "resimadi", "resimdosyasi", "resim", "foto", "fotograf", "userresim");
                    var resim = KatilimciResimDosyaAdiTemizle(resimHucre);
                    if (!string.IsNullOrWhiteSpace(resimHucre)
                        && !string.IsNullOrWhiteSpace(resim)
                        && !string.Equals(Path.GetExtension(resimHucre.Trim()), ".webp", StringComparison.OrdinalIgnoreCase))
                    {
                        sonuc.ResimAdiWebpYapilan++;
                    }
                    var telefon = MetniKirp(Hucre(satir, "telefon", "tel", "usertelefon"), 50);
                    var adres = Hucre(satir, "adres", "useradres");
                    var iseGirisTarihi = TarihDegeri(Hucre(satir, "isegiristarihi", "isegiris", "userisegiristarihi"));
                    var pasif = PasifDegeri(Hucre(satir, "pasif"));
                    var kayitTarihi = TarihDegeri(Hucre(satir, "kayittarihi", "kayittar"));

                    if (mevcutId.HasValue && mevcutId.Value > 0)
                    {
                        db.Database.ExecuteSqlCommand(
                            @"UPDATE dbo.[User]
                              SET UserAdi = @p0,
                                  UserTc = @p1,
                                  UserUnvan = @p2,
                                  UserCinsiyet = @p3,
                                  UserEgitim = @p4,
                                  UserDogumTar = @p5,
                                  UserBolumu = @p6,
                                  UserSube = @p7,
                                  UserBolge = @p8,
                                  UserDepartman = @p9,
                                  UserYoneticisi = @p10,
                                  UserYaka = @p11,
                                  UserSehir = @p12,
                                  UserMail = @p13,
                                  UserResim = @p14,
                                  UserTelefon = @p15,
                                  UserAdres = @p16,
                                  UserIseGirisTarihi = @p17,
                                  Pasif = @p18,
                                  KayitTarihi = COALESCE(@p19, KayitTarihi),
                                  CalismaAlaniId = @p20
                              WHERE UserId = @p21",
                            katilimciAdi,
                            tc,
                            SqlDegeri(unvanId),
                            SqlDegeri(cinsiyetId),
                            SqlDegeri(egitimId),
                            SqlDegeri(dogumTarihi),
                            SqlDegeri(bolumId),
                            SqlDegeri(subeId),
                            SqlDegeri(bolgeId),
                            SqlDegeri(departmanId),
                            SqlDegeri(yoneticiId),
                            SqlDegeri(yakaId),
                            SqlDegeri(sehirId),
                            SqlDegeri(eposta),
                            SqlDegeri(resim),
                            SqlDegeri(telefon),
                            SqlDegeri(adres),
                            SqlDegeri(iseGirisTarihi),
                            pasif,
                            SqlDegeri(kayitTarihi),
                            calismaAlaniId,
                            mevcutId.Value);
                        sonuc.Guncellenen++;
                    }
                    else
                    {
                        db.Database.SqlQuery<int>(
                            @"INSERT INTO dbo.[User]
                                (UserAdi, UserTc, UserUnvan, UserCinsiyet, UserEgitim, UserDogumTar,
                                 UserBolumu, UserSube, UserBolge, UserDepartman, UserYoneticisi,
                                 UserYaka, UserSehir, UserMail, UserResim, UserTelefon, UserAdres,
                                 UserIseGirisTarihi, Pasif, KayitTarihi, CalismaAlaniId)
                              VALUES
                                (@p0, @p1, @p2, @p3, @p4, @p5,
                                 @p6, @p7, @p8, @p9, @p10,
                                 @p11, @p12, @p13, @p14, @p15, @p16,
                                 @p17, @p18, @p19, @p20);
                              SELECT CAST(SCOPE_IDENTITY() AS int);",
                            katilimciAdi,
                            tc,
                            SqlDegeri(unvanId),
                            SqlDegeri(cinsiyetId),
                            SqlDegeri(egitimId),
                            SqlDegeri(dogumTarihi),
                            SqlDegeri(bolumId),
                            SqlDegeri(subeId),
                            SqlDegeri(bolgeId),
                            SqlDegeri(departmanId),
                            SqlDegeri(yoneticiId),
                            SqlDegeri(yakaId),
                            SqlDegeri(sehirId),
                            SqlDegeri(eposta),
                            SqlDegeri(resim),
                            SqlDegeri(telefon),
                            SqlDegeri(adres),
                            SqlDegeri(iseGirisTarihi),
                            pasif,
                            kayitTarihi ?? DateTime.Now,
                            calismaAlaniId).First();
                        sonuc.Eklenen++;
                    }
                }

                if (exceldeOlmayanlariPasifeAl)
                {
                    var pasifeAlinacaklar = UserImportKayitliKatilimcilar(calismaAlaniId)
                        .Where(x => x.Pasif != true)
                        .Where(x =>
                        {
                            var tc = (x.UserTc ?? string.Empty).Trim();
                            return string.IsNullOrWhiteSpace(tc) || !excelTcSet.Contains(tc);
                        })
                        .ToList();

                    foreach (var kayit in pasifeAlinacaklar)
                    {
                        sonuc.PasifeAlinan += db.Database.ExecuteSqlCommand(
                            "UPDATE dbo.[User] SET Pasif = 1 WHERE UserId = @p0 AND CalismaAlaniId = @p1",
                            kayit.UserId,
                            calismaAlaniId);
                    }
                }

                transaction.Commit();
                return sonuc;
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }

        private static string UserImportSonucMesaji(UserImportSonuc sonuc, bool exceldeOlmayanlariPasifeAl)
        {
            var webpBilgisi = sonuc.ResimAdiWebpYapilan > 0
                ? $" Resim alanındaki {sonuc.ResimAdiWebpYapilan} dosya adı .webp olarak kaydedildi."
                : " Resim alanları WebP uyumlu kaydedildi.";
            var pasifBilgisi = exceldeOlmayanlariPasifeAl
                ? $" Excel'de olmayan {sonuc.PasifeAlinan} aktif katılımcı pasife alındı."
                : " Excel'de olmayan mevcut katılımcılar olduğu gibi bırakıldı.";
            var secenekBilgisi =
                $" {sonuc.MevcutSecenekEslesen} seçenek mevcut kayıtla eşleşti, {sonuc.BenzerSecenekEslesen} benzer yazım mevcut kayda bağlandı, {sonuc.YeniSecenekEklenen} yeni seçenek eklendi.";

            var atlananBilgisi = sonuc.AtlananSatirOrnekleri.Any()
                ? " Atlanan satır örnekleri: " + string.Join("; ", sonuc.AtlananSatirOrnekleri)
                : string.Empty;
            var secenekUyariBilgisi = sonuc.SecenekUyariOrnekleri.Any()
                ? " Tanınmayan seçenekler kayıt açmadan boş bırakıldı: " + string.Join("; ", sonuc.SecenekUyariOrnekleri)
                : string.Empty;

            return $"{sonuc.Eklenen} katılımcı eklendi, {sonuc.Guncellenen} katılımcı güncellendi, {sonuc.Atlanan} satır atlandı.{pasifBilgisi}{webpBilgisi}{secenekBilgisi}{atlananBilgisi}{secenekUyariBilgisi}";
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult UserImportOnayla(string eksikKayitIslemi)
        {
            if (Session["id"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }

            var calismaAlaniId = AktifCalismaAlaniId();
            var onizleme = UserImportOnizlemeSessiondanOku();
            if (!calismaAlaniId.HasValue || onizleme == null)
            {
                TempData["UserImportMesaj"] = "Onay bekleyen Excel aktarımı bulunamadı. Lütfen dosyayı yeniden seçin.";
                return RedirectToAction("UserIndex");
            }

            var exceldeOlmayanlariPasifeAl = string.Equals(eksikKayitIslemi, "pasifeAl", StringComparison.OrdinalIgnoreCase);
            try
            {
                var sonuc = UserImportCalistir(onizleme.Satirlar, calismaAlaniId.Value, exceldeOlmayanlariPasifeAl);
                Session[UserImportOnizlemeSessionKey] = null;
                TempData["UserImportMesaj"] = UserImportSonucMesaji(sonuc, exceldeOlmayanlariPasifeAl);
            }
            catch (Exception ex)
            {
                TempData["UserImportMesaj"] = "Excel aktarımı tamamlanamadı: " + ex.Message;
            }

            return RedirectToAction("UserIndex");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult UserImportVazgec()
        {
            Session[UserImportOnizlemeSessionKey] = null;
            TempData["UserImportMesaj"] = "Excel aktarımı iptal edildi; kayıtlar değiştirilmedi.";
            return RedirectToAction("UserIndex");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult UserResimTopluYukle(List<IFormFile> resimDosyalari)
        {
            if (Session["id"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }

            var dosyalar = (resimDosyalari ?? new List<IFormFile>())
                .Where(x => x != null && x.Length > 0)
                .ToList();

            if (!dosyalar.Any())
            {
                TempData["UserImportMesaj"] = "Lütfen en az bir katılımcı fotoğrafı seçin.";
                return RedirectToAction("UserIndex");
            }

            var calismaAlaniId = AktifCalismaAlaniId();
            var yuklenen = 0;
            var gecersiz = 0;
            var otomatikBaglanan = 0;
            var mevcutEslesen = 0;
            var uzantisiWebpYapilan = 0;
            var dosyaAdlari = new List<string>();

            foreach (var dosya in dosyalar)
            {
                var dosyaAdi = KatilimciResimTopluKaydet(dosya);
                if (string.IsNullOrWhiteSpace(dosyaAdi))
                {
                    gecersiz++;
                    continue;
                }

                yuklenen++;
                dosyaAdlari.Add(dosyaAdi);
                if (!string.Equals(Path.GetExtension(dosya.FileName), ".webp", StringComparison.OrdinalIgnoreCase))
                {
                    uzantisiWebpYapilan++;
                }

                if (calismaAlaniId.HasValue)
                {
                    var tc = Path.GetFileNameWithoutExtension(dosyaAdi);
                    if (!string.IsNullOrWhiteSpace(tc))
                    {
                        otomatikBaglanan += db.Database.ExecuteSqlCommand(
                            @"UPDATE dbo.[User]
                              SET UserResim = @p0
                              WHERE CalismaAlaniId = @p1
                                AND LTRIM(RTRIM(UserTc)) = @p2
                                AND (UserResim IS NULL OR LTRIM(RTRIM(UserResim)) = '')",
                            dosyaAdi,
                            calismaAlaniId.Value,
                            tc);
                    }
                }
            }

            if (calismaAlaniId.HasValue)
            {
                foreach (var dosyaAdi in dosyaAdlari.Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    mevcutEslesen += db.Database.SqlQuery<int>(
                        @"SELECT COUNT(1)
                          FROM dbo.[User]
                          WHERE CalismaAlaniId = @p0
                            AND LTRIM(RTRIM(UserResim)) = @p1",
                        calismaAlaniId.Value,
                        dosyaAdi).FirstOrDefault();
                }
            }

            TempData["UserImportMesaj"] =
                $"{yuklenen} fotoğraf WebP olarak yüklendi, {uzantisiWebpYapilan} dosyanın uzantısı .webp'ye çevrildi, {otomatikBaglanan} katılımcıya TC dosya adından otomatik bağlandı, {mevcutEslesen} kayıt dosya adıyla eşleşiyor, {gecersiz} dosya atlandı.";

            return RedirectToAction("UserIndex");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult UserResimGuncelle(int id, IFormFile resimDosyasi, string kirpilmisResim, bool resimKaldir = false)
        {
            if (Session["id"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }

            var kayit = CalismaAlaniKaydiGetir<User>("User", "UserId", id);
            if (kayit == null)
            {
                return RedirectToAction("Hata1", "Ayar", null);
            }

            try
            {
                if (resimKaldir)
                {
                    kayit.UserResim = null;
                    db.SaveChanges();
                    TempData["UserImportMesaj"] = "Katılımcı resmi kaldırıldı; avatar kullanılacak.";
                    return RedirectToAction("UserIndex");
                }

                var yuklenenResim = KirpilmisResimKaydet(kirpilmisResim, KatilimciResimKlasoru, "katilimci");
                if (string.IsNullOrWhiteSpace(yuklenenResim))
                {
                    yuklenenResim = KatilimciResimKaydet(resimDosyasi);
                }

                if (string.IsNullOrWhiteSpace(yuklenenResim))
                {
                    TempData["UserImportMesaj"] = "Fotoğraf seçilmediği için değişiklik yapılmadı.";
                    return RedirectToAction("UserIndex");
                }

                kayit.UserResim = yuklenenResim;
                db.SaveChanges();
                TempData["UserImportMesaj"] = "Katılımcı fotoğrafı güncellendi.";
            }
            catch (Exception ex)
            {
                TempData["UserImportMesaj"] = ex.Message;
            }

            return RedirectToAction("UserIndex");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult YoneticiResimIndexGuncelle(int id, IFormFile resimDosyasi, string kirpilmisResim, bool resimKaldir = false)
        {
            if (Session["id"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }

            var kayit = CalismaAlaniKaydiGetir<Yonetici>("Yonetici", "YoneticiId", id);
            if (kayit == null)
            {
                return RedirectToAction("Hata1", "Ayar", null);
            }

            try
            {
                if (!YoneticiResimAlaniVarMi())
                {
                    TempData["YoneticiResimMesaj"] = "YoneticiResim alanı bulunamadı. DatabaseScripts/20260606_YoneticiResim.sql scriptini çalıştırın.";
                    return RedirectToAction("YoneticiIndex");
                }

                if (resimKaldir)
                {
                    YoneticiResimGuncelle(id, null);
                    TempData["YoneticiResimMesaj"] = "Yönetici resmi kaldırıldı; avatar kullanılacak.";
                    return RedirectToAction("YoneticiIndex");
                }

                var yuklenenResim = KirpilmisResimKaydet(kirpilmisResim, YoneticiResimKlasoru, "yonetici");
                if (string.IsNullOrWhiteSpace(yuklenenResim))
                {
                    yuklenenResim = YoneticiResimKaydet(resimDosyasi);
                }

                if (string.IsNullOrWhiteSpace(yuklenenResim))
                {
                    TempData["YoneticiResimMesaj"] = "Fotoğraf seçilmediği için değişiklik yapılmadı.";
                    return RedirectToAction("YoneticiIndex");
                }

                YoneticiResimGuncelle(id, yuklenenResim);
                TempData["YoneticiResimMesaj"] = "Yönetici fotoğrafı güncellendi.";
            }
            catch (Exception ex)
            {
                TempData["YoneticiResimMesaj"] = ex.Message;
            }

            return RedirectToAction("YoneticiIndex");
        }

        public ActionResult UserIndex()
        {
            if (Session["id"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }

            UserImportOnizlemeViewBagHazirla();
            VarsayilanSecenekViewBagHazirla();
            return View(CalismaAlaniKayitlari<User>("User", "UserAdi"));

        }
        public ActionResult UserIndex1(int id)
        {
            if (Session["id"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }

            var anket = CalismaAlaniAnketGetir(id);
            if (anket == null)
            {
                return RedirectToAction("Hata1", "Ayar", null);
            }
            ViewBag.AnketId = id;
            ViewBag.AnketAdi = anket?.AnketAdi ?? "Çalışma";

            var havuzUserIds = db.Havuz
                .Where(h => h.AnketId == id) // AnketId'si 11 olmayanları filtrele
                .Where(h => h.UserId != null)
                .Select(h => h.UserId)
                .ToList();

            // db.User'dan havuzda olmayan kullanıcıları filtrele
            var users = CalismaAlaniKayitlari<User>("User", "UserAdi")
                .Where(u => !havuzUserIds.Contains(u.UserId))
                .OrderBy(u => u.UserAdi)
                .ToList();

            return View(users);
        }
        public ActionResult UserIndex2(int id)
        {
            if (Session["id"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }

            //return View(db.User);


            // db.Havuz'daki UserId'leri ve AnketId'leri al
            var anket = CalismaAlaniAnketGetir(id);
            if (anket == null)
            {
                return RedirectToAction("Hata1", "Ayar", null);
            }
            ViewBag.AnketId = id;
            ViewBag.AnketAdi = anket?.AnketAdi ?? "Çalışma";

            var cevaplayanKodlar = db.Havuz
                .Where(h => h.AnketId == id) // AnketId'si 11 olmayanları filtrele
                .Select(h => h.UserId ?? h.Isimsiz)
                .Where(h => h != null)
                .Distinct()
                .ToList();

            // db.User'dan havuzda olmayan kullanıcıları filtrele
            var sadeceVideoIzleyenler = db.Izledim
                .Include("Anket")
                .Include("User")
                .Include("User.Cinsiyet")
                .Include("User.Departman")
                .Include("User.Unvan")
                .Include("User.Yaka")
                .Include("User.Sehir")
                .Where(x => x.AnketId == id && x.UseId != null && !cevaplayanKodlar.Contains(x.UseId))
                .OrderByDescending(x => x.BitisZaman ?? x.IzTarih)
                .ToList();

            return View(sadeceVideoIzleyenler);
        }

        public ActionResult UserCreate()
        {
            if (Session["id"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }

            UserLookupHazirla();
            return View();
        }

        [ValidateAntiForgeryToken()]
        [HttpPost]
        public ActionResult UserCreate(User dgskn, IFormFile resimDosyasi, string kirpilmisResim)
        {
            if (Session["id"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }

            try
            {
                var yuklenenResim = KirpilmisResimKaydet(kirpilmisResim, KatilimciResimKlasoru, "katilimci");
                if (string.IsNullOrWhiteSpace(yuklenenResim))
                {
                    yuklenenResim = KatilimciResimKaydet(resimDosyasi);
                }

                if (!string.IsNullOrWhiteSpace(yuklenenResim))
                {
                    dgskn.UserResim = yuklenenResim;
                }

                db.User.Add(dgskn);
                db.SaveChanges();
                CalismaAlaniKaydinaBagla("User", "UserId", dgskn.UserId);
                return RedirectToAction("UserIndex");

            }
            catch (Exception ex)
            {
                ModelState.AddModelError("", ex.Message);
                UserLookupHazirla();
                return View(dgskn);
            }
        }
        public ActionResult UserEdit(int id)
        {
            if (Session["id"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }

            UserLookupHazirla();
            var kayit = CalismaAlaniKaydiGetir<User>("User", "UserId", id);
            if (kayit == null)
            {
                return RedirectToAction("Hata1", "Ayar", null);
            }

            return View(kayit);
        }

        [ValidateAntiForgeryToken()]
        [HttpPost]
        public ActionResult UserEdit(User dgskn, IFormFile resimDosyasi, bool resimKaldir = false, string kirpilmisResim = null)
        {
            if (Session["id"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }

            try
            {
                if (!CalismaAlaniKaydiVarMi("User", "UserId", dgskn.UserId))
                {
                    return RedirectToAction("Hata1", "Ayar", null);
                }

                {
                    if (resimKaldir)
                    {
                        dgskn.UserResim = null;
                    }

                    var yuklenenResim = resimKaldir
                        ? string.Empty
                        : KirpilmisResimKaydet(kirpilmisResim, KatilimciResimKlasoru, "katilimci");
                    if (!resimKaldir && string.IsNullOrWhiteSpace(yuklenenResim))
                    {
                        yuklenenResim = KatilimciResimKaydet(resimDosyasi);
                    }

                    if (!string.IsNullOrWhiteSpace(yuklenenResim))
                    {
                        dgskn.UserResim = yuklenenResim;
                    }

                    db.Entry(dgskn).State = EntityState.Modified;
                    db.SaveChanges();
                }
                return RedirectToAction("UserIndex");

            }
            catch (Exception ex)
            {
                ModelState.AddModelError("", ex.Message);
                UserLookupHazirla();
                return View(dgskn);
            }
        }
        public ActionResult UserDelete(int id)
        {
            if (Session["id"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }
            var kayit = CalismaAlaniKaydiGetir<User>("User", "UserId", id);
            if (kayit == null)
            {
                return RedirectToAction("Hata1", "Ayar", null);
            }

            return View(kayit);
        }
        [ValidateAntiForgeryToken()]
        [HttpPost]
        public ActionResult UserDelete(int? id)
        {
            if (Session["id"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }
            if (!CalismaAlaniKaydiVarMi("User", "UserId", id))
            {
                return RedirectToAction("Hata1", "Ayar", null);
            }

            try
            {
                User unv = CalismaAlaniKaydiGetir<User>("User", "UserId", id.Value);
                db.User.Remove(unv);
                db.SaveChanges();
                return RedirectToAction("UserIndex");

            }
            catch
            {
                return View();
            }
        }


        public ActionResult PersonelIndex()
        {
            if (Session["id"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }
            if (!YoneticiYetkisiVarMi())
            {
                return YoneticiYetkisiYok();
            }

            return View(CalismaAlaniPersonelListeSatirlari());
        }
        public ActionResult PersonelCreate()
        {
            if (Session["id"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }
            if (!YoneticiYetkisiVarMi())
            {
                return YoneticiYetkisiYok();
            }

            PersonelLookupHazirla();

            return View(new PersonelYonetimForm
            {
                HesapAktif = true,
                MailOnaylandi = true,
                GirisKaynagi = "Panel",
                KayitTarihi = DateTime.Now
            });
        }

        [ValidateAntiForgeryToken()]
        [HttpPost]
        public ActionResult PersonelCreate(PersonelYonetimForm model)
        {
            if (Session["id"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }
            if (!YoneticiYetkisiVarMi())
            {
                return YoneticiYetkisiYok();
            }

            if (!PaketKullanimKontrolu.PanelKullanicisiEklenebilirMi(db, AktifCalismaAlaniId(), out var paketLimitMesaji, AktifPersonelId()))
            {
                ModelState.AddModelError("", paketLimitMesaji);
                PersonelLookupHazirla();
                return View(model);
            }

            try
            {
                model.MailOnaylandi = true;
                model.GirisKaynagi = string.IsNullOrWhiteSpace(model.GirisKaynagi) ? "Panel" : model.GirisKaynagi;
                model.KayitTarihi ??= DateTime.Now;

                var personel = new Personel();
                PersonelAlanlariniUygula(personel, model, true);
                db.Personel.Add(personel);
                db.SaveChanges();
                PersonelPanelAlanlariniUygula(personel.PersonelId, model);
                PersonelCalismaAlaninaEkle(personel.PersonelId);
                return RedirectToAction("PersonelIndex");

            }
            catch
            {
                PersonelLookupHazirla();
                return View(model);
            }
        }
        public ActionResult PersonelEdit(int id)
        {
            if (Session["id"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }
            if (!YoneticiYetkisiVarMi())
            {
                return YoneticiYetkisiYok();
            }

            PersonelLookupHazirla();
            var kayit = CalismaAlaniPersonelGetir(id);
            if (kayit == null)
            {
                return RedirectToAction("Hata1", "Ayar", null);
            }

            return View(PersonelFormModeli(kayit));
        }

        [ValidateAntiForgeryToken()]
        [HttpPost]
        public ActionResult PersonelEdit(PersonelYonetimForm model)
        {
            if (Session["id"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }
            if (!YoneticiYetkisiVarMi())
            {
                return YoneticiYetkisiYok();
            }

            try
            {
                if (!PersonelCalismaAlanindaMi(model.PersonelId))
                {
                    return RedirectToAction("Hata1", "Ayar", null);
                }

                var personel = CalismaAlaniPersonelGetir(model.PersonelId);
                if (personel == null)
                {
                    return RedirectToAction("Hata1", "Ayar", null);
                }

                model.MailOnaylandi = true;
                model.GirisKaynagi = string.IsNullOrWhiteSpace(model.GirisKaynagi) ? "Panel" : model.GirisKaynagi;
                if (model.PersonelId == AktifPersonelId())
                {
                    model.Admin = personel.Admin == true;
                    model.HesapAktif = personel.Pasif != true;
                }

                PersonelAlanlariniUygula(personel, model, false);
                db.SaveChanges();
                PersonelPanelAlanlariniUygula(personel.PersonelId, model);
                return RedirectToAction("PersonelIndex");

            }
            catch
            {
                PersonelLookupHazirla();
                return View(model);
            }
        }
        public ActionResult PersonelDelete(int id)
        {
            if (Session["id"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }
            if (!YoneticiYetkisiVarMi())
            {
                return YoneticiYetkisiYok();
            }

            var kayit = CalismaAlaniPersonelGetir(id);
            if (kayit == null)
            {
                return RedirectToAction("Hata1", "Ayar", null);
            }

            return View(kayit);
        }
        [ValidateAntiForgeryToken()]
        [HttpPost]
        public ActionResult PersonelDelete(int? id)
        {
            if (Session["id"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }
            if (!YoneticiYetkisiVarMi())
            {
                return YoneticiYetkisiYok();
            }

            if (!PersonelCalismaAlanindaMi(id) || id == AktifPersonelId())
            {
                return RedirectToAction("Hata1", "Ayar", null);
            }

            try
            {
                Personel unv = CalismaAlaniPersonelGetir(id.Value);
                unv.Pasif = true;
                db.Entry(unv).State = EntityState.Modified;
                db.SaveChanges();

                var calismaAlaniId = AktifCalismaAlaniId();
                if (calismaAlaniId.HasValue)
                {
                    db.Database.ExecuteSqlCommand(
                        @"UPDATE dbo.CalismaAlaniUye
                          SET Pasif = 1
                          WHERE CalismaAlaniId = @p0 AND PersonelId = @p1",
                        calismaAlaniId.Value,
                        id.Value);
                }

                return RedirectToAction("PersonelIndex");

            }
            catch
            {
                return View();
            }
        }




        public ActionResult SifremiUnuttum()
        {

            return View();
        }

        public ActionResult smtpayar()
        {
            if (Session["id"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }
            else
            {

                List<smtpayar> emails = CalismaAlaniKayitlari<smtpayar>("smtpayar", "MailId")
               .OrderByDescending(x => x.MailId)
               .ToList();
                return View(emails);
            }

        }
        public ActionResult SmtpDzn(int id)
        {
            if (Session["id"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }
            if (Session["admin"] == null)
            {
                return RedirectToAction("HataUser", "Ayar", null);
            }
            else
            {

                List<SelectListItem> tr =
            (from i in db.truefalse.ToList()
             select new SelectListItem
             {
                 Text = i.TrueFalsemi,
             }).ToList();
                ViewBag.Tru = tr;

                var kayit = CalismaAlaniKaydiGetir<smtpayar>("smtpayar", "MailId", id);
                if (kayit == null)
                {
                    return RedirectToAction("Hata1", "Ayar", null);
                }

                return View(kayit);
            }
        }

        [HttpPost]
        public ActionResult SmtpDzn(smtpayar smtpAyar)
        {
            if (Session["id"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }
            if (Session["misafir"] != null)
            {
                return RedirectToAction("HataUser", "Ayar", null);
            }
            if (Session["admin"] == null)
            {
                return RedirectToAction("HataUser", "Ayar", null);
            }

            try
            {
                if (!CalismaAlaniKaydiVarMi("smtpayar", "MailId", smtpAyar.MailId))
                {
                    return RedirectToAction("Hata1", "Ayar", null);
                }

                {
                    db.Entry(smtpAyar).State = EntityState.Modified;
                    db.SaveChanges();
                }
                return RedirectToAction("smtpayar");
            }
            catch
            {
                return View();
            }
        }

        [ValidateAntiForgeryToken(), HttpPost]
        public ActionResult SifremiUnuttum(string email, int MailId = 1)
        {
            email = (email ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(email))
            {
                ViewBag.Uyari = "Mail adresinizi yazın.";
                return View();
            }

            var sorgu = (from i in db.Personel where i.Mail == email select i).FirstOrDefault();
            var sorgu1 = (from ii in db.smtpayar where ii.MailId == MailId select ii).FirstOrDefault();
            if (sorgu == null)
            {
                ViewBag.Uyari = "Mail adresi kayıtlı değil.";
                return View();
            }

            if (sorgu1 == null)
            {
                ViewBag.Uyari = "SMTP ayarı bulunamadı. Lütfen mail ayarlarını kontrol edin.";
                return View();
            }

            var yeniSifre = Guid.NewGuid().ToString("N").Substring(0, 6);
            sorgu.Sifre = yeniSifre;
            MailMessage msg = new MailMessage();
            msg.To.Add(email);
            msg.IsBodyHtml = true;
            msg.Subject = "Şifre Değiştirme İsteği Bildirimi";
            msg.Body = "<h2>Merhaba " + sorgu.Mail + "</h2>"
                + "<p>Şifre değiştirme isteğiniz alınmıştır.</p>"
                + "<p>Geçici şifreniz: <strong>" + yeniSifre + "</strong></p>"
                + "<p>Hesabınıza girdikten sonra şifrenizi güncelleyiniz.</p>";
            msg.From = new MailAddress(sorgu1.Gonderen);
            msg.SubjectEncoding = Encoding.UTF8;
            msg.BodyEncoding = Encoding.UTF8;
            msg.DeliveryNotificationOptions = DeliveryNotificationOptions.OnFailure;

            SmtpClient sm = new SmtpClient
            {
                Host = sorgu1.Sunucu,
                Port = sorgu1.Portu,
                UseDefaultCredentials = false,
                Credentials = new NetworkCredential(sorgu1.UserName, sorgu1.Password),
                EnableSsl = sorgu1.Ssli,
                Timeout = 10000,
                DeliveryMethod = SmtpDeliveryMethod.Network
            };
#pragma warning disable SYSLIB0014
                ServicePointManager.ServerCertificateValidationCallback = delegate (object s, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors)
                {
                    return true;
                };
#pragma warning restore SYSLIB0014

                try
                {
                    sm.Send(msg);
                    db.SaveChanges();
                    ViewBag.Uyari = "Geçici şifre mail adresinize gönderildi.";
                }
                catch (SmtpException ex)
                {
                    ViewBag.Uyari = "Mail sunucusu gönderimi reddetti. SMTP kullanıcı adı, şifre, port, SSL ve gönderici adresini kontrol edin. Teknik mesaj: " + ex.Message;
                }
                catch (Exception ex)
                {
                    ViewBag.Uyari = "Şifre sıfırlama maili gönderilemedi. Teknik mesaj: " + ex.Message;
                }
            return View();
        }

        public ActionResult Hata1()
        {

            return View();
        }



    }
}
