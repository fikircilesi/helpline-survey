using survey.Models;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.IO;
using System.Linq;
using System.Net.Mail;
using System.Net.Security;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
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

        private void PersonelLookupHazirla()
        {
            ViewBag.KayitTar = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
            ViewBag.Unv = CalismaAlaniKayitlari<Unvan>("Unvan", "UnvanAdi")
                .Select(i => new SelectListItem { Text = i.UnvanAdi, Value = i.UnvanId.ToString() }).ToList();
        }

        public ActionResult AiAyar()
        {
            if (Session["id"] == null || Session["admin"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }

            var calismaAlaniId = AktifCalismaAlaniId();
            var model = AiAyarVarsayilan();
            model.TableReady = AiAyarTablosuVarMi() && calismaAlaniId.HasValue;
            if (model.TableReady)
            {
                var row = db.Database.SqlQuery<AiAyarForm>(
                    @"SELECT TOP 1 AiAyarId, Provider, Endpoint, ChatModel, EmbeddingModel, ApiKey, Aktif, GuncellemeTarihi,
                             CAST(1 AS bit) AS TableReady
                      FROM dbo.AiAyar
                      WHERE CalismaAlaniId = @p0
                      ORDER BY AiAyarId",
                    calismaAlaniId.Value).FirstOrDefault();

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
            if (Session["id"] == null || Session["admin"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }

            model ??= AiAyarVarsayilan();
            var calismaAlaniId = AktifCalismaAlaniId();
            var tableReady = AiAyarTablosuVarMi() && calismaAlaniId.HasValue;
            model.TableReady = tableReady;

            if (!tableReady)
            {
                ModelState.AddModelError("", "AI ayar tablosu veya CalismaAlaniId kolonu bulunamadi. DatabaseScripts/20260605_AyarCalismaAlani.sql scriptini calistirin.");
                model.ApiKeyMasked = string.Empty;
                return View(model);
            }

            var existing = db.Database.SqlQuery<AiAyarForm>(
                @"SELECT TOP 1 AiAyarId, Provider, Endpoint, ChatModel, EmbeddingModel, ApiKey, Aktif, GuncellemeTarihi,
                         CAST(1 AS bit) AS TableReady
                  FROM dbo.AiAyar
                  WHERE CalismaAlaniId = @p0
                  ORDER BY AiAyarId",
                calismaAlaniId.Value).FirstOrDefault();

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
                    @"INSERT INTO dbo.AiAyar (Provider, Endpoint, ChatModel, EmbeddingModel, ApiKey, Aktif, CalismaAlaniId)
                      VALUES (@p0, @p1, @p2, @p3, @p4, @p5, @p6)",
                    model.Provider,
                    model.Endpoint,
                    model.ChatModel,
                    model.EmbeddingModel,
                    apiKey,
                    model.Aktif,
                    calismaAlaniId.Value);
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
                      WHERE AiAyarId = @p6
                        AND CalismaAlaniId = @p7",
                    model.Provider,
                    model.Endpoint,
                    model.ChatModel,
                    model.EmbeddingModel,
                    apiKey,
                    model.Aktif,
                    existing.AiAyarId,
                    calismaAlaniId.Value);
            }

            TempData["AiAyarMesaj"] = "Yapay zeka ayarlari kaydedildi.";
            return RedirectToAction("AiAyar");
        }

        private bool AiAyarTablosuVarMi()
        {
            try
            {
                return db.Database.SqlQuery<int>(
                    "SELECT CASE WHEN OBJECT_ID(N'dbo.AiAyar', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.AiAyar', N'CalismaAlaniId') IS NOT NULL THEN 1 ELSE 0 END")
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
            return View(CalismaAlaniKayitlari<Yonetici>("Yonetici", "YoneticiAdi"));
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
        public ActionResult YoneticiCreate(Yonetici dgskn)
        {
            if (Session["id"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }

            try
            {
                db.Yonetici.Add(dgskn);
                db.SaveChanges();
                CalismaAlaniKaydinaBagla("Yonetici", "YoneticiId", dgskn.YoneticiId);
                return RedirectToAction("YoneticiIndex");

            }
            catch
            {
                return View();
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

            return View(kayit);
        }

        [ValidateAntiForgeryToken()]
        [HttpPost]
        public ActionResult YoneticiEdit(Yonetici dgskn)
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

                {
                    db.Entry(dgskn).State = EntityState.Modified;
                    db.SaveChanges();
                }
                return RedirectToAction("YoneticiIndex");

            }
            catch
            {
                return View();
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

        public ActionResult UserIndex()
        {
            if (Session["id"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }

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
        public ActionResult UserCreate(User dgskn)
        {
            if (Session["id"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }

            try
            {
                db.User.Add(dgskn);
                db.SaveChanges();
                CalismaAlaniKaydinaBagla("User", "UserId", dgskn.UserId);
                return RedirectToAction("UserIndex");

            }
            catch
            {
                UserLookupHazirla();
                return View();
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
        public ActionResult UserEdit(User dgskn)
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
                    db.Entry(dgskn).State = EntityState.Modified;
                    db.SaveChanges();
                }
                return RedirectToAction("UserIndex");

            }
            catch
            {
                UserLookupHazirla();
                return View();
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

        [HttpPost]
        public ActionResult SifremiUnuttum(string email, int MailId = 1)
        {
            var sorgu = (from i in db.Personel where i.Mail.Equals(email) select i).SingleOrDefault(); //üyeyi yakaladık
            var sorgu1 = (from ii in db.smtpayar where ii.MailId.Equals(MailId) select ii).SingleOrDefault(); //üyeyi yakaladık
            if (sorgu != null)
            {
                Guid randomkey = Guid.NewGuid(); //32 karakterli kodu ürettik
                sorgu.Sifre = randomkey.ToString().Substring(0, 5);///keyi ekleyip veritabanına ekledik
                MailMessage msg = new MailMessage();
                msg.To.Add(email.ToString());
                string Body = randomkey.ToString().Substring(0, 5);
                msg.IsBodyHtml = true;
                msg.Subject = "Şifre Degiştirme İsteği Bildirimi";
                msg.Body += "<h2>  Merhaba " + sorgu.Mail + " Şifre Degiştirme İsteğiniz Alınmıştır.  Şifreniz :" + randomkey.ToString().Substring(0, 5) + "  Hesabınıza girerek şifrenizi Güncelleyiniz </h2>  </br>  "; //randomkeyimizi 5 karatere düşdük
                msg.From = new MailAddress(sorgu1.Gonderen);
                msg.BodyEncoding = Encoding.UTF8;
                msg.DeliveryNotificationOptions = DeliveryNotificationOptions.OnFailure;

                SmtpClient sm = new SmtpClient
                {
                    Host = sorgu1.Sunucu,
                    Port = sorgu1.Portu,
                    UseDefaultCredentials = true,
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

                sm.Send(msg);
                db.SaveChanges();
                ViewBag.Uyari = "Doğrulama kodu mail adresinize gönderildi.";
            }
            else
            {
                ViewBag.Uyari = " Mail Adresi Mevcut Değil";
            }
            return View();
        }

        public ActionResult Hata1()
        {

            return View();
        }



    }
}
