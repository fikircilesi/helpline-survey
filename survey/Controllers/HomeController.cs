using survey.Models;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Mail;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Net.Http;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using QRCoder;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Processing;
using System.Web.Security;

namespace survey.Controllers
{
    public class HomeController : LegacyController
    {
        readonly SurveyEntities db = new SurveyEntities();
        readonly EnvanterTakipLisansEntities dbl = new EnvanterTakipLisansEntities();
        const int MailOnayKoduGecerlilikDakika = 20;
        const string KatilimciKoduCookieAdi = "survey_participant_code";
        const string BilgiFormuKatilimTokenSessionKey = "bilgi_formu_katilim_token";
        const string BilgiFormuKullaniciAdresOnEki = "BilgiFormu:";
        const string AnketGorselKlasoru = "~/Content/AnketGorsel/";
        const string AnketGorselEtiketOnEk = "[[gorsel:";
        const string AnketGorselEtiketSonEk = "]]";
        const int AnketGorselWebpKalite = 84;
        const int AnketGorselMaksimumKenar = 1400;

        public class KatilimPortalModel
        {
            public int? Kod { get; set; }
            public int? User { get; set; }
            public string KatilimciAdi { get; set; }
            public List<KatilimPortalItem> Calismalar { get; set; } = new List<KatilimPortalItem>();
        }

        public class KatilimPortalItem
        {
            public int AnketId { get; set; }
            public string AnketAdi { get; set; }
            public bool Sinav { get; set; }
            public bool VideoGerekli { get; set; }
            public string TipEtiketi { get; set; }
            public bool SertifikaVar { get; set; }
            public bool SertifikaHazir { get; set; }
            public string SertifikaDurumMesaji { get; set; }
            public bool DevamEdiyor { get; set; }
            public bool Tamamlandi { get; set; }
            public bool SuresiDoldu { get; set; }
            public bool YayinAcik { get; set; }
            public string YayinMesaji { get; set; }
            public double Puan { get; set; }
            public double GecmeNotu { get; set; }
            public int Cevaplanan { get; set; }
            public int ToplamSoru { get; set; }
            public DateTime? KatilimTarihi { get; set; }
            public string DurumMetni { get; set; }
        }

        public class DogruCevapEditorModel
        {
            public int AnketId { get; set; }
            public string AnketAdi { get; set; }
            public string ReturnUrl { get; set; }
            public List<DogruCevapQuestionModel> Sorular { get; set; } = new List<DogruCevapQuestionModel>();
        }

        public class DogruCevapQuestionModel
        {
            public int SoruId { get; set; }
            public int? SoruSira { get; set; }
            public string SoruAdi { get; set; }
            public double SoruPuan { get; set; }
            public int? CevapGrupId { get; set; }
            public int? DogruCevapId { get; set; }
            public List<DogruCevapAnswerModel> Cevaplar { get; set; } = new List<DogruCevapAnswerModel>();
        }

        public class DogruCevapAnswerModel
        {
            public int CevapId { get; set; }
            public string CevapAdi { get; set; }
            public bool Dogru { get; set; }
        }

        public class DogruCevapSaveModel
        {
            public int AnketId { get; set; }
            public string ReturnUrl { get; set; }
            public Dictionary<int, int> DogruCevaplar { get; set; } = new Dictionary<int, int>();
        }

        public class KatilimciDogrulamaFormu
        {
            public string Token { get; set; }
            public string TcKimlikNo { get; set; }
            public string AdSoyad { get; set; }
            public string Eposta { get; set; }
            public string Telefon { get; set; }
        }

        private void CaptchaYenile()
        {
            var birinci = RandomNumberGenerator.GetInt32(2, 10);
            var ikinci = RandomNumberGenerator.GetInt32(2, 10);
            Session["GirisCaptchaSonuc"] = (birinci + ikinci).ToString();
            ViewBag.CaptchaSoru = $"{birinci} + {ikinci} = ?";
            RecaptchaBilgisiniHazirla();
        }

        private bool CaptchaDogruMu(string guvenlikCevabi)
        {
            var beklenen = Session["GirisCaptchaSonuc"] as string;
            if (string.IsNullOrWhiteSpace(beklenen) || string.IsNullOrWhiteSpace(guvenlikCevabi))
            {
                return false;
            }

            return string.Equals(beklenen.Trim(), guvenlikCevabi.Trim(), StringComparison.Ordinal);
        }

        private bool GoogleRecaptchaAktifMi()
        {
            var configuration = HttpContext.RequestServices.GetRequiredService<IConfiguration>();
            var enabled = configuration.GetValue<bool?>("GoogleRecaptcha:Enabled") ?? true;
            var siteKey = configuration["GoogleRecaptcha:SiteKey"];
            var secretKey = configuration["GoogleRecaptcha:SecretKey"];

            return enabled
                && !string.IsNullOrWhiteSpace(siteKey)
                && !string.IsNullOrWhiteSpace(secretKey)
                && !siteKey.Contains("BURAYA_", StringComparison.OrdinalIgnoreCase)
                && !secretKey.Contains("BURAYA_", StringComparison.OrdinalIgnoreCase);
        }

        private void RecaptchaBilgisiniHazirla()
        {
            var configuration = HttpContext.RequestServices.GetRequiredService<IConfiguration>();
            var localhostBypass = GoogleRecaptchaAktifMi() && LocalhostGelistirmeOrtamiMi();
            ViewBag.RecaptchaAktif = GoogleRecaptchaAktifMi() && !localhostBypass;
            ViewBag.CaptchaAtlandi = localhostBypass;
            ViewBag.RecaptchaSiteKey = configuration["GoogleRecaptcha:SiteKey"];
        }

        private class RecaptchaDogrulamaSonucu
        {
            public bool Basarili { get; set; }
            public string Mesaj { get; set; }
        }

        private async System.Threading.Tasks.Task<RecaptchaDogrulamaSonucu> GoogleRecaptchaDogrula()
        {
            if (!GoogleRecaptchaAktifMi())
            {
                return new RecaptchaDogrulamaSonucu { Basarili = true };
            }

            if (LocalhostGelistirmeOrtamiMi())
            {
                return new RecaptchaDogrulamaSonucu { Basarili = true };
            }

            var token = Request.Form["g-recaptcha-response"].FirstOrDefault();
            if (string.IsNullOrWhiteSpace(token))
            {
                return new RecaptchaDogrulamaSonucu
                {
                    Basarili = false,
                    Mesaj = "Ben robot deÄŸilim kutusunu iÅŸaretleyin."
                };
            }

            var configuration = HttpContext.RequestServices.GetRequiredService<IConfiguration>();
            var form = new Dictionary<string, string>
            {
                ["secret"] = configuration["GoogleRecaptcha:SecretKey"],
                ["response"] = token
            };

            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
                using var response = await http.PostAsync(
                    "https://www.google.com/recaptcha/api/siteverify",
                    new FormUrlEncodedContent(form));

                if (!response.IsSuccessStatusCode)
                {
                    return new RecaptchaDogrulamaSonucu
                    {
                        Basarili = false,
                        Mesaj = "Google reCAPTCHA servisine ulaÅŸÄ±lamadÄ±. HTTP " + (int)response.StatusCode
                    };
                }

                var raw = await response.Content.ReadAsStringAsync();
                using var document = JsonDocument.Parse(raw);
                var basarili = document.RootElement.TryGetProperty("success", out var success)
                    && success.ValueKind == JsonValueKind.True;

                if (basarili)
                {
                    return new RecaptchaDogrulamaSonucu { Basarili = true };
                }

                var hataKodlari = new List<string>();
                if (document.RootElement.TryGetProperty("error-codes", out var errorCodes)
                    && errorCodes.ValueKind == JsonValueKind.Array)
                {
                    foreach (var errorCode in errorCodes.EnumerateArray())
                    {
                        var code = errorCode.GetString();
                        if (!string.IsNullOrWhiteSpace(code))
                        {
                            hataKodlari.Add(code);
                        }
                    }
                }

                return new RecaptchaDogrulamaSonucu
                {
                    Basarili = false,
                    Mesaj = RecaptchaHataMesaji(hataKodlari)
                };
            }
            catch (Exception ex)
            {
                return new RecaptchaDogrulamaSonucu
                {
                    Basarili = false,
                    Mesaj = "Google reCAPTCHA doÄŸrulamasÄ± tamamlanamadÄ±: " + ex.Message
                };
            }
        }

        private static string RecaptchaHataMesaji(List<string> hataKodlari)
        {
            if (hataKodlari == null || !hataKodlari.Any())
            {
                return "Google reCAPTCHA doÄŸrulamasÄ± baÅŸarÄ±sÄ±z oldu. Kutuyu tekrar iÅŸaretleyin.";
            }

            if (hataKodlari.Contains("missing-input-response"))
            {
                return "Ben robot deÄŸilim kutusunu iÅŸaretleyin.";
            }

            if (hataKodlari.Contains("timeout-or-duplicate") || hataKodlari.Contains("invalid-input-response"))
            {
                return "reCAPTCHA doÄŸrulamasÄ± sÃ¼resi doldu veya geÃ§ersizleÅŸti. Kutuyu tekrar iÅŸaretleyip yeniden deneyin. Kod: " + string.Join(", ", hataKodlari);
            }

            if (hataKodlari.Contains("invalid-input-secret") || hataKodlari.Contains("missing-input-secret"))
            {
                return "reCAPTCHA gizli anahtarÄ± geÃ§ersiz. appsettings.json iÃ§indeki SecretKey kontrol edilmeli. Kod: " + string.Join(", ", hataKodlari);
            }

            return "Google reCAPTCHA doÄŸrulamasÄ± baÅŸarÄ±sÄ±z oldu: " + string.Join(", ", hataKodlari);
        }

        private bool LocalhostGelistirmeOrtamiMi()
        {
            var env = HttpContext.RequestServices.GetService<IWebHostEnvironment>();
            var host = Request.Host.Host;

            return env?.IsDevelopment() == true
                && (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(host, "127.0.0.1", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(host, "::1", StringComparison.OrdinalIgnoreCase));
        }

        private static string MailOnayKoduUret()
        {
            return RandomNumberGenerator.GetInt32(100000, 1000000).ToString();
        }

        private static string KullaniciAdiUret(string email)
        {
            var kaynak = (email ?? "google-kullanici").Split('@')[0].ToLowerInvariant();
            kaynak = Regex.Replace(kaynak, @"[^a-z0-9._-]", string.Empty);
            if (string.IsNullOrWhiteSpace(kaynak))
            {
                kaynak = "google-kullanici";
            }

            return kaynak.Length > 40 ? kaynak.Substring(0, 40) : kaynak;
        }

        private PersonelGuvenlikBilgisi GuvenlikBilgisiGetir(int personelId)
        {
            try
            {
                return db.Database.SqlQuery<PersonelGuvenlikBilgisi>(
                    @"SELECT PersonelId,
                             ISNULL(MailOnaylandi, 1) AS MailOnaylandi,
                             MailOnayKodu,
                             MailOnayKoduTarihi,
                             GoogleKimlikId,
                             GirisKaynagi
                      FROM dbo.Personel
                      WHERE PersonelId = @p0",
                    personelId).FirstOrDefault();
            }
            catch
            {
                return new PersonelGuvenlikBilgisi
                {
                    PersonelId = personelId,
                    MailOnaylandi = true
                };
            }
        }

        private bool GoogleAyariHazirMi()
        {
            var clientId = HttpContext.RequestServices.GetRequiredService<IConfiguration>()["Authentication:Google:ClientId"];
            var clientSecret = HttpContext.RequestServices.GetRequiredService<IConfiguration>()["Authentication:Google:ClientSecret"];
            return !string.IsNullOrWhiteSpace(clientId) && !string.IsNullOrWhiteSpace(clientSecret);
        }

        private int CalismaAlaniHazirla(int personelId, string firmaAdi, string calismaAlaniAdi)
        {
            try
            {
                var mevcut = db.Database.SqlQuery<int?>(
                    @"SELECT TOP 1 cau.CalismaAlaniId
                      FROM dbo.CalismaAlaniUye cau
                      INNER JOIN dbo.CalismaAlani ca ON ca.CalismaAlaniId = cau.CalismaAlaniId
                      WHERE cau.PersonelId = @p0 AND cau.Pasif = 0 AND ca.Pasif = 0
                      ORDER BY cau.CalismaAlaniUyeId",
                    personelId).FirstOrDefault();

                if (mevcut.HasValue)
                {
                    return mevcut.Value;
                }

                var yeniCalismaAlaniId = db.Database.SqlQuery<int>(
                    @"INSERT INTO dbo.CalismaAlani
                        (CalismaAlaniAdi, FirmaAdi, SahipPersonelId, KrediBakiyesi, Pasif, KayitTarihi)
                      VALUES
                        (@p0, @p1, @p2, 0, 0, GETDATE());
                      SELECT CAST(SCOPE_IDENTITY() AS int);",
                    string.IsNullOrWhiteSpace(calismaAlaniAdi) ? "Ã‡alÄ±ÅŸma AlanÄ±m" : calismaAlaniAdi,
                    firmaAdi,
                    personelId).First();

                db.Database.ExecuteSqlCommand(
                    @"INSERT INTO dbo.CalismaAlaniUye
                        (CalismaAlaniId, PersonelId, Rol, Pasif, KayitTarihi)
                      VALUES
                        (@p0, @p1, N'Sahip', 0, GETDATE());",
                    yeniCalismaAlaniId,
                    personelId);

                return yeniCalismaAlaniId;
            }
            catch
            {
                return 0;
            }
        }

        private void PersonelOturumuAc(Personel personel)
        {
            FormsAuthentication.SetAuthCookie(personel.PersonelAdi, false);
            Session["id"] = personel.PersonelId;
            Session["adi"] = personel.PersonelAdi;
            Session["kuladi"] = personel.KullaniciAdi;
            Session["admin"] = personel.Admin == true;
            Session["panel"] = true;
            Session["resim"] = personel.Resim;
            Session["ipadres"] = GetClientIp();

            var calismaAlaniId = CalismaAlaniHazirla(
                personel.PersonelId,
                personel.Adres,
                $"{personel.PersonelAdi} Ã‡alÄ±ÅŸma AlanÄ±");

            if (calismaAlaniId > 0)
            {
                Session["CalismaAlaniId"] = calismaAlaniId;
            }

            try
            {
                db.Database.ExecuteSqlCommand(
                    "UPDATE dbo.Personel SET SonGirisTarihi = GETDATE() WHERE PersonelId = @p0",
                    personel.PersonelId);
            }
            catch
            {
                // GÃ¼venlik kolonlarÄ± SQL tarafÄ±nda henÃ¼z eklenmemiÅŸse giriÅŸ akÄ±ÅŸÄ±nÄ± kÄ±rmayalÄ±m.
            }
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

        private int? AktifPersonelId()
        {
            var deger = Convert.ToString(Session["id"]);
            if (int.TryParse(deger, out var personelId) && personelId > 0)
            {
                return personelId;
            }

            return null;
        }

        private bool AnketCalismaAlanindaMi(int? anketId)
        {
            if (!anketId.HasValue || anketId.Value <= 0)
            {
                return false;
            }

            var calismaAlaniId = AktifCalismaAlaniId();
            var personelId = AktifPersonelId();

            try
            {
                if (calismaAlaniId.HasValue)
                {
                    return db.Database.SqlQuery<int>(
                        @"SELECT COUNT(1)
                          FROM dbo.Anket
                          WHERE AnketId = @p0
                            AND CalismaAlaniId = @p1",
                        anketId.Value,
                        calismaAlaniId.Value).FirstOrDefault() > 0;
                }

                if (personelId.HasValue)
                {
                    return db.Database.SqlQuery<int>(
                        @"SELECT COUNT(1)
                          FROM dbo.Anket a
                          LEFT JOIN dbo.CalismaAlaniUye cau ON cau.CalismaAlaniId = a.CalismaAlaniId
                          WHERE a.AnketId = @p0
                            AND (a.SahipPersonelId = @p1 OR (cau.PersonelId = @p1 AND ISNULL(cau.Pasif, 0) = 0))",
                        anketId.Value,
                        personelId.Value).FirstOrDefault() > 0;
                }
            }
            catch
            {
            }

            return false;
        }

        private Anket CalismaAlaniAnketGetir(int? anketId)
        {
            if (!AnketCalismaAlanindaMi(anketId))
            {
                return null;
            }

            return db.Anket.FirstOrDefault(x => x.AnketId == anketId.Value);
        }

        private List<Anket> CalismaAlaniAnketleri()
        {
            var calismaAlaniId = AktifCalismaAlaniId();
            var personelId = AktifPersonelId();

            try
            {
                if (calismaAlaniId.HasValue)
                {
                    return db.Anket.SqlQuery(
                        "SELECT * FROM dbo.Anket WHERE CalismaAlaniId = @p0 ORDER BY AnketId DESC",
                        calismaAlaniId.Value).ToList();
                }

                if (personelId.HasValue)
                {
                    return db.Anket.SqlQuery(
                        @"SELECT DISTINCT a.*
                          FROM dbo.Anket a
                          LEFT JOIN dbo.CalismaAlaniUye cau ON cau.CalismaAlaniId = a.CalismaAlaniId
                          WHERE a.SahipPersonelId = @p0
                             OR (cau.PersonelId = @p0 AND ISNULL(cau.Pasif, 0) = 0)
                          ORDER BY a.AnketId DESC",
                        personelId.Value).ToList();
                }
            }
            catch
            {
            }

            return new List<Anket>();
        }

        private static string BankaSqlTablo(string tablo)
        {
            return tablo switch
            {
                "Soru" => "dbo.Soru",
                "SoruGrup" => "dbo.SoruGrup",
                "Cevap" => "dbo.Cevap",
                "CevapGrup" => "dbo.CevapGrup",
                _ => throw new ArgumentOutOfRangeException(nameof(tablo), tablo, null)
            };
        }

        private static string BankaSqlNesneAdi(string tablo)
        {
            return tablo switch
            {
                "Soru" => "dbo.Soru",
                "SoruGrup" => "dbo.SoruGrup",
                "Cevap" => "dbo.Cevap",
                "CevapGrup" => "dbo.CevapGrup",
                _ => throw new ArgumentOutOfRangeException(nameof(tablo), tablo, null)
            };
        }

        private static string BankaSqlKolon(string kolon)
        {
            return kolon switch
            {
                "SoruId" => "[SoruId]",
                "SoruAdi" => "[SoruAdi]",
                "SoruGrupId" => "[SoruGrupId]",
                "SoruGrupAdi" => "[SoruGrupAdi]",
                "SoruGrupSira" => "[SoruGrupSira]",
                "CevapId" => "[CevapId]",
                "CevapAdi" => "[CevapAdi]",
                "CevapGrupId" => "[CevapGrupId]",
                "CevapGrupAdi" => "[CevapGrupAdi]",
                _ => throw new ArgumentOutOfRangeException(nameof(kolon), kolon, null)
            };
        }

        private bool BankaTablosuCalismaAlaniKolonuVarMi(string tablo)
        {
            try
            {
                return db.Database.SqlQuery<int>(
                    "SELECT CASE WHEN COL_LENGTH(@p0, N'CalismaAlaniId') IS NULL THEN 0 ELSE 1 END",
                    BankaSqlNesneAdi(tablo)).FirstOrDefault() == 1;
            }
            catch
            {
                return false;
            }
        }

        private bool BankaCalismaAlaniHazirla()
        {
            var calismaAlaniId = AktifCalismaAlaniId();
            if (!calismaAlaniId.HasValue)
            {
                return false;
            }

            try
            {
                db.Database.ExecuteSqlCommand(
                    @"
IF COL_LENGTH('dbo.SoruGrup', 'CalismaAlaniId') IS NULL
    ALTER TABLE dbo.SoruGrup ADD CalismaAlaniId INT NULL;

IF COL_LENGTH('dbo.Soru', 'CalismaAlaniId') IS NULL
    ALTER TABLE dbo.Soru ADD CalismaAlaniId INT NULL;

IF COL_LENGTH('dbo.CevapGrup', 'CalismaAlaniId') IS NULL
    ALTER TABLE dbo.CevapGrup ADD CalismaAlaniId INT NULL;

IF COL_LENGTH('dbo.Cevap', 'CalismaAlaniId') IS NULL
    ALTER TABLE dbo.Cevap ADD CalismaAlaniId INT NULL;

UPDATE sg
SET CalismaAlaniId = kaynak.CalismaAlaniId
FROM dbo.SoruGrup sg
INNER JOIN (
    SELECT ag.SoruGrupId, MIN(a.CalismaAlaniId) AS CalismaAlaniId
    FROM dbo.AnketGrup ag
    INNER JOIN dbo.Anket a ON a.AnketId = ag.AnketId
    WHERE a.CalismaAlaniId IS NOT NULL
    GROUP BY ag.SoruGrupId
) kaynak ON kaynak.SoruGrupId = sg.SoruGrupId
WHERE sg.CalismaAlaniId IS NULL;

UPDATE s
SET CalismaAlaniId = sg.CalismaAlaniId
FROM dbo.Soru s
INNER JOIN dbo.SoruGrup sg ON sg.SoruGrupId = s.SoruGrupId
WHERE s.CalismaAlaniId IS NULL
  AND sg.CalismaAlaniId IS NOT NULL;

UPDATE cg
SET CalismaAlaniId = kaynak.CalismaAlaniId
FROM dbo.CevapGrup cg
INNER JOIN (
    SELECT s.CevapGrupId, MIN(s.CalismaAlaniId) AS CalismaAlaniId
    FROM dbo.Soru s
    WHERE s.CevapGrupId IS NOT NULL
      AND s.CalismaAlaniId IS NOT NULL
    GROUP BY s.CevapGrupId
) kaynak ON kaynak.CevapGrupId = cg.CevapGrupId
WHERE cg.CalismaAlaniId IS NULL;

UPDATE c
SET CalismaAlaniId = cg.CalismaAlaniId
FROM dbo.Cevap c
INNER JOIN dbo.CevapGrup cg ON cg.CevapGrupId = c.CevapGrupId
WHERE c.CalismaAlaniId IS NULL
  AND cg.CalismaAlaniId IS NOT NULL;

UPDATE dbo.SoruGrup SET CalismaAlaniId = @p0 WHERE CalismaAlaniId IS NULL;
UPDATE dbo.Soru SET CalismaAlaniId = @p0 WHERE CalismaAlaniId IS NULL;
UPDATE dbo.CevapGrup SET CalismaAlaniId = @p0 WHERE CalismaAlaniId IS NULL;
UPDATE dbo.Cevap SET CalismaAlaniId = @p0 WHERE CalismaAlaniId IS NULL;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_SoruGrup_CalismaAlaniId' AND object_id = OBJECT_ID(N'dbo.SoruGrup'))
    CREATE INDEX IX_SoruGrup_CalismaAlaniId ON dbo.SoruGrup(CalismaAlaniId);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Soru_CalismaAlaniId' AND object_id = OBJECT_ID(N'dbo.Soru'))
    CREATE INDEX IX_Soru_CalismaAlaniId ON dbo.Soru(CalismaAlaniId);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_CevapGrup_CalismaAlaniId' AND object_id = OBJECT_ID(N'dbo.CevapGrup'))
    CREATE INDEX IX_CevapGrup_CalismaAlaniId ON dbo.CevapGrup(CalismaAlaniId);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Cevap_CalismaAlaniId' AND object_id = OBJECT_ID(N'dbo.Cevap'))
    CREATE INDEX IX_Cevap_CalismaAlaniId ON dbo.Cevap(CalismaAlaniId);",
                    calismaAlaniId.Value);

                return true;
            }
            catch
            {
                return BankaTablosuCalismaAlaniKolonuVarMi("Soru")
                    && BankaTablosuCalismaAlaniKolonuVarMi("SoruGrup")
                    && BankaTablosuCalismaAlaniKolonuVarMi("Cevap")
                    && BankaTablosuCalismaAlaniKolonuVarMi("CevapGrup");
            }
        }

        private List<T> CalismaAlaniBankaKayitlari<T>(string tablo, string siralamaKolonu) where T : class
        {
            var calismaAlaniId = AktifCalismaAlaniId();
            if (!calismaAlaniId.HasValue || !BankaCalismaAlaniHazirla())
            {
                return new List<T>();
            }

            var sql = $"SELECT * FROM {BankaSqlTablo(tablo)} WHERE CalismaAlaniId = @p0 ORDER BY {BankaSqlKolon(siralamaKolonu)}";
            return db.Set<T>().SqlQuery(sql, calismaAlaniId.Value).ToList();
        }

        private T CalismaAlaniBankaKaydiGetir<T>(string tablo, string idKolonu, int id) where T : class
        {
            var calismaAlaniId = AktifCalismaAlaniId();
            if (!calismaAlaniId.HasValue || !BankaCalismaAlaniHazirla())
            {
                return null;
            }

            var sql = $"SELECT * FROM {BankaSqlTablo(tablo)} WHERE {BankaSqlKolon(idKolonu)} = @p0 AND CalismaAlaniId = @p1";
            return db.Set<T>().SqlQuery(sql, id, calismaAlaniId.Value).FirstOrDefault();
        }

        private bool CalismaAlaniBankaKaydiVarMi(string tablo, string idKolonu, int? id)
        {
            var calismaAlaniId = AktifCalismaAlaniId();
            if (!id.HasValue || !calismaAlaniId.HasValue || !BankaCalismaAlaniHazirla())
            {
                return false;
            }

            var sql = $"SELECT COUNT(1) FROM {BankaSqlTablo(tablo)} WHERE {BankaSqlKolon(idKolonu)} = @p0 AND CalismaAlaniId = @p1";
            return db.Database.SqlQuery<int>(sql, id.Value, calismaAlaniId.Value).FirstOrDefault() > 0;
        }

        private bool CalismaAlaniBankaSecimiGecerliMi(string tablo, string idKolonu, int? id)
        {
            return !id.HasValue || CalismaAlaniBankaKaydiVarMi(tablo, idKolonu, id);
        }

        private void CalismaAlaniBankaKaydinaBagla(string tablo, string idKolonu, int id)
        {
            var calismaAlaniId = AktifCalismaAlaniId();
            if (!calismaAlaniId.HasValue || !BankaCalismaAlaniHazirla())
            {
                return;
            }

            var sql = $"UPDATE {BankaSqlTablo(tablo)} SET CalismaAlaniId = @p0 WHERE {BankaSqlKolon(idKolonu)} = @p1";
            db.Database.ExecuteSqlCommand(sql, calismaAlaniId.Value, id);
        }

        private bool CalismaAlaniAyniAnketAdiVar(string anketAdi, int? haricAnketId = null)
        {
            var temizAd = (anketAdi ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(temizAd))
            {
                return false;
            }

            var haricId = haricAnketId ?? 0;
            var calismaAlaniId = AktifCalismaAlaniId();
            var personelId = AktifPersonelId();

            try
            {
                if (calismaAlaniId.HasValue)
                {
                    return db.Database.SqlQuery<int>(
                        @"SELECT COUNT(1)
                          FROM dbo.Anket
                          WHERE ISNULL(CalismaAlaniId, 0) = @p0
                            AND AnketId <> @p1
                            AND LOWER(LTRIM(RTRIM(ISNULL(AnketAdi, N'')))) = LOWER(LTRIM(RTRIM(@p2)))",
                        calismaAlaniId.Value,
                        haricId,
                        temizAd).FirstOrDefault() > 0;
                }

                if (personelId.HasValue)
                {
                    return db.Database.SqlQuery<int>(
                        @"SELECT COUNT(1)
                          FROM (
                              SELECT DISTINCT a.AnketId
                              FROM dbo.Anket a
                              LEFT JOIN dbo.CalismaAlaniUye cau ON cau.CalismaAlaniId = a.CalismaAlaniId
                              WHERE (a.SahipPersonelId = @p0 OR (cau.PersonelId = @p0 AND ISNULL(cau.Pasif, 0) = 0))
                                AND a.AnketId <> @p1
                                AND LOWER(LTRIM(RTRIM(ISNULL(a.AnketAdi, N'')))) = LOWER(LTRIM(RTRIM(@p2)))
                          ) q",
                        personelId.Value,
                        haricId,
                        temizAd).FirstOrDefault() > 0;
                }
            }
            catch
            {
            }

            return db.Anket.ToList().Any(x =>
                x.AnketId != haricId &&
                !string.IsNullOrWhiteSpace(x.AnketAdi) &&
                string.Equals(x.AnketAdi.Trim(), temizAd, StringComparison.CurrentCultureIgnoreCase));
        }

        private bool MailOnayKoduGonder(string email, string adSoyad, string kod, out string hataMesaji)
        {
            hataMesaji = string.Empty;
            var smtpAyar = db.smtpayar.OrderByDescending(x => x.MailId).FirstOrDefault();
            if (smtpAyar == null)
            {
                hataMesaji = "SMTP ayarÄ± bulunamadÄ±.";
                return false;
            }

            try
            {
                using var msg = new MailMessage();
                msg.To.Add(email);
                msg.From = new MailAddress(smtpAyar.Gonderen);
                msg.Subject = "Survey by Aslana Teknoloji e-posta doÄŸrulama kodu";
                msg.IsBodyHtml = true;
                msg.BodyEncoding = Encoding.UTF8;
                msg.Body =
                    $"<h2>Merhaba {WebUtility.HtmlEncode(adSoyad)},</h2>" +
                    "<p>Survey by Aslana Teknoloji hesabÄ±nÄ±zÄ± aktifleÅŸtirmek iÃ§in doÄŸrulama kodunuz:</p>" +
                    $"<h1 style=\"letter-spacing:6px\">{kod}</h1>" +
                    $"<p>Bu kod {MailOnayKoduGecerlilikDakika} dakika geÃ§erlidir.</p>";

                using var smtp = new SmtpClient
                {
                    Host = smtpAyar.Sunucu,
                    Port = smtpAyar.Portu,
                    UseDefaultCredentials = false,
                    Credentials = new NetworkCredential(smtpAyar.UserName, smtpAyar.Password),
                    EnableSsl = smtpAyar.Ssli,
                    Timeout = 15000,
                    DeliveryMethod = SmtpDeliveryMethod.Network
                };

                smtp.Send(msg);
                return true;
            }
            catch (Exception ex)
            {
                hataMesaji = ex.Message;
                return false;
            }
        }

        public ActionResult Giris(string panel = null)
        {
            Session.Clear();
            if (string.Equals(panel, "participant", StringComparison.OrdinalIgnoreCase))
            {
                ViewBag.ActiveAuthPanel = "participant";
            }
            CaptchaYenile();
            return View();
        }
        [ValidateAntiForgeryToken(), HttpPost]
        public async System.Threading.Tasks.Task<ActionResult> Giris(string objUser, string objUser1, string guvenlikCevabi)
        {
            if (GoogleRecaptchaAktifMi())
            {
                var recaptcha = await GoogleRecaptchaDogrula();
                if (!recaptcha.Basarili)
                {
                    ViewBag.Uyari = recaptcha.Mesaj;
                    CaptchaYenile();
                    return View();
                }
            }
            else if (!CaptchaDogruMu(guvenlikCevabi))
            {
                ViewBag.Uyari = "GÃ¼venlik sorusunu kontrol edin.";
                CaptchaYenile();
                return View();
            }

            var obj = db.Personel.Where(a => a.KullaniciAdi.Equals(objUser) && a.Sifre.Equals(objUser1)).FirstOrDefault();

            if (obj != null && obj.KullaniciAdi == objUser && obj.Sifre == objUser1 && obj.Pasif != true)
            {
                var guvenlik = GuvenlikBilgisiGetir(obj.PersonelId);
                if (guvenlik?.MailOnaylandi == false)
                {
                    ViewBag.Uyari = "E-posta onayÄ±nÄ±z tamamlanmamÄ±ÅŸ. Mail adresinize gelen kodu onaylayÄ±n.";
                    ViewBag.ActiveAuthPanel = "verify";
                    ViewBag.VerifyEmail = obj.Mail;
                    CaptchaYenile();
                    return View();
                }

                PersonelOturumuAc(obj);
                return RedirectToAction("Indexgosterge", "Home", new { id = obj.PersonelId });
            }

            else
            {
                ViewBag.Uyari = "KullanÄ±cÄ± AdÄ± veya Åifreyi Kontrol Ediniz";
            }
            CaptchaYenile();
            return View();

        }

        [ValidateAntiForgeryToken(), HttpPost]
        public ActionResult KatilimKoduSorgula(string katilimKodu)
        {
            katilimKodu = (katilimKodu ?? string.Empty).Trim();
            if (!int.TryParse(katilimKodu, out var kod) || !KatilimciKoduGecerliMi(kod))
            {
                ViewBag.KatilimSorguError = "KatÄ±lÄ±m kodu 9 haneli olmalÄ±.";
                ViewBag.ActiveAuthPanel = "participant";
                CaptchaYenile();
                return View("Giris");
            }

            var kodKullanilmis = db.Havuz.Any(x => x.Isimsiz == kod || x.UserId == kod)
                || db.Izledim.Any(x => x.UseId == kod);

            if (!kodKullanilmis)
            {
                ViewBag.KatilimSorguError = "Bu koda ait katÄ±lÄ±m kaydÄ± bulunamadÄ±.";
                ViewBag.ActiveAuthPanel = "participant";
                CaptchaYenile();
                return View("Giris");
            }

            KatilimciKoduHatirla(kod);
            return RedirectToAction("KatilimPortal", "Home", new { kod, user = kod });
        }

        [ValidateAntiForgeryToken(), HttpPost]
        public ActionResult KayitOl(string fullName, string companyName, string email, string phone, string username, string password, string passwordConfirm, string guvenlikCevabi)
        {
            if (!CaptchaDogruMu(guvenlikCevabi))
            {
                ViewBag.RegisterError = "GÃ¼venlik sorusunu kontrol edin.";
                ViewBag.ActiveAuthPanel = "register";
                CaptchaYenile();
                return View("Giris");
            }

            if (string.IsNullOrWhiteSpace(fullName) ||
                string.IsNullOrWhiteSpace(companyName) ||
                string.IsNullOrWhiteSpace(email) ||
                string.IsNullOrWhiteSpace(username) ||
                string.IsNullOrWhiteSpace(password))
            {
                ViewBag.RegisterError = "LÃ¼tfen zorunlu alanlarÄ± doldurun.";
                ViewBag.ActiveAuthPanel = "register";
                CaptchaYenile();
                return View("Giris");
            }

            if (!string.Equals(password, passwordConfirm, StringComparison.Ordinal))
            {
                ViewBag.RegisterError = "Åifreler birbiriyle aynÄ± olmalÄ±.";
                ViewBag.ActiveAuthPanel = "register";
                CaptchaYenile();
                return View("Giris");
            }

            username = username.Trim();
            email = email.Trim();

            var kullaniciVar = db.Personel.Any(x => x.KullaniciAdi == username || x.Mail == email);
            if (kullaniciVar)
            {
                ViewBag.RegisterError = "Bu kullanÄ±cÄ± adÄ± veya e-posta ile kayÄ±t var.";
                ViewBag.ActiveAuthPanel = "register";
                CaptchaYenile();
                return View("Giris");
            }

            var onayKodu = MailOnayKoduUret();
            var yeniUye = new Personel
            {
                PersonelAdi = fullName.Trim(),
                Mail = email,
                Telefon = phone?.Trim(),
                Adres = companyName.Trim(),
                KullaniciAdi = username,
                Sifre = password,
                KayitTarihi = DateTime.Now,
                Admin = true,
                Pasif = true
            };

            db.Personel.Add(yeniUye);
            db.SaveChanges();

            try
            {
                db.Database.ExecuteSqlCommand(
                    @"UPDATE dbo.Personel
                      SET MailOnaylandi = 0,
                          MailOnayKodu = @p0,
                          MailOnayKoduTarihi = GETDATE(),
                          GirisKaynagi = N'Eposta'
                      WHERE PersonelId = @p1",
                    onayKodu,
                    yeniUye.PersonelId);
            }
            catch
            {
                ViewBag.RegisterError = "GÃ¼venlik kolonlarÄ± SQL tarafÄ±nda henÃ¼z eklenmemiÅŸ gÃ¶rÃ¼nÃ¼yor.";
                ViewBag.ActiveAuthPanel = "register";
                CaptchaYenile();
                return View("Giris");
            }

            if (!MailOnayKoduGonder(email, fullName, onayKodu, out var mailHatasi))
            {
                ViewBag.RegisterError = $"Hesap oluÅŸturuldu ama doÄŸrulama maili gÃ¶nderilemedi: {mailHatasi}";
                ViewBag.ActiveAuthPanel = "verify";
                ViewBag.VerifyEmail = email;
                CaptchaYenile();
                return View("Giris");
            }

            ViewBag.RegisterSuccess = "DoÄŸrulama kodu mail adresinize gÃ¶nderildi.";
            ViewBag.ActiveAuthPanel = "verify";
            ViewBag.VerifyEmail = email;
            CaptchaYenile();
            return View("Giris");
        }

        [ValidateAntiForgeryToken(), HttpPost]
        public ActionResult KayitOnayla(string email, string onayKodu, string guvenlikCevabi)
        {
            if (!CaptchaDogruMu(guvenlikCevabi))
            {
                ViewBag.VerifyError = "GÃ¼venlik sorusunu kontrol edin.";
                ViewBag.ActiveAuthPanel = "verify";
                ViewBag.VerifyEmail = email;
                CaptchaYenile();
                return View("Giris");
            }

            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(onayKodu))
            {
                ViewBag.VerifyError = "E-posta ve doÄŸrulama kodu zorunludur.";
                ViewBag.ActiveAuthPanel = "verify";
                ViewBag.VerifyEmail = email;
                CaptchaYenile();
                return View("Giris");
            }

            email = email.Trim();
            onayKodu = onayKodu.Trim();

            var personel = db.Personel.FirstOrDefault(x => x.Mail == email);
            if (personel == null)
            {
                ViewBag.VerifyError = "Bu e-posta adresiyle kayÄ±t bulunamadÄ±.";
                ViewBag.ActiveAuthPanel = "verify";
                ViewBag.VerifyEmail = email;
                CaptchaYenile();
                return View("Giris");
            }

            var guvenlik = GuvenlikBilgisiGetir(personel.PersonelId);
            if (guvenlik == null ||
                !string.Equals(guvenlik.MailOnayKodu, onayKodu, StringComparison.Ordinal) ||
                guvenlik.MailOnayKoduTarihi == null ||
                guvenlik.MailOnayKoduTarihi.Value.AddMinutes(MailOnayKoduGecerlilikDakika) < DateTime.Now)
            {
                ViewBag.VerifyError = "DoÄŸrulama kodu hatalÄ± veya sÃ¼resi dolmuÅŸ.";
                ViewBag.ActiveAuthPanel = "verify";
                ViewBag.VerifyEmail = email;
                CaptchaYenile();
                return View("Giris");
            }

            personel.Pasif = false;
            db.Entry(personel).State = EntityState.Modified;
            db.SaveChanges();

            db.Database.ExecuteSqlCommand(
                @"UPDATE dbo.Personel
                  SET MailOnaylandi = 1,
                      MailOnayKodu = NULL,
                      MailOnayKoduTarihi = NULL,
                      GirisKaynagi = N'Eposta'
                  WHERE PersonelId = @p0",
                personel.PersonelId);

            PersonelOturumuAc(personel);
            return RedirectToAction("Indexgosterge", "Home", new { id = personel.PersonelId });
        }

        public ActionResult GoogleGiris()
        {
            if (!GoogleAyariHazirMi())
            {
                ViewBag.Uyari = "Google giriÅŸ iÃ§in ClientId ve ClientSecret appsettings.json iÃ§ine eklenmeli.";
                CaptchaYenile();
                return View("Giris");
            }

            var redirectUrl = Url.Action("GoogleDonus", "Home");
            var properties = new AuthenticationProperties { RedirectUri = redirectUrl };
            return Challenge(properties, GoogleDefaults.AuthenticationScheme);
        }

        public async Task<ActionResult> GoogleDonus()
        {
            var sonuc = await HttpContext.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            if (!sonuc.Succeeded || sonuc.Principal == null)
            {
                ViewBag.Uyari = "Google hesabÄ± doÄŸrulanamadÄ±.";
                CaptchaYenile();
                return View("Giris");
            }

            var email = sonuc.Principal.FindFirstValue(ClaimTypes.Email);
            var adSoyad = sonuc.Principal.FindFirstValue(ClaimTypes.Name);
            var googleKimlikId = sonuc.Principal.FindFirstValue(ClaimTypes.NameIdentifier);

            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(googleKimlikId))
            {
                ViewBag.Uyari = "Google hesabÄ±ndan e-posta bilgisi alÄ±namadÄ±.";
                CaptchaYenile();
                return View("Giris");
            }

            if (email.Length > 50)
            {
                ViewBag.Uyari = "E-posta adresi 50 karakterden uzun. Personel.Mail alanÄ±nÄ± bÃ¼yÃ¼tmemiz gerekiyor.";
                CaptchaYenile();
                return View("Giris");
            }

            Personel personel;
            try
            {
                var personelId = db.Database.SqlQuery<int?>(
                    @"SELECT TOP 1 PersonelId
                      FROM dbo.Personel
                      WHERE GoogleKimlikId = @p0 OR Mail = @p1
                      ORDER BY PersonelId",
                    googleKimlikId,
                    email).FirstOrDefault();

                personel = personelId.HasValue ? db.Personel.Find(personelId.Value) : null;
            }
            catch
            {
                ViewBag.Uyari = "Google gÃ¼venlik kolonlarÄ± SQL tarafÄ±nda henÃ¼z eklenmemiÅŸ gÃ¶rÃ¼nÃ¼yor.";
                CaptchaYenile();
                return View("Giris");
            }

            if (personel == null)
            {
                var kullaniciAdi = KullaniciAdiUret(email);
                var temelKullaniciAdi = kullaniciAdi;
                var sayac = 1;
                while (db.Personel.Any(x => x.KullaniciAdi == kullaniciAdi))
                {
                    sayac++;
                    kullaniciAdi = $"{temelKullaniciAdi}{sayac}";
                    if (kullaniciAdi.Length > 50)
                    {
                        kullaniciAdi = kullaniciAdi.Substring(0, 50);
                    }
                }

                personel = new Personel
                {
                    PersonelAdi = string.IsNullOrWhiteSpace(adSoyad) ? email : adSoyad,
                    Mail = email,
                    Adres = "Google HesabÄ±",
                    KullaniciAdi = kullaniciAdi,
                    Sifre = Guid.NewGuid().ToString("N").Substring(0, 12),
                    KayitTarihi = DateTime.Now,
                    Admin = true,
                    Pasif = false
                };

                db.Personel.Add(personel);
                db.SaveChanges();
            }
            else if (personel.Pasif == true)
            {
                personel.Pasif = false;
                db.Entry(personel).State = EntityState.Modified;
                db.SaveChanges();
            }

            db.Database.ExecuteSqlCommand(
                @"UPDATE dbo.Personel
                  SET GoogleKimlikId = @p0,
                      MailOnaylandi = 1,
                      MailOnayKodu = NULL,
                      MailOnayKoduTarihi = NULL,
                      GirisKaynagi = N'Google'
                  WHERE PersonelId = @p1",
                googleKimlikId,
                personel.PersonelId);

            PersonelOturumuAc(personel);
            return RedirectToAction("Indexgosterge", "Home", new { id = personel.PersonelId });
        }

        public ActionResult AnketGiris()
        {
            return RedirectToAction("Giris", "Home", new { panel = "participant" });

        }
        [ValidateAntiForgeryToken(), HttpPost]
        public ActionResult AnketGiris(string objUser)
        {
            var obj = KayitliKatilimciSorgusu().Where(a => a.UserTc.Equals(objUser)).FirstOrDefault();

            if (obj != null && obj.UserTc == objUser && obj.Pasif != true)
            {
                KatilimciOturumuAc(obj);

                return RedirectToAction("AnketGirisIndex", "Home", new { id = obj.UserId });
            }

            else
            {
                ViewBag.Uyari = "Tc No Kontrol Ediniz";
            }
            return View();

        }
        private void PrepareAnonymousSurveyLookups()
        {
            ViewBag.Cin = db.Cinsiyet.OrderBy(x => x.CinsiyetAdi).ToList()
                .Select(x => new SelectListItem { Text = x.CinsiyetAdi, Value = x.CinsiyetId.ToString() }).ToList();
            ViewBag.Egi = db.Egitim.OrderBy(x => x.EgitimAdi).ToList()
                .Select(x => new SelectListItem { Text = x.EgitimAdi, Value = x.EgitimId.ToString() }).ToList();
            ViewBag.Bol = db.Bolum.OrderBy(x => x.BolumAdi).ToList()
                .Select(x => new SelectListItem { Text = x.BolumAdi, Value = x.BolumId.ToString() }).ToList();
            ViewBag.Sub = db.Sube.OrderBy(x => x.SubeAdi).ToList()
                .Select(x => new SelectListItem { Text = x.SubeAdi, Value = x.SubeId.ToString() }).ToList();
            ViewBag.Blg = db.Bolge.OrderBy(x => x.BolgeAdi).ToList()
                .Select(x => new SelectListItem { Text = x.BolgeAdi, Value = x.BolgeId.ToString() }).ToList();
            ViewBag.Dep = db.Departman.OrderBy(x => x.DepartmanAdi).ToList()
                .Select(x => new SelectListItem { Text = x.DepartmanAdi, Value = x.DepartmanId.ToString() }).ToList();
            ViewBag.Yak = db.Yaka.OrderBy(x => x.YakaAdi).ToList()
                .Select(x => new SelectListItem { Text = x.YakaAdi, Value = x.YakaId.ToString() }).ToList();
            ViewBag.Seh = db.Sehir.OrderBy(x => x.SehiarAdi).ToList()
                .Select(x => new SelectListItem { Text = x.SehiarAdi, Value = x.SehirId.ToString() }).ToList();
        }

        public ActionResult AnketGirisIsimsiz()
        {
            Session.Clear();
            PrepareAnonymousSurveyLookups();
            return View(new User());
        }

        [ValidateAntiForgeryToken(), HttpPost]
        public ActionResult AnketGirisIsimsiz(User objUser)
        {
            PrepareAnonymousSurveyLookups();

            string ip = GetClientIp();
            Session["ipadres"] = ip;

            ViewBag.Uyari = "Tc No Kontrol Ediniz";
            return View(objUser);
        }
        public async Task<ActionResult> GirisCikis()
        {
            await OturumuKapatAsync();
            return RedirectToAction("Giris", "Home");
        }

        public async Task<ActionResult> KatilimciCikis()
        {
            await OturumuKapatAsync();
            return RedirectToAction("Giris", "Home", new { panel = "participant" });
        }
        public ActionResult Index()
        {
            if (Session["id"] == null || Session["admin"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }

            return RedirectToAction("Indexgosterge", "Home", new { idi = Session["id"] });
        }
        public ActionResult AssessmentWizard()
        {
            if (Session["id"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }
            if (Session["admin"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }

            return View();
        }

        private static bool AnketGorselUzantisiGecerliMi(string dosyaAdi)
        {
            var uzanti = Path.GetExtension(dosyaAdi ?? string.Empty).ToLowerInvariant();
            return uzanti == ".jpg" || uzanti == ".jpeg" || uzanti == ".png" || uzanti == ".webp" || uzanti == ".gif";
        }

        private static string AnketGorselDosyaAdiTemizle(string dosyaAdi)
        {
            var fileName = Path.GetFileName((dosyaAdi ?? string.Empty).Trim());
            var name = Path.GetFileNameWithoutExtension(fileName);
            name = Regex.Replace(name ?? string.Empty, @"[^\p{L}\p{Nd}\-_ ]", "-");
            name = Regex.Replace(name, @"\s+", "-").Trim('-', '_');
            if (string.IsNullOrWhiteSpace(name))
            {
                name = "anket-gorsel";
            }

            var maxNameLength = 42;
            if (name.Length > maxNameLength)
            {
                name = name.Substring(0, maxNameLength).Trim('-', '_');
            }

            return name + "-" + DateTime.Now.ToString("yyyyMMddHHmmssfff") + ".webp";
        }

        private void AnketGorselWebpKaydet(IFormFile dosya, string dosyaAdi)
        {
            using var stream = dosya.OpenReadStream();
            using var image = SixLabors.ImageSharp.Image.Load(stream);
            if (image.Width > AnketGorselMaksimumKenar || image.Height > AnketGorselMaksimumKenar)
            {
                image.Mutate(x => x.Resize(new ResizeOptions
                {
                    Mode = ResizeMode.Max,
                    Size = new SixLabors.ImageSharp.Size(AnketGorselMaksimumKenar, AnketGorselMaksimumKenar)
                }));
            }

            var hedefPath = MapPath(AnketGorselKlasoru + dosyaAdi);
            var hedefKlasor = Path.GetDirectoryName(hedefPath);
            if (!string.IsNullOrWhiteSpace(hedefKlasor) && !Directory.Exists(hedefKlasor))
            {
                Directory.CreateDirectory(hedefKlasor);
            }

            image.SaveAsWebp(hedefPath, new WebpEncoder { Quality = AnketGorselWebpKalite });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult AssessmentWizardImageUpload(IFormFile file)
        {
            if (Session["id"] == null || Session["admin"] == null)
            {
                return Json(new { success = false, message = "Oturum sureniz doldu. Lutfen tekrar giris yapin." });
            }

            if (file == null || file.Length <= 0)
            {
                return Json(new { success = false, message = "Yuklenecek gorsel bulunamadi." });
            }

            if (file.Length > 8 * 1024 * 1024)
            {
                return Json(new { success = false, message = "Gorsel en fazla 8 MB olabilir." });
            }

            if (!AnketGorselUzantisiGecerliMi(file.FileName))
            {
                return Json(new { success = false, message = "Gorsel jpg, jpeg, png, gif veya webp olmali." });
            }

            try
            {
                var dosyaAdi = AnketGorselDosyaAdiTemizle(file.FileName);
                AnketGorselWebpKaydet(file, dosyaAdi);
                return Json(new
                {
                    success = true,
                    fileName = dosyaAdi,
                    url = Url.Content(AnketGorselKlasoru + dosyaAdi),
                    message = "Gorsel WebP olarak yuklendi."
                });
            }
            catch
            {
                return Json(new { success = false, message = "Gorsel okunamadi veya WebP'ye cevrilemedi." });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async System.Threading.Tasks.Task<ActionResult> AssessmentWizardGenerateAi(
            string topic,
            string mode,
            int count,
            string difficulty,
            string questionType)
        {
            if (Session["id"] == null || Session["admin"] == null)
            {
                return Json(new { success = false, message = "Oturum sureniz doldu. Lutfen tekrar giris yapin." });
            }

            topic = (topic ?? string.Empty).Trim();
            mode = string.IsNullOrWhiteSpace(mode) ? "exam" : mode.Trim();
            difficulty = string.IsNullOrWhiteSpace(difficulty) ? "orta" : difficulty.Trim();
            questionType = string.IsNullOrWhiteSpace(questionType) ? "multiple" : questionType.Trim();
            mode = InferAssessmentModeFromTopic(topic, mode);
            count = ClampWizardNumber(count <= 0 ? 5 : count, 1, 25);

            if (topic.Length < 3)
            {
                return Json(new { success = false, message = "AI ile uretmek icin konu veya egitim metni girin." });
            }

            var konuKontrol = ValidateAiAssessmentTopic(topic);
            if (!konuKontrol.Valid)
            {
                return Json(new { success = false, message = konuKontrol.Message });
            }

            var aiAyar = OpenAiAyarlariOku();
            if (!aiAyar.TableReady)
            {
                return Json(new { success = false, message = "AI ayar tablosu yok. Once Ayarlar > Yapay Zeka Ayarlari icin SQL scriptini calistirin." });
            }

            if (!aiAyar.Aktif)
            {
                return Json(new { success = false, message = "Studio AI uretimi platform tarafindan gecici olarak pasif durumda." });
            }

            if (string.IsNullOrWhiteSpace(aiAyar.ApiKey)
                || aiAyar.ApiKey.Contains("BURAYA_OPENAI_API_KEY", StringComparison.OrdinalIgnoreCase))
            {
                return Json(new { success = false, message = "Studio AI baglantisi platform tarafinda tanimli degil. Lutfen platform yoneticisine bildirin." });
            }

            var endpoint = NormalizeOpenAiEndpoint(aiAyar.Endpoint);
            var requestedGroupCount = ExtractRequestedGroupCount(topic, count);
            var prompt = BuildAssessmentAiPrompt(topic, mode, count, difficulty, questionType, requestedGroupCount);

            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(70) };
                http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", aiAyar.ApiKey);

                async System.Threading.Tasks.Task<AssessmentWizardAiResult> RequestGeneratedAsync(string promptText)
                {
                    var requestBody = new
                    {
                        model = string.IsNullOrWhiteSpace(aiAyar.ChatModel) ? "gpt-4o-mini" : aiAyar.ChatModel,
                        temperature = 0.35,
                        response_format = new { type = "json_object" },
                        messages = new[]
                        {
                            new
                            {
                                role = "system",
                                content = "Turkce kurumsal egitim, sinav ve anket sorulari ureten titiz bir olcme-degerlendirme uzmanisin. Yalnizca gecerli JSON dondur."
                            },
                            new { role = "user", content = promptText }
                        }
                    };

                    var response = await http.PostAsync(
                        endpoint + "/chat/completions",
                        new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json"));

                    var raw = await response.Content.ReadAsStringAsync();
                    if (!response.IsSuccessStatusCode)
                    {
                        throw new InvalidOperationException("OpenAI istegi basarisiz: " + ExtractOpenAiError(raw));
                    }

                    var content = ExtractOpenAiContent(raw);
                    var json = ExtractJsonObject(content);
                    return JsonSerializer.Deserialize<AssessmentWizardAiResult>(
                        json,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                }

                var generated = await RequestGeneratedAsync(prompt);
                var questions = NormalizeGeneratedQuestions(generated?.Questions, count, questionType, mode);
                if (questions.Count > 0 && questions.Count < count)
                {
                    var missing = count - questions.Count;
                    var existingTitles = string.Join("\n", questions.Select(x => "- " + x.Title).Take(40));
                    var existingGroups = string.Join("\n", questions
                        .Select(x => x.Group)
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .Take(20)
                        .Select(x => "- " + x));

                    var supplementPrompt = BuildAssessmentAiPrompt($@"
{topic}

Daha once uretilen soru basliklari:
{existingTitles}

Mevcut soru gruplari:
{existingGroups}

Bu basliklari tekrar etme. Eksik kalan {missing} yeni soruyu tamamla.
Yeni sorularin group alaninda mumkunse mevcut soru gruplarindan birini kullan.", mode, missing, difficulty, questionType);

                    try
                    {
                        var supplement = await RequestGeneratedAsync(supplementPrompt);
                        var extraQuestions = NormalizeGeneratedQuestions(supplement?.Questions, missing, questionType, mode)
                            .Where(x => !questions.Any(q => string.Equals(q.Title, x.Title, StringComparison.OrdinalIgnoreCase)))
                            .Take(missing)
                            .ToList();

                        questions.AddRange(extraQuestions);
                    }
                    catch
                    {
                        // Ilk uretim kullanilabilir durumdaysa eksik tamamlama hatasi kullanici akisini kesmesin.
                    }
                }

                if (!questions.Any())
                {
                    return Json(new { success = false, message = "AI yaniti soru formatina donusturulemedi. Konuyu biraz daha acik yazip tekrar deneyin." });
                }

                NormalizeGeneratedQuestionGroups(questions, requestedGroupCount, string.IsNullOrWhiteSpace(generated?.Title) ? topic : generated.Title);

                return Json(new
                {
                    success = true,
                    mode,
                    title = string.IsNullOrWhiteSpace(generated?.Title) ? topic : generated.Title,
                    questions
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "AI uretimi tamamlanamadi: " + ex.Message });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult AssessmentWizardPublish(string payload)
        {
            if (Session["id"] == null || Session["admin"] == null)
            {
                return Json(new { success = false, message = "Oturum sÃ¼reniz doldu. LÃ¼tfen tekrar giriÅŸ yapÄ±n." });
            }

            AssessmentWizardDraft draft;
            try
            {
                draft = JsonSerializer.Deserialize<AssessmentWizardDraft>(
                    payload ?? string.Empty,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch
            {
                return Json(new { success = false, message = "Taslak okunamadÄ±. SayfayÄ± yenileyip tekrar deneyin." });
            }

            var draftQuestions = draft?.Questions?
                .Where(x => x != null)
                .Take(200)
                .ToList() ?? new List<AssessmentWizardQuestion>();

            var validationErrors = ValidateAssessmentWizardDraft(draft, draftQuestions);
            if (validationErrors.Any())
            {
                return Json(new { success = false, message = string.Join(" ", validationErrors.Take(3)) });
            }

            var questions = draftQuestions
                .Where(x => !string.IsNullOrWhiteSpace(x.Title))
                .ToList();

            var title = TrimWizardText(draft.Title, "Yeni DeÄŸerlendirme", 250);
            var owner = TrimWizardText(draft.Owner, "Ä°nsan KaynaklarÄ±", 250);
            var isSurvey = string.Equals(draft.Mode, "survey", StringComparison.OrdinalIgnoreCase);
            var duration = ClampWizardNumber(draft.Duration, 0, 240);
            var passScore = ClampWizardNumber(draft.PassScore, 0, 100);
            var rawQuestionPoints = questions.Select(x => Math.Max(0, x.Points)).ToList();
            var publishQuestionPoints = isSurvey
                ? rawQuestionPoints.Select(x => (double)x).ToList()
                : NormalizeExamPoints(rawQuestionPoints);
            var personelId = Convert.ToInt32(Session["id"]);
            BankaCalismaAlaniHazirla();
            if (!PaketKullanimKontrolu.AktifAnketEklenebilirMi(db, AktifCalismaAlaniId(), out var paketLimitMesaji))
            {
                return Json(new { success = false, message = paketLimitMesaji });
            }

            if (!isSurvey && questions.Any(x => RequiresCorrectAnswer(x) && x.Answers?.Any(a => a.Correct) != true))
            {
                return Json(new { success = false, message = "YayÄ±na almadan Ã¶nce her sÄ±nav sorusunda doÄŸru cevabÄ± iÅŸaretleyin." });
            }

            using var tx = db.Database.BeginTransaction();
            try
            {
                var anket = new Anket
                {
                    AnketAdi = title,
                    Pasif = false,
                    Tanimsiz = draft.Audience != null && !draft.Audience.Contains("all"),
                    Sinav = !isSurvey,
                    Link = string.Empty,
                    Zaman = duration,
                    Sonuc = !isSurvey,
                    EgitimVeren = owner,
                    SertifikaNotu = draft.Certificate && !isSurvey ? passScore : (int?)null
                };

                db.Anket.Add(anket);
                db.SaveChanges();

                var calismaAlaniId = AktifCalismaAlaniId();
                if (calismaAlaniId.HasValue)
                {
                    db.Database.ExecuteSqlCommand(
                        @"UPDATE dbo.Anket
                          SET CalismaAlaniId = @p0,
                              SahipPersonelId = @p1,
                              YayinDurumu = N'Yayinda',
                              OlusturmaTarihi = ISNULL(OlusturmaTarihi, GETDATE())
                          WHERE AnketId = @p2",
                        calismaAlaniId.Value,
                        personelId,
                        anket.AnketId);
                }

                var nextGroupOrder = (db.SoruGrup
                    .OrderByDescending(x => x.SoruGrupSira)
                    .Select(x => x.SoruGrupSira)
                    .FirstOrDefault() ?? 0) + 1;

                var publishQuestions = questions
                    .Select((question, questionIndex) => new
                    {
                        Question = question,
                        Points = publishQuestionPoints[questionIndex],
                        GroupName = TrimWizardText(
                            string.IsNullOrWhiteSpace(question.Group) ? title + " SorularÄ±" : question.Group,
                            title + " SorularÄ±",
                            250)
                    })
                    .ToList();

                var soruGruplari = new Dictionary<string, SoruGrup>(StringComparer.OrdinalIgnoreCase);
                foreach (var group in publishQuestions.GroupBy(x => x.GroupName))
                {
                    var soruGrup = new SoruGrup
                    {
                        SoruGrupAdi = group.Key,
                        SoruGrupSira = nextGroupOrder++,
                        SoruGrupPuan = group.Sum(x => x.Points)
                    };

                    db.SoruGrup.Add(soruGrup);
                    soruGruplari[group.Key] = soruGrup;
                }
                db.SaveChanges();

                foreach (var soruGrup in soruGruplari.Values)
                {
                    CalismaAlaniBankaKaydinaBagla("SoruGrup", "SoruGrupId", soruGrup.SoruGrupId);
                }

                foreach (var soruGrup in soruGruplari.Values)
                {
                    db.AnketGrup.Add(new AnketGrup
                    {
                        AnketId = anket.AnketId,
                        SoruGrupId = soruGrup.SoruGrupId
                    });
                }

                var order = 1;
                var yeniSorular = new List<Soru>();
                var yeniCevaplar = new List<Cevap>();
                for (var questionIndex = 0; questionIndex < publishQuestions.Count; questionIndex++)
                {
                    var publishQuestion = publishQuestions[questionIndex];
                    var question = publishQuestion.Question;
                    var questionPoints = publishQuestion.Points;
                    var soruGrup = soruGruplari[publishQuestion.GroupName];
                    var answers = NormalizeWizardAnswers(question, isSurvey);
                    var cevapGrup = new CevapGrup
                    {
                        CevapGrupAdi = TrimWizardText(question.Title + " CevaplarÄ±", "Cevap Grubu", 250)
                    };
                    db.CevapGrup.Add(cevapGrup);
                    db.SaveChanges();
                    CalismaAlaniBankaKaydinaBagla("CevapGrup", "CevapGrupId", cevapGrup.CevapGrupId);

                    var soru = new Soru
                    {
                        SoruAdi = WizardMetniGorselEtiketiyle(question.Title, question.Image, "Soru", 250),
                        SoruSira = order++,
                        SoruGrupId = soruGrup.SoruGrupId,
                        CevapGrupId = cevapGrup.CevapGrupId,
                        SoruPuan = questionPoints
                    };
                    db.Soru.Add(soru);
                    yeniSorular.Add(soru);

                    foreach (var answer in answers)
                    {
                        var cevap = new Cevap
                        {
                            CevapAdi = WizardMetniGorselEtiketiyle(answer.Text, answer.Image, "Cevap", 250),
                            CevapGrupId = cevapGrup.CevapGrupId,
                            Dogru = isSurvey ? (bool?)null : answer.Correct,
                            CevapPuan = isSurvey ? ClampSurveyScore(answer.Score) : (answer.Correct ? questionPoints : 0)
                        };
                        db.Cevap.Add(cevap);
                        yeniCevaplar.Add(cevap);
                    }
                }

                db.SaveChanges();

                foreach (var soru in yeniSorular)
                {
                    CalismaAlaniBankaKaydinaBagla("Soru", "SoruId", soru.SoruId);
                }

                foreach (var cevap in yeniCevaplar)
                {
                    CalismaAlaniBankaKaydinaBagla("Cevap", "CevapId", cevap.CevapId);
                }

                tx.Commit();

                return Json(new
                {
                    success = true,
                    anketId = anket.AnketId,
                    redirectUrl = Url.Action("AnketAdEdit", "Home", new { id = anket.AnketId, yayinaHazirlik = 1 })
                });
            }
            catch (Exception ex)
            {
                tx.Rollback();
                return Json(new { success = false, message = "DeÄŸerlendirme kaydedilemedi: " + ex.Message });
            }
        }

        private AiAyarForm OpenAiAyarlariOku()
        {
            var fallback = new AiAyarForm
            {
                Provider = "OpenAI",
                Endpoint = "https://api.openai.com/v1",
                ChatModel = "gpt-4o-mini",
                EmbeddingModel = "text-embedding-3-small",
                Aktif = true,
                TableReady = false
            };

            try
            {
                var tableReady = db.Database.SqlQuery<int>("SELECT CASE WHEN OBJECT_ID(N'dbo.AiAyar', N'U') IS NULL THEN 0 ELSE 1 END")
                    .FirstOrDefault() == 1;
                fallback.TableReady = tableReady;
                if (!tableReady)
                {
                    return fallback;
                }

                var row = db.Database.SqlQuery<AiAyarForm>(
                    @"SELECT TOP 1 AiAyarId, Provider, Endpoint, ChatModel, EmbeddingModel, ApiKey, Aktif, GuncellemeTarihi,
                             CAST(1 AS bit) AS TableReady
                      FROM dbo.AiAyar
                      ORDER BY AiAyarId").FirstOrDefault();

                return row ?? fallback;
            }
            catch
            {
                return fallback;
            }
        }

        private static string NormalizeOpenAiEndpoint(string endpoint)
        {
            return (endpoint ?? "https://api.openai.com/v1").Trim().TrimEnd('/');
        }

        private static (bool Valid, string Message) ValidateAiAssessmentTopic(string topic)
        {
            var normalized = NormalizeAiTopicText(topic);
            var wordCount = Regex.Matches(normalized, @"[\p{L}\p{N}]+").Count;

            var blockedPatterns = new[]
            {
                @"\bnaber+\b",
                @"\bnapiyon\b",
                @"\bselam+\b",
                @"\bmerhaba+\b",
                @"\blo+o+\b",
                @"\bÅŸaka\b",
                @"\bespri\b",
                @"\bsohbet\b",
                @"\bhikaye\b",
                @"\bÅŸiir\b",
                @"\bfilm\b",
                @"\bmaÃ§\b",
                @"\bbahis\b",
                @"\biddia\b",
                @"\bwhatsapp\b",
                @"\binstagram\b",
                @"\btiktok\b",
                @"\boyun\b"
            };

            if (blockedPatterns.Any(pattern => Regex.IsMatch(normalized, pattern, RegexOptions.IgnoreCase)))
            {
                return (false, "AI soru uretimi yalnizca egitim, sinav, anket veya ise alim degerlendirmesi konulari icin kullanilabilir.");
            }

            var acceptedKeywords = new[]
            {
                "egitim", "eÄŸitim", "sinav", "sÄ±nav", "anket", "degerlendirme", "deÄŸerlendirme",
                "test", "quiz", "oryantasyon", "farkindalik", "farkÄ±ndalÄ±k", "prosedur", "prosedÃ¼r",
                "talimat", "politika", "surec", "sÃ¼reÃ§", "kalite", "guvenlik", "gÃ¼venlik",
                "isg", "iÅŸ saÄŸlÄ±ÄŸÄ±", "is sagligi", "hijyen", "gida", "gÄ±da", "haccp", "kkn",
                "alerjen", "helal", "brcgs", "iso", "kvkk", "gdpr", "bilgi gÃ¼venliÄŸi",
                "siber", "yangin", "yangÄ±n", "ilk yardim", "ilk yardÄ±m", "musteri", "mÃ¼ÅŸteri",
                "satis", "satÄ±ÅŸ", "insan kaynaklari", "insan kaynaklarÄ±", "ise alim", "iÅŸe alÄ±m",
                "yetkinlik", "liderlik", "operasyon", "uretim", "Ã¼retim", "bakim", "bakÄ±m",
                "denetim", "sertifika", "uyum", "compliance", "risk", "pest", "temizlik"
            };

            if (acceptedKeywords.Any(keyword => normalized.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
            {
                return (true, null);
            }

            if (topic.Length >= 20 && wordCount >= 4)
            {
                return (true, null);
            }

            if (Regex.IsMatch(topic.Trim(), @"^[A-ZÃ‡ÄÄ°Ã–ÅÃœ0-9 .\-]{3,18}$"))
            {
                return (true, null);
            }

            return (false, "Konu cok genel veya amac disi gorunuyor. Ornek: 'Gida guvenligi hijyen egitimi', 'KVKK farkindalik sinavi', 'HACCP KKN degerlendirmesi'.");
        }

        private static string NormalizeAiTopicText(string value)
        {
            return (value ?? string.Empty)
                .Trim()
                .ToLowerInvariant()
                .Replace("Ä±", "i");
        }

        private static string NormalizeAssessmentMode(string mode)
        {
            return (mode ?? string.Empty).Trim().ToLowerInvariant() switch
            {
                "survey" => "survey",
                "hiring" => "hiring",
                "training" => "training",
                _ => "exam"
            };
        }

        private static string InferAssessmentModeFromTopic(string topic, string currentMode)
        {
            var normalized = NormalizeSurveyScoreText(topic);
            var fallback = NormalizeAssessmentMode(currentMode);

            bool HasAny(params string[] keywords) =>
                keywords.Any(keyword => normalized.Contains(keyword, StringComparison.OrdinalIgnoreCase));

            if (HasAny("anket", "memnuniyet", "geri bildirim", "geribildirim", "nabiz", "algi"))
            {
                return "survey";
            }

            if (HasAny("ise alim", "mulakat", "aday", "yetkinlik degerlendirme"))
            {
                return "hiring";
            }

            if (HasAny("sinav", "test", "quiz", "olcme"))
            {
                return "exam";
            }

            if (HasAny("egitim", "oryantasyon", "sertifika"))
            {
                return "training";
            }

            return fallback;
        }

        private static string BuildAssessmentAiPrompt(string topic, string mode, int count, string difficulty, string questionType, int requestedGroupCount = 0)
        {
            var isSurvey = string.Equals(mode, "survey", StringComparison.OrdinalIgnoreCase);
            var modeLabel = mode switch
            {
                "survey" => "anket",
                "training" => "egitim sonu sinavi",
                "hiring" => "ise alim degerlendirmesi",
                _ => "sinav"
            };
            var typeLabel = questionType switch
            {
                "truefalse" => "dogru/yanlis",
                "mixed" => "coktan secmeli ve dogru/yanlis karisik",
                _ => "coktan secmeli"
            };
            var groupInstruction = requestedGroupCount > 0
                ? $"- Toplam soru grubu sayisi kesinlikle {requestedGroupCount} olsun; group alaninda yalnizca bu {requestedGroupCount} grup adindan biri kullanilsin. {count} soru bu gruplara mumkun oldugunca esit dagilsin."
                : "- Kaynak metinde soru grubu, kategori, bolum veya konu basliklari varsa her soru icin uygun group alanini doldur ve sorulari bu gruplara dengeli dagit.";

            if (isSurvey)
            {
                return $@"
Konu veya kaynak metin:
{topic}

Gorev:
- {modeLabel} icin tam olarak {count} adet {difficulty} seviyede {typeLabel} soru uret; questions dizisinin eleman sayisi kesinlikle {count} olsun.
- Bu bir memnuniyet, algi veya geri bildirim anketidir; dogru cevap mantigi kullanma.
- Her cevap secenegi icin 0 ile 5 arasinda score ver.
- Olumlu/yuksek memnuniyet: 5; iyi: 4; orta/kismen: 3; dusuk/olumsuz: 1; bilmiyorum/uygun degil/cevap yok: 0.
- Secenek metinleri dogal katilimci ifadeleri olsun; metnin sonuna ""cevap"", ""yanit"" veya ""secenek"" kelimesi ekleme.
{groupInstruction}
- Konu egitim, anket, ise alim, kurumsal prosedur, kalite, memnuniyet veya mesleki degerlendirme amaci tasimiyorsa soru uretme.
- Selamlasma, sohbet, saka, rastgele istek, genel kultur oyunu veya konu disi metin gelirse yalnizca {{ ""title"": ""Konu disi istek"", ""questions"": [] }} JSON'u dondur.
- Dil Turkce olsun.
- Coktan secmeli sorularda 4 dengeli secenek olsun.
- Puan degeri her olculebilir soru icin 10 olsun.

Yalnizca su JSON semasinda cevap ver:
{{
  ""title"": ""Kisa anket basligi"",
  ""questions"": [
    {{
      ""type"": ""multiple"",
      ""group"": ""Soru grubu veya kategori"",
      ""title"": ""Anket soru metni"",
      ""points"": 10,
      ""required"": true,
      ""answers"": [
        {{ ""text"": ""Cok memnunum"", ""correct"": false, ""score"": 5 }},
        {{ ""text"": ""Orta duzeyde memnunum"", ""correct"": false, ""score"": 3 }},
        {{ ""text"": ""Memnun degilim"", ""correct"": false, ""score"": 1 }},
        {{ ""text"": ""Fikrim yok"", ""correct"": false, ""score"": 0 }}
      ]
    }}
  ]
}}";
            }

            return $@"
Konu veya kaynak metin:
{topic}

Gorev:
- {modeLabel} icin tam olarak {count} adet {difficulty} seviyede {typeLabel} soru uret; questions dizisinin eleman sayisi kesinlikle {count} olsun.
- Konu egitim, sinav, anket, ise alim, kurumsal prosedur veya mesleki degerlendirme amaci tasimiyorsa soru uretme.
- Selamlasma, sohbet, saka, rastgele istek, genel kultur oyunu veya konu disi metin gelirse yalnizca {{ ""title"": ""Konu disi istek"", ""questions"": [] }} JSON'u dondur.
- Dil Turkce olsun.
- Coktan secmeli sorularda 4 secenek olsun ve yalnizca 1 dogru cevap isaretlensin.
- Dogru/yanlis sorularda iki secenek olsun: Dogru, Yanlis.
{groupInstruction}
- Sorular olcme-degerlendirme acisindan net, kurumsal ve egitim amacina uygun olsun.
- Puan degeri her olculebilir soru icin 10 olsun.

Yalnizca su JSON semasinda cevap ver:
{{
  ""title"": ""Kisa degerlendirme basligi"",
  ""questions"": [
    {{
      ""type"": ""multiple"",
      ""group"": ""Soru grubu veya kategori"",
      ""title"": ""Soru metni"",
      ""points"": 10,
      ""required"": true,
      ""answers"": [
        {{ ""text"": ""Secenek"", ""correct"": false, ""score"": 0 }},
        {{ ""text"": ""Dogru secenek"", ""correct"": true, ""score"": 0 }}
      ]
    }}
  ]
}}";
        }

        private static string ExtractOpenAiContent(string raw)
        {
            using var document = JsonDocument.Parse(raw);
            var choices = document.RootElement.GetProperty("choices");
            if (choices.GetArrayLength() == 0)
            {
                return string.Empty;
            }

            var message = choices[0].GetProperty("message");
            return message.TryGetProperty("content", out var content)
                ? content.GetString()
                : string.Empty;
        }

        private static string ExtractOpenAiError(string raw)
        {
            try
            {
                using var document = JsonDocument.Parse(raw);
                if (document.RootElement.TryGetProperty("error", out var error)
                    && error.TryGetProperty("message", out var message))
                {
                    return TrimWizardText(message.GetString(), "OpenAI hata dondu.", 500);
                }
            }
            catch
            {
                // Ham hata metni asagida kisaltilarak dondurulur.
            }

            return TrimWizardText(raw, "OpenAI hata dondu.", 500);
        }

        private static string ExtractJsonObject(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "{}";
            }

            var start = value.IndexOf('{');
            var end = value.LastIndexOf('}');
            return start >= 0 && end >= start
                ? value.Substring(start, end - start + 1)
                : value;
        }

        private static int ExtractRequestedGroupCount(string topic, int questionCount)
        {
            var normalized = NormalizeSurveyScoreText(topic);
            var patterns = new[]
            {
                @"\b(\d{1,2})\s*(?:adet\s*)?(?:soru\s*)?(?:grubu|grubunda|grupta|grup|kategori|kategoride|bolum|bolumde)\b",
                @"\b(\d{1,2})\s*farkli\s*(?:grup|kategori|bolum)\b"
            };

            foreach (var pattern in patterns)
            {
                var match = Regex.Match(normalized, pattern, RegexOptions.IgnoreCase);
                if (match.Success
                    && int.TryParse(match.Groups[1].Value, out var value)
                    && value > 0)
                {
                    return ClampWizardNumber(value, 1, Math.Max(1, Math.Min(questionCount, 25)));
                }
            }

            return 0;
        }

        private static string NormalizeQuestionGroupKey(string value)
        {
            return Regex.Replace(NormalizeSurveyScoreText(value), @"\s+", " ").Trim();
        }

        private static void NormalizeGeneratedQuestionGroups(List<AssessmentWizardQuestion> questions, int requestedGroupCount, string fallbackTitle)
        {
            if (questions == null || !questions.Any())
            {
                return;
            }

            if (requestedGroupCount <= 0)
            {
                foreach (var question in questions)
                {
                    question.Group = TrimWizardText(question.Group, fallbackTitle, 250);
                }

                return;
            }

            requestedGroupCount = Math.Min(requestedGroupCount, questions.Count);
            var groupInfos = questions
                .Select((question, index) => new
                {
                    Name = TrimWizardText(question.Group, string.Empty, 250),
                    Key = NormalizeQuestionGroupKey(question.Group),
                    Index = index
                })
                .Where(x => !string.IsNullOrWhiteSpace(x.Key))
                .GroupBy(x => x.Key)
                .Select(x => new
                {
                    Name = x.First().Name,
                    Key = x.Key,
                    Count = x.Count(),
                    FirstIndex = x.Min(y => y.Index)
                })
                .OrderBy(x => x.FirstIndex)
                .ToList();

            var selectedGroups = groupInfos
                .Where(x => x.Count > 1)
                .Take(requestedGroupCount)
                .ToList();

            if (selectedGroups.Count < requestedGroupCount)
            {
                selectedGroups.AddRange(groupInfos
                    .Where(x => selectedGroups.All(y => y.Key != x.Key))
                    .Take(requestedGroupCount - selectedGroups.Count));
            }

            while (selectedGroups.Count < requestedGroupCount)
            {
                var name = $"Grup {selectedGroups.Count + 1}";
                selectedGroups.Add(new
                {
                    Name = name,
                    Key = NormalizeQuestionGroupKey(name),
                    Count = 0,
                    FirstIndex = int.MaxValue
                });
            }

            var baseSize = questions.Count / requestedGroupCount;
            var remainder = questions.Count % requestedGroupCount;
            var targetSizes = selectedGroups
                .Select((group, index) => new
                {
                    group.Name,
                    group.Key,
                    Target = baseSize + (index < remainder ? 1 : 0)
                })
                .ToList();

            var counts = targetSizes.ToDictionary(x => x.Name, _ => 0, StringComparer.OrdinalIgnoreCase);
            var unassigned = new List<AssessmentWizardQuestion>();

            foreach (var question in questions)
            {
                var key = NormalizeQuestionGroupKey(question.Group);
                var target = targetSizes.FirstOrDefault(x => x.Key == key);
                if (target != null && counts[target.Name] < target.Target)
                {
                    question.Group = target.Name;
                    counts[target.Name]++;
                }
                else
                {
                    unassigned.Add(question);
                }
            }

            foreach (var question in unassigned)
            {
                var target = targetSizes
                    .OrderBy(x => counts[x.Name] >= x.Target)
                    .ThenBy(x => counts[x.Name])
                    .First();

                question.Group = target.Name;
                counts[target.Name]++;
            }
        }

        private static List<AssessmentWizardQuestion> NormalizeGeneratedQuestions(
            IEnumerable<AssessmentWizardQuestion> generatedQuestions,
            int count,
            string requestedType,
            string mode)
        {
            var questions = new List<AssessmentWizardQuestion>();
            var isSurvey = string.Equals(mode, "survey", StringComparison.OrdinalIgnoreCase);
            if (generatedQuestions == null)
            {
                return questions;
            }

            foreach (var item in generatedQuestions)
            {
                if (item == null || string.IsNullOrWhiteSpace(item.Title))
                {
                    continue;
                }

                var type = NormalizeWizardQuestionType(
                    string.Equals(requestedType, "mixed", StringComparison.OrdinalIgnoreCase) ? item.Type : requestedType);

                var answers = item.Answers?
                    .Where(x => x != null && !string.IsNullOrWhiteSpace(x.Text))
                    .Take(8)
                    .Select((x, answerIndex) => new AssessmentWizardAnswer
                    {
                        Text = CleanGeneratedAnswerText(x.Text),
                        Image = WizardGorselDosyaAdi(x.Image),
                        Correct = !isSurvey && x.Correct,
                        Score = isSurvey ? InferSurveyAnswerScore(x, answerIndex, item.Answers?.Count ?? 0) : x.Score
                    })
                    .Where(x => !string.IsNullOrWhiteSpace(x.Text))
                    .ToList() ?? new List<AssessmentWizardAnswer>();

                if (!isSurvey && type == "truefalse")
                {
                    answers = NormalizeTrueFalseAnswers(answers);
                }

                if (RequiresCorrectAnswer(new AssessmentWizardQuestion { Type = type }) && answers.Count < 2)
                {
                    continue;
                }

                if (!isSurvey && RequiresCorrectAnswer(new AssessmentWizardQuestion { Type = type }))
                {
                    if (!answers.Any(x => x.Correct))
                    {
                        answers[0].Correct = true;
                    }

                    var correctFound = false;
                    foreach (var answer in answers)
                    {
                        if (answer.Correct && !correctFound)
                        {
                            correctFound = true;
                        }
                        else if (answer.Correct)
                        {
                            answer.Correct = false;
                        }
                    }
                }
                else if (isSurvey)
                {
                    for (var answerIndex = 0; answerIndex < answers.Count; answerIndex++)
                    {
                        answers[answerIndex].Correct = false;
                        answers[answerIndex].Score = InferSurveyAnswerScore(answers[answerIndex], answerIndex, answers.Count);
                    }
                }

                questions.Add(new AssessmentWizardQuestion
                {
                    Type = type,
                    Group = TrimWizardText(item.Group, string.Empty, 250),
                    Title = TrimWizardText(item.Title, "Soru", 250),
                    Image = WizardGorselDosyaAdi(item.Image),
                    Points = item.Points > 0 ? item.Points : 10,
                    Required = true,
                    Answers = answers
                });

                if (questions.Count >= count)
                {
                    break;
                }
            }

            return questions;
        }

        private static string CleanGeneratedAnswerText(string value)
        {
            var text = Regex.Replace(value ?? string.Empty, @"\s+", " ").Trim();
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            text = Regex.Replace(
                text,
                "\\s+(?:cevap|cevab[\u0131i]|yan[\u0131i]t|yan[\u0131i]t[\u0131i]|se[c\u00e7]enek)\\s*$",
                string.Empty,
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant).Trim();

            return TrimWizardText(text, string.Empty, 250);
        }

        private static double ClampSurveyScore(double? value)
        {
            if (!value.HasValue || double.IsNaN(value.Value) || double.IsInfinity(value.Value))
            {
                return 0;
            }

            return Math.Max(0, Math.Min(5, Math.Round(value.Value, 1)));
        }

        private static string NormalizeSurveyScoreText(string value)
        {
            return (value ?? string.Empty)
                .Trim()
                .ToLowerInvariant()
                .Replace("Ä±", "i")
                .Replace("Ä°", "i")
                .Replace("Ã§", "c")
                .Replace("ÄŸ", "g")
                .Replace("Ã¶", "o")
                .Replace("ÅŸ", "s")
                .Replace("Ã¼", "u");
        }

        private static double InferSurveyAnswerScore(AssessmentWizardAnswer answer, int index, int count)
        {
            if (answer?.Score != null)
            {
                return ClampSurveyScore(answer.Score);
            }

            var normalized = NormalizeSurveyScoreText(answer?.Text);
            if (double.TryParse(normalized.Replace(',', '.'), System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var numeric))
            {
                return ClampSurveyScore(numeric);
            }

            var unknownWords = new[] { "bilmiyorum", "fikrim yok", "kararsiz", "emin degil", "cevap yok", "bilgi yok", "uygun degil" };
            var negativeWords = new[] { "hayir", "yetersiz", "bayat", "pahali", "zor", "kotu", "memnun degil", "kuramadim", "olumsuz", "hic" };
            var mediumWords = new[] { "bazen", "orta", "kismen", "kararsizim", "normal", "idare", "fena degil" };
            var positiveWords = new[] { "evet", "her zaman", "cok", "kolay", "uygun", "cesitli", "memnun", "taze", "iyi", "basarili", "yeterli" };

            if (unknownWords.Any(word => normalized.Contains(word))) return 0;
            if (negativeWords.Any(word => normalized.Contains(word))) return 1;
            if (mediumWords.Any(word => normalized.Contains(word))) return 3;
            if (positiveWords.Any(word => normalized.Contains(word))) return 5;

            var fallback = new[] { 5d, 4d, 3d, 2d, 1d, 0d };
            return fallback[Math.Min(index, fallback.Length - 1)];
        }

        private static List<AssessmentWizardAnswer> NormalizeTrueFalseAnswers(List<AssessmentWizardAnswer> answers)
        {
            var correctIsTrue = answers.FirstOrDefault(x => x.Correct)?.Text?.Trim()
                .Equals("Yanlis", StringComparison.OrdinalIgnoreCase) != true
                && answers.FirstOrDefault(x => x.Correct)?.Text?.Trim()
                    .Equals("YanlÄ±ÅŸ", StringComparison.OrdinalIgnoreCase) != true;

            return new List<AssessmentWizardAnswer>
            {
                new AssessmentWizardAnswer { Text = "DoÄŸru", Correct = correctIsTrue },
                new AssessmentWizardAnswer { Text = "YanlÄ±ÅŸ", Correct = !correctIsTrue }
            };
        }

        private static string NormalizeWizardQuestionType(string type)
        {
            type = (type ?? "multiple").Trim().ToLowerInvariant();
            return type switch
            {
                "truefalse" => "truefalse",
                "text" => "text",
                "info" => "info",
                "rating" => "rating",
                _ => "multiple"
            };
        }

        private static List<string> ValidateAssessmentWizardDraft(AssessmentWizardDraft draft, List<AssessmentWizardQuestion> questions)
        {
            var errors = new List<string>();
            if (draft == null)
            {
                errors.Add("Taslak okunamadi. Sayfayi yenileyip tekrar deneyin.");
                return errors;
            }

            var isSurvey = string.Equals(draft.Mode, "survey", StringComparison.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(draft.Title))
            {
                errors.Add("Baslik zorunlu.");
            }

            if (string.IsNullOrWhiteSpace(draft.Owner))
            {
                errors.Add("Egitimi veya degerlendirmeyi duzenleyen alan zorunlu.");
            }

            if (!isSurvey && draft.Duration <= 0)
            {
                errors.Add("Sinav veya egitim icin sure dakika cinsinden zorunlu.");
            }

            if (!isSurvey && (draft.PassScore <= 0 || draft.PassScore > 100))
            {
                errors.Add("Gecme notu 1 ile 100 arasinda olmali.");
            }

            if (questions == null || !questions.Any())
            {
                errors.Add("Yayina almak icin en az bir soru ekleyin.");
                return errors;
            }

            for (var index = 0; index < questions.Count; index++)
            {
                var question = questions[index];
                var prefix = $"{index + 1}. soru:";
                var type = NormalizeWizardQuestionType(question.Type);
                if (string.IsNullOrWhiteSpace(question.Title))
                {
                    errors.Add(prefix + " soru metni zorunlu.");
                }

                if (RequiresCorrectAnswer(new AssessmentWizardQuestion { Type = type }))
                {
                    if (question.Points <= 0)
                    {
                        errors.Add(prefix + " puan 0'dan buyuk olmali.");
                    }

                    var answers = question.Answers?
                        .Where(x => x != null && !string.IsNullOrWhiteSpace(x.Text))
                        .ToList() ?? new List<AssessmentWizardAnswer>();

                    if (answers.Count < 2)
                    {
                        errors.Add(prefix + " en az iki cevap secenegi olmali.");
                    }

                    if (isSurvey)
                    {
                        for (var answerIndex = 0; answerIndex < answers.Count; answerIndex++)
                        {
                            var score = InferSurveyAnswerScore(answers[answerIndex], answerIndex, answers.Count);
                            if (score < 0 || score > 5)
                            {
                                errors.Add(prefix + " anket cevap puanlari 0 ile 5 arasinda olmali.");
                            }
                        }
                    }

                    if (!isSurvey && !answers.Any(x => x.Correct))
                    {
                        errors.Add(prefix + " dogru cevap secilmeli.");
                    }
                }
            }

            return errors;
        }

        private static List<AssessmentWizardAnswer> NormalizeWizardAnswers(AssessmentWizardQuestion question, bool isSurvey)
        {
            var rawAnswers = question.Answers?
                .Where(x => x != null && !string.IsNullOrWhiteSpace(x.Text))
                .Take(12)
                .ToList() ?? new List<AssessmentWizardAnswer>();

            var answers = rawAnswers
                .Select((x, index) => new AssessmentWizardAnswer
                {
                    Text = x.Text,
                    Image = WizardGorselDosyaAdi(x.Image),
                    Correct = !isSurvey && x.Correct,
                    Score = isSurvey ? InferSurveyAnswerScore(x, index, rawAnswers.Count) : x.Score
                })
                .ToList();

            if (!answers.Any())
            {
                answers.Add(new AssessmentWizardAnswer { Text = "Okudum", Correct = !isSurvey, Score = isSurvey ? 5 : (double?)null });
            }

            return answers;
        }

        private static List<double> NormalizeExamPoints(List<int> rawPoints)
        {
            if (rawPoints == null || !rawPoints.Any())
            {
                return new List<double>();
            }

            var positivePoints = rawPoints.Select(x => x > 0 ? x : 1).ToList();
            var total = positivePoints.Sum();
            return positivePoints.Select(x => 100.0 * x / total).ToList();
        }

        private static bool RequiresCorrectAnswer(AssessmentWizardQuestion question)
        {
            return !string.Equals(question.Type, "text", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(question.Type, "info", StringComparison.OrdinalIgnoreCase);
        }

        private bool TryApplyTrustedAnswerValues(Havuz hav, out string message)
        {
            message = null;

            if (hav == null || hav.AnketId == null || hav.SoruID == null || hav.CevapId == null)
            {
                message = "Soru veya cevap bilgisi eksik.";
                return false;
            }

            var anket = db.Anket.FirstOrDefault(x => x.AnketId == hav.AnketId.Value);
            var soru = db.Soru.Include("SoruGrup").FirstOrDefault(x => x.SoruId == hav.SoruID.Value);
            var cevap = db.Cevap.FirstOrDefault(x => x.CevapId == hav.CevapId.Value);

            if (anket == null || soru == null || cevap == null)
            {
                message = "Soru veya cevap kaydÄ± bulunamadÄ±.";
                return false;
            }

            if (soru.CevapGrupId != cevap.CevapGrupId)
            {
                message = "SeÃ§ilen cevap bu soruya ait deÄŸil.";
                return false;
            }

            var soruAnketeAit = db.AnketGrup.Any(x =>
                x.AnketId == hav.AnketId.Value &&
                x.SoruGrupId == soru.SoruGrupId);

            if (!soruAnketeAit)
            {
                message = "SeÃ§ilen soru bu Ã§alÄ±ÅŸmaya ait deÄŸil.";
                return false;
            }

            var soruPuani = soru.SoruPuan ?? 0;
            hav.SoruGrupId = soru.SoruGrupId;
            hav.CevapGrupId = cevap.CevapGrupId;
            hav.SoruPuan = soruPuani;
            hav.SoruGrupPuan = soru.SoruGrup?.SoruGrupPuan ?? 0;
            hav.CevapPuan = SinavTurundeMi(anket)
                ? (cevap.Dogru == true ? soruPuani : 0)
                : (cevap.CevapPuan ?? 0);

            return true;
        }

        private static int ClampWizardNumber(int value, int min, int max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        private static string TrimWizardText(string value, string fallback, int maxLength)
        {
            var text = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
            return text.Length <= maxLength ? text : text.Substring(0, maxLength);
        }

        private static string WizardGorselDosyaAdi(string value)
        {
            var fileName = Path.GetFileName((value ?? string.Empty).Trim());
            if (string.IsNullOrWhiteSpace(fileName))
            {
                return string.Empty;
            }

            return string.Equals(Path.GetExtension(fileName), ".webp", StringComparison.OrdinalIgnoreCase)
                ? fileName
                : string.Empty;
        }

        private static string WizardGorselEtiketiniTemizle(string value)
        {
            return Regex.Replace(value ?? string.Empty, @"\s*\[\[gorsel:[^\]]+\]\]\s*$", "", RegexOptions.IgnoreCase).Trim();
        }

        private static string WizardMetniGorselEtiketiyle(string value, string image, string fallback, int maxLength)
        {
            var fileName = WizardGorselDosyaAdi(image);
            var cleanText = WizardGorselEtiketiniTemizle(value);
            if (string.IsNullOrWhiteSpace(fileName))
            {
                return TrimWizardText(cleanText, fallback, maxLength);
            }

            var marker = " " + AnketGorselEtiketOnEk + fileName + AnketGorselEtiketSonEk;
            var textRoom = Math.Max(1, maxLength - marker.Length);
            var trimmedText = string.IsNullOrWhiteSpace(cleanText)
                ? string.Empty
                : TrimWizardText(cleanText, fallback, textRoom).TrimEnd();
            return string.IsNullOrWhiteSpace(trimmedText)
                ? (AnketGorselEtiketOnEk + fileName + AnketGorselEtiketSonEk)
                : trimmedText + marker;
        }

        public class AssessmentWizardDraft
        {
            public string Mode { get; set; }
            public string Title { get; set; }
            public string Owner { get; set; }
            public int Duration { get; set; }
            public int PassScore { get; set; }
            public bool Certificate { get; set; }
            public List<string> Audience { get; set; }
            public List<AssessmentWizardQuestion> Questions { get; set; }
        }

        public class AssessmentWizardQuestion
        {
            public string Type { get; set; }
            public string Group { get; set; }
            public string Title { get; set; }
            public string Image { get; set; }
            public int Points { get; set; }
            public bool Required { get; set; }
            public List<AssessmentWizardAnswer> Answers { get; set; }
        }

        public class AssessmentWizardAnswer
        {
            public string Text { get; set; }
            public string Image { get; set; }
            public bool Correct { get; set; }
            public double? Score { get; set; }
        }

        public class AssessmentWizardAiResult
        {
            public string Title { get; set; }
            public List<AssessmentWizardQuestion> Questions { get; set; }
        }

        private class AnketYayinAyarRow
        {
            public DateTime? YayinBaslangicTarihi { get; set; }
            public DateTime? YayinBitisTarihi { get; set; }
            public string YayinDurumu { get; set; }
            public bool? Pasif { get; set; }
            public string KatilimYontemi { get; set; }
        }

        private class AnketKatilimYontemiRow
        {
            public int AnketId { get; set; }
            public string KatilimYontemi { get; set; }
        }

        private class SertifikaAyarRow
        {
            public bool? SertifikaAktif { get; set; }
            public bool? SertifikaKatilimciErisimi { get; set; }
            public string SertifikaVerilisZamani { get; set; }
            public string SertifikaBaslik { get; set; }
            public string SertifikaMetni { get; set; }
            public string SertifikaTema { get; set; }
            public string SertifikaLogo { get; set; }
            public string SertifikaVurguRengi { get; set; }
            public string SertifikaCerceve { get; set; }
            public string SertifikaFont { get; set; }
            public int? SertifikaYaziPunto { get; set; }
            public int? SertifikaBaslikPunto { get; set; }
            public DateTime? YayinBitisTarihi { get; set; }
        }

        private class SertifikaTasarimRow
        {
            public string SertifikaTema { get; set; }
            public string SertifikaLogo { get; set; }
            public string SertifikaVurguRengi { get; set; }
            public string SertifikaCerceve { get; set; }
            public string SertifikaFont { get; set; }
            public int? SertifikaYaziPunto { get; set; }
            public int? SertifikaBaslikPunto { get; set; }
        }

        public class SertifikaWizardForm
        {
            public int AnketId { get; set; }
            public string AnketAdi { get; set; }
            public string ReturnUrl { get; set; }
            public bool Sinav { get; set; }
            public bool SertifikaAktif { get; set; }
            public bool SertifikaKatilimciErisimi { get; set; }
            public string SertifikaVerilisZamani { get; set; }
            public int? SertifikaNotu { get; set; }
            public string EgitimVeren { get; set; }
            public string SertifikaBaslik { get; set; }
            public string SertifikaMetni { get; set; }
            public string SertifikaTema { get; set; }
            public string SertifikaLogo { get; set; }
            public string SertifikaVurguRengi { get; set; }
            public string SertifikaCerceve { get; set; }
            public string SertifikaFont { get; set; }
            public int? SertifikaYaziPunto { get; set; }
            public int? SertifikaBaslikPunto { get; set; }
            public string Imza { get; set; }
        }

        private static string DateTimeLocalValue(DateTime? value)
        {
            return value.HasValue ? value.Value.ToString("yyyy-MM-ddTHH:mm") : string.Empty;
        }

        private const string KatilimYontemiHerkeseAcik = "HerkeseAcik";
        private const string KatilimYontemiKayitli = "Kayitli";
        private const string KatilimYontemiBilgiFormu = "BilgiFormu";
        private const string KatilimYontemiKisiyeOzel = "KisiyeOzel";

        private void KatilimYontemiKolonunuHazirla()
        {
            try
            {
                db.Database.ExecuteSqlCommand(
                    @"IF COL_LENGTH('dbo.Anket', 'KatilimYontemi') IS NULL
                      BEGIN
                          ALTER TABLE dbo.Anket
                              ADD KatilimYontemi NVARCHAR(30) NOT NULL
                                  CONSTRAINT DF_Anket_KatilimYontemi DEFAULT (N'HerkeseAcik');
                      END;

                      UPDATE dbo.Anket
                      SET KatilimYontemi = N'HerkeseAcik'
                      WHERE KatilimYontemi IS NULL
                         OR LTRIM(RTRIM(KatilimYontemi)) = N''
                         OR KatilimYontemi NOT IN (N'HerkeseAcik', N'Kayitli', N'BilgiFormu', N'KisiyeOzel');");
            }
            catch
            {
                // DDL yetkisi yoksa DatabaseScripts altindaki script elle calistirilabilir.
            }
        }

        private static string NormalizeKatilimYontemi(string value)
        {
            switch ((value ?? string.Empty).Trim())
            {
                case KatilimYontemiKayitli:
                case KatilimYontemiBilgiFormu:
                case KatilimYontemiKisiyeOzel:
                    return value.Trim();
                default:
                    return KatilimYontemiHerkeseAcik;
            }
        }

        private string KatilimYontemiGetir(int anketId)
        {
            KatilimYontemiKolonunuHazirla();

            try
            {
                var yontem = db.Database.SqlQuery<string>(
                    @"SELECT TOP 1 ISNULL(NULLIF(KatilimYontemi, N''), N'HerkeseAcik')
                      FROM dbo.Anket
                      WHERE AnketId = @p0",
                    anketId).FirstOrDefault();

                return NormalizeKatilimYontemi(yontem);
            }
            catch
            {
                return KatilimYontemiHerkeseAcik;
            }
        }

        private Dictionary<int, string> KatilimYontemleriGetir(IEnumerable<int> anketIds)
        {
            var ids = (anketIds ?? Enumerable.Empty<int>())
                .Where(x => x > 0)
                .Distinct()
                .ToList();

            var result = ids.ToDictionary(x => x, _ => KatilimYontemiHerkeseAcik);
            if (!ids.Any())
            {
                return result;
            }

            KatilimYontemiKolonunuHazirla();

            try
            {
                var idList = string.Join(",", ids);
                var rows = db.Database.SqlQuery<AnketKatilimYontemiRow>(
                    $@"SELECT AnketId,
                              ISNULL(NULLIF(KatilimYontemi, N''), N'HerkeseAcik') AS KatilimYontemi
                       FROM dbo.Anket
                       WHERE AnketId IN ({idList})").ToList();

                foreach (var row in rows)
                {
                    result[row.AnketId] = NormalizeKatilimYontemi(row.KatilimYontemi);
                }
            }
            catch
            {
            }

            return result;
        }

        private bool KatilimYonteminiKaydet(int anketId, string yontem)
        {
            KatilimYontemiKolonunuHazirla();

            try
            {
                db.Database.ExecuteSqlCommand(
                    @"UPDATE dbo.Anket
                      SET KatilimYontemi = @p0
                      WHERE AnketId = @p1",
                    NormalizeKatilimYontemi(yontem),
                    anketId);
                return true;
            }
            catch
            {
                // Kolon yoksa ya da yetki yoksa ana kayit akisini kirmayalim.
                return false;
            }
        }

        private static string KatilimYontemiEtiketi(string yontem)
        {
            switch (NormalizeKatilimYontemi(yontem))
            {
                case KatilimYontemiKayitli:
                    return "KayÄ±tlÄ± kiÅŸiler";
                case KatilimYontemiBilgiFormu:
                    return "Bilgi formu";
                case KatilimYontemiKisiyeOzel:
                    return "KiÅŸiye Ã¶zel";
                default:
                    return "Herkese aÃ§Ä±k";
            }
        }

        private static string KatilimYontemiPaylasimAciklamasi(string yontem)
        {
            switch (NormalizeKatilimYontemi(yontem))
            {
                case KatilimYontemiKayitli:
                case KatilimYontemiKisiyeOzel:
                    return "Bu baÄŸlantÄ± kayÄ±tlÄ± katÄ±lÄ±mcÄ±lara Ã¶zeldir. GiriÅŸ yapan uygun kiÅŸilerin sonuÃ§larÄ± kendi profiline iÅŸlenir.";
                case KatilimYontemiBilgiFormu:
                    return "Bu baÄŸlantÄ± bilgi formu ile aÃ§Ä±lÄ±r; katÄ±lÄ±mcÄ±dan ad soyad ve TC / numara veya e-posta alÄ±nÄ±r.";
                default:
                    return "Bu baÄŸlantÄ± herkese aÃ§Ä±ktÄ±r; giriÅŸ yapan kayÄ±tlÄ± kiÅŸiler kendi profiliyle, diÄŸerleri katÄ±lÄ±m koduyla izlenir.";
            }
        }

        private AnketYayinAyarRow AnketYayinAyariniGetir(int anketId)
        {
            KatilimYontemiKolonunuHazirla();

            try
            {
                return db.Database.SqlQuery<AnketYayinAyarRow>(
                    @"SELECT TOP 1
                             YayinBaslangicTarihi,
                             YayinBitisTarihi,
                             YayinDurumu,
                             Pasif,
                             ISNULL(NULLIF(KatilimYontemi, N''), N'HerkeseAcik') AS KatilimYontemi
                      FROM dbo.Anket
                      WHERE AnketId = @p0",
                    anketId).FirstOrDefault();
            }
            catch
            {
                return null;
            }
        }

        private void SertifikaTasarimKolonlariniHazirla()
        {
            try
            {
                db.Database.ExecuteSqlCommand(
                    @"IF COL_LENGTH('dbo.Anket', 'SertifikaTema') IS NULL
                      BEGIN
                          ALTER TABLE dbo.Anket
                              ADD SertifikaTema NVARCHAR(30) NOT NULL
                                  CONSTRAINT DF_Anket_SertifikaTema DEFAULT (N'Modern');
                      END;

                      IF COL_LENGTH('dbo.Anket', 'SertifikaLogo') IS NULL
                      BEGIN
                          ALTER TABLE dbo.Anket
                              ADD SertifikaLogo NVARCHAR(300) NULL;
                      END;

                      IF COL_LENGTH('dbo.Anket', 'SertifikaVurguRengi') IS NULL
                      BEGIN
                          ALTER TABLE dbo.Anket
                              ADD SertifikaVurguRengi NVARCHAR(20) NOT NULL
                                  CONSTRAINT DF_Anket_SertifikaVurguRengi DEFAULT (N'#2563eb');
                      END;

                      IF COL_LENGTH('dbo.Anket', 'SertifikaCerceve') IS NULL
                      BEGIN
                          ALTER TABLE dbo.Anket
                              ADD SertifikaCerceve NVARCHAR(40) NOT NULL
                                  CONSTRAINT DF_Anket_SertifikaCerceve DEFAULT (N'Classic');
                      END;

                      IF COL_LENGTH('dbo.Anket', 'SertifikaFont') IS NULL
                      BEGIN
                          ALTER TABLE dbo.Anket
                              ADD SertifikaFont NVARCHAR(40) NOT NULL
                                  CONSTRAINT DF_Anket_SertifikaFont DEFAULT (N'Georgia');
                      END;

                      IF COL_LENGTH('dbo.Anket', 'SertifikaYaziPunto') IS NULL
                      BEGIN
                          ALTER TABLE dbo.Anket
                              ADD SertifikaYaziPunto INT NOT NULL
                                  CONSTRAINT DF_Anket_SertifikaYaziPunto DEFAULT (17);
                      END;

                      IF COL_LENGTH('dbo.Anket', 'SertifikaBaslikPunto') IS NULL
                      BEGIN
                          ALTER TABLE dbo.Anket
                              ADD SertifikaBaslikPunto INT NOT NULL
                                  CONSTRAINT DF_Anket_SertifikaBaslikPunto DEFAULT (44);
                      END;");
            }
            catch
            {
                // VeritabanÄ± kullanÄ±cÄ±sÄ±nÄ±n DDL yetkisi yoksa SQL scripti elle Ã§alÄ±ÅŸtÄ±rÄ±labilir.
            }
        }

        private SertifikaAyarRow SertifikaAyariniGetir(int anketId)
        {
            SertifikaTasarimKolonlariniHazirla();

            try
            {
                var ayar = db.Database.SqlQuery<SertifikaAyarRow>(
                    @"SELECT TOP 1
                             CAST(ISNULL(SertifikaAktif, ISNULL(Sonuc, 0)) AS bit) AS SertifikaAktif,
                             CAST(ISNULL(SertifikaKatilimciErisimi, 1) AS bit) AS SertifikaKatilimciErisimi,
                             ISNULL(NULLIF(SertifikaVerilisZamani, N''), N'SureBitince') AS SertifikaVerilisZamani,
                             ISNULL(NULLIF(SertifikaBaslik, N''), N'KatÄ±lÄ±m SertifikasÄ±') AS SertifikaBaslik,
                             SertifikaMetni,
                             YayinBitisTarihi
                      FROM dbo.Anket
                      WHERE AnketId = @p0",
                    anketId).FirstOrDefault();

                if (ayar != null)
                {
                    ayar.SertifikaBaslik = NormalizeSertifikaBaslik(ayar.SertifikaBaslik);
                    try
                    {
                        var tasarim = db.Database.SqlQuery<SertifikaTasarimRow>(
                            @"SELECT TOP 1
                                     SertifikaTema,
                                     SertifikaLogo,
                                     SertifikaVurguRengi,
                                     SertifikaCerceve,
                                     SertifikaFont,
                                     SertifikaYaziPunto,
                                     SertifikaBaslikPunto
                              FROM dbo.Anket
                              WHERE AnketId = @p0",
                            anketId).FirstOrDefault();

                        if (tasarim != null)
                        {
                            ayar.SertifikaTema = tasarim.SertifikaTema;
                            ayar.SertifikaLogo = tasarim.SertifikaLogo;
                            ayar.SertifikaVurguRengi = tasarim.SertifikaVurguRengi;
                            ayar.SertifikaCerceve = tasarim.SertifikaCerceve;
                            ayar.SertifikaFont = tasarim.SertifikaFont;
                            ayar.SertifikaYaziPunto = tasarim.SertifikaYaziPunto;
                            ayar.SertifikaBaslikPunto = tasarim.SertifikaBaslikPunto;
                        }
                    }
                    catch
                    {
                        // Yeni tasarÄ±m kolonlarÄ± eklenmemiÅŸse sertifika eski ayarlarla Ã§alÄ±ÅŸmaya devam eder.
                    }

                    ayar.SertifikaTema = NormalizeSertifikaTema(ayar.SertifikaTema);
                    ayar.SertifikaVurguRengi = NormalizeSertifikaRengi(ayar.SertifikaVurguRengi);
                    ayar.SertifikaCerceve = NormalizeSertifikaCerceve(ayar.SertifikaCerceve);
                    ayar.SertifikaFont = NormalizeSertifikaFont(ayar.SertifikaFont);
                    ayar.SertifikaYaziPunto = NormalizeSertifikaPunto(ayar.SertifikaYaziPunto, 17, 11, 28);
                    ayar.SertifikaBaslikPunto = NormalizeSertifikaPunto(ayar.SertifikaBaslikPunto, 44, 24, 72);
                }

                return ayar;
            }
            catch
            {
                var anket = db.Anket.FirstOrDefault(x => x.AnketId == anketId);
                return new SertifikaAyarRow
                {
                    SertifikaAktif = anket?.Sonuc == true,
                    SertifikaKatilimciErisimi = true,
                    SertifikaVerilisZamani = "SureBitince",
                    SertifikaBaslik = "KatÄ±lÄ±m SertifikasÄ±",
                    SertifikaTema = "Modern",
                    SertifikaVurguRengi = "#2563eb",
                    SertifikaCerceve = "Classic",
                    SertifikaFont = "Georgia",
                    SertifikaYaziPunto = 17,
                    SertifikaBaslikPunto = 44
                };
            }
        }

        private static string NormalizeSertifikaZamani(string value)
        {
            return value == "Tamamlayinca" || value == "YayinBitince" || value == "SureBitince"
                ? value
                : "SureBitince";
        }

        private static string NormalizeSertifikaBaslik(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "KatÄ±lÄ±m SertifikasÄ±";
            }

            var baslik = value.Trim();
            return baslik == "KatÃ„Â±lÃ„Â±m SertifikasÃ„Â±" ? "KatÄ±lÄ±m SertifikasÄ±" : baslik;
        }

        private static string NormalizeSertifikaTema(string value)
        {
            switch ((value ?? string.Empty).Trim())
            {
                case "Modern":
                case "Prestij":
                case "Minimal":
                case "Kurumsal":
                    return value.Trim();
                default:
                    return "Modern";
            }
        }

        private static string NormalizeSertifikaRengi(string value)
        {
            var renk = string.IsNullOrWhiteSpace(value) ? "#2563eb" : value.Trim();
            return System.Text.RegularExpressions.Regex.IsMatch(renk, "^#[0-9a-fA-F]{6}$") ? renk : "#2563eb";
        }

        private static string NormalizeSertifikaCerceve(string value)
        {
            switch ((value ?? string.Empty).Trim())
            {
                case "Classic":
                case "DoubleGold":
                case "Ribbon":
                case "CornerSeal":
                case "GradientWash":
                case "Royal":
                case "SideStripe":
                case "TopBand":
                case "MinimalLine":
                case "ArtDeco":
                case "BlueClassic":
                case "LaceColor":
                case "GoldOrnate":
                case "SealRibbon":
                case "FormalAward":
                case "FloralGold":
                case "Ottoman":
                case "AofTurquoise":
                case "Guilloche":
                case "Victorian":
                case "Laurel":
                case "CornerFlourish":
                case "FineLace":
                case "Antique":
                    return value.Trim();
                default:
                    return "Classic";
            }
        }

        private static string NormalizeSertifikaFont(string value)
        {
            switch ((value ?? string.Empty).Trim())
            {
                case "Georgia":
                case "Garamond":
                case "Playfair":
                case "Merriweather":
                case "Cambria":
                case "Times":
                case "Arial":
                case "Verdana":
                case "OpenSans":
                    return value.Trim();
                default:
                    return "Georgia";
            }
        }

        private static int NormalizeSertifikaPunto(int? value, int fallback, int min, int max)
        {
            var punto = value ?? fallback;
            if (punto < min) return min;
            if (punto > max) return max;
            return punto;
        }

        private static bool SertifikaGorseliUzantisiGecerli(string fileName)
        {
            var extension = Path.GetExtension(fileName)?.ToLowerInvariant();
            return extension == ".png" || extension == ".jpg" || extension == ".jpeg" || extension == ".webp";
        }

        private bool SertifikaZamaniGeldiMi(SertifikaAyarRow ayar, bool sureDevamEdiyor, bool tamamlandi, out string mesaj)
        {
            var zaman = NormalizeSertifikaZamani(ayar?.SertifikaVerilisZamani);
            mesaj = string.Empty;

            if (zaman == "Tamamlayinca")
            {
                if (tamamlandi)
                {
                    return true;
                }

                mesaj = "Sertifika tÃ¼m sorular tamamlanÄ±nca aÃ§Ä±lacak.";
                return false;
            }

            if (zaman == "YayinBitince")
            {
                if (ayar?.YayinBitisTarihi != null && DateTime.Now >= ayar.YayinBitisTarihi.Value)
                {
                    return true;
                }

                mesaj = ayar?.YayinBitisTarihi != null
                    ? $"Sertifika yayÄ±n bitiÅŸinde aÃ§Ä±lacak: {ayar.YayinBitisTarihi.Value:dd.MM.yyyy HH:mm}."
                    : "Sertifika yayÄ±n bitiÅŸinde aÃ§Ä±lacak; Ã¶nce Ã§alÄ±ÅŸma bitiÅŸ tarihi belirlenmeli.";
                return false;
            }

            if (!sureDevamEdiyor)
            {
                return true;
            }

            mesaj = "Sertifika sÄ±nav sÃ¼resi kapandÄ±ktan sonra aÃ§Ä±lacak.";
            return false;
        }

        private void YayinAyarlariniKaydet(int anketId, DateTime? baslangic, DateTime? bitis)
        {
            try
            {
                db.Database.ExecuteSqlCommand(
                    @"UPDATE dbo.Anket
                      SET YayinBaslangicTarihi = @p0,
                          YayinBitisTarihi = @p1
                      WHERE AnketId = @p2",
                    (object)baslangic ?? DBNull.Value,
                    (object)bitis ?? DBNull.Value,
                    anketId);
            }
            catch
            {
                // Yeni kolonlar henuz uygulanmamis ortamlarda kayit akisini kirmayalim.
            }
        }

        private void PrepareAnketYayinViewBag(int anketId, DateTime? baslangic = null, DateTime? bitis = null, bool postedValues = false)
        {
            if (!postedValues)
            {
                var ayar = AnketYayinAyariniGetir(anketId);
                baslangic = ayar?.YayinBaslangicTarihi;
                bitis = ayar?.YayinBitisTarihi;
            }

            ViewBag.YayinBaslangicLocal = DateTimeLocalValue(baslangic);
            ViewBag.YayinBitisLocal = DateTimeLocalValue(bitis);
        }

        private bool AnketKatilimaAcikMi(int anketId, out string mesaj)
        {
            mesaj = string.Empty;
            var ayar = AnketYayinAyariniGetir(anketId);
            if (ayar == null)
            {
                return true;
            }

            if (ayar.Pasif == true)
            {
                mesaj = "Bu calisma su anda yayinda degil.";
                return false;
            }

            var simdi = DateTime.Now;
            if (ayar.YayinBaslangicTarihi.HasValue && simdi < ayar.YayinBaslangicTarihi.Value)
            {
                mesaj = $"Bu calisma {ayar.YayinBaslangicTarihi.Value:dd.MM.yyyy HH:mm} tarihinde baslayacak.";
                return false;
            }

            if (ayar.YayinBitisTarihi.HasValue && simdi > ayar.YayinBitisTarihi.Value)
            {
                mesaj = $"Bu calismanin katilim suresi {ayar.YayinBitisTarihi.Value:dd.MM.yyyy HH:mm} tarihinde sona erdi.";
                return false;
            }

            return true;
        }

        private static string YeniKatilimToken()
        {
            var bytes = RandomNumberGenerator.GetBytes(32);
            return Convert.ToBase64String(bytes)
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
        }

        private static string NormalizeKatilimToken(string token)
        {
            var value = token?.Trim();
            if (string.IsNullOrWhiteSpace(value) || value.Length < 20 || value.Length > 128)
            {
                return null;
            }

            return Regex.IsMatch(value, "^[A-Za-z0-9_-]+$") ? value : null;
        }

        private string EnsureAnketPaylasimToken(int anketId)
        {
            try
            {
                var mevcut = db.Database.SqlQuery<string>(
                    "SELECT KatilimToken FROM dbo.Anket WHERE AnketId = @p0",
                    anketId).FirstOrDefault();

                if (!string.IsNullOrWhiteSpace(mevcut))
                {
                    return mevcut;
                }

                for (var attempt = 0; attempt < 4; attempt++)
                {
                    var token = YeniKatilimToken();
                    try
                    {
                        db.Database.ExecuteSqlCommand(
                            @"UPDATE dbo.Anket
                              SET KatilimToken = @p0,
                                  KatilimTokenTarihi = SYSDATETIME()
                              WHERE AnketId = @p1
                                AND (KatilimToken IS NULL OR LTRIM(RTRIM(KatilimToken)) = N'')",
                            token,
                            anketId);

                        var kayitliToken = db.Database.SqlQuery<string>(
                            "SELECT KatilimToken FROM dbo.Anket WHERE AnketId = @p0",
                            anketId).FirstOrDefault();

                        if (!string.IsNullOrWhiteSpace(kayitliToken))
                        {
                            return kayitliToken;
                        }
                    }
                    catch
                    {
                        // Cok dusuk ihtimalle token cakisirsa yeniden uret.
                    }
                }
            }
            catch
            {
                // Kolonlar henuz yoksa eski akisi tamamen kirmamak icin bos don.
            }

            return string.Empty;
        }

        private Dictionary<int, string> EnsureAnketPaylasimTokenlari(IEnumerable<int> anketIds)
        {
            var result = new Dictionary<int, string>();
            foreach (var anketId in anketIds.Distinct())
            {
                result[anketId] = EnsureAnketPaylasimToken(anketId);
            }

            return result;
        }

        private int? AnketIdFromKatilimToken(string token)
        {
            var normalized = NormalizeKatilimToken(token);
            if (normalized == null)
            {
                return null;
            }

            try
            {
                var anketId = db.Database.SqlQuery<int>(
                    "SELECT TOP 1 AnketId FROM dbo.Anket WHERE KatilimToken = @p0",
                    normalized).FirstOrDefault();

                return anketId > 0 ? anketId : (int?)null;
            }
            catch
            {
                return null;
            }
        }

        public ActionResult Indexgosterge()
        {

            if (Session["id"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }
            if (Session["admin"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }


            var anketler = CalismaAlaniAnketleri();
            var anketIdleri = anketler.Select(x => (int?)x.AnketId).ToList();
            var izlemeKayitlari = db.Izledim
                .Where(x => anketIdleri.Contains(x.AnketId))
                .ToList();
            var kayitliKatilimCiftleri = db.Havuz.AsNoTracking()
                .Where(x => anketIdleri.Contains(x.AnketId) && x.AnketId != null && x.UserId != null && x.UserId != 1)
                .GroupBy(x => new { AnketId = x.AnketId.Value, UserId = x.UserId.Value })
                .Select(x => x.Key)
                .ToList();
            var anonimKatilimCiftleri = db.Havuz.AsNoTracking()
                .Where(x => anketIdleri.Contains(x.AnketId) && x.AnketId != null && x.UserId == null && x.Isimsiz != null)
                .GroupBy(x => new { AnketId = x.AnketId.Value, Isimsiz = x.Isimsiz.Value })
                .Select(x => x.Key)
                .ToList();
            var havuzSoruCiftleri = db.Havuz.AsNoTracking()
                .Where(x => anketIdleri.Contains(x.AnketId) && x.AnketId != null && x.SoruID != null)
                .GroupBy(x => new { AnketId = x.AnketId.Value, SoruId = x.SoruID.Value })
                .Select(x => x.Key)
                .ToList();
            var kayitliKatilimMap = kayitliKatilimCiftleri
                .GroupBy(x => x.AnketId)
                .ToDictionary(x => x.Key, x => x.Count());
            var anonimKatilimMap = anonimKatilimCiftleri
                .GroupBy(x => x.AnketId)
                .ToDictionary(x => x.Key, x => x.Count());
            var cevapliKatilimMap = anketler.ToDictionary(
                x => x.AnketId,
                x => (kayitliKatilimMap.ContainsKey(x.AnketId) ? kayitliKatilimMap[x.AnketId] : 0)
                    + (anonimKatilimMap.ContainsKey(x.AnketId) ? anonimKatilimMap[x.AnketId] : 0));
            var havuzSoruSayisiMap = havuzSoruCiftleri
                .GroupBy(x => x.AnketId)
                .ToDictionary(x => x.Key, x => x.Count());

            ViewBag.KatilimTokenMap = EnsureAnketPaylasimTokenlari(anketler.Select(x => x.AnketId));
            ViewBag.KatilimYontemiMap = KatilimYontemleriGetir(anketler.Select(x => x.AnketId));
            ViewBag.SureKayitMap = izlemeKayitlari
                .Where(x => x.AnketId.HasValue)
                .GroupBy(x => x.AnketId.Value)
                .ToDictionary(x => x.Key, x => x.Count());
            ViewBag.SureKayitSayisi = izlemeKayitlari.Count;
            ViewBag.KatilimSayisi = kayitliKatilimCiftleri.Select(x => x.UserId).Distinct().Count()
                + anonimKatilimCiftleri.Select(x => x.Isimsiz).Distinct().Count();
            ViewBag.CevapliKatilimMap = cevapliKatilimMap;
            ViewBag.HavuzSoruSayisiMap = havuzSoruSayisiMap;
            Tumcontroller model = new Tumcontroller()
            {
                Ank = anketler,
                Hav = Enumerable.Empty<Havuz>(),
                AnkGrp = db.AnketGrup.Where(x => anketIdleri.Contains(x.AnketId)),
                Sor = CalismaAlaniBankaKayitlari<Soru>("Soru", "SoruAdi"),
            };
            return View(model);
        }
        public ActionResult UserEdit(int id)
        {

            if (Session["id"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }
            if (Session["admin"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }

            var aktifPersonelId = AktifPersonelId();
            if (!aktifPersonelId.HasValue)
            {
                return RedirectToAction("Giris", "Home", null);
            }

            if (id != aktifPersonelId.Value)
            {
                return RedirectToAction("UserEdit", "Home", new { id = aktifPersonelId.Value });
            }

            ViewBag.KayitTar = DateTime.Now.ToString("yyyy-MM-dd HH:mm");

            var kayit = db.Personel.FirstOrDefault(x => x.PersonelId == aktifPersonelId.Value);
            if (kayit == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }

            kayit.Sifre = string.Empty;
            return View(kayit);
        }
        [ValidateAntiForgeryToken()]
        [HttpPost]
        [ValidateInput(false)]
        public ActionResult UserEdit(Personel Personel, IFormFile uploadfile)
        {
            if (Session["id"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }
            if (Session["admin"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }

            var aktifPersonelId = AktifPersonelId();
            if (!aktifPersonelId.HasValue || Personel == null || Personel.PersonelId != aktifPersonelId.Value)
            {
                return RedirectToAction("Giris", "Home", null);
            }

            try
            {
                var kayit = db.Personel.FirstOrDefault(x => x.PersonelId == aktifPersonelId.Value);
                if (kayit == null)
                {
                    return RedirectToAction("Giris", "Home", null);
                }

                if (uploadfile != null && uploadfile.Length > 0)
                {
                    string ResimAdi = Path.GetFileName(uploadfile.FileName);
                    string adres = MapPath("~/Content/Personel/" + ResimAdi);
                    uploadfile.SaveAs(adres);
                    kayit.Resim = ResimAdi;
                    Session["resim"] = ResimAdi;
                }

                if (!string.IsNullOrWhiteSpace(Personel.Sifre))
                {
                    kayit.Sifre = Personel.Sifre;
                }

                db.SaveChanges();
                return RedirectToAction("Indexgosterge", "Home", new { id = Session["id"] });
            }
            catch
            {
                var kayit = db.Personel.FirstOrDefault(x => x.PersonelId == aktifPersonelId.Value);
                if (kayit != null)
                {
                    kayit.Sifre = string.Empty;
                }

                return View(kayit);
            }

        }
        public ActionResult AnketIndex()
        {
            if (Session["id"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }
            if (Session["admin"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }

            return View(CalismaAlaniAnketleri());
        }

        private IQueryable<Havuz> RaporHavuzSorgusu(int? anketId)
        {
            return db.Havuz.AsNoTracking()
                .Include("Anket")
                .Include("User")
                .Include("User.Egitim")
                .Include("User.Departman")
                .Include("User.Cinsiyet")
                .Include("User.Sehir")
                .Include("User.Sube")
                .Include("User.Unvan")
                .Include("User.Yaka")
                .Include("User.Yonetici")
                .Include("Cevap")
                .Include("Soru")
                .Include("Soru.SoruGrup")
                .Include("SoruGrup")
                .Where(x => x.AnketId == anketId);
        }

        public ActionResult AnketIndex1(int id, int? filterEgitimId = null, string returnUrl = null)
        {
            if (Session["id"] == null || Session["admin"] == null)
                return RedirectToAction("Giris", "Home");

            var anket = CalismaAlaniAnketGetir(id);
            if (anket == null)
                return RedirectToAction("AnketIndex");

            ViewBag.adi = anket.AnketAdi;
            ViewBag.anketadi = filterEgitimId != null
                ? db.Egitim.FirstOrDefault(x => x.EgitimId == filterEgitimId)?.EgitimAdi
                : "TÃ¼mÃ¼";
            ViewBag.id = anket.AnketId;
            ViewBag.sinav = SinavTurundeMi(anket);
            ViewBag.FilterEgitim = filterEgitimId;
            ViewBag.ReturnUrl = CalismaAlaniDonusAdresi(returnUrl);

            var havuz = RaporHavuzSorgusu(id).ToList();

            ViewBag.YoneticiResimleri = YoneticiResimSozlugu(havuz.Select(x => x.User?.UserYoneticisi));

            return View("AnketIndex1", havuz);
        }
        public ActionResult AnketGenelIndex(int id, int? ank)
        {
            if (Session["id"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }
            if (Session["admin"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }

            var anket = CalismaAlaniAnketGetir(id);
            if (anket == null)
            {
                return RedirectToAction("AnketIndex");
            }

            ViewBag.id = id;
            ViewBag.adi = anket.AnketAdi;
            ViewBag.sinav = SinavTurundeMi(anket);

            var bul = RaporHavuzSorgusu(id).ToList();
            ViewBag.YoneticiResimleri = YoneticiResimSozlugu(bul.Select(x => x.User?.UserYoneticisi));
            var adi = bul.FirstOrDefault();
            if (ank != null)
            {
                ViewBag.ank = ank;

            }
            if (adi != null)
            {
                ViewBag.anketadi = adi.Anket?.AnketAdi ?? anket?.AnketAdi;
                ViewBag.id = adi.AnketId;
                ViewBag.sinav = SinavTurundeMi(adi.Anket ?? anket);

            }

            // Genel
            ViewBag.baslik1 = new List<string>();
            ViewBag.puan1 = new List<float>();
            ViewBag.adet1 = new List<int>();

            // Departman
            ViewBag.baslik2 = new List<string>();
            ViewBag.puan2 = new List<float>();
            ViewBag.adet2 = new List<int>();

            // Cinsiyet
            ViewBag.baslik3 = new List<string>();
            ViewBag.puan3 = new List<float>();
            ViewBag.adet3 = new List<int>();

            // EÄŸitim
            ViewBag.baslik4 = new List<string>();
            ViewBag.puan4 = new List<float>();
            ViewBag.adet4 = new List<int>();

            // YaÅŸ
            ViewBag.baslik5 = new List<string>();  // int yerine string yapalÄ±m â†’ chart iÃ§in daha kolay
            ViewBag.puan5 = new List<float>();
            ViewBag.adet5 = new List<int>();

            // Åehir
            ViewBag.baslik6 = new List<string>();
            ViewBag.puan6 = new List<float>();
            ViewBag.adet6 = new List<int>();

            // Åube
            ViewBag.baslik7 = new List<string>();
            ViewBag.puan7 = new List<float>();
            ViewBag.adet7 = new List<int>();

            // Ãœnvan
            ViewBag.baslik8 = new List<string>();
            ViewBag.puan8 = new List<float>();
            ViewBag.adet8 = new List<int>();

            // Yaka
            ViewBag.baslik9 = new List<string>();
            ViewBag.puan9 = new List<float>();
            ViewBag.adet9 = new List<int>();

            // YÃ¶netici
            ViewBag.baslik10 = new List<string>();
            ViewBag.puan10 = new List<float>();
            ViewBag.adet10 = new List<int>();
            ViewBag.yoneticiResim10 = new List<string>();

            var yoneticiResimSozlugu = YoneticiResimSozlugu(bul.AsEnumerable().Select(x => x.User?.UserYoneticisi));

            // Soru
            ViewBag.baslik11 = new List<string>();
            ViewBag.puan11 = new List<float>();
            ViewBag.adet11 = new List<int>();
            ViewBag.soruGrup11 = new List<string>();


            // Soru Grup
            ViewBag.baslik12 = new List<string>();
            ViewBag.puan12 = new List<float>();
            ViewBag.adet12 = new List<int>();

            foreach (var item in bul.GroupBy(x => x.Anket?.AnketAdi ?? anket.AnketAdi ?? "Ã‡alÄ±ÅŸma"))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerÃ§ek kiÅŸi adedi

                var c5 = item.Count(x => x.CevapPuan == 5);
                var c4 = item.Count(x => x.CevapPuan == 4);
                var c3 = item.Count(x => x.CevapPuan == 3);
                var c2 = item.Count(x => x.CevapPuan == 2);
                var c1 = item.Count(x => x.CevapPuan == 1);

                ViewBag.d5 = (float)c5 * 5 / 5 * 100;
                ViewBag.d4 = (float)c4 * 4 / 5 * 100;
                ViewBag.d3 = (float)c3 * 3 / 5 * 100;
                ViewBag.d2 = (float)c2 * 2 / 5 * 100;
                ViewBag.d1 = (float)c1 * 1 / 5 * 100;

                // PuanÄ± hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanÄ±nÄ± yÃ¼zdesini bulur
                var p1 = p2 / soru;

                ViewBag.baslik1.Add(item.Key);
                ViewBag.puan1.Add(p1);
                ViewBag.adet1.Add(kisiSayisi);
            }
            foreach (var item in bul.GroupBy(x => x.User?.Departman?.DepartmanAdi ?? "TanÄ±msÄ±z"))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerÃ§ek kiÅŸi adedi


                var c5 = item.Count(x => x.CevapPuan == 5);
                var c4 = item.Count(x => x.CevapPuan == 4);
                var c3 = item.Count(x => x.CevapPuan == 3);
                var c2 = item.Count(x => x.CevapPuan == 2);
                var c1 = item.Count(x => x.CevapPuan == 1);

                ViewBag.d5 = (float)c5 * 5 / 5 * 100;
                ViewBag.d4 = (float)c4 * 4 / 5 * 100;
                ViewBag.d3 = (float)c3 * 3 / 5 * 100;
                ViewBag.d2 = (float)c2 * 2 / 5 * 100;
                ViewBag.d1 = (float)c1 * 1 / 5 * 100;

                // PuanÄ± hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanÄ±nÄ± yÃ¼zdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik2.Add(item.Key);
                ViewBag.puan2.Add(p1);
                ViewBag.adet2.Add(kisiSayisi);

            }
            foreach (var item in bul.GroupBy(x => x.User?.Cinsiyet?.CinsiyetAdi ?? "TanÄ±msÄ±z"))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerÃ§ek kiÅŸi adedi

                var c5 = item.Count(x => x.CevapPuan == 5);
                var c4 = item.Count(x => x.CevapPuan == 4);
                var c3 = item.Count(x => x.CevapPuan == 3);
                var c2 = item.Count(x => x.CevapPuan == 2);
                var c1 = item.Count(x => x.CevapPuan == 1);

                ViewBag.d5 = (float)c5 * 5 / 5 * 100;
                ViewBag.d4 = (float)c4 * 4 / 5 * 100;
                ViewBag.d3 = (float)c3 * 3 / 5 * 100;
                ViewBag.d2 = (float)c2 * 2 / 5 * 100;
                ViewBag.d1 = (float)c1 * 1 / 5 * 100;

                // PuanÄ± hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanÄ±nÄ± yÃ¼zdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik3.Add(item.Key);
                ViewBag.puan3.Add(p1);
                ViewBag.adet3.Add(kisiSayisi);

            }
            foreach (var item in bul.GroupBy(x => x.User?.Egitim?.EgitimAdi ?? "TanÄ±msÄ±z"))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerÃ§ek kiÅŸi adedi

                var c5 = item.Count(x => x.CevapPuan == 5);
                var c4 = item.Count(x => x.CevapPuan == 4);
                var c3 = item.Count(x => x.CevapPuan == 3);
                var c2 = item.Count(x => x.CevapPuan == 2);
                var c1 = item.Count(x => x.CevapPuan == 1);

                ViewBag.d5 = (float)c5 * 5 / 5 * 100;
                ViewBag.d4 = (float)c4 * 4 / 5 * 100;
                ViewBag.d3 = (float)c3 * 3 / 5 * 100;
                ViewBag.d2 = (float)c2 * 2 / 5 * 100;
                ViewBag.d1 = (float)c1 * 1 / 5 * 100;

                // PuanÄ± hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanÄ±nÄ± yÃ¼zdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik4.Add(item.Key);
                ViewBag.puan4.Add(p1);
                ViewBag.adet4.Add(kisiSayisi);

            }
            var bugun = DateTime.Today;

            // Ã–nce boÅŸ listeleri hazÄ±rla
            ViewBag.baslik5 = new List<string>();
            ViewBag.puan5 = new List<float>();

            foreach (var item in bul.AsEnumerable()
                .Where(x => x.User?.UserDogumTar != null)
                .GroupBy(x => x.User.UserDogumTar.Value.Year))
            {
                var dogumYili = item.Key;
                var yas = bugun.Year - dogumYili;
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerÃ§ek kiÅŸi adedi


                // EÄŸer doÄŸum gÃ¼nÃ¼ bu yÄ±l daha gelmediyse yaÅŸÄ± 1 azalt
                var ilkKayit = item.FirstOrDefault()?.User?.UserDogumTar;
                if (ilkKayit != null && bugun.DayOfYear < ilkKayit.Value.DayOfYear)
                {
                    yas--;
                }

                // Ortalama puanÄ± hesapla (kiÅŸiye gÃ¶re normalize)
                var ortalama = item
                    .GroupBy(x => x.UserId)
                    .Select(g => g.Average(y => y.CevapPuan))
                    .Average();

                var ortalamaYuzde = ortalama * 20; // 5 Ã¼zerinden 100'e Ã§evirme

                // ğŸ”¹ Burada artÄ±k Add et
                ViewBag.baslik5.Add(yas.ToString());       // X ekseni â†’ yaÅŸ
                ViewBag.puan5.Add((float)ortalamaYuzde);   // Y ekseni â†’ puan
                ViewBag.adet5.Add(kisiSayisi);   // Y ekseni â†’ puan
            }

            foreach (var item in bul.GroupBy(x => x.User?.Sehir?.SehiarAdi ?? "TanÄ±msÄ±z"))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerÃ§ek kiÅŸi adedi

                var c5 = item.Count(x => x.CevapPuan == 5);
                var c4 = item.Count(x => x.CevapPuan == 4);
                var c3 = item.Count(x => x.CevapPuan == 3);
                var c2 = item.Count(x => x.CevapPuan == 2);
                var c1 = item.Count(x => x.CevapPuan == 1);

                ViewBag.d5 = (float)c5 * 5 / 5 * 100;
                ViewBag.d4 = (float)c4 * 4 / 5 * 100;
                ViewBag.d3 = (float)c3 * 3 / 5 * 100;
                ViewBag.d2 = (float)c2 * 2 / 5 * 100;
                ViewBag.d1 = (float)c1 * 1 / 5 * 100;

                // PuanÄ± hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanÄ±nÄ± yÃ¼zdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik6.Add(item.Key);
                ViewBag.puan6.Add(p1);
                ViewBag.adet6.Add(kisiSayisi);

            }
            foreach (var item in bul.GroupBy(x => x.User?.Sube?.SubeAdi ?? "TanÄ±msÄ±z"))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerÃ§ek kiÅŸi adedi

                var c5 = item.Count(x => x.CevapPuan == 5);
                var c4 = item.Count(x => x.CevapPuan == 4);
                var c3 = item.Count(x => x.CevapPuan == 3);
                var c2 = item.Count(x => x.CevapPuan == 2);
                var c1 = item.Count(x => x.CevapPuan == 1);

                ViewBag.d5 = (float)c5 * 5 / 5 * 100;
                ViewBag.d4 = (float)c4 * 4 / 5 * 100;
                ViewBag.d3 = (float)c3 * 3 / 5 * 100;
                ViewBag.d2 = (float)c2 * 2 / 5 * 100;
                ViewBag.d1 = (float)c1 * 1 / 5 * 100;

                // PuanÄ± hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanÄ±nÄ± yÃ¼zdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik7.Add(item.Key);
                ViewBag.puan7.Add(p1);
                ViewBag.adet7.Add(kisiSayisi);

            }
            foreach (var item in bul.GroupBy(x => x.User?.Unvan?.UnvanAdi ?? "TanÄ±msÄ±z"))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerÃ§ek kiÅŸi adedi

                var c5 = item.Count(x => x.CevapPuan == 5);
                var c4 = item.Count(x => x.CevapPuan == 4);
                var c3 = item.Count(x => x.CevapPuan == 3);
                var c2 = item.Count(x => x.CevapPuan == 2);
                var c1 = item.Count(x => x.CevapPuan == 1);

                ViewBag.d5 = (float)c5 * 5 / 5 * 100;
                ViewBag.d4 = (float)c4 * 4 / 5 * 100;
                ViewBag.d3 = (float)c3 * 3 / 5 * 100;
                ViewBag.d2 = (float)c2 * 2 / 5 * 100;
                ViewBag.d1 = (float)c1 * 1 / 5 * 100;

                // PuanÄ± hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanÄ±nÄ± yÃ¼zdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik8.Add(item.Key);
                ViewBag.puan8.Add(p1);
                ViewBag.adet8.Add(kisiSayisi);

            }
            foreach (var item in bul.GroupBy(x => x.User?.Yaka?.YakaAdi ?? "TanÄ±msÄ±z"))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerÃ§ek kiÅŸi adedi

                var c5 = item.Count(x => x.CevapPuan == 5);
                var c4 = item.Count(x => x.CevapPuan == 4);
                var c3 = item.Count(x => x.CevapPuan == 3);
                var c2 = item.Count(x => x.CevapPuan == 2);
                var c1 = item.Count(x => x.CevapPuan == 1);

                ViewBag.d5 = (float)c5 * 5 / 5 * 100;
                ViewBag.d4 = (float)c4 * 4 / 5 * 100;
                ViewBag.d3 = (float)c3 * 3 / 5 * 100;
                ViewBag.d2 = (float)c2 * 2 / 5 * 100;
                ViewBag.d1 = (float)c1 * 1 / 5 * 100;

                // PuanÄ± hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanÄ±nÄ± yÃ¼zdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik9.Add(item.Key);
                ViewBag.puan9.Add(p1);
                ViewBag.adet9.Add(kisiSayisi);

            }
            foreach (var item in bul.AsEnumerable().GroupBy(x => new
            {
                Id = x.User?.UserYoneticisi ?? 0,
                Ad = x.User?.Yonetici?.YoneticiAdi ?? "Tanımsız"
            }))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerÃ§ek kiÅŸi adedi

                var c5 = item.Count(x => x.CevapPuan == 5);
                var c4 = item.Count(x => x.CevapPuan == 4);
                var c3 = item.Count(x => x.CevapPuan == 3);
                var c2 = item.Count(x => x.CevapPuan == 2);
                var c1 = item.Count(x => x.CevapPuan == 1);

                ViewBag.d5 = (float)c5 * 5 / 5 * 100;
                ViewBag.d4 = (float)c4 * 4 / 5 * 100;
                ViewBag.d3 = (float)c3 * 3 / 5 * 100;
                ViewBag.d2 = (float)c2 * 2 / 5 * 100;
                ViewBag.d1 = (float)c1 * 1 / 5 * 100;

                // PuanÄ± hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanÄ±nÄ± yÃ¼zdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik10.Add(item.Key.Ad);
                ViewBag.puan10.Add(p1);
                ViewBag.adet10.Add(kisiSayisi);

            }
            // Soru + Soru Grup

            foreach (var item in bul.GroupBy(x => new
            {
                SoruAdi = x.Soru?.SoruAdi ?? "TanÄ±msÄ±z soru",
                SoruGrupAdi = x.SoruGrup?.SoruGrupAdi ?? "TanÄ±msÄ±z rapor baÅŸlÄ±ÄŸÄ±"
            }))
            {
                var soru = item.Count();
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count();

                var c5 = item.Count(x => x.CevapPuan == 5);
                var c4 = item.Count(x => x.CevapPuan == 4);
                var c3 = item.Count(x => x.CevapPuan == 3);
                var c2 = item.Count(x => x.CevapPuan == 2);
                var c1 = item.Count(x => x.CevapPuan == 1);

                var d5 = (float)c5 * 5 / 5 * 100;
                var d4 = (float)c4 * 4 / 5 * 100;
                var d3 = (float)c3 * 3 / 5 * 100;
                var d2 = (float)c2 * 2 / 5 * 100;
                var d1 = (float)c1 * 1 / 5 * 100;

                var p2 = d1 + d2 + d3 + d4 + d5;
                var p1 = p2 / soru;

                ViewBag.baslik11.Add(item.Key.SoruAdi);          // Soru
                ViewBag.soruGrup11.Add(item.Key.SoruGrupAdi);    // Soru Grup
                ViewBag.puan11.Add(p1);
                ViewBag.adet11.Add(kisiSayisi);
            }

            foreach (var item in bul.GroupBy(x => x.SoruGrup?.SoruGrupAdi ?? "TanÄ±msÄ±z rapor baÅŸlÄ±ÄŸÄ±"))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerÃ§ek kiÅŸi adedi

                var c5 = item.Count(x => x.CevapPuan == 5);
                var c4 = item.Count(x => x.CevapPuan == 4);
                var c3 = item.Count(x => x.CevapPuan == 3);
                var c2 = item.Count(x => x.CevapPuan == 2);
                var c1 = item.Count(x => x.CevapPuan == 1);

                ViewBag.d5 = (float)c5 * 5 / 5 * 100;
                ViewBag.d4 = (float)c4 * 4 / 5 * 100;
                ViewBag.d3 = (float)c3 * 3 / 5 * 100;
                ViewBag.d2 = (float)c2 * 2 / 5 * 100;
                ViewBag.d1 = (float)c1 * 1 / 5 * 100;

                // PuanÄ± hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanÄ±nÄ± yÃ¼zdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik12.Add(item.Key);
                ViewBag.puan12.Add(p1);
                ViewBag.adet12.Add(kisiSayisi);

            }

            RaporYoneticiResimleriHazirla(bul);

            return View("AnketSoruGrupIndex", bul);
        }

        public ActionResult MemnuniyetRaporu(int id)
        {
            if (Session["id"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }
            if (Session["admin"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }

            if (!AnketCalismaAlanindaMi(id))
            {
                return RedirectToAction("AnketIndex");
            }

            var anket = db.Anket
                .Include("Departman")
                .Include("Sehir")
                .Include("Sube")
                .Include("Unvan")
                .FirstOrDefault(x => x.AnketId == id);

            if (anket == null)
            {
                return RedirectToAction("AnketIndex");
            }

            var hedefKitle = db.User.Where(x => x.Pasif != true);

            if (anket.DepartmanId != null)
            {
                hedefKitle = hedefKitle.Where(x => x.UserDepartman == anket.DepartmanId);
            }
            if (anket.SehirId != null)
            {
                hedefKitle = hedefKitle.Where(x => x.UserSehir == anket.SehirId);
            }
            if (anket.SubeId != null)
            {
                hedefKitle = hedefKitle.Where(x => x.UserSube == anket.SubeId);
            }
            if (anket.UnvanId != null)
            {
                hedefKitle = hedefKitle.Where(x => x.UserUnvan == anket.UnvanId);
            }

            ViewBag.Anket = anket;
            ViewBag.id = anket.AnketId;
            ViewBag.adi = anket.AnketAdi;
            ViewBag.sinav = SinavTurundeMi(anket);
            ViewBag.HedefKitle = hedefKitle.Count();
            ViewBag.RaporTarihi = DateTime.Now;

            var havuz = db.Havuz
                .Include("User")
                .Include("User.Egitim")
                .Include("User.Departman")
                .Include("User.Cinsiyet")
                .Include("User.Sehir")
                .Include("User.Sube")
                .Include("User.Unvan")
                .Include("User.Yaka")
                .Include("User.Yonetici")
                .Include("Cevap")
                .Include("Soru")
                .Include("Soru.SoruGrup")
                .Include("SoruGrup")
                .Where(x => x.AnketId == id)
                .ToList();

            return View(havuz);
        }

        public ActionResult AnketDepartmanIndex(int id, int? ank)
        {
            if (Session["id"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }
            if (Session["admin"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }

            if (!AnketCalismaAlanindaMi(ank))
            {
                return RedirectToAction("AnketIndex");
            }

            var bul = RaporHavuzSorgusu(ank)
                .Where(x => x.User.UserDepartman == id)
                .ToList();
            var adi = bul.FirstOrDefault();
            var ankadi = db.Anket.Where(x => x.AnketId == ank).FirstOrDefault();
            if (ankadi != null)
            {
                ViewBag.adi = ankadi.AnketAdi;
                ViewBag.id = ankadi.AnketId;
                ViewBag.sinav = SinavTurundeMi(ankadi);

            }
            if (ank != null)
            {
                ViewBag.ank = ank;
            }
            if (adi != null)
            {
                ViewBag.anketadi = adi.User?.Departman?.DepartmanAdi ?? "Tanımsız";
            }

            // Genel
            ViewBag.baslik1 = new List<string>();
            ViewBag.puan1 = new List<float>();
            ViewBag.adet1 = new List<int>();

            // Departman
            ViewBag.baslik2 = new List<string>();
            ViewBag.puan2 = new List<float>();
            ViewBag.adet2 = new List<int>();

            // Cinsiyet
            ViewBag.baslik3 = new List<string>();
            ViewBag.puan3 = new List<float>();
            ViewBag.adet3 = new List<int>();

            // EÄŸitim
            ViewBag.baslik4 = new List<string>();
            ViewBag.puan4 = new List<float>();
            ViewBag.adet4 = new List<int>();

            // YaÅŸ
            ViewBag.baslik5 = new List<string>();  // int yerine string yapalÄ±m â†’ chart iÃ§in daha kolay
            ViewBag.puan5 = new List<float>();
            ViewBag.adet5 = new List<int>();

            // Åehir
            ViewBag.baslik6 = new List<string>();
            ViewBag.puan6 = new List<float>();
            ViewBag.adet6 = new List<int>();

            // Åube
            ViewBag.baslik7 = new List<string>();
            ViewBag.puan7 = new List<float>();
            ViewBag.adet7 = new List<int>();

            // Ãœnvan
            ViewBag.baslik8 = new List<string>();
            ViewBag.puan8 = new List<float>();
            ViewBag.adet8 = new List<int>();

            // Yaka
            ViewBag.baslik9 = new List<string>();
            ViewBag.puan9 = new List<float>();
            ViewBag.adet9 = new List<int>();

            // YÃ¶netici
            ViewBag.baslik10 = new List<string>();
            ViewBag.puan10 = new List<float>();
            ViewBag.adet10 = new List<int>();

            // Soru
            ViewBag.baslik11 = new List<string>();
            ViewBag.puan11 = new List<float>();
            ViewBag.adet11 = new List<int>();

            // Soru Grup
            ViewBag.baslik12 = new List<string>();
            ViewBag.puan12 = new List<float>();
            ViewBag.adet12 = new List<int>();


            foreach (var item in bul.GroupBy(x => x.Anket.AnketAdi))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerÃ§ek kiÅŸi adedi

                var c5 = item.Count(x => x.CevapPuan == 5);
                var c4 = item.Count(x => x.CevapPuan == 4);
                var c3 = item.Count(x => x.CevapPuan == 3);
                var c2 = item.Count(x => x.CevapPuan == 2);
                var c1 = item.Count(x => x.CevapPuan == 1);

                ViewBag.d5 = (float)c5 * 5 / 5 * 100;
                ViewBag.d4 = (float)c4 * 4 / 5 * 100;
                ViewBag.d3 = (float)c3 * 3 / 5 * 100;
                ViewBag.d2 = (float)c2 * 2 / 5 * 100;
                ViewBag.d1 = (float)c1 * 1 / 5 * 100;

                // PuanÄ± hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanÄ±nÄ± yÃ¼zdesini bulur
                var p1 = p2 / soru;

                ViewBag.baslik1.Add(item.Key);
                ViewBag.puan1.Add(p1);
                ViewBag.adet1.Add(kisiSayisi);

            }
            foreach (var item in bul.AsEnumerable().GroupBy(x => x.User?.Departman?.DepartmanAdi ?? "Tanımsız"))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerÃ§ek kiÅŸi adedi

                var c5 = item.Count(x => x.CevapPuan == 5);
                var c4 = item.Count(x => x.CevapPuan == 4);
                var c3 = item.Count(x => x.CevapPuan == 3);
                var c2 = item.Count(x => x.CevapPuan == 2);
                var c1 = item.Count(x => x.CevapPuan == 1);

                ViewBag.d5 = (float)c5 * 5 / 5 * 100;
                ViewBag.d4 = (float)c4 * 4 / 5 * 100;
                ViewBag.d3 = (float)c3 * 3 / 5 * 100;
                ViewBag.d2 = (float)c2 * 2 / 5 * 100;
                ViewBag.d1 = (float)c1 * 1 / 5 * 100;

                // PuanÄ± hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanÄ±nÄ± yÃ¼zdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik2.Add(item.Key);
                ViewBag.puan2.Add(p1);
                ViewBag.adet2.Add(kisiSayisi);

            }
            foreach (var item in bul.AsEnumerable().GroupBy(x => x.User?.Cinsiyet?.CinsiyetAdi ?? "Tanımsız"))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerÃ§ek kiÅŸi adedi

                var c5 = item.Count(x => x.CevapPuan == 5);
                var c4 = item.Count(x => x.CevapPuan == 4);
                var c3 = item.Count(x => x.CevapPuan == 3);
                var c2 = item.Count(x => x.CevapPuan == 2);
                var c1 = item.Count(x => x.CevapPuan == 1);

                ViewBag.d5 = (float)c5 * 5 / 5 * 100;
                ViewBag.d4 = (float)c4 * 4 / 5 * 100;
                ViewBag.d3 = (float)c3 * 3 / 5 * 100;
                ViewBag.d2 = (float)c2 * 2 / 5 * 100;
                ViewBag.d1 = (float)c1 * 1 / 5 * 100;

                // PuanÄ± hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanÄ±nÄ± yÃ¼zdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik3.Add(item.Key);
                ViewBag.puan3.Add(p1);
                ViewBag.adet3.Add(kisiSayisi);

            }
            foreach (var item in bul.AsEnumerable().GroupBy(x => x.User?.Egitim?.EgitimAdi ?? "Tanımsız"))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerÃ§ek kiÅŸi adedi

                var c5 = item.Count(x => x.CevapPuan == 5);
                var c4 = item.Count(x => x.CevapPuan == 4);
                var c3 = item.Count(x => x.CevapPuan == 3);
                var c2 = item.Count(x => x.CevapPuan == 2);
                var c1 = item.Count(x => x.CevapPuan == 1);

                ViewBag.d5 = (float)c5 * 5 / 5 * 100;
                ViewBag.d4 = (float)c4 * 4 / 5 * 100;
                ViewBag.d3 = (float)c3 * 3 / 5 * 100;
                ViewBag.d2 = (float)c2 * 2 / 5 * 100;
                ViewBag.d1 = (float)c1 * 1 / 5 * 100;

                // PuanÄ± hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanÄ±nÄ± yÃ¼zdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik4.Add(item.Key);
                ViewBag.puan4.Add(p1);
                ViewBag.adet4.Add(kisiSayisi);


            }
            var bugun = DateTime.Today;

            // Ã–nce boÅŸ listeleri hazÄ±rla
            ViewBag.baslik5 = new List<string>();
            ViewBag.puan5 = new List<float>();

            foreach (var item in bul.AsEnumerable()
                .Where(x => x.User?.UserDogumTar != null)
                .GroupBy(x => x.User.UserDogumTar.Value.Year))
            {
                var dogumYili = item.Key;
                var yas = bugun.Year - dogumYili;
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerÃ§ek kiÅŸi adedi


                // EÄŸer doÄŸum gÃ¼nÃ¼ bu yÄ±l daha gelmediyse yaÅŸÄ± 1 azalt
                var ilkKayit = item.FirstOrDefault()?.User?.UserDogumTar;
                if (ilkKayit != null && bugun.DayOfYear < ilkKayit.Value.DayOfYear)
                {
                    yas--;
                }

                // Ortalama puanÄ± hesapla (kiÅŸiye gÃ¶re normalize)
                var ortalama = item
                    .GroupBy(x => x.UserId)
                    .Select(g => g.Average(y => y.CevapPuan))
                    .Average();

                var ortalamaYuzde = ortalama * 20; // 5 Ã¼zerinden 100'e Ã§evirme

                // ğŸ”¹ Burada artÄ±k Add et
                ViewBag.baslik5.Add(yas.ToString());       // X ekseni â†’ yaÅŸ
                ViewBag.puan5.Add((float)ortalamaYuzde);   // Y ekseni â†’ puan
                ViewBag.adet5.Add(kisiSayisi);   // Y ekseni â†’ puan
            }

            foreach (var item in bul.AsEnumerable().GroupBy(x => x.User?.Sehir?.SehiarAdi ?? "Tanımsız"))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerÃ§ek kiÅŸi adedi

                var c5 = item.Count(x => x.CevapPuan == 5);
                var c4 = item.Count(x => x.CevapPuan == 4);
                var c3 = item.Count(x => x.CevapPuan == 3);
                var c2 = item.Count(x => x.CevapPuan == 2);
                var c1 = item.Count(x => x.CevapPuan == 1);

                ViewBag.d5 = (float)c5 * 5 / 5 * 100;
                ViewBag.d4 = (float)c4 * 4 / 5 * 100;
                ViewBag.d3 = (float)c3 * 3 / 5 * 100;
                ViewBag.d2 = (float)c2 * 2 / 5 * 100;
                ViewBag.d1 = (float)c1 * 1 / 5 * 100;

                // PuanÄ± hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanÄ±nÄ± yÃ¼zdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik6.Add(item.Key);
                ViewBag.puan6.Add(p1);
                ViewBag.adet6.Add(kisiSayisi);

            }
            foreach (var item in bul.AsEnumerable().GroupBy(x => x.User?.Sube?.SubeAdi ?? "Tanımsız"))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerÃ§ek kiÅŸi adedi

                var c5 = item.Count(x => x.CevapPuan == 5);
                var c4 = item.Count(x => x.CevapPuan == 4);
                var c3 = item.Count(x => x.CevapPuan == 3);
                var c2 = item.Count(x => x.CevapPuan == 2);
                var c1 = item.Count(x => x.CevapPuan == 1);

                ViewBag.d5 = (float)c5 * 5 / 5 * 100;
                ViewBag.d4 = (float)c4 * 4 / 5 * 100;
                ViewBag.d3 = (float)c3 * 3 / 5 * 100;
                ViewBag.d2 = (float)c2 * 2 / 5 * 100;
                ViewBag.d1 = (float)c1 * 1 / 5 * 100;

                // PuanÄ± hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanÄ±nÄ± yÃ¼zdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik7.Add(item.Key);
                ViewBag.puan7.Add(p1);
                ViewBag.adet7.Add(kisiSayisi);

            }
            foreach (var item in bul.AsEnumerable().GroupBy(x => x.User?.Unvan?.UnvanAdi ?? "Tanımsız"))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerÃ§ek kiÅŸi adedi

                var c5 = item.Count(x => x.CevapPuan == 5);
                var c4 = item.Count(x => x.CevapPuan == 4);
                var c3 = item.Count(x => x.CevapPuan == 3);
                var c2 = item.Count(x => x.CevapPuan == 2);
                var c1 = item.Count(x => x.CevapPuan == 1);

                ViewBag.d5 = (float)c5 * 5 / 5 * 100;
                ViewBag.d4 = (float)c4 * 4 / 5 * 100;
                ViewBag.d3 = (float)c3 * 3 / 5 * 100;
                ViewBag.d2 = (float)c2 * 2 / 5 * 100;
                ViewBag.d1 = (float)c1 * 1 / 5 * 100;

                // PuanÄ± hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanÄ±nÄ± yÃ¼zdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik8.Add(item.Key);
                ViewBag.puan8.Add(p1);
                ViewBag.adet8.Add(kisiSayisi);

            }
            foreach (var item in bul.AsEnumerable().GroupBy(x => x.User?.Yaka?.YakaAdi ?? "Tanımsız"))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerÃ§ek kiÅŸi adedi

                var c5 = item.Count(x => x.CevapPuan == 5);
                var c4 = item.Count(x => x.CevapPuan == 4);
                var c3 = item.Count(x => x.CevapPuan == 3);
                var c2 = item.Count(x => x.CevapPuan == 2);
                var c1 = item.Count(x => x.CevapPuan == 1);

                ViewBag.d5 = (float)c5 * 5 / 5 * 100;
                ViewBag.d4 = (float)c4 * 4 / 5 * 100;
                ViewBag.d3 = (float)c3 * 3 / 5 * 100;
                ViewBag.d2 = (float)c2 * 2 / 5 * 100;
                ViewBag.d1 = (float)c1 * 1 / 5 * 100;

                // PuanÄ± hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanÄ±nÄ± yÃ¼zdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik9.Add(item.Key);
                ViewBag.puan9.Add(p1);
                ViewBag.adet9.Add(kisiSayisi);

            }
            foreach (var item in bul.AsEnumerable().GroupBy(x => new
            {
                Id = x.User?.UserYoneticisi ?? 0,
                Ad = x.User?.Yonetici?.YoneticiAdi ?? "Tanımsız"
            }))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerÃ§ek kiÅŸi adedi

                var c5 = item.Count(x => x.CevapPuan == 5);
                var c4 = item.Count(x => x.CevapPuan == 4);
                var c3 = item.Count(x => x.CevapPuan == 3);
                var c2 = item.Count(x => x.CevapPuan == 2);
                var c1 = item.Count(x => x.CevapPuan == 1);

                ViewBag.d5 = (float)c5 * 5 / 5 * 100;
                ViewBag.d4 = (float)c4 * 4 / 5 * 100;
                ViewBag.d3 = (float)c3 * 3 / 5 * 100;
                ViewBag.d2 = (float)c2 * 2 / 5 * 100;
                ViewBag.d1 = (float)c1 * 1 / 5 * 100;

                // PuanÄ± hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanÄ±nÄ± yÃ¼zdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik10.Add(item.Key.Ad);
                ViewBag.puan10.Add(p1);
                ViewBag.adet10.Add(kisiSayisi);

            }
            foreach (var item in bul.GroupBy(x => x.Soru.SoruAdi))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerÃ§ek kiÅŸi adedi

                var c5 = item.Count(x => x.CevapPuan == 5);
                var c4 = item.Count(x => x.CevapPuan == 4);
                var c3 = item.Count(x => x.CevapPuan == 3);
                var c2 = item.Count(x => x.CevapPuan == 2);
                var c1 = item.Count(x => x.CevapPuan == 1);

                ViewBag.d5 = (float)c5 * 5 / 5 * 100;
                ViewBag.d4 = (float)c4 * 4 / 5 * 100;
                ViewBag.d3 = (float)c3 * 3 / 5 * 100;
                ViewBag.d2 = (float)c2 * 2 / 5 * 100;
                ViewBag.d1 = (float)c1 * 1 / 5 * 100;

                // PuanÄ± hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanÄ±nÄ± yÃ¼zdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik11.Add(item.Key);
                ViewBag.puan11.Add(p1);
                ViewBag.adet11.Add(kisiSayisi);

            }
            foreach (var item in bul.GroupBy(x => x.SoruGrup.SoruGrupAdi))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerÃ§ek kiÅŸi adedi

                var c5 = item.Count(x => x.CevapPuan == 5);
                var c4 = item.Count(x => x.CevapPuan == 4);
                var c3 = item.Count(x => x.CevapPuan == 3);
                var c2 = item.Count(x => x.CevapPuan == 2);
                var c1 = item.Count(x => x.CevapPuan == 1);

                ViewBag.d5 = (float)c5 * 5 / 5 * 100;
                ViewBag.d4 = (float)c4 * 4 / 5 * 100;
                ViewBag.d3 = (float)c3 * 3 / 5 * 100;
                ViewBag.d2 = (float)c2 * 2 / 5 * 100;
                ViewBag.d1 = (float)c1 * 1 / 5 * 100;

                // PuanÄ± hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanÄ±nÄ± yÃ¼zdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik12.Add(item.Key);
                ViewBag.puan12.Add(p1);
                ViewBag.adet12.Add(kisiSayisi);
            }

            RaporYoneticiResimleriHazirla(bul);

            return View("AnketSoruGrupIndex", bul);
        }
        public ActionResult AnketCinsiyetIndex(int id, int? ank)
        {
            if (Session["id"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }
            if (Session["admin"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }

            if (!AnketCalismaAlanindaMi(ank))
            {
                return RedirectToAction("AnketIndex");
            }

            var bul = RaporHavuzSorgusu(ank)
                .Where(x => x.User.UserCinsiyet == id)
                .ToList();
            var adi = bul.FirstOrDefault();
            var ankadi = db.Anket.Where(x => x.AnketId == ank).FirstOrDefault();
            if (ankadi != null)
            {
                ViewBag.adi = ankadi.AnketAdi;
                ViewBag.id = ankadi.AnketId;
                ViewBag.sinav = SinavTurundeMi(ankadi);

            }
            if (ank != null)
            {
                ViewBag.ank = ank;
            }
            if (adi != null)
            {
                ViewBag.anketadi = adi.User?.Cinsiyet?.CinsiyetAdi ?? "Tanımsız";
            }

            // Genel
            ViewBag.baslik1 = new List<string>();
            ViewBag.puan1 = new List<float>();
            ViewBag.adet1 = new List<int>();

            // Departman
            ViewBag.baslik2 = new List<string>();
            ViewBag.puan2 = new List<float>();
            ViewBag.adet2 = new List<int>();

            // Cinsiyet
            ViewBag.baslik3 = new List<string>();
            ViewBag.puan3 = new List<float>();
            ViewBag.adet3 = new List<int>();

            // EÄŸitim
            ViewBag.baslik4 = new List<string>();
            ViewBag.puan4 = new List<float>();
            ViewBag.adet4 = new List<int>();

            // YaÅŸ
            ViewBag.baslik5 = new List<string>();  // int yerine string yapalÄ±m â†’ chart iÃ§in daha kolay
            ViewBag.puan5 = new List<float>();
            ViewBag.adet5 = new List<int>();

            // Åehir
            ViewBag.baslik6 = new List<string>();
            ViewBag.puan6 = new List<float>();
            ViewBag.adet6 = new List<int>();

            // Åube
            ViewBag.baslik7 = new List<string>();
            ViewBag.puan7 = new List<float>();
            ViewBag.adet7 = new List<int>();

            // Ãœnvan
            ViewBag.baslik8 = new List<string>();
            ViewBag.puan8 = new List<float>();
            ViewBag.adet8 = new List<int>();

            // Yaka
            ViewBag.baslik9 = new List<string>();
            ViewBag.puan9 = new List<float>();
            ViewBag.adet9 = new List<int>();

            // YÃ¶netici
            ViewBag.baslik10 = new List<string>();
            ViewBag.puan10 = new List<float>();
            ViewBag.adet10 = new List<int>();

            // Soru
            ViewBag.baslik11 = new List<string>();
            ViewBag.puan11 = new List<float>();
            ViewBag.adet11 = new List<int>();

            // Soru Grup
            ViewBag.baslik12 = new List<string>();
            ViewBag.puan12 = new List<float>();
            ViewBag.adet12 = new List<int>();


            foreach (var item in bul.GroupBy(x => x.Anket.AnketAdi))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerÃ§ek kiÅŸi adedi

                var c5 = item.Count(x => x.CevapPuan == 5);
                var c4 = item.Count(x => x.CevapPuan == 4);
                var c3 = item.Count(x => x.CevapPuan == 3);
                var c2 = item.Count(x => x.CevapPuan == 2);
                var c1 = item.Count(x => x.CevapPuan == 1);

                ViewBag.d5 = (float)c5 * 5 / 5 * 100;
                ViewBag.d4 = (float)c4 * 4 / 5 * 100;
                ViewBag.d3 = (float)c3 * 3 / 5 * 100;
                ViewBag.d2 = (float)c2 * 2 / 5 * 100;
                ViewBag.d1 = (float)c1 * 1 / 5 * 100;

                // PuanÄ± hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanÄ±nÄ± yÃ¼zdesini bulur
                var p1 = p2 / soru;

                ViewBag.baslik1.Add(item.Key);
                ViewBag.puan1.Add(p1);
                ViewBag.adet1.Add(kisiSayisi);

            }
            foreach (var item in bul.AsEnumerable().GroupBy(x => x.User?.Departman?.DepartmanAdi ?? "Tanımsız"))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerÃ§ek kiÅŸi adedi

                var c5 = item.Count(x => x.CevapPuan == 5);
                var c4 = item.Count(x => x.CevapPuan == 4);
                var c3 = item.Count(x => x.CevapPuan == 3);
                var c2 = item.Count(x => x.CevapPuan == 2);
                var c1 = item.Count(x => x.CevapPuan == 1);

                ViewBag.d5 = (float)c5 * 5 / 5 * 100;
                ViewBag.d4 = (float)c4 * 4 / 5 * 100;
                ViewBag.d3 = (float)c3 * 3 / 5 * 100;
                ViewBag.d2 = (float)c2 * 2 / 5 * 100;
                ViewBag.d1 = (float)c1 * 1 / 5 * 100;

                // PuanÄ± hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanÄ±nÄ± yÃ¼zdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik2.Add(item.Key);
                ViewBag.puan2.Add(p1);
                ViewBag.adet2.Add(kisiSayisi);

            }
            foreach (var item in bul.AsEnumerable().GroupBy(x => x.User?.Cinsiyet?.CinsiyetAdi ?? "Tanımsız"))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerÃ§ek kiÅŸi adedi

                var c5 = item.Count(x => x.CevapPuan == 5);
                var c4 = item.Count(x => x.CevapPuan == 4);
                var c3 = item.Count(x => x.CevapPuan == 3);
                var c2 = item.Count(x => x.CevapPuan == 2);
                var c1 = item.Count(x => x.CevapPuan == 1);

                ViewBag.d5 = (float)c5 * 5 / 5 * 100;
                ViewBag.d4 = (float)c4 * 4 / 5 * 100;
                ViewBag.d3 = (float)c3 * 3 / 5 * 100;
                ViewBag.d2 = (float)c2 * 2 / 5 * 100;
                ViewBag.d1 = (float)c1 * 1 / 5 * 100;

                // PuanÄ± hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanÄ±nÄ± yÃ¼zdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik3.Add(item.Key);
                ViewBag.puan3.Add(p1);
                ViewBag.adet3.Add(kisiSayisi);

            }
            foreach (var item in bul.AsEnumerable().GroupBy(x => x.User?.Egitim?.EgitimAdi ?? "Tanımsız"))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerÃ§ek kiÅŸi adedi

                var c5 = item.Count(x => x.CevapPuan == 5);
                var c4 = item.Count(x => x.CevapPuan == 4);
                var c3 = item.Count(x => x.CevapPuan == 3);
                var c2 = item.Count(x => x.CevapPuan == 2);
                var c1 = item.Count(x => x.CevapPuan == 1);

                ViewBag.d5 = (float)c5 * 5 / 5 * 100;
                ViewBag.d4 = (float)c4 * 4 / 5 * 100;
                ViewBag.d3 = (float)c3 * 3 / 5 * 100;
                ViewBag.d2 = (float)c2 * 2 / 5 * 100;
                ViewBag.d1 = (float)c1 * 1 / 5 * 100;

                // PuanÄ± hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanÄ±nÄ± yÃ¼zdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik4.Add(item.Key);
                ViewBag.puan4.Add(p1);
                ViewBag.adet4.Add(kisiSayisi);


            }
            var bugun = DateTime.Today;

            // Ã–nce boÅŸ listeleri hazÄ±rla
            ViewBag.baslik5 = new List<string>();
            ViewBag.puan5 = new List<float>();

            foreach (var item in bul.AsEnumerable()
                .Where(x => x.User?.UserDogumTar != null)
                .GroupBy(x => x.User.UserDogumTar.Value.Year))
            {
                var dogumYili = item.Key;
                var yas = bugun.Year - dogumYili;
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerÃ§ek kiÅŸi adedi


                // EÄŸer doÄŸum gÃ¼nÃ¼ bu yÄ±l daha gelmediyse yaÅŸÄ± 1 azalt
                var ilkKayit = item.FirstOrDefault()?.User?.UserDogumTar;
                if (ilkKayit != null && bugun.DayOfYear < ilkKayit.Value.DayOfYear)
                {
                    yas--;
                }

                // Ortalama puanÄ± hesapla (kiÅŸiye gÃ¶re normalize)
                var ortalama = item
                    .GroupBy(x => x.UserId)
                    .Select(g => g.Average(y => y.CevapPuan))
                    .Average();

                var ortalamaYuzde = ortalama * 20; // 5 Ã¼zerinden 100'e Ã§evirme

                // ğŸ”¹ Burada artÄ±k Add et
                ViewBag.baslik5.Add(yas.ToString());       // X ekseni â†’ yaÅŸ
                ViewBag.puan5.Add((float)ortalamaYuzde);   // Y ekseni â†’ puan
                ViewBag.adet5.Add(kisiSayisi);   // Y ekseni â†’ puan
            }

            foreach (var item in bul.AsEnumerable().GroupBy(x => x.User?.Sehir?.SehiarAdi ?? "Tanımsız"))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerÃ§ek kiÅŸi adedi

                var c5 = item.Count(x => x.CevapPuan == 5);
                var c4 = item.Count(x => x.CevapPuan == 4);
                var c3 = item.Count(x => x.CevapPuan == 3);
                var c2 = item.Count(x => x.CevapPuan == 2);
                var c1 = item.Count(x => x.CevapPuan == 1);

                ViewBag.d5 = (float)c5 * 5 / 5 * 100;
                ViewBag.d4 = (float)c4 * 4 / 5 * 100;
                ViewBag.d3 = (float)c3 * 3 / 5 * 100;
                ViewBag.d2 = (float)c2 * 2 / 5 * 100;
                ViewBag.d1 = (float)c1 * 1 / 5 * 100;

                // PuanÄ± hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanÄ±nÄ± yÃ¼zdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik6.Add(item.Key);
                ViewBag.puan6.Add(p1);
                ViewBag.adet6.Add(kisiSayisi);

            }
            foreach (var item in bul.AsEnumerable().GroupBy(x => x.User?.Sube?.SubeAdi ?? "Tanımsız"))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerÃ§ek kiÅŸi adedi

                var c5 = item.Count(x => x.CevapPuan == 5);
                var c4 = item.Count(x => x.CevapPuan == 4);
                var c3 = item.Count(x => x.CevapPuan == 3);
                var c2 = item.Count(x => x.CevapPuan == 2);
                var c1 = item.Count(x => x.CevapPuan == 1);

                ViewBag.d5 = (float)c5 * 5 / 5 * 100;
                ViewBag.d4 = (float)c4 * 4 / 5 * 100;
                ViewBag.d3 = (float)c3 * 3 / 5 * 100;
                ViewBag.d2 = (float)c2 * 2 / 5 * 100;
                ViewBag.d1 = (float)c1 * 1 / 5 * 100;

                // PuanÄ± hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanÄ±nÄ± yÃ¼zdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik7.Add(item.Key);
                ViewBag.puan7.Add(p1);
                ViewBag.adet7.Add(kisiSayisi);

            }
            foreach (var item in bul.AsEnumerable().GroupBy(x => x.User?.Unvan?.UnvanAdi ?? "Tanımsız"))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerÃ§ek kiÅŸi adedi

                var c5 = item.Count(x => x.CevapPuan == 5);
                var c4 = item.Count(x => x.CevapPuan == 4);
                var c3 = item.Count(x => x.CevapPuan == 3);
                var c2 = item.Count(x => x.CevapPuan == 2);
                var c1 = item.Count(x => x.CevapPuan == 1);

                ViewBag.d5 = (float)c5 * 5 / 5 * 100;
                ViewBag.d4 = (float)c4 * 4 / 5 * 100;
                ViewBag.d3 = (float)c3 * 3 / 5 * 100;
                ViewBag.d2 = (float)c2 * 2 / 5 * 100;
                ViewBag.d1 = (float)c1 * 1 / 5 * 100;

                // PuanÄ± hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanÄ±nÄ± yÃ¼zdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik8.Add(item.Key);
                ViewBag.puan8.Add(p1);
                ViewBag.adet8.Add(kisiSayisi);

            }
            foreach (var item in bul.AsEnumerable().GroupBy(x => x.User?.Yaka?.YakaAdi ?? "Tanımsız"))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerÃ§ek kiÅŸi adedi

                var c5 = item.Count(x => x.CevapPuan == 5);
                var c4 = item.Count(x => x.CevapPuan == 4);
                var c3 = item.Count(x => x.CevapPuan == 3);
                var c2 = item.Count(x => x.CevapPuan == 2);
                var c1 = item.Count(x => x.CevapPuan == 1);

                ViewBag.d5 = (float)c5 * 5 / 5 * 100;
                ViewBag.d4 = (float)c4 * 4 / 5 * 100;
                ViewBag.d3 = (float)c3 * 3 / 5 * 100;
                ViewBag.d2 = (float)c2 * 2 / 5 * 100;
                ViewBag.d1 = (float)c1 * 1 / 5 * 100;

                // PuanÄ± hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanÄ±nÄ± yÃ¼zdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik9.Add(item.Key);
                ViewBag.puan9.Add(p1);
                ViewBag.adet9.Add(kisiSayisi);

            }
            foreach (var item in bul.AsEnumerable().GroupBy(x => new
            {
                Id = x.User?.UserYoneticisi ?? 0,
                Ad = x.User?.Yonetici?.YoneticiAdi ?? "Tanımsız"
            }))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerÃ§ek kiÅŸi adedi

                var c5 = item.Count(x => x.CevapPuan == 5);
                var c4 = item.Count(x => x.CevapPuan == 4);
                var c3 = item.Count(x => x.CevapPuan == 3);
                var c2 = item.Count(x => x.CevapPuan == 2);
                var c1 = item.Count(x => x.CevapPuan == 1);

                ViewBag.d5 = (float)c5 * 5 / 5 * 100;
                ViewBag.d4 = (float)c4 * 4 / 5 * 100;
                ViewBag.d3 = (float)c3 * 3 / 5 * 100;
                ViewBag.d2 = (float)c2 * 2 / 5 * 100;
                ViewBag.d1 = (float)c1 * 1 / 5 * 100;

                // PuanÄ± hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanÄ±nÄ± yÃ¼zdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik10.Add(item.Key.Ad);
                ViewBag.puan10.Add(p1);
                ViewBag.adet10.Add(kisiSayisi);

            }
            foreach (var item in bul.GroupBy(x => x.Soru.SoruAdi))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerÃ§ek kiÅŸi adedi

                var c5 = item.Count(x => x.CevapPuan == 5);
                var c4 = item.Count(x => x.CevapPuan == 4);
                var c3 = item.Count(x => x.CevapPuan == 3);
                var c2 = item.Count(x => x.CevapPuan == 2);
                var c1 = item.Count(x => x.CevapPuan == 1);

                ViewBag.d5 = (float)c5 * 5 / 5 * 100;
                ViewBag.d4 = (float)c4 * 4 / 5 * 100;
                ViewBag.d3 = (float)c3 * 3 / 5 * 100;
                ViewBag.d2 = (float)c2 * 2 / 5 * 100;
                ViewBag.d1 = (float)c1 * 1 / 5 * 100;

                // PuanÄ± hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanÄ±nÄ± yÃ¼zdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik11.Add(item.Key);
                ViewBag.puan11.Add(p1);
                ViewBag.adet11.Add(kisiSayisi);

            }
            foreach (var item in bul.GroupBy(x => x.SoruGrup.SoruGrupAdi))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerÃ§ek kiÅŸi adedi

                var c5 = item.Count(x => x.CevapPuan == 5);
                var c4 = item.Count(x => x.CevapPuan == 4);
                var c3 = item.Count(x => x.CevapPuan == 3);
                var c2 = item.Count(x => x.CevapPuan == 2);
                var c1 = item.Count(x => x.CevapPuan == 1);

                ViewBag.d5 = (float)c5 * 5 / 5 * 100;
                ViewBag.d4 = (float)c4 * 4 / 5 * 100;
                ViewBag.d3 = (float)c3 * 3 / 5 * 100;
                ViewBag.d2 = (float)c2 * 2 / 5 * 100;
                ViewBag.d1 = (float)c1 * 1 / 5 * 100;

                // PuanÄ± hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanÄ±nÄ± yÃ¼zdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik12.Add(item.Key);
                ViewBag.puan12.Add(p1);
                ViewBag.adet12.Add(kisiSayisi);
            }

            RaporYoneticiResimleriHazirla(bul);

            return View("AnketSoruGrupIndex", bul);
        }
        public ActionResult AnketEgitimIndex(int id, int? ank)
        {
            if (Session["id"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }
            if (Session["admin"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }

            if (!AnketCalismaAlanindaMi(ank))
            {
                return RedirectToAction("AnketIndex");
            }

            var bul = RaporHavuzSorgusu(ank)
                .Where(x => x.User.UserEgitim == id)
                .ToList();
            var adi = bul.FirstOrDefault();
            var ankadi = db.Anket.Where(x => x.AnketId == ank).FirstOrDefault();
            if (ankadi != null)
            {
                ViewBag.adi = ankadi.AnketAdi;
                ViewBag.id = ankadi.AnketId;
                ViewBag.sinav = SinavTurundeMi(ankadi);

            }
            if (ank != null)
            {
                ViewBag.ank = ank;
            }
            if (adi != null)
            {
                ViewBag.anketadi = adi.User?.Egitim?.EgitimAdi ?? "Tanımsız";
            }

            // Genel
            ViewBag.baslik1 = new List<string>();
            ViewBag.puan1 = new List<float>();
            ViewBag.adet1 = new List<int>();

            // Departman
            ViewBag.baslik2 = new List<string>();
            ViewBag.puan2 = new List<float>();
            ViewBag.adet2 = new List<int>();

            // Cinsiyet
            ViewBag.baslik3 = new List<string>();
            ViewBag.puan3 = new List<float>();
            ViewBag.adet3 = new List<int>();

            // EÄŸitim
            ViewBag.baslik4 = new List<string>();
            ViewBag.puan4 = new List<float>();
            ViewBag.adet4 = new List<int>();

            // YaÅŸ
            ViewBag.baslik5 = new List<string>();  // int yerine string yapalÄ±m â†’ chart iÃ§in daha kolay
            ViewBag.puan5 = new List<float>();
            ViewBag.adet5 = new List<int>();

            // Åehir
            ViewBag.baslik6 = new List<string>();
            ViewBag.puan6 = new List<float>();
            ViewBag.adet6 = new List<int>();

            // Åube
            ViewBag.baslik7 = new List<string>();
            ViewBag.puan7 = new List<float>();
            ViewBag.adet7 = new List<int>();

            // Ãœnvan
            ViewBag.baslik8 = new List<string>();
            ViewBag.puan8 = new List<float>();
            ViewBag.adet8 = new List<int>();

            // Yaka
            ViewBag.baslik9 = new List<string>();
            ViewBag.puan9 = new List<float>();
            ViewBag.adet9 = new List<int>();

            // YÃ¶netici
            ViewBag.baslik10 = new List<string>();
            ViewBag.puan10 = new List<float>();
            ViewBag.adet10 = new List<int>();

            // Soru
            ViewBag.baslik11 = new List<string>();
            ViewBag.puan11 = new List<float>();
            ViewBag.adet11 = new List<int>();

            // Soru Grup
            ViewBag.baslik12 = new List<string>();
            ViewBag.puan12 = new List<float>();
            ViewBag.adet12 = new List<int>();


            foreach (var item in bul.GroupBy(x => x.Anket.AnketAdi))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerÃ§ek kiÅŸi adedi

                var c5 = item.Count(x => x.CevapPuan == 5);
                var c4 = item.Count(x => x.CevapPuan == 4);
                var c3 = item.Count(x => x.CevapPuan == 3);
                var c2 = item.Count(x => x.CevapPuan == 2);
                var c1 = item.Count(x => x.CevapPuan == 1);

                ViewBag.d5 = (float)c5 * 5 / 5 * 100;
                ViewBag.d4 = (float)c4 * 4 / 5 * 100;
                ViewBag.d3 = (float)c3 * 3 / 5 * 100;
                ViewBag.d2 = (float)c2 * 2 / 5 * 100;
                ViewBag.d1 = (float)c1 * 1 / 5 * 100;

                // PuanÄ± hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanÄ±nÄ± yÃ¼zdesini bulur
                var p1 = p2 / soru;

                ViewBag.baslik1.Add(item.Key);
                ViewBag.puan1.Add(p1);
                ViewBag.adet1.Add(kisiSayisi);

            }
            foreach (var item in bul.AsEnumerable().GroupBy(x => x.User?.Departman?.DepartmanAdi ?? "Tanımsız"))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerÃ§ek kiÅŸi adedi

                var c5 = item.Count(x => x.CevapPuan == 5);
                var c4 = item.Count(x => x.CevapPuan == 4);
                var c3 = item.Count(x => x.CevapPuan == 3);
                var c2 = item.Count(x => x.CevapPuan == 2);
                var c1 = item.Count(x => x.CevapPuan == 1);

                ViewBag.d5 = (float)c5 * 5 / 5 * 100;
                ViewBag.d4 = (float)c4 * 4 / 5 * 100;
                ViewBag.d3 = (float)c3 * 3 / 5 * 100;
                ViewBag.d2 = (float)c2 * 2 / 5 * 100;
                ViewBag.d1 = (float)c1 * 1 / 5 * 100;

                // PuanÄ± hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanÄ±nÄ± yÃ¼zdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik2.Add(item.Key);
                ViewBag.puan2.Add(p1);
                ViewBag.adet2.Add(kisiSayisi);

            }
            foreach (var item in bul.AsEnumerable().GroupBy(x => x.User?.Cinsiyet?.CinsiyetAdi ?? "Tanımsız"))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerÃ§ek kiÅŸi adedi

                var c5 = item.Count(x => x.CevapPuan == 5);
                var c4 = item.Count(x => x.CevapPuan == 4);
                var c3 = item.Count(x => x.CevapPuan == 3);
                var c2 = item.Count(x => x.CevapPuan == 2);
                var c1 = item.Count(x => x.CevapPuan == 1);

                ViewBag.d5 = (float)c5 * 5 / 5 * 100;
                ViewBag.d4 = (float)c4 * 4 / 5 * 100;
                ViewBag.d3 = (float)c3 * 3 / 5 * 100;
                ViewBag.d2 = (float)c2 * 2 / 5 * 100;
                ViewBag.d1 = (float)c1 * 1 / 5 * 100;

                // PuanÄ± hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanÄ±nÄ± yÃ¼zdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik3.Add(item.Key);
                ViewBag.puan3.Add(p1);
                ViewBag.adet3.Add(kisiSayisi);

            }
            foreach (var item in bul.AsEnumerable().GroupBy(x => x.User?.Egitim?.EgitimAdi ?? "Tanımsız"))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerÃ§ek kiÅŸi adedi

                var c5 = item.Count(x => x.CevapPuan == 5);
                var c4 = item.Count(x => x.CevapPuan == 4);
                var c3 = item.Count(x => x.CevapPuan == 3);
                var c2 = item.Count(x => x.CevapPuan == 2);
                var c1 = item.Count(x => x.CevapPuan == 1);

                ViewBag.d5 = (float)c5 * 5 / 5 * 100;
                ViewBag.d4 = (float)c4 * 4 / 5 * 100;
                ViewBag.d3 = (float)c3 * 3 / 5 * 100;
                ViewBag.d2 = (float)c2 * 2 / 5 * 100;
                ViewBag.d1 = (float)c1 * 1 / 5 * 100;

                // PuanÄ± hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanÄ±nÄ± yÃ¼zdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik4.Add(item.Key);
                ViewBag.puan4.Add(p1);
                ViewBag.adet4.Add(kisiSayisi);


            }
            var bugun = DateTime.Today;

            // Ã–nce boÅŸ listeleri hazÄ±rla
            ViewBag.baslik5 = new List<string>();
            ViewBag.puan5 = new List<float>();

            foreach (var item in bul.AsEnumerable()
                .Where(x => x.User?.UserDogumTar != null)
                .GroupBy(x => x.User.UserDogumTar.Value.Year))
            {
                var dogumYili = item.Key;
                var yas = bugun.Year - dogumYili;
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerÃ§ek kiÅŸi adedi


                // EÄŸer doÄŸum gÃ¼nÃ¼ bu yÄ±l daha gelmediyse yaÅŸÄ± 1 azalt
                var ilkKayit = item.FirstOrDefault()?.User?.UserDogumTar;
                if (ilkKayit != null && bugun.DayOfYear < ilkKayit.Value.DayOfYear)
                {
                    yas--;
                }

                // Ortalama puanÄ± hesapla (kiÅŸiye gÃ¶re normalize)
                var ortalama = item
                    .GroupBy(x => x.UserId)
                    .Select(g => g.Average(y => y.CevapPuan))
                    .Average();

                var ortalamaYuzde = ortalama * 20; // 5 Ã¼zerinden 100'e Ã§evirme

                // ğŸ”¹ Burada artÄ±k Add et
                ViewBag.baslik5.Add(yas.ToString());       // X ekseni â†’ yaÅŸ
                ViewBag.puan5.Add((float)ortalamaYuzde);   // Y ekseni â†’ puan
                ViewBag.adet5.Add(kisiSayisi);   // Y ekseni â†’ puan
            }

            foreach (var item in bul.AsEnumerable().GroupBy(x => x.User?.Sehir?.SehiarAdi ?? "Tanımsız"))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerÃ§ek kiÅŸi adedi

                var c5 = item.Count(x => x.CevapPuan == 5);
                var c4 = item.Count(x => x.CevapPuan == 4);
                var c3 = item.Count(x => x.CevapPuan == 3);
                var c2 = item.Count(x => x.CevapPuan == 2);
                var c1 = item.Count(x => x.CevapPuan == 1);

                ViewBag.d5 = (float)c5 * 5 / 5 * 100;
                ViewBag.d4 = (float)c4 * 4 / 5 * 100;
                ViewBag.d3 = (float)c3 * 3 / 5 * 100;
                ViewBag.d2 = (float)c2 * 2 / 5 * 100;
                ViewBag.d1 = (float)c1 * 1 / 5 * 100;

                // PuanÄ± hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanÄ±nÄ± yÃ¼zdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik6.Add(item.Key);
                ViewBag.puan6.Add(p1);
                ViewBag.adet6.Add(kisiSayisi);

            }
            foreach (var item in bul.AsEnumerable().GroupBy(x => x.User?.Sube?.SubeAdi ?? "Tanımsız"))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerÃ§ek kiÅŸi adedi

                var c5 = item.Count(x => x.CevapPuan == 5);
                var c4 = item.Count(x => x.CevapPuan == 4);
                var c3 = item.Count(x => x.CevapPuan == 3);
                var c2 = item.Count(x => x.CevapPuan == 2);
                var c1 = item.Count(x => x.CevapPuan == 1);

                ViewBag.d5 = (float)c5 * 5 / 5 * 100;
                ViewBag.d4 = (float)c4 * 4 / 5 * 100;
                ViewBag.d3 = (float)c3 * 3 / 5 * 100;
                ViewBag.d2 = (float)c2 * 2 / 5 * 100;
                ViewBag.d1 = (float)c1 * 1 / 5 * 100;

                // PuanÄ± hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanÄ±nÄ± yÃ¼zdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik7.Add(item.Key);
                ViewBag.puan7.Add(p1);
                ViewBag.adet7.Add(kisiSayisi);

            }
            foreach (var item in bul.AsEnumerable().GroupBy(x => x.User?.Unvan?.UnvanAdi ?? "Tanımsız"))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerÃ§ek kiÅŸi adedi

                var c5 = item.Count(x => x.CevapPuan == 5);
                var c4 = item.Count(x => x.CevapPuan == 4);
                var c3 = item.Count(x => x.CevapPuan == 3);
                var c2 = item.Count(x => x.CevapPuan == 2);
                var c1 = item.Count(x => x.CevapPuan == 1);

                ViewBag.d5 = (float)c5 * 5 / 5 * 100;
                ViewBag.d4 = (float)c4 * 4 / 5 * 100;
                ViewBag.d3 = (float)c3 * 3 / 5 * 100;
                ViewBag.d2 = (float)c2 * 2 / 5 * 100;
                ViewBag.d1 = (float)c1 * 1 / 5 * 100;

                // PuanÄ± hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanÄ±nÄ± yÃ¼zdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik8.Add(item.Key);
                ViewBag.puan8.Add(p1);
                ViewBag.adet8.Add(kisiSayisi);

            }
            foreach (var item in bul.AsEnumerable().GroupBy(x => x.User?.Yaka?.YakaAdi ?? "Tanımsız"))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerÃ§ek kiÅŸi adedi

                var c5 = item.Count(x => x.CevapPuan == 5);
                var c4 = item.Count(x => x.CevapPuan == 4);
                var c3 = item.Count(x => x.CevapPuan == 3);
                var c2 = item.Count(x => x.CevapPuan == 2);
                var c1 = item.Count(x => x.CevapPuan == 1);

                ViewBag.d5 = (float)c5 * 5 / 5 * 100;
                ViewBag.d4 = (float)c4 * 4 / 5 * 100;
                ViewBag.d3 = (float)c3 * 3 / 5 * 100;
                ViewBag.d2 = (float)c2 * 2 / 5 * 100;
                ViewBag.d1 = (float)c1 * 1 / 5 * 100;

                // PuanÄ± hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanÄ±nÄ± yÃ¼zdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik9.Add(item.Key);
                ViewBag.puan9.Add(p1);
                ViewBag.adet9.Add(kisiSayisi);

            }
            foreach (var item in bul.AsEnumerable().GroupBy(x => new
            {
                Id = x.User?.UserYoneticisi ?? 0,
                Ad = x.User?.Yonetici?.YoneticiAdi ?? "Tanımsız"
            }))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerÃ§ek kiÅŸi adedi

                var c5 = item.Count(x => x.CevapPuan == 5);
                var c4 = item.Count(x => x.CevapPuan == 4);
                var c3 = item.Count(x => x.CevapPuan == 3);
                var c2 = item.Count(x => x.CevapPuan == 2);
                var c1 = item.Count(x => x.CevapPuan == 1);

                ViewBag.d5 = (float)c5 * 5 / 5 * 100;
                ViewBag.d4 = (float)c4 * 4 / 5 * 100;
                ViewBag.d3 = (float)c3 * 3 / 5 * 100;
                ViewBag.d2 = (float)c2 * 2 / 5 * 100;
                ViewBag.d1 = (float)c1 * 1 / 5 * 100;

                // PuanÄ± hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanÄ±nÄ± yÃ¼zdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik10.Add(item.Key.Ad);
                ViewBag.puan10.Add(p1);
                ViewBag.adet10.Add(kisiSayisi);

            }
            foreach (var item in bul.GroupBy(x => x.Soru.SoruAdi))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerÃ§ek kiÅŸi adedi

                var c5 = item.Count(x => x.CevapPuan == 5);
                var c4 = item.Count(x => x.CevapPuan == 4);
                var c3 = item.Count(x => x.CevapPuan == 3);
                var c2 = item.Count(x => x.CevapPuan == 2);
                var c1 = item.Count(x => x.CevapPuan == 1);

                ViewBag.d5 = (float)c5 * 5 / 5 * 100;
                ViewBag.d4 = (float)c4 * 4 / 5 * 100;
                ViewBag.d3 = (float)c3 * 3 / 5 * 100;
                ViewBag.d2 = (float)c2 * 2 / 5 * 100;
                ViewBag.d1 = (float)c1 * 1 / 5 * 100;

                // PuanÄ± hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanÄ±nÄ± yÃ¼zdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik11.Add(item.Key);
                ViewBag.puan11.Add(p1);
                ViewBag.adet11.Add(kisiSayisi);

            }
            foreach (var item in bul.GroupBy(x => x.SoruGrup.SoruGrupAdi))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerÃ§ek kiÅŸi adedi

                var c5 = item.Count(x => x.CevapPuan == 5);
                var c4 = item.Count(x => x.CevapPuan == 4);
                var c3 = item.Count(x => x.CevapPuan == 3);
                var c2 = item.Count(x => x.CevapPuan == 2);
                var c1 = item.Count(x => x.CevapPuan == 1);

                ViewBag.d5 = (float)c5 * 5 / 5 * 100;
                ViewBag.d4 = (float)c4 * 4 / 5 * 100;
                ViewBag.d3 = (float)c3 * 3 / 5 * 100;
                ViewBag.d2 = (float)c2 * 2 / 5 * 100;
                ViewBag.d1 = (float)c1 * 1 / 5 * 100;

                // PuanÄ± hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanÄ±nÄ± yÃ¼zdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik12.Add(item.Key);
                ViewBag.puan12.Add(p1);
                ViewBag.adet12.Add(kisiSayisi);
            }

            RaporYoneticiResimleriHazirla(bul);

            return View("AnketSoruGrupIndex", bul);
        }
        public ActionResult AnketYasIndex(int id, int? ank)
        {
            if (Session["id"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }
            if (Session["admin"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }

            if (!AnketCalismaAlanindaMi(ank))
            {
                return RedirectToAction("AnketIndex");
            }

            var bul = RaporHavuzSorgusu(ank)
                .Where(x => x.User.UserDogumTar != null && x.User.UserDogumTar.Value.Year == id)
                .ToList();
            var adi = bul.FirstOrDefault();
            var ankadi = db.Anket.Where(x => x.AnketId == ank).FirstOrDefault();
            if (ankadi != null)
            {
                ViewBag.adi = ankadi.AnketAdi;
                ViewBag.id = ankadi.AnketId;
                ViewBag.sinav = SinavTurundeMi(ankadi);

            }
            if (ank != null)
            {
                ViewBag.ank = ank;
            }
            if (adi != null)
            {
                ViewBag.anketadi = adi.User.UserDogumTar.Value.Year;
            }

            // Genel
            ViewBag.baslik1 = new List<string>();
            ViewBag.puan1 = new List<float>();
            ViewBag.adet1 = new List<int>();

            // Departman
            ViewBag.baslik2 = new List<string>();
            ViewBag.puan2 = new List<float>();
            ViewBag.adet2 = new List<int>();

            // Cinsiyet
            ViewBag.baslik3 = new List<string>();
            ViewBag.puan3 = new List<float>();
            ViewBag.adet3 = new List<int>();

            // EÄŸitim
            ViewBag.baslik4 = new List<string>();
            ViewBag.puan4 = new List<float>();
            ViewBag.adet4 = new List<int>();

            // YaÅŸ
            ViewBag.baslik5 = new List<string>();  // int yerine string yapalÄ±m â†’ chart iÃ§in daha kolay
            ViewBag.puan5 = new List<float>();
            ViewBag.adet5 = new List<int>();

            // Åehir
            ViewBag.baslik6 = new List<string>();
            ViewBag.puan6 = new List<float>();
            ViewBag.adet6 = new List<int>();

            // Åube
            ViewBag.baslik7 = new List<string>();
            ViewBag.puan7 = new List<float>();
            ViewBag.adet7 = new List<int>();

            // Ãœnvan
            ViewBag.baslik8 = new List<string>();
            ViewBag.puan8 = new List<float>();
            ViewBag.adet8 = new List<int>();

            // Yaka
            ViewBag.baslik9 = new List<string>();
            ViewBag.puan9 = new List<float>();
            ViewBag.adet9 = new List<int>();

            // YÃ¶netici
            ViewBag.baslik10 = new List<string>();
            ViewBag.puan10 = new List<float>();
            ViewBag.adet10 = new List<int>();

            // Soru
            ViewBag.baslik11 = new List<string>();
            ViewBag.puan11 = new List<float>();
            ViewBag.adet11 = new List<int>();

            // Soru Grup
            ViewBag.baslik12 = new List<string>();
            ViewBag.puan12 = new List<float>();
            ViewBag.adet12 = new List<int>();


            foreach (var item in bul.GroupBy(x => x.Anket.AnketAdi))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerÃ§ek kiÅŸi adedi

                var c5 = item.Count(x => x.CevapPuan == 5);
                var c4 = item.Count(x => x.CevapPuan == 4);
                var c3 = item.Count(x => x.CevapPuan == 3);
                var c2 = item.Count(x => x.CevapPuan == 2);
                var c1 = item.Count(x => x.CevapPuan == 1);

                ViewBag.d5 = (float)c5 * 5 / 5 * 100;
                ViewBag.d4 = (float)c4 * 4 / 5 * 100;
                ViewBag.d3 = (float)c3 * 3 / 5 * 100;
                ViewBag.d2 = (float)c2 * 2 / 5 * 100;
                ViewBag.d1 = (float)c1 * 1 / 5 * 100;

                // PuanÄ± hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanÄ±nÄ± yÃ¼zdesini bulur
                var p1 = p2 / soru;

                ViewBag.baslik1.Add(item.Key);
                ViewBag.puan1.Add(p1);
                ViewBag.adet1.Add(kisiSayisi);

            }
            foreach (var item in bul.AsEnumerable().GroupBy(x => x.User?.Departman?.DepartmanAdi ?? "Tanımsız"))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerÃ§ek kiÅŸi adedi

                var c5 = item.Count(x => x.CevapPuan == 5);
                var c4 = item.Count(x => x.CevapPuan == 4);
                var c3 = item.Count(x => x.CevapPuan == 3);
                var c2 = item.Count(x => x.CevapPuan == 2);
                var c1 = item.Count(x => x.CevapPuan == 1);

                ViewBag.d5 = (float)c5 * 5 / 5 * 100;
                ViewBag.d4 = (float)c4 * 4 / 5 * 100;
                ViewBag.d3 = (float)c3 * 3 / 5 * 100;
                ViewBag.d2 = (float)c2 * 2 / 5 * 100;
                ViewBag.d1 = (float)c1 * 1 / 5 * 100;

                // PuanÄ± hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanÄ±nÄ± yÃ¼zdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik2.Add(item.Key);
                ViewBag.puan2.Add(p1);
                ViewBag.adet2.Add(kisiSayisi);

            }
            foreach (var item in bul.AsEnumerable().GroupBy(x => x.User?.Cinsiyet?.CinsiyetAdi ?? "Tanımsız"))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerÃ§ek kiÅŸi adedi

                var c5 = item.Count(x => x.CevapPuan == 5);
                var c4 = item.Count(x => x.CevapPuan == 4);
                var c3 = item.Count(x => x.CevapPuan == 3);
                var c2 = item.Count(x => x.CevapPuan == 2);
                var c1 = item.Count(x => x.CevapPuan == 1);

                ViewBag.d5 = (float)c5 * 5 / 5 * 100;
                ViewBag.d4 = (float)c4 * 4 / 5 * 100;
                ViewBag.d3 = (float)c3 * 3 / 5 * 100;
                ViewBag.d2 = (float)c2 * 2 / 5 * 100;
                ViewBag.d1 = (float)c1 * 1 / 5 * 100;

                // PuanÄ± hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanÄ±nÄ± yÃ¼zdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik3.Add(item.Key);
                ViewBag.puan3.Add(p1);
                ViewBag.adet3.Add(kisiSayisi);

            }
            foreach (var item in bul.AsEnumerable().GroupBy(x => x.User?.Egitim?.EgitimAdi ?? "Tanımsız"))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerÃ§ek kiÅŸi adedi

                var c5 = item.Count(x => x.CevapPuan == 5);
                var c4 = item.Count(x => x.CevapPuan == 4);
                var c3 = item.Count(x => x.CevapPuan == 3);
                var c2 = item.Count(x => x.CevapPuan == 2);
                var c1 = item.Count(x => x.CevapPuan == 1);

                ViewBag.d5 = (float)c5 * 5 / 5 * 100;
                ViewBag.d4 = (float)c4 * 4 / 5 * 100;
                ViewBag.d3 = (float)c3 * 3 / 5 * 100;
                ViewBag.d2 = (float)c2 * 2 / 5 * 100;
                ViewBag.d1 = (float)c1 * 1 / 5 * 100;

                // PuanÄ± hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanÄ±nÄ± yÃ¼zdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik4.Add(item.Key);
                ViewBag.puan4.Add(p1);
                ViewBag.adet4.Add(kisiSayisi);


            }
            var bugun = DateTime.Today;

            // Ã–nce boÅŸ listeleri hazÄ±rla
            ViewBag.baslik5 = new List<string>();
            ViewBag.puan5 = new List<float>();

            foreach (var item in bul.AsEnumerable()
                .Where(x => x.User?.UserDogumTar != null)
                .GroupBy(x => x.User.UserDogumTar.Value.Year))
            {
                var dogumYili = item.Key;
                var yas = bugun.Year - dogumYili;
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerÃ§ek kiÅŸi adedi


                // EÄŸer doÄŸum gÃ¼nÃ¼ bu yÄ±l daha gelmediyse yaÅŸÄ± 1 azalt
                var ilkKayit = item.FirstOrDefault()?.User?.UserDogumTar;
                if (ilkKayit != null && bugun.DayOfYear < ilkKayit.Value.DayOfYear)
                {
                    yas--;
                }

                // Ortalama puanÄ± hesapla (kiÅŸiye gÃ¶re normalize)
                var ortalama = item
                    .GroupBy(x => x.UserId)
                    .Select(g => g.Average(y => y.CevapPuan))
                    .Average();

                var ortalamaYuzde = ortalama * 20; // 5 Ã¼zerinden 100'e Ã§evirme

                // ğŸ”¹ Burada artÄ±k Add et
                ViewBag.baslik5.Add(yas.ToString());       // X ekseni â†’ yaÅŸ
                ViewBag.puan5.Add((float)ortalamaYuzde);   // Y ekseni â†’ puan
                ViewBag.adet5.Add(kisiSayisi);   // Y ekseni â†’ puan
            }

            foreach (var item in bul.AsEnumerable().GroupBy(x => x.User?.Sehir?.SehiarAdi ?? "Tanımsız"))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerÃ§ek kiÅŸi adedi

                var c5 = item.Count(x => x.CevapPuan == 5);
                var c4 = item.Count(x => x.CevapPuan == 4);
                var c3 = item.Count(x => x.CevapPuan == 3);
                var c2 = item.Count(x => x.CevapPuan == 2);
                var c1 = item.Count(x => x.CevapPuan == 1);

                ViewBag.d5 = (float)c5 * 5 / 5 * 100;
                ViewBag.d4 = (float)c4 * 4 / 5 * 100;
                ViewBag.d3 = (float)c3 * 3 / 5 * 100;
                ViewBag.d2 = (float)c2 * 2 / 5 * 100;
                ViewBag.d1 = (float)c1 * 1 / 5 * 100;

                // PuanÄ± hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanÄ±nÄ± yÃ¼zdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik6.Add(item.Key);
                ViewBag.puan6.Add(p1);
                ViewBag.adet6.Add(kisiSayisi);

            }
            foreach (var item in bul.AsEnumerable().GroupBy(x => x.User?.Sube?.SubeAdi ?? "Tanımsız"))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerÃ§ek kiÅŸi adedi

                var c5 = item.Count(x => x.CevapPuan == 5);
                var c4 = item.Count(x => x.CevapPuan == 4);
                var c3 = item.Count(x => x.CevapPuan == 3);
                var c2 = item.Count(x => x.CevapPuan == 2);
                var c1 = item.Count(x => x.CevapPuan == 1);

                ViewBag.d5 = (float)c5 * 5 / 5 * 100;
                ViewBag.d4 = (float)c4 * 4 / 5 * 100;
                ViewBag.d3 = (float)c3 * 3 / 5 * 100;
                ViewBag.d2 = (float)c2 * 2 / 5 * 100;
                ViewBag.d1 = (float)c1 * 1 / 5 * 100;

                // PuanÄ± hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanÄ±nÄ± yÃ¼zdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik7.Add(item.Key);
                ViewBag.puan7.Add(p1);
                ViewBag.adet7.Add(kisiSayisi);

            }
            foreach (var item in bul.AsEnumerable().GroupBy(x => x.User?.Unvan?.UnvanAdi ?? "Tanımsız"))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerÃ§ek kiÅŸi adedi

                var c5 = item.Count(x => x.CevapPuan == 5);
                var c4 = item.Count(x => x.CevapPuan == 4);
                var c3 = item.Count(x => x.CevapPuan == 3);
                var c2 = item.Count(x => x.CevapPuan == 2);
                var c1 = item.Count(x => x.CevapPuan == 1);

                ViewBag.d5 = (float)c5 * 5 / 5 * 100;
                ViewBag.d4 = (float)c4 * 4 / 5 * 100;
                ViewBag.d3 = (float)c3 * 3 / 5 * 100;
                ViewBag.d2 = (float)c2 * 2 / 5 * 100;
                ViewBag.d1 = (float)c1 * 1 / 5 * 100;

                // PuanÄ± hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanÄ±nÄ± yÃ¼zdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik8.Add(item.Key);
                ViewBag.puan8.Add(p1);
                ViewBag.adet8.Add(kisiSayisi);

            }
            foreach (var item in bul.AsEnumerable().GroupBy(x => x.User?.Yaka?.YakaAdi ?? "Tanımsız"))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerÃ§ek kiÅŸi adedi

                var c5 = item.Count(x => x.CevapPuan == 5);
                var c4 = item.Count(x => x.CevapPuan == 4);
                var c3 = item.Count(x => x.CevapPuan == 3);
                var c2 = item.Count(x => x.CevapPuan == 2);
                var c1 = item.Count(x => x.CevapPuan == 1);

                ViewBag.d5 = (float)c5 * 5 / 5 * 100;
                ViewBag.d4 = (float)c4 * 4 / 5 * 100;
                ViewBag.d3 = (float)c3 * 3 / 5 * 100;
                ViewBag.d2 = (float)c2 * 2 / 5 * 100;
                ViewBag.d1 = (float)c1 * 1 / 5 * 100;

                // PuanÄ± hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanÄ±nÄ± yÃ¼zdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik9.Add(item.Key);
                ViewBag.puan9.Add(p1);
                ViewBag.adet9.Add(kisiSayisi);

            }
            foreach (var item in bul.AsEnumerable().GroupBy(x => new
            {
                Id = x.User?.UserYoneticisi ?? 0,
                Ad = x.User?.Yonetici?.YoneticiAdi ?? "Tanımsız"
            }))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerÃ§ek kiÅŸi adedi

                var c5 = item.Count(x => x.CevapPuan == 5);
                var c4 = item.Count(x => x.CevapPuan == 4);
                var c3 = item.Count(x => x.CevapPuan == 3);
                var c2 = item.Count(x => x.CevapPuan == 2);
                var c1 = item.Count(x => x.CevapPuan == 1);

                ViewBag.d5 = (float)c5 * 5 / 5 * 100;
                ViewBag.d4 = (float)c4 * 4 / 5 * 100;
                ViewBag.d3 = (float)c3 * 3 / 5 * 100;
                ViewBag.d2 = (float)c2 * 2 / 5 * 100;
                ViewBag.d1 = (float)c1 * 1 / 5 * 100;

                // PuanÄ± hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanÄ±nÄ± yÃ¼zdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik10.Add(item.Key.Ad);
                ViewBag.puan10.Add(p1);
                ViewBag.adet10.Add(kisiSayisi);

            }
            foreach (var item in bul.GroupBy(x => x.Soru.SoruAdi))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerÃ§ek kiÅŸi adedi

                var c5 = item.Count(x => x.CevapPuan == 5);
                var c4 = item.Count(x => x.CevapPuan == 4);
                var c3 = item.Count(x => x.CevapPuan == 3);
                var c2 = item.Count(x => x.CevapPuan == 2);
                var c1 = item.Count(x => x.CevapPuan == 1);

                ViewBag.d5 = (float)c5 * 5 / 5 * 100;
                ViewBag.d4 = (float)c4 * 4 / 5 * 100;
                ViewBag.d3 = (float)c3 * 3 / 5 * 100;
                ViewBag.d2 = (float)c2 * 2 / 5 * 100;
                ViewBag.d1 = (float)c1 * 1 / 5 * 100;

                // PuanÄ± hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanÄ±nÄ± yÃ¼zdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik11.Add(item.Key);
                ViewBag.puan11.Add(p1);
                ViewBag.adet11.Add(kisiSayisi);

            }
            foreach (var item in bul.GroupBy(x => x.SoruGrup.SoruGrupAdi))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerÃ§ek kiÅŸi adedi

                var c5 = item.Count(x => x.CevapPuan == 5);
                var c4 = item.Count(x => x.CevapPuan == 4);
                var c3 = item.Count(x => x.CevapPuan == 3);
                var c2 = item.Count(x => x.CevapPuan == 2);
                var c1 = item.Count(x => x.CevapPuan == 1);

                ViewBag.d5 = (float)c5 * 5 / 5 * 100;
                ViewBag.d4 = (float)c4 * 4 / 5 * 100;
                ViewBag.d3 = (float)c3 * 3 / 5 * 100;
                ViewBag.d2 = (float)c2 * 2 / 5 * 100;
                ViewBag.d1 = (float)c1 * 1 / 5 * 100;

                // PuanÄ± hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanÄ±nÄ± yÃ¼zdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik12.Add(item.Key);
                ViewBag.puan12.Add(p1);
                ViewBag.adet12.Add(kisiSayisi);
            }

            RaporYoneticiResimleriHazirla(bul);

            return View("AnketSoruGrupIndex", bul);
        }
        public ActionResult AnketSehirIndex(int id, int? ank)
        {
            if (Session["id"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }
            if (Session["admin"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }

            if (!AnketCalismaAlanindaMi(ank))
            {
                return RedirectToAction("AnketIndex");
            }

            var bul = RaporHavuzSorgusu(ank)
                .Where(x => x.User.UserSehir == id)
                .ToList();
            var adi = bul.FirstOrDefault();
            var ankadi = db.Anket.Where(x => x.AnketId == ank).FirstOrDefault();
            if (ankadi != null)
            {
                ViewBag.adi = ankadi.AnketAdi;
                ViewBag.id = ankadi.AnketId;
                ViewBag.sinav = SinavTurundeMi(ankadi);

            }
            if (ank != null)
            {
                ViewBag.ank = ank;
            }
            if (adi != null)
            {
                ViewBag.anketadi = adi.User?.Sehir?.SehiarAdi ?? "Tanımsız";
            }

            // Genel
            ViewBag.baslik1 = new List<string>();
            ViewBag.puan1 = new List<float>();
            ViewBag.adet1 = new List<int>();

            // Departman
            ViewBag.baslik2 = new List<string>();
            ViewBag.puan2 = new List<float>();
            ViewBag.adet2 = new List<int>();

            // Cinsiyet
            ViewBag.baslik3 = new List<string>();
            ViewBag.puan3 = new List<float>();
            ViewBag.adet3 = new List<int>();

            // EÄŸitim
            ViewBag.baslik4 = new List<string>();
            ViewBag.puan4 = new List<float>();
            ViewBag.adet4 = new List<int>();

            // YaÅŸ
            ViewBag.baslik5 = new List<string>();  // int yerine string yapalÄ±m â†’ chart iÃ§in daha kolay
            ViewBag.puan5 = new List<float>();
            ViewBag.adet5 = new List<int>();

            // Åehir
            ViewBag.baslik6 = new List<string>();
            ViewBag.puan6 = new List<float>();
            ViewBag.adet6 = new List<int>();

            // Åube
            ViewBag.baslik7 = new List<string>();
            ViewBag.puan7 = new List<float>();
            ViewBag.adet7 = new List<int>();

            // Ãœnvan
            ViewBag.baslik8 = new List<string>();
            ViewBag.puan8 = new List<float>();
            ViewBag.adet8 = new List<int>();

            // Yaka
            ViewBag.baslik9 = new List<string>();
            ViewBag.puan9 = new List<float>();
            ViewBag.adet9 = new List<int>();

            // YÃ¶netici
            ViewBag.baslik10 = new List<string>();
            ViewBag.puan10 = new List<float>();
            ViewBag.adet10 = new List<int>();

            // Soru
            ViewBag.baslik11 = new List<string>();
            ViewBag.puan11 = new List<float>();
            ViewBag.adet11 = new List<int>();

            // Soru Grup
            ViewBag.baslik12 = new List<string>();
            ViewBag.puan12 = new List<float>();
            ViewBag.adet12 = new List<int>();


            foreach (var item in bul.GroupBy(x => x.Anket.AnketAdi))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerÃ§ek kiÅŸi adedi

                var c5 = item.Count(x => x.CevapPuan == 5);
                var c4 = item.Count(x => x.CevapPuan == 4);
                var c3 = item.Count(x => x.CevapPuan == 3);
                var c2 = item.Count(x => x.CevapPuan == 2);
                var c1 = item.Count(x => x.CevapPuan == 1);

                ViewBag.d5 = (float)c5 * 5 / 5 * 100;
                ViewBag.d4 = (float)c4 * 4 / 5 * 100;
                ViewBag.d3 = (float)c3 * 3 / 5 * 100;
                ViewBag.d2 = (float)c2 * 2 / 5 * 100;
                ViewBag.d1 = (float)c1 * 1 / 5 * 100;

                // PuanÄ± hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanÄ±nÄ± yÃ¼zdesini bulur
                var p1 = p2 / soru;

                ViewBag.baslik1.Add(item.Key);
                ViewBag.puan1.Add(p1);
                ViewBag.adet1.Add(kisiSayisi);

            }
            foreach (var item in bul.AsEnumerable().GroupBy(x => x.User?.Departman?.DepartmanAdi ?? "Tanımsız"))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerÃ§ek kiÅŸi adedi

                var c5 = item.Count(x => x.CevapPuan == 5);
                var c4 = item.Count(x => x.CevapPuan == 4);
                var c3 = item.Count(x => x.CevapPuan == 3);
                var c2 = item.Count(x => x.CevapPuan == 2);
                var c1 = item.Count(x => x.CevapPuan == 1);

                ViewBag.d5 = (float)c5 * 5 / 5 * 100;
                ViewBag.d4 = (float)c4 * 4 / 5 * 100;
                ViewBag.d3 = (float)c3 * 3 / 5 * 100;
                ViewBag.d2 = (float)c2 * 2 / 5 * 100;
                ViewBag.d1 = (float)c1 * 1 / 5 * 100;

                // PuanÄ± hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanÄ±nÄ± yÃ¼zdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik2.Add(item.Key);
                ViewBag.puan2.Add(p1);
                ViewBag.adet2.Add(kisiSayisi);

            }
            foreach (var item in bul.AsEnumerable().GroupBy(x => x.User?.Cinsiyet?.CinsiyetAdi ?? "Tanımsız"))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerÃ§ek kiÅŸi adedi

                var c5 = item.Count(x => x.CevapPuan == 5);
                var c4 = item.Count(x => x.CevapPuan == 4);
                var c3 = item.Count(x => x.CevapPuan == 3);
                var c2 = item.Count(x => x.CevapPuan == 2);
                var c1 = item.Count(x => x.CevapPuan == 1);

                ViewBag.d5 = (float)c5 * 5 / 5 * 100;
                ViewBag.d4 = (float)c4 * 4 / 5 * 100;
                ViewBag.d3 = (float)c3 * 3 / 5 * 100;
                ViewBag.d2 = (float)c2 * 2 / 5 * 100;
                ViewBag.d1 = (float)c1 * 1 / 5 * 100;

                // PuanÄ± hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanÄ±nÄ± yÃ¼zdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik3.Add(item.Key);
                ViewBag.puan3.Add(p1);
                ViewBag.adet3.Add(kisiSayisi);

            }
            foreach (var item in bul.AsEnumerable().GroupBy(x => x.User?.Egitim?.EgitimAdi ?? "Tanımsız"))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerÃ§ek kiÅŸi adedi

                var c5 = item.Count(x => x.CevapPuan == 5);
                var c4 = item.Count(x => x.CevapPuan == 4);
                var c3 = item.Count(x => x.CevapPuan == 3);
                var c2 = item.Count(x => x.CevapPuan == 2);
                var c1 = item.Count(x => x.CevapPuan == 1);

                ViewBag.d5 = (float)c5 * 5 / 5 * 100;
                ViewBag.d4 = (float)c4 * 4 / 5 * 100;
                ViewBag.d3 = (float)c3 * 3 / 5 * 100;
                ViewBag.d2 = (float)c2 * 2 / 5 * 100;
                ViewBag.d1 = (float)c1 * 1 / 5 * 100;

                // PuanÄ± hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanÄ±nÄ± yÃ¼zdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik4.Add(item.Key);
                ViewBag.puan4.Add(p1);
                ViewBag.adet4.Add(kisiSayisi);


            }
            var bugun = DateTime.Today;

            // Ã–nce boÅŸ listeleri hazÄ±rla
            ViewBag.baslik5 = new List<string>();
            ViewBag.puan5 = new List<float>();

            foreach (var item in bul.AsEnumerable()
                .Where(x => x.User?.UserDogumTar != null)
                .GroupBy(x => x.User.UserDogumTar.Value.Year))
            {
                var dogumYili = item.Key;
                var yas = bugun.Year - dogumYili;
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerÃ§ek kiÅŸi adedi


                // EÄŸer doÄŸum gÃ¼nÃ¼ bu yÄ±l daha gelmediyse yaÅŸÄ± 1 azalt
                var ilkKayit = item.FirstOrDefault()?.User?.UserDogumTar;
                if (ilkKayit != null && bugun.DayOfYear < ilkKayit.Value.DayOfYear)
                {
                    yas--;
                }

                // Ortalama puanÄ± hesapla (kiÅŸiye gÃ¶re normalize)
                var ortalama = item
                    .GroupBy(x => x.UserId)
                    .Select(g => g.Average(y => y.CevapPuan))
                    .Average();

                var ortalamaYuzde = ortalama * 20; // 5 Ã¼zerinden 100'e Ã§evirme

                // ğŸ”¹ Burada artÄ±k Add et
                ViewBag.baslik5.Add(yas.ToString());       // X ekseni â†’ yaÅŸ
                ViewBag.puan5.Add((float)ortalamaYuzde);   // Y ekseni â†’ puan
                ViewBag.adet5.Add(kisiSayisi);   // Y ekseni â†’ puan
            }

            foreach (var item in bul.AsEnumerable().GroupBy(x => x.User?.Sehir?.SehiarAdi ?? "Tanımsız"))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerÃ§ek kiÅŸi adedi

                var c5 = item.Count(x => x.CevapPuan == 5);
                var c4 = item.Count(x => x.CevapPuan == 4);
                var c3 = item.Count(x => x.CevapPuan == 3);
                var c2 = item.Count(x => x.CevapPuan == 2);
                var c1 = item.Count(x => x.CevapPuan == 1);

                ViewBag.d5 = (float)c5 * 5 / 5 * 100;
                ViewBag.d4 = (float)c4 * 4 / 5 * 100;
                ViewBag.d3 = (float)c3 * 3 / 5 * 100;
                ViewBag.d2 = (float)c2 * 2 / 5 * 100;
                ViewBag.d1 = (float)c1 * 1 / 5 * 100;

                // PuanÄ± hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanÄ±nÄ± yÃ¼zdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik6.Add(item.Key);
                ViewBag.puan6.Add(p1);
                ViewBag.adet6.Add(kisiSayisi);

            }
            foreach (var item in bul.AsEnumerable().GroupBy(x => x.User?.Sube?.SubeAdi ?? "Tanımsız"))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerÃ§ek kiÅŸi adedi

                var c5 = item.Count(x => x.CevapPuan == 5);
                var c4 = item.Count(x => x.CevapPuan == 4);
                var c3 = item.Count(x => x.CevapPuan == 3);
                var c2 = item.Count(x => x.CevapPuan == 2);
                var c1 = item.Count(x => x.CevapPuan == 1);

                ViewBag.d5 = (float)c5 * 5 / 5 * 100;
                ViewBag.d4 = (float)c4 * 4 / 5 * 100;
                ViewBag.d3 = (float)c3 * 3 / 5 * 100;
                ViewBag.d2 = (float)c2 * 2 / 5 * 100;
                ViewBag.d1 = (float)c1 * 1 / 5 * 100;

                // PuanÄ± hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanÄ±nÄ± yÃ¼zdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik7.Add(item.Key);
                ViewBag.puan7.Add(p1);
                ViewBag.adet7.Add(kisiSayisi);

            }
            foreach (var item in bul.AsEnumerable().GroupBy(x => x.User?.Unvan?.UnvanAdi ?? "Tanımsız"))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerÃ§ek kiÅŸi adedi

                var c5 = item.Count(x => x.CevapPuan == 5);
                var c4 = item.Count(x => x.CevapPuan == 4);
                var c3 = item.Count(x => x.CevapPuan == 3);
                var c2 = item.Count(x => x.CevapPuan == 2);
                var c1 = item.Count(x => x.CevapPuan == 1);

                ViewBag.d5 = (float)c5 * 5 / 5 * 100;
                ViewBag.d4 = (float)c4 * 4 / 5 * 100;
                ViewBag.d3 = (float)c3 * 3 / 5 * 100;
                ViewBag.d2 = (float)c2 * 2 / 5 * 100;
                ViewBag.d1 = (float)c1 * 1 / 5 * 100;

                // PuanÄ± hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanÄ±nÄ± yÃ¼zdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik8.Add(item.Key);
                ViewBag.puan8.Add(p1);
                ViewBag.adet8.Add(kisiSayisi);

            }
            foreach (var item in bul.AsEnumerable().GroupBy(x => x.User?.Yaka?.YakaAdi ?? "Tanımsız"))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerÃ§ek kiÅŸi adedi

                var c5 = item.Count(x => x.CevapPuan == 5);
                var c4 = item.Count(x => x.CevapPuan == 4);
                var c3 = item.Count(x => x.CevapPuan == 3);
                var c2 = item.Count(x => x.CevapPuan == 2);
                var c1 = item.Count(x => x.CevapPuan == 1);

                ViewBag.d5 = (float)c5 * 5 / 5 * 100;
                ViewBag.d4 = (float)c4 * 4 / 5 * 100;
                ViewBag.d3 = (float)c3 * 3 / 5 * 100;
                ViewBag.d2 = (float)c2 * 2 / 5 * 100;
                ViewBag.d1 = (float)c1 * 1 / 5 * 100;

                // PuanÄ± hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanÄ±nÄ± yÃ¼zdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik9.Add(item.Key);
                ViewBag.puan9.Add(p1);
                ViewBag.adet9.Add(kisiSayisi);

            }
            foreach (var item in bul.AsEnumerable().GroupBy(x => new
            {
                Id = x.User?.UserYoneticisi ?? 0,
                Ad = x.User?.Yonetici?.YoneticiAdi ?? "Tanımsız"
            }))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerÃ§ek kiÅŸi adedi

                var c5 = item.Count(x => x.CevapPuan == 5);
                var c4 = item.Count(x => x.CevapPuan == 4);
                var c3 = item.Count(x => x.CevapPuan == 3);
                var c2 = item.Count(x => x.CevapPuan == 2);
                var c1 = item.Count(x => x.CevapPuan == 1);

                ViewBag.d5 = (float)c5 * 5 / 5 * 100;
                ViewBag.d4 = (float)c4 * 4 / 5 * 100;
                ViewBag.d3 = (float)c3 * 3 / 5 * 100;
                ViewBag.d2 = (float)c2 * 2 / 5 * 100;
                ViewBag.d1 = (float)c1 * 1 / 5 * 100;

                // PuanÄ± hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanÄ±nÄ± yÃ¼zdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik10.Add(item.Key.Ad);
                ViewBag.puan10.Add(p1);
                ViewBag.adet10.Add(kisiSayisi);

            }
            foreach (var item in bul.GroupBy(x => x.Soru.SoruAdi))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerÃ§ek kiÅŸi adedi

                var c5 = item.Count(x => x.CevapPuan == 5);
                var c4 = item.Count(x => x.CevapPuan == 4);
                var c3 = item.Count(x => x.CevapPuan == 3);
                var c2 = item.Count(x => x.CevapPuan == 2);
                var c1 = item.Count(x => x.CevapPuan == 1);

                ViewBag.d5 = (float)c5 * 5 / 5 * 100;
                ViewBag.d4 = (float)c4 * 4 / 5 * 100;
                ViewBag.d3 = (float)c3 * 3 / 5 * 100;
                ViewBag.d2 = (float)c2 * 2 / 5 * 100;
                ViewBag.d1 = (float)c1 * 1 / 5 * 100;

                // PuanÄ± hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanÄ±nÄ± yÃ¼zdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik11.Add(item.Key);
                ViewBag.puan11.Add(p1);
                ViewBag.adet11.Add(kisiSayisi);

            }
            foreach (var item in bul.GroupBy(x => x.SoruGrup.SoruGrupAdi))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerÃ§ek kiÅŸi adedi

                var c5 = item.Count(x => x.CevapPuan == 5);
                var c4 = item.Count(x => x.CevapPuan == 4);
                var c3 = item.Count(x => x.CevapPuan == 3);
                var c2 = item.Count(x => x.CevapPuan == 2);
                var c1 = item.Count(x => x.CevapPuan == 1);

                ViewBag.d5 = (float)c5 * 5 / 5 * 100;
                ViewBag.d4 = (float)c4 * 4 / 5 * 100;
                ViewBag.d3 = (float)c3 * 3 / 5 * 100;
                ViewBag.d2 = (float)c2 * 2 / 5 * 100;
                ViewBag.d1 = (float)c1 * 1 / 5 * 100;

                // PuanÄ± hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanÄ±nÄ± yÃ¼zdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik12.Add(item.Key);
                ViewBag.puan12.Add(p1);
                ViewBag.adet12.Add(kisiSayisi);
            }

            RaporYoneticiResimleriHazirla(bul);

            return View("AnketSoruGrupIndex", bul);
        }
        public ActionResult AnketSubeIndex(int id, int? ank)
        {
            if (Session["id"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }
            if (Session["admin"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }

            if (!AnketCalismaAlanindaMi(ank))
            {
                return RedirectToAction("AnketIndex");
            }

            var bul = RaporHavuzSorgusu(ank)
                .Where(x => x.User.UserSube == id)
                .ToList();
            var adi = bul.FirstOrDefault();
            var ankadi = db.Anket.Where(x => x.AnketId == ank).FirstOrDefault();
            if (ankadi != null)
            {
                ViewBag.adi = ankadi.AnketAdi;
                ViewBag.id = ankadi.AnketId;
                ViewBag.sinav = SinavTurundeMi(ankadi);

            }
            if (ank != null)
            {
                ViewBag.ank = ank;
            }
            if (adi != null)
            {
                ViewBag.anketadi = adi.User?.Sube?.SubeAdi ?? "Tanımsız";
            }

            // Genel
            ViewBag.baslik1 = new List<string>();
            ViewBag.puan1 = new List<float>();
            ViewBag.adet1 = new List<int>();

            // Departman
            ViewBag.baslik2 = new List<string>();
            ViewBag.puan2 = new List<float>();
            ViewBag.adet2 = new List<int>();

            // Cinsiyet
            ViewBag.baslik3 = new List<string>();
            ViewBag.puan3 = new List<float>();
            ViewBag.adet3 = new List<int>();

            // EÄŸitim
            ViewBag.baslik4 = new List<string>();
            ViewBag.puan4 = new List<float>();
            ViewBag.adet4 = new List<int>();

            // YaÅŸ
            ViewBag.baslik5 = new List<string>();  // int yerine string yapalÄ±m â†’ chart iÃ§in daha kolay
            ViewBag.puan5 = new List<float>();
            ViewBag.adet5 = new List<int>();

            // Åehir
            ViewBag.baslik6 = new List<string>();
            ViewBag.puan6 = new List<float>();
            ViewBag.adet6 = new List<int>();

            // Åube
            ViewBag.baslik7 = new List<string>();
            ViewBag.puan7 = new List<float>();
            ViewBag.adet7 = new List<int>();

            // Ãœnvan
            ViewBag.baslik8 = new List<string>();
            ViewBag.puan8 = new List<float>();
            ViewBag.adet8 = new List<int>();

            // Yaka
            ViewBag.baslik9 = new List<string>();
            ViewBag.puan9 = new List<float>();
            ViewBag.adet9 = new List<int>();

            // YÃ¶netici
            ViewBag.baslik10 = new List<string>();
            ViewBag.puan10 = new List<float>();
            ViewBag.adet10 = new List<int>();

            // Soru
            ViewBag.baslik11 = new List<string>();
            ViewBag.puan11 = new List<float>();
            ViewBag.adet11 = new List<int>();

            // Soru Grup
            ViewBag.baslik12 = new List<string>();
            ViewBag.puan12 = new List<float>();
            ViewBag.adet12 = new List<int>();


            foreach (var item in bul.GroupBy(x => x.Anket.AnketAdi))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerÃ§ek kiÅŸi adedi

                var c5 = item.Count(x => x.CevapPuan == 5);
                var c4 = item.Count(x => x.CevapPuan == 4);
                var c3 = item.Count(x => x.CevapPuan == 3);
                var c2 = item.Count(x => x.CevapPuan == 2);
                var c1 = item.Count(x => x.CevapPuan == 1);

                ViewBag.d5 = (float)c5 * 5 / 5 * 100;
                ViewBag.d4 = (float)c4 * 4 / 5 * 100;
                ViewBag.d3 = (float)c3 * 3 / 5 * 100;
                ViewBag.d2 = (float)c2 * 2 / 5 * 100;
                ViewBag.d1 = (float)c1 * 1 / 5 * 100;

                // PuanÄ± hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanÄ±nÄ± yÃ¼zdesini bulur
                var p1 = p2 / soru;

                ViewBag.baslik1.Add(item.Key);
                ViewBag.puan1.Add(p1);
                ViewBag.adet1.Add(kisiSayisi);

            }
            foreach (var item in bul.AsEnumerable().GroupBy(x => x.User?.Departman?.DepartmanAdi ?? "Tanımsız"))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerÃ§ek kiÅŸi adedi

                var c5 = item.Count(x => x.CevapPuan == 5);
                var c4 = item.Count(x => x.CevapPuan == 4);
                var c3 = item.Count(x => x.CevapPuan == 3);
                var c2 = item.Count(x => x.CevapPuan == 2);
                var c1 = item.Count(x => x.CevapPuan == 1);

                ViewBag.d5 = (float)c5 * 5 / 5 * 100;
                ViewBag.d4 = (float)c4 * 4 / 5 * 100;
                ViewBag.d3 = (float)c3 * 3 / 5 * 100;
                ViewBag.d2 = (float)c2 * 2 / 5 * 100;
                ViewBag.d1 = (float)c1 * 1 / 5 * 100;

                // PuanÄ± hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanÄ±nÄ± yÃ¼zdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik2.Add(item.Key);
                ViewBag.puan2.Add(p1);
                ViewBag.adet2.Add(kisiSayisi);

            }
            foreach (var item in bul.AsEnumerable().GroupBy(x => x.User?.Cinsiyet?.CinsiyetAdi ?? "Tanımsız"))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerÃ§ek kiÅŸi adedi

                var c5 = item.Count(x => x.CevapPuan == 5);
                var c4 = item.Count(x => x.CevapPuan == 4);
                var c3 = item.Count(x => x.CevapPuan == 3);
                var c2 = item.Count(x => x.CevapPuan == 2);
                var c1 = item.Count(x => x.CevapPuan == 1);

                ViewBag.d5 = (float)c5 * 5 / 5 * 100;
                ViewBag.d4 = (float)c4 * 4 / 5 * 100;
                ViewBag.d3 = (float)c3 * 3 / 5 * 100;
                ViewBag.d2 = (float)c2 * 2 / 5 * 100;
                ViewBag.d1 = (float)c1 * 1 / 5 * 100;

                // PuanÄ± hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanÄ±nÄ± yÃ¼zdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik3.Add(item.Key);
                ViewBag.puan3.Add(p1);
                ViewBag.adet3.Add(kisiSayisi);

            }
            foreach (var item in bul.AsEnumerable().GroupBy(x => x.User?.Egitim?.EgitimAdi ?? "Tanımsız"))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerÃ§ek kiÅŸi adedi

                var c5 = item.Count(x => x.CevapPuan == 5);
                var c4 = item.Count(x => x.CevapPuan == 4);
                var c3 = item.Count(x => x.CevapPuan == 3);
                var c2 = item.Count(x => x.CevapPuan == 2);
                var c1 = item.Count(x => x.CevapPuan == 1);

                ViewBag.d5 = (float)c5 * 5 / 5 * 100;
                ViewBag.d4 = (float)c4 * 4 / 5 * 100;
                ViewBag.d3 = (float)c3 * 3 / 5 * 100;
                ViewBag.d2 = (float)c2 * 2 / 5 * 100;
                ViewBag.d1 = (float)c1 * 1 / 5 * 100;

                // PuanÄ± hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanÄ±nÄ± yÃ¼zdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik4.Add(item.Key);
                ViewBag.puan4.Add(p1);
                ViewBag.adet4.Add(kisiSayisi);


            }
            var bugun = DateTime.Today;

            // Ã–nce boÅŸ listeleri hazÄ±rla
            ViewBag.baslik5 = new List<string>();
            ViewBag.puan5 = new List<float>();

            foreach (var item in bul.AsEnumerable()
                .Where(x => x.User?.UserDogumTar != null)
                .GroupBy(x => x.User.UserDogumTar.Value.Year))
            {
                var dogumYili = item.Key;
                var yas = bugun.Year - dogumYili;
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerÃ§ek kiÅŸi adedi


                // EÄŸer doÄŸum gÃ¼nÃ¼ bu yÄ±l daha gelmediyse yaÅŸÄ± 1 azalt
                var ilkKayit = item.FirstOrDefault()?.User?.UserDogumTar;
                if (ilkKayit != null && bugun.DayOfYear < ilkKayit.Value.DayOfYear)
                {
                    yas--;
                }

                // Ortalama puanÄ± hesapla (kiÅŸiye gÃ¶re normalize)
                var ortalama = item
                    .GroupBy(x => x.UserId)
                    .Select(g => g.Average(y => y.CevapPuan))
                    .Average();

                var ortalamaYuzde = ortalama * 20; // 5 Ã¼zerinden 100'e Ã§evirme

                // ğŸ”¹ Burada artÄ±k Add et
                ViewBag.baslik5.Add(yas.ToString());       // X ekseni â†’ yaÅŸ
                ViewBag.puan5.Add((float)ortalamaYuzde);   // Y ekseni â†’ puan
                ViewBag.adet5.Add(kisiSayisi);   // Y ekseni â†’ puan
            }

            foreach (var item in bul.AsEnumerable().GroupBy(x => x.User?.Sehir?.SehiarAdi ?? "Tanımsız"))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerÃ§ek kiÅŸi adedi

                var c5 = item.Count(x => x.CevapPuan == 5);
                var c4 = item.Count(x => x.CevapPuan == 4);
                var c3 = item.Count(x => x.CevapPuan == 3);
                var c2 = item.Count(x => x.CevapPuan == 2);
                var c1 = item.Count(x => x.CevapPuan == 1);

                ViewBag.d5 = (float)c5 * 5 / 5 * 100;
                ViewBag.d4 = (float)c4 * 4 / 5 * 100;
                ViewBag.d3 = (float)c3 * 3 / 5 * 100;
                ViewBag.d2 = (float)c2 * 2 / 5 * 100;
                ViewBag.d1 = (float)c1 * 1 / 5 * 100;

                // PuanÄ± hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanÄ±nÄ± yÃ¼zdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik6.Add(item.Key);
                ViewBag.puan6.Add(p1);
                ViewBag.adet6.Add(kisiSayisi);

            }
            foreach (var item in bul.AsEnumerable().GroupBy(x => x.User?.Sube?.SubeAdi ?? "Tanımsız"))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerÃ§ek kiÅŸi adedi

                var c5 = item.Count(x => x.CevapPuan == 5);
                var c4 = item.Count(x => x.CevapPuan == 4);
                var c3 = item.Count(x => x.CevapPuan == 3);
                var c2 = item.Count(x => x.CevapPuan == 2);
                var c1 = item.Count(x => x.CevapPuan == 1);

                ViewBag.d5 = (float)c5 * 5 / 5 * 100;
                ViewBag.d4 = (float)c4 * 4 / 5 * 100;
                ViewBag.d3 = (float)c3 * 3 / 5 * 100;
                ViewBag.d2 = (float)c2 * 2 / 5 * 100;
                ViewBag.d1 = (float)c1 * 1 / 5 * 100;

                // PuanÄ± hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanÄ±nÄ± yÃ¼zdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik7.Add(item.Key);
                ViewBag.puan7.Add(p1);
                ViewBag.adet7.Add(kisiSayisi);

            }
            foreach (var item in bul.AsEnumerable().GroupBy(x => x.User?.Unvan?.UnvanAdi ?? "Tanımsız"))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerÃ§ek kiÅŸi adedi

                var c5 = item.Count(x => x.CevapPuan == 5);
                var c4 = item.Count(x => x.CevapPuan == 4);
                var c3 = item.Count(x => x.CevapPuan == 3);
                var c2 = item.Count(x => x.CevapPuan == 2);
                var c1 = item.Count(x => x.CevapPuan == 1);

                ViewBag.d5 = (float)c5 * 5 / 5 * 100;
                ViewBag.d4 = (float)c4 * 4 / 5 * 100;
                ViewBag.d3 = (float)c3 * 3 / 5 * 100;
                ViewBag.d2 = (float)c2 * 2 / 5 * 100;
                ViewBag.d1 = (float)c1 * 1 / 5 * 100;

                // PuanÄ± hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanÄ±nÄ± yÃ¼zdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik8.Add(item.Key);
                ViewBag.puan8.Add(p1);
                ViewBag.adet8.Add(kisiSayisi);

            }
            foreach (var item in bul.AsEnumerable().GroupBy(x => x.User?.Yaka?.YakaAdi ?? "Tanımsız"))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerÃ§ek kiÅŸi adedi

                var c5 = item.Count(x => x.CevapPuan == 5);
                var c4 = item.Count(x => x.CevapPuan == 4);
                var c3 = item.Count(x => x.CevapPuan == 3);
                var c2 = item.Count(x => x.CevapPuan == 2);
                var c1 = item.Count(x => x.CevapPuan == 1);

                ViewBag.d5 = (float)c5 * 5 / 5 * 100;
                ViewBag.d4 = (float)c4 * 4 / 5 * 100;
                ViewBag.d3 = (float)c3 * 3 / 5 * 100;
                ViewBag.d2 = (float)c2 * 2 / 5 * 100;
                ViewBag.d1 = (float)c1 * 1 / 5 * 100;

                // PuanÄ± hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanÄ±nÄ± yÃ¼zdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik9.Add(item.Key);
                ViewBag.puan9.Add(p1);
                ViewBag.adet9.Add(kisiSayisi);

            }
            foreach (var item in bul.AsEnumerable().GroupBy(x => new
            {
                Id = x.User?.UserYoneticisi ?? 0,
                Ad = x.User?.Yonetici?.YoneticiAdi ?? "Tanımsız"
            }))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerÃ§ek kiÅŸi adedi

                var c5 = item.Count(x => x.CevapPuan == 5);
                var c4 = item.Count(x => x.CevapPuan == 4);
                var c3 = item.Count(x => x.CevapPuan == 3);
                var c2 = item.Count(x => x.CevapPuan == 2);
                var c1 = item.Count(x => x.CevapPuan == 1);

                ViewBag.d5 = (float)c5 * 5 / 5 * 100;
                ViewBag.d4 = (float)c4 * 4 / 5 * 100;
                ViewBag.d3 = (float)c3 * 3 / 5 * 100;
                ViewBag.d2 = (float)c2 * 2 / 5 * 100;
                ViewBag.d1 = (float)c1 * 1 / 5 * 100;

                // PuanÄ± hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanÄ±nÄ± yÃ¼zdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik10.Add(item.Key.Ad);
                ViewBag.puan10.Add(p1);
                ViewBag.adet10.Add(kisiSayisi);

            }
            foreach (var item in bul.GroupBy(x => x.Soru.SoruAdi))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerÃ§ek kiÅŸi adedi

                var c5 = item.Count(x => x.CevapPuan == 5);
                var c4 = item.Count(x => x.CevapPuan == 4);
                var c3 = item.Count(x => x.CevapPuan == 3);
                var c2 = item.Count(x => x.CevapPuan == 2);
                var c1 = item.Count(x => x.CevapPuan == 1);

                ViewBag.d5 = (float)c5 * 5 / 5 * 100;
                ViewBag.d4 = (float)c4 * 4 / 5 * 100;
                ViewBag.d3 = (float)c3 * 3 / 5 * 100;
                ViewBag.d2 = (float)c2 * 2 / 5 * 100;
                ViewBag.d1 = (float)c1 * 1 / 5 * 100;

                // PuanÄ± hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanÄ±nÄ± yÃ¼zdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik11.Add(item.Key);
                ViewBag.puan11.Add(p1);
                ViewBag.adet11.Add(kisiSayisi);

            }
            foreach (var item in bul.GroupBy(x => x.SoruGrup.SoruGrupAdi))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerÃ§ek kiÅŸi adedi

                var c5 = item.Count(x => x.CevapPuan == 5);
                var c4 = item.Count(x => x.CevapPuan == 4);
                var c3 = item.Count(x => x.CevapPuan == 3);
                var c2 = item.Count(x => x.CevapPuan == 2);
                var c1 = item.Count(x => x.CevapPuan == 1);

                ViewBag.d5 = (float)c5 * 5 / 5 * 100;
                ViewBag.d4 = (float)c4 * 4 / 5 * 100;
                ViewBag.d3 = (float)c3 * 3 / 5 * 100;
                ViewBag.d2 = (float)c2 * 2 / 5 * 100;
                ViewBag.d1 = (float)c1 * 1 / 5 * 100;

                // PuanÄ± hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanÄ±nÄ± yÃ¼zdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik12.Add(item.Key);
                ViewBag.puan12.Add(p1);
                ViewBag.adet12.Add(kisiSayisi);
            }

            RaporYoneticiResimleriHazirla(bul);

            return View("AnketSoruGrupIndex", bul);
        }
        public ActionResult AnketUnvanIndex(int id, int? ank)
        {
            if (Session["id"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }
            if (Session["admin"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }

            if (!AnketCalismaAlanindaMi(ank))
            {
                return RedirectToAction("AnketIndex");
            }

            var bul = RaporHavuzSorgusu(ank)
                .Where(x => x.User.UserUnvan == id)
                .ToList();
            var adi = bul.FirstOrDefault();
            var ankadi = db.Anket.Where(x => x.AnketId == ank).FirstOrDefault();
            if (ankadi != null)
            {
                ViewBag.adi = ankadi.AnketAdi;
                ViewBag.id = ankadi.AnketId;
                ViewBag.sinav = SinavTurundeMi(ankadi);

            }
            if (ank != null)
            {
                ViewBag.ank = ank;
            }
            if (adi != null)
            {
                ViewBag.anketadi = adi.User?.Unvan?.UnvanAdi ?? "Tanımsız";
            }

            // Genel
            ViewBag.baslik1 = new List<string>();
            ViewBag.puan1 = new List<float>();
            ViewBag.adet1 = new List<int>();

            // Departman
            ViewBag.baslik2 = new List<string>();
            ViewBag.puan2 = new List<float>();
            ViewBag.adet2 = new List<int>();

            // Cinsiyet
            ViewBag.baslik3 = new List<string>();
            ViewBag.puan3 = new List<float>();
            ViewBag.adet3 = new List<int>();

            // EÄŸitim
            ViewBag.baslik4 = new List<string>();
            ViewBag.puan4 = new List<float>();
            ViewBag.adet4 = new List<int>();

            // YaÅŸ
            ViewBag.baslik5 = new List<string>();  // int yerine string yapalÄ±m â†’ chart iÃ§in daha kolay
            ViewBag.puan5 = new List<float>();
            ViewBag.adet5 = new List<int>();

            // Åehir
            ViewBag.baslik6 = new List<string>();
            ViewBag.puan6 = new List<float>();
            ViewBag.adet6 = new List<int>();

            // Åube
            ViewBag.baslik7 = new List<string>();
            ViewBag.puan7 = new List<float>();
            ViewBag.adet7 = new List<int>();

            // Ãœnvan
            ViewBag.baslik8 = new List<string>();
            ViewBag.puan8 = new List<float>();
            ViewBag.adet8 = new List<int>();

            // Yaka
            ViewBag.baslik9 = new List<string>();
            ViewBag.puan9 = new List<float>();
            ViewBag.adet9 = new List<int>();

            // YÃ¶netici
            ViewBag.baslik10 = new List<string>();
            ViewBag.puan10 = new List<float>();
            ViewBag.adet10 = new List<int>();

            // Soru
            ViewBag.baslik11 = new List<string>();
            ViewBag.puan11 = new List<float>();
            ViewBag.adet11 = new List<int>();

            // Soru Grup
            ViewBag.baslik12 = new List<string>();
            ViewBag.puan12 = new List<float>();
            ViewBag.adet12 = new List<int>();


            foreach (var item in bul.GroupBy(x => x.Anket.AnketAdi))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerÃ§ek kiÅŸi adedi

                var c5 = item.Count(x => x.CevapPuan == 5);
                var c4 = item.Count(x => x.CevapPuan == 4);
                var c3 = item.Count(x => x.CevapPuan == 3);
                var c2 = item.Count(x => x.CevapPuan == 2);
                var c1 = item.Count(x => x.CevapPuan == 1);

                ViewBag.d5 = (float)c5 * 5 / 5 * 100;
                ViewBag.d4 = (float)c4 * 4 / 5 * 100;
                ViewBag.d3 = (float)c3 * 3 / 5 * 100;
                ViewBag.d2 = (float)c2 * 2 / 5 * 100;
                ViewBag.d1 = (float)c1 * 1 / 5 * 100;

                // PuanÄ± hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanÄ±nÄ± yÃ¼zdesini bulur
                var p1 = p2 / soru;

                ViewBag.baslik1.Add(item.Key);
                ViewBag.puan1.Add(p1);
                ViewBag.adet1.Add(kisiSayisi);

            }
            foreach (var item in bul.AsEnumerable().GroupBy(x => x.User?.Departman?.DepartmanAdi ?? "Tanımsız"))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerÃ§ek kiÅŸi adedi

                var c5 = item.Count(x => x.CevapPuan == 5);
                var c4 = item.Count(x => x.CevapPuan == 4);
                var c3 = item.Count(x => x.CevapPuan == 3);
                var c2 = item.Count(x => x.CevapPuan == 2);
                var c1 = item.Count(x => x.CevapPuan == 1);

                ViewBag.d5 = (float)c5 * 5 / 5 * 100;
                ViewBag.d4 = (float)c4 * 4 / 5 * 100;
                ViewBag.d3 = (float)c3 * 3 / 5 * 100;
                ViewBag.d2 = (float)c2 * 2 / 5 * 100;
                ViewBag.d1 = (float)c1 * 1 / 5 * 100;

                // PuanÄ± hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanÄ±nÄ± yÃ¼zdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik2.Add(item.Key);
                ViewBag.puan2.Add(p1);
                ViewBag.adet2.Add(kisiSayisi);

            }
            foreach (var item in bul.AsEnumerable().GroupBy(x => x.User?.Cinsiyet?.CinsiyetAdi ?? "Tanımsız"))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerÃ§ek kiÅŸi adedi

                var c5 = item.Count(x => x.CevapPuan == 5);
                var c4 = item.Count(x => x.CevapPuan == 4);
                var c3 = item.Count(x => x.CevapPuan == 3);
                var c2 = item.Count(x => x.CevapPuan == 2);
                var c1 = item.Count(x => x.CevapPuan == 1);

                ViewBag.d5 = (float)c5 * 5 / 5 * 100;
                ViewBag.d4 = (float)c4 * 4 / 5 * 100;
                ViewBag.d3 = (float)c3 * 3 / 5 * 100;
                ViewBag.d2 = (float)c2 * 2 / 5 * 100;
                ViewBag.d1 = (float)c1 * 1 / 5 * 100;

                // PuanÄ± hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanÄ±nÄ± yÃ¼zdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik3.Add(item.Key);
                ViewBag.puan3.Add(p1);
                ViewBag.adet3.Add(kisiSayisi);

            }
            foreach (var item in bul.AsEnumerable().GroupBy(x => x.User?.Egitim?.EgitimAdi ?? "Tanımsız"))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerÃ§ek kiÅŸi adedi

                var c5 = item.Count(x => x.CevapPuan == 5);
                var c4 = item.Count(x => x.CevapPuan == 4);
                var c3 = item.Count(x => x.CevapPuan == 3);
                var c2 = item.Count(x => x.CevapPuan == 2);
                var c1 = item.Count(x => x.CevapPuan == 1);

                ViewBag.d5 = (float)c5 * 5 / 5 * 100;
                ViewBag.d4 = (float)c4 * 4 / 5 * 100;
                ViewBag.d3 = (float)c3 * 3 / 5 * 100;
                ViewBag.d2 = (float)c2 * 2 / 5 * 100;
                ViewBag.d1 = (float)c1 * 1 / 5 * 100;

                // PuanÄ± hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanÄ±nÄ± yÃ¼zdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik4.Add(item.Key);
                ViewBag.puan4.Add(p1);
                ViewBag.adet4.Add(kisiSayisi);


            }
            var bugun = DateTime.Today;

            // Ã–nce boÅŸ listeleri hazÄ±rla
            ViewBag.baslik5 = new List<string>();
            ViewBag.puan5 = new List<float>();

            foreach (var item in bul.AsEnumerable()
                .Where(x => x.User?.UserDogumTar != null)
                .GroupBy(x => x.User.UserDogumTar.Value.Year))
            {
                var dogumYili = item.Key;
                var yas = bugun.Year - dogumYili;
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerÃ§ek kiÅŸi adedi


                // EÄŸer doÄŸum gÃ¼nÃ¼ bu yÄ±l daha gelmediyse yaÅŸÄ± 1 azalt
                var ilkKayit = item.FirstOrDefault()?.User?.UserDogumTar;
                if (ilkKayit != null && bugun.DayOfYear < ilkKayit.Value.DayOfYear)
                {
                    yas--;
                }

                // Ortalama puanÄ± hesapla (kiÅŸiye gÃ¶re normalize)
                var ortalama = item
                    .GroupBy(x => x.UserId)
                    .Select(g => g.Average(y => y.CevapPuan))
                    .Average();

                var ortalamaYuzde = ortalama * 20; // 5 Ã¼zerinden 100'e Ã§evirme

                // ğŸ”¹ Burada artÄ±k Add et
                ViewBag.baslik5.Add(yas.ToString());       // X ekseni â†’ yaÅŸ
                ViewBag.puan5.Add((float)ortalamaYuzde);   // Y ekseni â†’ puan
                ViewBag.adet5.Add(kisiSayisi);   // Y ekseni â†’ puan
            }

            foreach (var item in bul.AsEnumerable().GroupBy(x => x.User?.Sehir?.SehiarAdi ?? "Tanımsız"))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerÃ§ek kiÅŸi adedi

                var c5 = item.Count(x => x.CevapPuan == 5);
                var c4 = item.Count(x => x.CevapPuan == 4);
                var c3 = item.Count(x => x.CevapPuan == 3);
                var c2 = item.Count(x => x.CevapPuan == 2);
                var c1 = item.Count(x => x.CevapPuan == 1);

                ViewBag.d5 = (float)c5 * 5 / 5 * 100;
                ViewBag.d4 = (float)c4 * 4 / 5 * 100;
                ViewBag.d3 = (float)c3 * 3 / 5 * 100;
                ViewBag.d2 = (float)c2 * 2 / 5 * 100;
                ViewBag.d1 = (float)c1 * 1 / 5 * 100;

                // PuanÄ± hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanÄ±nÄ± yÃ¼zdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik6.Add(item.Key);
                ViewBag.puan6.Add(p1);
                ViewBag.adet6.Add(kisiSayisi);

            }
            foreach (var item in bul.AsEnumerable().GroupBy(x => x.User?.Sube?.SubeAdi ?? "Tanımsız"))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerÃ§ek kiÅŸi adedi

                var c5 = item.Count(x => x.CevapPuan == 5);
                var c4 = item.Count(x => x.CevapPuan == 4);
                var c3 = item.Count(x => x.CevapPuan == 3);
                var c2 = item.Count(x => x.CevapPuan == 2);
                var c1 = item.Count(x => x.CevapPuan == 1);

                ViewBag.d5 = (float)c5 * 5 / 5 * 100;
                ViewBag.d4 = (float)c4 * 4 / 5 * 100;
                ViewBag.d3 = (float)c3 * 3 / 5 * 100;
                ViewBag.d2 = (float)c2 * 2 / 5 * 100;
                ViewBag.d1 = (float)c1 * 1 / 5 * 100;

                // PuanÄ± hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanÄ±nÄ± yÃ¼zdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik7.Add(item.Key);
                ViewBag.puan7.Add(p1);
                ViewBag.adet7.Add(kisiSayisi);

            }
            foreach (var item in bul.AsEnumerable().GroupBy(x => x.User?.Unvan?.UnvanAdi ?? "Tanımsız"))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerÃ§ek kiÅŸi adedi

                var c5 = item.Count(x => x.CevapPuan == 5);
                var c4 = item.Count(x => x.CevapPuan == 4);
                var c3 = item.Count(x => x.CevapPuan == 3);
                var c2 = item.Count(x => x.CevapPuan == 2);
                var c1 = item.Count(x => x.CevapPuan == 1);

                ViewBag.d5 = (float)c5 * 5 / 5 * 100;
                ViewBag.d4 = (float)c4 * 4 / 5 * 100;
                ViewBag.d3 = (float)c3 * 3 / 5 * 100;
                ViewBag.d2 = (float)c2 * 2 / 5 * 100;
                ViewBag.d1 = (float)c1 * 1 / 5 * 100;

                // PuanÄ± hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanÄ±nÄ± yÃ¼zdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik8.Add(item.Key);
                ViewBag.puan8.Add(p1);
                ViewBag.adet8.Add(kisiSayisi);

            }
            foreach (var item in bul.AsEnumerable().GroupBy(x => x.User?.Yaka?.YakaAdi ?? "Tanımsız"))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerÃ§ek kiÅŸi adedi

                var c5 = item.Count(x => x.CevapPuan == 5);
                var c4 = item.Count(x => x.CevapPuan == 4);
                var c3 = item.Count(x => x.CevapPuan == 3);
                var c2 = item.Count(x => x.CevapPuan == 2);
                var c1 = item.Count(x => x.CevapPuan == 1);

                ViewBag.d5 = (float)c5 * 5 / 5 * 100;
                ViewBag.d4 = (float)c4 * 4 / 5 * 100;
                ViewBag.d3 = (float)c3 * 3 / 5 * 100;
                ViewBag.d2 = (float)c2 * 2 / 5 * 100;
                ViewBag.d1 = (float)c1 * 1 / 5 * 100;

                // PuanÄ± hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanÄ±nÄ± yÃ¼zdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik9.Add(item.Key);
                ViewBag.puan9.Add(p1);
                ViewBag.adet9.Add(kisiSayisi);

            }
            foreach (var item in bul.AsEnumerable().GroupBy(x => new
            {
                Id = x.User?.UserYoneticisi ?? 0,
                Ad = x.User?.Yonetici?.YoneticiAdi ?? "Tanımsız"
            }))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerÃ§ek kiÅŸi adedi

                var c5 = item.Count(x => x.CevapPuan == 5);
                var c4 = item.Count(x => x.CevapPuan == 4);
                var c3 = item.Count(x => x.CevapPuan == 3);
                var c2 = item.Count(x => x.CevapPuan == 2);
                var c1 = item.Count(x => x.CevapPuan == 1);

                ViewBag.d5 = (float)c5 * 5 / 5 * 100;
                ViewBag.d4 = (float)c4 * 4 / 5 * 100;
                ViewBag.d3 = (float)c3 * 3 / 5 * 100;
                ViewBag.d2 = (float)c2 * 2 / 5 * 100;
                ViewBag.d1 = (float)c1 * 1 / 5 * 100;

                // PuanÄ± hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanÄ±nÄ± yÃ¼zdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik10.Add(item.Key.Ad);
                ViewBag.puan10.Add(p1);
                ViewBag.adet10.Add(kisiSayisi);

            }
            foreach (var item in bul.GroupBy(x => x.Soru.SoruAdi))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerÃ§ek kiÅŸi adedi

                var c5 = item.Count(x => x.CevapPuan == 5);
                var c4 = item.Count(x => x.CevapPuan == 4);
                var c3 = item.Count(x => x.CevapPuan == 3);
                var c2 = item.Count(x => x.CevapPuan == 2);
                var c1 = item.Count(x => x.CevapPuan == 1);

                ViewBag.d5 = (float)c5 * 5 / 5 * 100;
                ViewBag.d4 = (float)c4 * 4 / 5 * 100;
                ViewBag.d3 = (float)c3 * 3 / 5 * 100;
                ViewBag.d2 = (float)c2 * 2 / 5 * 100;
                ViewBag.d1 = (float)c1 * 1 / 5 * 100;

                // PuanÄ± hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanÄ±nÄ± yÃ¼zdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik11.Add(item.Key);
                ViewBag.puan11.Add(p1);
                ViewBag.adet11.Add(kisiSayisi);

            }
            foreach (var item in bul.GroupBy(x => x.SoruGrup.SoruGrupAdi))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerÃ§ek kiÅŸi adedi

                var c5 = item.Count(x => x.CevapPuan == 5);
                var c4 = item.Count(x => x.CevapPuan == 4);
                var c3 = item.Count(x => x.CevapPuan == 3);
                var c2 = item.Count(x => x.CevapPuan == 2);
                var c1 = item.Count(x => x.CevapPuan == 1);

                ViewBag.d5 = (float)c5 * 5 / 5 * 100;
                ViewBag.d4 = (float)c4 * 4 / 5 * 100;
                ViewBag.d3 = (float)c3 * 3 / 5 * 100;
                ViewBag.d2 = (float)c2 * 2 / 5 * 100;
                ViewBag.d1 = (float)c1 * 1 / 5 * 100;

                // PuanÄ± hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanÄ±nÄ± yÃ¼zdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik12.Add(item.Key);
                ViewBag.puan12.Add(p1);
                ViewBag.adet12.Add(kisiSayisi);
            }

            RaporYoneticiResimleriHazirla(bul);

            return View("AnketSoruGrupIndex", bul);
        }
        public ActionResult AnketUserIndex(int id, int? ank, int? user)
        {
            if (Session["id"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }

            if (ank == null)
            {
                return RedirectToAction("AnketIndex");
            }

            var routeId = id > 0 ? (int?)id : null;
            var raporUserId = user.HasValue && user.Value > 0 ? user : routeId;
            var effectiveUseId = ResolveKatilimUseId(raporUserId, routeId);

            var izll = db.Izledim
                .Where(x => x.AnketId == ank && x.UseId == effectiveUseId)
                .OrderByDescending(x => x.IzleId)
                .FirstOrDefault();

            if (izll != null)

            {
                if (izll.BitisZaman > DateTime.Now)
                {
                    return RedirectToAction("Hata6", "Home", null);
                }
            }


            if (!AnketCalismaAlanindaMi(ank))
            {
                return RedirectToAction("AnketIndex");
            }

            var tumCevaplar = KatilimHavuzlariniGetir(ank.Value, routeId, raporUserId);
            var bul = SonSoruCevaplari(tumCevaplar);
            var adi = tumCevaplar
                .OrderByDescending(x => x.KayitTar ?? DateTime.MinValue)
                .ThenByDescending(x => x.HavuzId)
                .FirstOrDefault();
            var ankadi = db.Anket.Where(x => x.AnketId == ank).FirstOrDefault();
            if (adi?.User != null)
            {
                ViewBag.adisoyadi = adi.User.UserAdi;
                ViewBag.unvan = adi.User.Unvan?.UnvanAdi;
                ViewBag.egitim = adi.User.Egitim?.EgitimAdi;
                ViewBag.cinsiyet = adi.User.Cinsiyet?.CinsiyetAdi;
                ViewBag.departman = adi.User.Departman?.DepartmanAdi;
                ViewBag.giris = adi.User.UserIseGirisTarihi;
                ViewBag.kayit = adi.KayitTar;
                ViewBag.yonetici = adi.User.Yonetici?.YoneticiAdi;
                ViewBag.yaka = adi.User.Yaka?.YakaAdi;
                ViewBag.sehir = adi.User.Sehir?.SehiarAdi;
                ViewBag.sube = adi.User.Sube?.SubeAdi;
                ViewBag.resim = adi.User.UserResim;
            }
            else if (adi != null)
            {
                ViewBag.adisoyadi = adi.Isimsiz.HasValue
                    ? "Katilimci #" + adi.Isimsiz.Value
                    : "Katilimci";
                ViewBag.kayit = adi.KayitTar;
            }
            if (ankadi != null)
            {
                ViewBag.adi = ankadi.AnketAdi;
                ViewBag.sinav = SinavTurundeMi(ankadi);
                ViewBag.id = ankadi.AnketId;


            }
            if (ank != null)
            {
                ViewBag.ank = ank;
            }
            if (adi != null)
            {
                ViewBag.anketadi = adi.User?.UserAdi ?? ViewBag.adisoyadi;
            }

            if (ankadi != null && ankadi.Sinav != true)
            {
                var soru = bul.Count(); // toplam Soru
                var c5 = bul.Count(x => x.CevapPuan == 5);
                var c4 = bul.Count(x => x.CevapPuan == 4);
                var c3 = bul.Count(x => x.CevapPuan == 3);
                var c2 = bul.Count(x => x.CevapPuan == 2);
                var c1 = bul.Count(x => x.CevapPuan == 1);

                ViewBag.d5 = (float)c5 * 5 / 5 * 100;
                ViewBag.d4 = (float)c4 * 4 / 5 * 100;
                ViewBag.d3 = (float)c3 * 3 / 5 * 100;
                ViewBag.d2 = (float)c2 * 2 / 5 * 100;
                ViewBag.d1 = (float)c1 * 1 / 5 * 100;

                // PuanÄ± hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanÄ±nÄ± yÃ¼zdesini bulur
                var p1 = p2 / soru;

                if (p1 != null)
                {
                    ViewBag.puan = p1;

                }
                else
                {
                    ViewBag.puan = 0;
                }

                var sr = db.AnketGrup.Where(x => x.AnketId == ank);
                var st = from detay in sr
                         join master in db.Soru on detay.SoruGrupId equals master.SoruGrupId
                         select master;
                ViewBag.soru = st.Count();
                ViewBag.soru1 = soru;

            }
            else
            {
                var cx = bul.Where(x => x.Cevap != null && x.Cevap.Dogru == true);
                var tp = cx.Sum(x => x.SoruPuan);
                if (tp != null)
                {
                    ViewBag.puan = tp;

                }
                else
                {
                    ViewBag.puan = 0;
                }


                var soru = bul.Count(); // toplam Soru
                var sr = db.AnketGrup.Where(x => x.AnketId == ank);
                var st = from detay in sr
                         join master in db.Soru on detay.SoruGrupId equals master.SoruGrupId
                         select master;
                ViewBag.soru = st.Count();
                ViewBag.soru1 = soru;


            }



            ViewBag.puan = ankadi != null ? KatilimPuaniHesapla(ankadi, bul) : 0;
            ViewBag.soru = AnketSoruSayisi(ank.Value);
            ViewBag.soru1 = bul.Count;
            ViewBag.dogruSayisi = bul.Count(x => x.Cevap != null && x.Cevap.Dogru == true);
            ViewBag.yanlisSayisi = bul.Count(x => x.Cevap != null && x.Cevap.Dogru != true);
            ViewBag.gecmeNotu = ankadi?.SertifikaNotu;
            ViewBag.raporTarihi = DateTime.Now;

            return View(bul);
        }
        public ActionResult AnketYakaIndex(int id, int? ank)
        {
            if (Session["id"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }
            if (Session["admin"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }

            if (!AnketCalismaAlanindaMi(ank))
            {
                return RedirectToAction("AnketIndex");
            }

            var bul = RaporHavuzSorgusu(ank)
                .Where(x => x.User.UserYaka == id)
                .ToList();
            var adi = bul.FirstOrDefault();
            var ankadi = db.Anket.Where(x => x.AnketId == ank).FirstOrDefault();
            if (ankadi != null)
            {
                ViewBag.adi = ankadi.AnketAdi;
                ViewBag.id = ankadi.AnketId;
                ViewBag.sinav = SinavTurundeMi(ankadi);

            }
            if (ank != null)
            {
                ViewBag.ank = ank;
            }
            if (adi != null)
            {
                ViewBag.anketadi = adi.User?.Yaka?.YakaAdi ?? "Tanımsız";
            }


            // Genel
            ViewBag.baslik1 = new List<string>();
            ViewBag.puan1 = new List<float>();
            ViewBag.adet1 = new List<int>();

            // Departman
            ViewBag.baslik2 = new List<string>();
            ViewBag.puan2 = new List<float>();
            ViewBag.adet2 = new List<int>();

            // Cinsiyet
            ViewBag.baslik3 = new List<string>();
            ViewBag.puan3 = new List<float>();
            ViewBag.adet3 = new List<int>();

            // EÄŸitim
            ViewBag.baslik4 = new List<string>();
            ViewBag.puan4 = new List<float>();
            ViewBag.adet4 = new List<int>();

            // YaÅŸ
            ViewBag.baslik5 = new List<string>();  // int yerine string yapalÄ±m â†’ chart iÃ§in daha kolay
            ViewBag.puan5 = new List<float>();
            ViewBag.adet5 = new List<int>();

            // Åehir
            ViewBag.baslik6 = new List<string>();
            ViewBag.puan6 = new List<float>();
            ViewBag.adet6 = new List<int>();

            // Åube
            ViewBag.baslik7 = new List<string>();
            ViewBag.puan7 = new List<float>();
            ViewBag.adet7 = new List<int>();

            // Ãœnvan
            ViewBag.baslik8 = new List<string>();
            ViewBag.puan8 = new List<float>();
            ViewBag.adet8 = new List<int>();

            // Yaka
            ViewBag.baslik9 = new List<string>();
            ViewBag.puan9 = new List<float>();
            ViewBag.adet9 = new List<int>();

            // YÃ¶netici
            ViewBag.baslik10 = new List<string>();
            ViewBag.puan10 = new List<float>();
            ViewBag.adet10 = new List<int>();

            // Soru
            ViewBag.baslik11 = new List<string>();
            ViewBag.puan11 = new List<float>();
            ViewBag.adet11 = new List<int>();

            // Soru Grup
            ViewBag.baslik12 = new List<string>();
            ViewBag.puan12 = new List<float>();
            ViewBag.adet12 = new List<int>();


            foreach (var item in bul.GroupBy(x => x.Anket.AnketAdi))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerÃ§ek kiÅŸi adedi

                var c5 = item.Count(x => x.CevapPuan == 5);
                var c4 = item.Count(x => x.CevapPuan == 4);
                var c3 = item.Count(x => x.CevapPuan == 3);
                var c2 = item.Count(x => x.CevapPuan == 2);
                var c1 = item.Count(x => x.CevapPuan == 1);

                ViewBag.d5 = (float)c5 * 5 / 5 * 100;
                ViewBag.d4 = (float)c4 * 4 / 5 * 100;
                ViewBag.d3 = (float)c3 * 3 / 5 * 100;
                ViewBag.d2 = (float)c2 * 2 / 5 * 100;
                ViewBag.d1 = (float)c1 * 1 / 5 * 100;

                // PuanÄ± hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanÄ±nÄ± yÃ¼zdesini bulur
                var p1 = p2 / soru;

                ViewBag.baslik1.Add(item.Key);
                ViewBag.puan1.Add(p1);
                ViewBag.adet1.Add(kisiSayisi);

            }
            foreach (var item in bul.AsEnumerable().GroupBy(x => x.User?.Departman?.DepartmanAdi ?? "Tanımsız"))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerÃ§ek kiÅŸi adedi

                var c5 = item.Count(x => x.CevapPuan == 5);
                var c4 = item.Count(x => x.CevapPuan == 4);
                var c3 = item.Count(x => x.CevapPuan == 3);
                var c2 = item.Count(x => x.CevapPuan == 2);
                var c1 = item.Count(x => x.CevapPuan == 1);

                ViewBag.d5 = (float)c5 * 5 / 5 * 100;
                ViewBag.d4 = (float)c4 * 4 / 5 * 100;
                ViewBag.d3 = (float)c3 * 3 / 5 * 100;
                ViewBag.d2 = (float)c2 * 2 / 5 * 100;
                ViewBag.d1 = (float)c1 * 1 / 5 * 100;

                // PuanÄ± hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanÄ±nÄ± yÃ¼zdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik2.Add(item.Key);
                ViewBag.puan2.Add(p1);
                ViewBag.adet2.Add(kisiSayisi);

            }
            foreach (var item in bul.AsEnumerable().GroupBy(x => x.User?.Cinsiyet?.CinsiyetAdi ?? "Tanımsız"))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerÃ§ek kiÅŸi adedi

                var c5 = item.Count(x => x.CevapPuan == 5);
                var c4 = item.Count(x => x.CevapPuan == 4);
                var c3 = item.Count(x => x.CevapPuan == 3);
                var c2 = item.Count(x => x.CevapPuan == 2);
                var c1 = item.Count(x => x.CevapPuan == 1);

                ViewBag.d5 = (float)c5 * 5 / 5 * 100;
                ViewBag.d4 = (float)c4 * 4 / 5 * 100;
                ViewBag.d3 = (float)c3 * 3 / 5 * 100;
                ViewBag.d2 = (float)c2 * 2 / 5 * 100;
                ViewBag.d1 = (float)c1 * 1 / 5 * 100;

                // PuanÄ± hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanÄ±nÄ± yÃ¼zdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik3.Add(item.Key);
                ViewBag.puan3.Add(p1);
                ViewBag.adet3.Add(kisiSayisi);

            }
            foreach (var item in bul.AsEnumerable().GroupBy(x => x.User?.Egitim?.EgitimAdi ?? "Tanımsız"))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerÃ§ek kiÅŸi adedi

                var c5 = item.Count(x => x.CevapPuan == 5);
                var c4 = item.Count(x => x.CevapPuan == 4);
                var c3 = item.Count(x => x.CevapPuan == 3);
                var c2 = item.Count(x => x.CevapPuan == 2);
                var c1 = item.Count(x => x.CevapPuan == 1);

                ViewBag.d5 = (float)c5 * 5 / 5 * 100;
                ViewBag.d4 = (float)c4 * 4 / 5 * 100;
                ViewBag.d3 = (float)c3 * 3 / 5 * 100;
                ViewBag.d2 = (float)c2 * 2 / 5 * 100;
                ViewBag.d1 = (float)c1 * 1 / 5 * 100;

                // PuanÄ± hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanÄ±nÄ± yÃ¼zdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik4.Add(item.Key);
                ViewBag.puan4.Add(p1);
                ViewBag.adet4.Add(kisiSayisi);


            }
            var bugun = DateTime.Today;

            // Ã–nce boÅŸ listeleri hazÄ±rla
            ViewBag.baslik5 = new List<string>();
            ViewBag.puan5 = new List<float>();

            foreach (var item in bul.AsEnumerable()
                .Where(x => x.User?.UserDogumTar != null)
                .GroupBy(x => x.User.UserDogumTar.Value.Year))
            {
                var dogumYili = item.Key;
                var yas = bugun.Year - dogumYili;
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerÃ§ek kiÅŸi adedi


                // EÄŸer doÄŸum gÃ¼nÃ¼ bu yÄ±l daha gelmediyse yaÅŸÄ± 1 azalt
                var ilkKayit = item.FirstOrDefault()?.User?.UserDogumTar;
                if (ilkKayit != null && bugun.DayOfYear < ilkKayit.Value.DayOfYear)
                {
                    yas--;
                }

                // Ortalama puanÄ± hesapla (kiÅŸiye gÃ¶re normalize)
                var ortalama = item
                    .GroupBy(x => x.UserId)
                    .Select(g => g.Average(y => y.CevapPuan))
                    .Average();

                var ortalamaYuzde = ortalama * 20; // 5 Ã¼zerinden 100'e Ã§evirme

                // ğŸ”¹ Burada artÄ±k Add et
                ViewBag.baslik5.Add(yas.ToString());       // X ekseni â†’ yaÅŸ
                ViewBag.puan5.Add((float)ortalamaYuzde);   // Y ekseni â†’ puan
                ViewBag.adet5.Add(kisiSayisi);   // Y ekseni â†’ puan
            }

            foreach (var item in bul.AsEnumerable().GroupBy(x => x.User?.Sehir?.SehiarAdi ?? "Tanımsız"))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerÃ§ek kiÅŸi adedi

                var c5 = item.Count(x => x.CevapPuan == 5);
                var c4 = item.Count(x => x.CevapPuan == 4);
                var c3 = item.Count(x => x.CevapPuan == 3);
                var c2 = item.Count(x => x.CevapPuan == 2);
                var c1 = item.Count(x => x.CevapPuan == 1);

                ViewBag.d5 = (float)c5 * 5 / 5 * 100;
                ViewBag.d4 = (float)c4 * 4 / 5 * 100;
                ViewBag.d3 = (float)c3 * 3 / 5 * 100;
                ViewBag.d2 = (float)c2 * 2 / 5 * 100;
                ViewBag.d1 = (float)c1 * 1 / 5 * 100;

                // PuanÄ± hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanÄ±nÄ± yÃ¼zdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik6.Add(item.Key);
                ViewBag.puan6.Add(p1);
                ViewBag.adet6.Add(kisiSayisi);

            }
            foreach (var item in bul.AsEnumerable().GroupBy(x => x.User?.Sube?.SubeAdi ?? "Tanımsız"))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerÃ§ek kiÅŸi adedi

                var c5 = item.Count(x => x.CevapPuan == 5);
                var c4 = item.Count(x => x.CevapPuan == 4);
                var c3 = item.Count(x => x.CevapPuan == 3);
                var c2 = item.Count(x => x.CevapPuan == 2);
                var c1 = item.Count(x => x.CevapPuan == 1);

                ViewBag.d5 = (float)c5 * 5 / 5 * 100;
                ViewBag.d4 = (float)c4 * 4 / 5 * 100;
                ViewBag.d3 = (float)c3 * 3 / 5 * 100;
                ViewBag.d2 = (float)c2 * 2 / 5 * 100;
                ViewBag.d1 = (float)c1 * 1 / 5 * 100;

                // PuanÄ± hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanÄ±nÄ± yÃ¼zdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik7.Add(item.Key);
                ViewBag.puan7.Add(p1);
                ViewBag.adet7.Add(kisiSayisi);

            }
            foreach (var item in bul.AsEnumerable().GroupBy(x => x.User?.Unvan?.UnvanAdi ?? "Tanımsız"))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerÃ§ek kiÅŸi adedi

                var c5 = item.Count(x => x.CevapPuan == 5);
                var c4 = item.Count(x => x.CevapPuan == 4);
                var c3 = item.Count(x => x.CevapPuan == 3);
                var c2 = item.Count(x => x.CevapPuan == 2);
                var c1 = item.Count(x => x.CevapPuan == 1);

                ViewBag.d5 = (float)c5 * 5 / 5 * 100;
                ViewBag.d4 = (float)c4 * 4 / 5 * 100;
                ViewBag.d3 = (float)c3 * 3 / 5 * 100;
                ViewBag.d2 = (float)c2 * 2 / 5 * 100;
                ViewBag.d1 = (float)c1 * 1 / 5 * 100;

                // PuanÄ± hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanÄ±nÄ± yÃ¼zdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik8.Add(item.Key);
                ViewBag.puan8.Add(p1);
                ViewBag.adet8.Add(kisiSayisi);

            }
            foreach (var item in bul.AsEnumerable().GroupBy(x => x.User?.Yaka?.YakaAdi ?? "Tanımsız"))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerÃ§ek kiÅŸi adedi

                var c5 = item.Count(x => x.CevapPuan == 5);
                var c4 = item.Count(x => x.CevapPuan == 4);
                var c3 = item.Count(x => x.CevapPuan == 3);
                var c2 = item.Count(x => x.CevapPuan == 2);
                var c1 = item.Count(x => x.CevapPuan == 1);

                ViewBag.d5 = (float)c5 * 5 / 5 * 100;
                ViewBag.d4 = (float)c4 * 4 / 5 * 100;
                ViewBag.d3 = (float)c3 * 3 / 5 * 100;
                ViewBag.d2 = (float)c2 * 2 / 5 * 100;
                ViewBag.d1 = (float)c1 * 1 / 5 * 100;

                // PuanÄ± hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanÄ±nÄ± yÃ¼zdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik9.Add(item.Key);
                ViewBag.puan9.Add(p1);
                ViewBag.adet9.Add(kisiSayisi);

            }
            foreach (var item in bul.AsEnumerable().GroupBy(x => new
            {
                Id = x.User?.UserYoneticisi ?? 0,
                Ad = x.User?.Yonetici?.YoneticiAdi ?? "Tanımsız"
            }))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerÃ§ek kiÅŸi adedi

                var c5 = item.Count(x => x.CevapPuan == 5);
                var c4 = item.Count(x => x.CevapPuan == 4);
                var c3 = item.Count(x => x.CevapPuan == 3);
                var c2 = item.Count(x => x.CevapPuan == 2);
                var c1 = item.Count(x => x.CevapPuan == 1);

                ViewBag.d5 = (float)c5 * 5 / 5 * 100;
                ViewBag.d4 = (float)c4 * 4 / 5 * 100;
                ViewBag.d3 = (float)c3 * 3 / 5 * 100;
                ViewBag.d2 = (float)c2 * 2 / 5 * 100;
                ViewBag.d1 = (float)c1 * 1 / 5 * 100;

                // PuanÄ± hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanÄ±nÄ± yÃ¼zdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik10.Add(item.Key.Ad);
                ViewBag.puan10.Add(p1);
                ViewBag.adet10.Add(kisiSayisi);

            }
            foreach (var item in bul.GroupBy(x => x.Soru.SoruAdi))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerÃ§ek kiÅŸi adedi

                var c5 = item.Count(x => x.CevapPuan == 5);
                var c4 = item.Count(x => x.CevapPuan == 4);
                var c3 = item.Count(x => x.CevapPuan == 3);
                var c2 = item.Count(x => x.CevapPuan == 2);
                var c1 = item.Count(x => x.CevapPuan == 1);

                ViewBag.d5 = (float)c5 * 5 / 5 * 100;
                ViewBag.d4 = (float)c4 * 4 / 5 * 100;
                ViewBag.d3 = (float)c3 * 3 / 5 * 100;
                ViewBag.d2 = (float)c2 * 2 / 5 * 100;
                ViewBag.d1 = (float)c1 * 1 / 5 * 100;

                // PuanÄ± hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanÄ±nÄ± yÃ¼zdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik11.Add(item.Key);
                ViewBag.puan11.Add(p1);
                ViewBag.adet11.Add(kisiSayisi);

            }
            foreach (var item in bul.GroupBy(x => x.SoruGrup.SoruGrupAdi))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerÃ§ek kiÅŸi adedi

                var c5 = item.Count(x => x.CevapPuan == 5);
                var c4 = item.Count(x => x.CevapPuan == 4);
                var c3 = item.Count(x => x.CevapPuan == 3);
                var c2 = item.Count(x => x.CevapPuan == 2);
                var c1 = item.Count(x => x.CevapPuan == 1);

                ViewBag.d5 = (float)c5 * 5 / 5 * 100;
                ViewBag.d4 = (float)c4 * 4 / 5 * 100;
                ViewBag.d3 = (float)c3 * 3 / 5 * 100;
                ViewBag.d2 = (float)c2 * 2 / 5 * 100;
                ViewBag.d1 = (float)c1 * 1 / 5 * 100;

                // PuanÄ± hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanÄ±nÄ± yÃ¼zdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik12.Add(item.Key);
                ViewBag.puan12.Add(p1);
                ViewBag.adet12.Add(kisiSayisi);
            }

            RaporYoneticiResimleriHazirla(bul);

            return View("AnketSoruGrupIndex", bul);
        }
        public ActionResult AnketYoneticiIndex(int id, int? ank)
        {
            if (Session["id"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }
            if (Session["admin"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }

            if (!AnketCalismaAlanindaMi(ank))
            {
                return RedirectToAction("AnketIndex");
            }

            var bul = RaporHavuzSorgusu(ank)
                .Where(x => x.User.UserYoneticisi == id)
                .ToList();
            ViewBag.YoneticiResimleri = YoneticiResimSozlugu(new int?[] { id });
            var adi = bul.FirstOrDefault();
            var ankadi = db.Anket.Where(x => x.AnketId == ank).FirstOrDefault();
            if (ankadi != null)
            {
                ViewBag.adi = ankadi.AnketAdi;
                ViewBag.id = ankadi.AnketId;
                ViewBag.sinav = SinavTurundeMi(ankadi);

            }
            if (ank != null)
            {
                ViewBag.ank = ank;
            }
            if (adi != null)
            {
                ViewBag.anketadi = adi.User?.Yonetici?.YoneticiAdi ?? "Tanımsız";
            }


            // Genel
            ViewBag.baslik1 = new List<string>();
            ViewBag.puan1 = new List<float>();
            ViewBag.adet1 = new List<int>();

            // Departman
            ViewBag.baslik2 = new List<string>();
            ViewBag.puan2 = new List<float>();
            ViewBag.adet2 = new List<int>();

            // Cinsiyet
            ViewBag.baslik3 = new List<string>();
            ViewBag.puan3 = new List<float>();
            ViewBag.adet3 = new List<int>();

            // EÄŸitim
            ViewBag.baslik4 = new List<string>();
            ViewBag.puan4 = new List<float>();
            ViewBag.adet4 = new List<int>();

            // YaÅŸ
            ViewBag.baslik5 = new List<string>();  // int yerine string yapalÄ±m â†’ chart iÃ§in daha kolay
            ViewBag.puan5 = new List<float>();
            ViewBag.adet5 = new List<int>();

            // Åehir
            ViewBag.baslik6 = new List<string>();
            ViewBag.puan6 = new List<float>();
            ViewBag.adet6 = new List<int>();

            // Åube
            ViewBag.baslik7 = new List<string>();
            ViewBag.puan7 = new List<float>();
            ViewBag.adet7 = new List<int>();

            // Ãœnvan
            ViewBag.baslik8 = new List<string>();
            ViewBag.puan8 = new List<float>();
            ViewBag.adet8 = new List<int>();

            // Yaka
            ViewBag.baslik9 = new List<string>();
            ViewBag.puan9 = new List<float>();
            ViewBag.adet9 = new List<int>();

            // YÃ¶netici
            ViewBag.baslik10 = new List<string>();
            ViewBag.puan10 = new List<float>();
            ViewBag.adet10 = new List<int>();

            // Soru
            ViewBag.baslik11 = new List<string>();
            ViewBag.puan11 = new List<float>();
            ViewBag.adet11 = new List<int>();

            // Soru Grup
            ViewBag.baslik12 = new List<string>();
            ViewBag.puan12 = new List<float>();
            ViewBag.adet12 = new List<int>();


            foreach (var item in bul.GroupBy(x => x.Anket.AnketAdi))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerÃ§ek kiÅŸi adedi

                var c5 = item.Count(x => x.CevapPuan == 5);
                var c4 = item.Count(x => x.CevapPuan == 4);
                var c3 = item.Count(x => x.CevapPuan == 3);
                var c2 = item.Count(x => x.CevapPuan == 2);
                var c1 = item.Count(x => x.CevapPuan == 1);

                ViewBag.d5 = (float)c5 * 5 / 5 * 100;
                ViewBag.d4 = (float)c4 * 4 / 5 * 100;
                ViewBag.d3 = (float)c3 * 3 / 5 * 100;
                ViewBag.d2 = (float)c2 * 2 / 5 * 100;
                ViewBag.d1 = (float)c1 * 1 / 5 * 100;

                // PuanÄ± hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanÄ±nÄ± yÃ¼zdesini bulur
                var p1 = p2 / soru;

                ViewBag.baslik1.Add(item.Key);
                ViewBag.puan1.Add(p1);
                ViewBag.adet1.Add(kisiSayisi);

            }
            foreach (var item in bul.AsEnumerable().GroupBy(x => x.User?.Departman?.DepartmanAdi ?? "Tanımsız"))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerÃ§ek kiÅŸi adedi

                var c5 = item.Count(x => x.CevapPuan == 5);
                var c4 = item.Count(x => x.CevapPuan == 4);
                var c3 = item.Count(x => x.CevapPuan == 3);
                var c2 = item.Count(x => x.CevapPuan == 2);
                var c1 = item.Count(x => x.CevapPuan == 1);

                ViewBag.d5 = (float)c5 * 5 / 5 * 100;
                ViewBag.d4 = (float)c4 * 4 / 5 * 100;
                ViewBag.d3 = (float)c3 * 3 / 5 * 100;
                ViewBag.d2 = (float)c2 * 2 / 5 * 100;
                ViewBag.d1 = (float)c1 * 1 / 5 * 100;

                // PuanÄ± hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanÄ±nÄ± yÃ¼zdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik2.Add(item.Key);
                ViewBag.puan2.Add(p1);
                ViewBag.adet2.Add(kisiSayisi);

            }
            foreach (var item in bul.AsEnumerable().GroupBy(x => x.User?.Cinsiyet?.CinsiyetAdi ?? "Tanımsız"))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerÃ§ek kiÅŸi adedi

                var c5 = item.Count(x => x.CevapPuan == 5);
                var c4 = item.Count(x => x.CevapPuan == 4);
                var c3 = item.Count(x => x.CevapPuan == 3);
                var c2 = item.Count(x => x.CevapPuan == 2);
                var c1 = item.Count(x => x.CevapPuan == 1);

                ViewBag.d5 = (float)c5 * 5 / 5 * 100;
                ViewBag.d4 = (float)c4 * 4 / 5 * 100;
                ViewBag.d3 = (float)c3 * 3 / 5 * 100;
                ViewBag.d2 = (float)c2 * 2 / 5 * 100;
                ViewBag.d1 = (float)c1 * 1 / 5 * 100;

                // PuanÄ± hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanÄ±nÄ± yÃ¼zdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik3.Add(item.Key);
                ViewBag.puan3.Add(p1);
                ViewBag.adet3.Add(kisiSayisi);

            }
            foreach (var item in bul.AsEnumerable().GroupBy(x => x.User?.Egitim?.EgitimAdi ?? "Tanımsız"))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerÃ§ek kiÅŸi adedi

                var c5 = item.Count(x => x.CevapPuan == 5);
                var c4 = item.Count(x => x.CevapPuan == 4);
                var c3 = item.Count(x => x.CevapPuan == 3);
                var c2 = item.Count(x => x.CevapPuan == 2);
                var c1 = item.Count(x => x.CevapPuan == 1);

                ViewBag.d5 = (float)c5 * 5 / 5 * 100;
                ViewBag.d4 = (float)c4 * 4 / 5 * 100;
                ViewBag.d3 = (float)c3 * 3 / 5 * 100;
                ViewBag.d2 = (float)c2 * 2 / 5 * 100;
                ViewBag.d1 = (float)c1 * 1 / 5 * 100;

                // PuanÄ± hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanÄ±nÄ± yÃ¼zdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik4.Add(item.Key);
                ViewBag.puan4.Add(p1);
                ViewBag.adet4.Add(kisiSayisi);


            }
            var bugun = DateTime.Today;

            // Ã–nce boÅŸ listeleri hazÄ±rla
            ViewBag.baslik5 = new List<string>();
            ViewBag.puan5 = new List<float>();

            foreach (var item in bul.AsEnumerable()
                .Where(x => x.User?.UserDogumTar != null)
                .GroupBy(x => x.User.UserDogumTar.Value.Year))
            {
                var dogumYili = item.Key;
                var yas = bugun.Year - dogumYili;
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerÃ§ek kiÅŸi adedi


                // EÄŸer doÄŸum gÃ¼nÃ¼ bu yÄ±l daha gelmediyse yaÅŸÄ± 1 azalt
                var ilkKayit = item.FirstOrDefault()?.User?.UserDogumTar;
                if (ilkKayit != null && bugun.DayOfYear < ilkKayit.Value.DayOfYear)
                {
                    yas--;
                }

                // Ortalama puanÄ± hesapla (kiÅŸiye gÃ¶re normalize)
                var ortalama = item
                    .GroupBy(x => x.UserId)
                    .Select(g => g.Average(y => y.CevapPuan))
                    .Average();

                var ortalamaYuzde = ortalama * 20; // 5 Ã¼zerinden 100'e Ã§evirme

                // ğŸ”¹ Burada artÄ±k Add et
                ViewBag.baslik5.Add(yas.ToString());       // X ekseni â†’ yaÅŸ
                ViewBag.puan5.Add((float)ortalamaYuzde);   // Y ekseni â†’ puan
                ViewBag.adet5.Add(kisiSayisi);   // Y ekseni â†’ puan
            }

            foreach (var item in bul.AsEnumerable().GroupBy(x => x.User?.Sehir?.SehiarAdi ?? "Tanımsız"))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerÃ§ek kiÅŸi adedi

                var c5 = item.Count(x => x.CevapPuan == 5);
                var c4 = item.Count(x => x.CevapPuan == 4);
                var c3 = item.Count(x => x.CevapPuan == 3);
                var c2 = item.Count(x => x.CevapPuan == 2);
                var c1 = item.Count(x => x.CevapPuan == 1);

                ViewBag.d5 = (float)c5 * 5 / 5 * 100;
                ViewBag.d4 = (float)c4 * 4 / 5 * 100;
                ViewBag.d3 = (float)c3 * 3 / 5 * 100;
                ViewBag.d2 = (float)c2 * 2 / 5 * 100;
                ViewBag.d1 = (float)c1 * 1 / 5 * 100;

                // PuanÄ± hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanÄ±nÄ± yÃ¼zdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik6.Add(item.Key);
                ViewBag.puan6.Add(p1);
                ViewBag.adet6.Add(kisiSayisi);

            }
            foreach (var item in bul.AsEnumerable().GroupBy(x => x.User?.Sube?.SubeAdi ?? "Tanımsız"))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerÃ§ek kiÅŸi adedi

                var c5 = item.Count(x => x.CevapPuan == 5);
                var c4 = item.Count(x => x.CevapPuan == 4);
                var c3 = item.Count(x => x.CevapPuan == 3);
                var c2 = item.Count(x => x.CevapPuan == 2);
                var c1 = item.Count(x => x.CevapPuan == 1);

                ViewBag.d5 = (float)c5 * 5 / 5 * 100;
                ViewBag.d4 = (float)c4 * 4 / 5 * 100;
                ViewBag.d3 = (float)c3 * 3 / 5 * 100;
                ViewBag.d2 = (float)c2 * 2 / 5 * 100;
                ViewBag.d1 = (float)c1 * 1 / 5 * 100;

                // PuanÄ± hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanÄ±nÄ± yÃ¼zdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik7.Add(item.Key);
                ViewBag.puan7.Add(p1);
                ViewBag.adet7.Add(kisiSayisi);

            }
            foreach (var item in bul.AsEnumerable().GroupBy(x => x.User?.Unvan?.UnvanAdi ?? "Tanımsız"))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerÃ§ek kiÅŸi adedi

                var c5 = item.Count(x => x.CevapPuan == 5);
                var c4 = item.Count(x => x.CevapPuan == 4);
                var c3 = item.Count(x => x.CevapPuan == 3);
                var c2 = item.Count(x => x.CevapPuan == 2);
                var c1 = item.Count(x => x.CevapPuan == 1);

                ViewBag.d5 = (float)c5 * 5 / 5 * 100;
                ViewBag.d4 = (float)c4 * 4 / 5 * 100;
                ViewBag.d3 = (float)c3 * 3 / 5 * 100;
                ViewBag.d2 = (float)c2 * 2 / 5 * 100;
                ViewBag.d1 = (float)c1 * 1 / 5 * 100;

                // PuanÄ± hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanÄ±nÄ± yÃ¼zdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik8.Add(item.Key);
                ViewBag.puan8.Add(p1);
                ViewBag.adet8.Add(kisiSayisi);

            }
            foreach (var item in bul.AsEnumerable().GroupBy(x => x.User?.Yaka?.YakaAdi ?? "Tanımsız"))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerÃ§ek kiÅŸi adedi

                var c5 = item.Count(x => x.CevapPuan == 5);
                var c4 = item.Count(x => x.CevapPuan == 4);
                var c3 = item.Count(x => x.CevapPuan == 3);
                var c2 = item.Count(x => x.CevapPuan == 2);
                var c1 = item.Count(x => x.CevapPuan == 1);

                ViewBag.d5 = (float)c5 * 5 / 5 * 100;
                ViewBag.d4 = (float)c4 * 4 / 5 * 100;
                ViewBag.d3 = (float)c3 * 3 / 5 * 100;
                ViewBag.d2 = (float)c2 * 2 / 5 * 100;
                ViewBag.d1 = (float)c1 * 1 / 5 * 100;

                // PuanÄ± hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanÄ±nÄ± yÃ¼zdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik9.Add(item.Key);
                ViewBag.puan9.Add(p1);
                ViewBag.adet9.Add(kisiSayisi);

            }
            foreach (var item in bul.AsEnumerable().GroupBy(x => new
            {
                Id = x.User?.UserYoneticisi ?? 0,
                Ad = x.User?.Yonetici?.YoneticiAdi ?? "Tanımsız"
            }))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerÃ§ek kiÅŸi adedi

                var c5 = item.Count(x => x.CevapPuan == 5);
                var c4 = item.Count(x => x.CevapPuan == 4);
                var c3 = item.Count(x => x.CevapPuan == 3);
                var c2 = item.Count(x => x.CevapPuan == 2);
                var c1 = item.Count(x => x.CevapPuan == 1);

                ViewBag.d5 = (float)c5 * 5 / 5 * 100;
                ViewBag.d4 = (float)c4 * 4 / 5 * 100;
                ViewBag.d3 = (float)c3 * 3 / 5 * 100;
                ViewBag.d2 = (float)c2 * 2 / 5 * 100;
                ViewBag.d1 = (float)c1 * 1 / 5 * 100;

                // PuanÄ± hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanÄ±nÄ± yÃ¼zdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik10.Add(item.Key.Ad);
                ViewBag.puan10.Add(p1);
                ViewBag.adet10.Add(kisiSayisi);

            }
            foreach (var item in bul.GroupBy(x => x.Soru.SoruAdi))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerÃ§ek kiÅŸi adedi

                var c5 = item.Count(x => x.CevapPuan == 5);
                var c4 = item.Count(x => x.CevapPuan == 4);
                var c3 = item.Count(x => x.CevapPuan == 3);
                var c2 = item.Count(x => x.CevapPuan == 2);
                var c1 = item.Count(x => x.CevapPuan == 1);

                ViewBag.d5 = (float)c5 * 5 / 5 * 100;
                ViewBag.d4 = (float)c4 * 4 / 5 * 100;
                ViewBag.d3 = (float)c3 * 3 / 5 * 100;
                ViewBag.d2 = (float)c2 * 2 / 5 * 100;
                ViewBag.d1 = (float)c1 * 1 / 5 * 100;

                // PuanÄ± hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanÄ±nÄ± yÃ¼zdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik11.Add(item.Key);
                ViewBag.puan11.Add(p1);
                ViewBag.adet11.Add(kisiSayisi);

            }
            foreach (var item in bul.GroupBy(x => x.SoruGrup.SoruGrupAdi))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerÃ§ek kiÅŸi adedi

                var c5 = item.Count(x => x.CevapPuan == 5);
                var c4 = item.Count(x => x.CevapPuan == 4);
                var c3 = item.Count(x => x.CevapPuan == 3);
                var c2 = item.Count(x => x.CevapPuan == 2);
                var c1 = item.Count(x => x.CevapPuan == 1);

                ViewBag.d5 = (float)c5 * 5 / 5 * 100;
                ViewBag.d4 = (float)c4 * 4 / 5 * 100;
                ViewBag.d3 = (float)c3 * 3 / 5 * 100;
                ViewBag.d2 = (float)c2 * 2 / 5 * 100;
                ViewBag.d1 = (float)c1 * 1 / 5 * 100;

                // PuanÄ± hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanÄ±nÄ± yÃ¼zdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik12.Add(item.Key);
                ViewBag.puan12.Add(p1);
                ViewBag.adet12.Add(kisiSayisi);
            }

            RaporYoneticiResimleriHazirla(bul);

            return View("AnketSoruGrupIndex", bul);
        }
        public ActionResult AnketSoruIndex(int id, int? ank)
        {
            if (Session["id"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }
            if (Session["admin"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }

            if (!AnketCalismaAlanindaMi(ank))
            {
                return RedirectToAction("AnketIndex");
            }

            var bul = RaporHavuzSorgusu(ank)
                .Where(x => x.SoruID == id)
                .ToList();
            var adi = bul.FirstOrDefault();
            var ankadi = db.Anket.Where(x => x.AnketId == ank).FirstOrDefault();
            if (ankadi != null)
            {
                ViewBag.adi = ankadi.AnketAdi;
                ViewBag.id = ankadi.AnketId;
                ViewBag.sinav = SinavTurundeMi(ankadi);

            }
            if (ank != null)
            {
                ViewBag.ank = ank;
            }
            if (adi != null)
            {
                ViewBag.anketadi = adi.Soru?.SoruAdi ?? "Soru detayÄ±";
            }



            // Genel
            ViewBag.baslik1 = new List<string>();
            ViewBag.puan1 = new List<float>();
            ViewBag.adet1 = new List<int>();

            // Departman
            ViewBag.baslik2 = new List<string>();
            ViewBag.puan2 = new List<float>();
            ViewBag.adet2 = new List<int>();

            // Cinsiyet
            ViewBag.baslik3 = new List<string>();
            ViewBag.puan3 = new List<float>();
            ViewBag.adet3 = new List<int>();

            // EÄŸitim
            ViewBag.baslik4 = new List<string>();
            ViewBag.puan4 = new List<float>();
            ViewBag.adet4 = new List<int>();

            // YaÅŸ
            ViewBag.baslik5 = new List<string>();  // int yerine string yapalÄ±m â†’ chart iÃ§in daha kolay
            ViewBag.puan5 = new List<float>();
            ViewBag.adet5 = new List<int>();

            // Åehir
            ViewBag.baslik6 = new List<string>();
            ViewBag.puan6 = new List<float>();
            ViewBag.adet6 = new List<int>();

            // Åube
            ViewBag.baslik7 = new List<string>();
            ViewBag.puan7 = new List<float>();
            ViewBag.adet7 = new List<int>();

            // Ãœnvan
            ViewBag.baslik8 = new List<string>();
            ViewBag.puan8 = new List<float>();
            ViewBag.adet8 = new List<int>();

            // Yaka
            ViewBag.baslik9 = new List<string>();
            ViewBag.puan9 = new List<float>();
            ViewBag.adet9 = new List<int>();

            // YÃ¶netici
            ViewBag.baslik10 = new List<string>();
            ViewBag.puan10 = new List<float>();
            ViewBag.adet10 = new List<int>();
            ViewBag.yoneticiResim10 = new List<string>();
            var yoneticiResimSozlugu = YoneticiResimSozlugu(bul.AsEnumerable().Select(x => x.User?.UserYoneticisi));

            // Soru
            ViewBag.baslik11 = new List<string>();
            ViewBag.puan11 = new List<float>();
            ViewBag.adet11 = new List<int>();

            // Soru Grup
            ViewBag.baslik12 = new List<string>();
            ViewBag.puan12 = new List<float>();
            ViewBag.adet12 = new List<int>();


            foreach (var item in bul.GroupBy(x => x.Anket?.AnketAdi ?? "Ã‡alÄ±ÅŸma"))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerÃ§ek kiÅŸi adedi

                var c5 = item.Count(x => x.CevapPuan == 5);
                var c4 = item.Count(x => x.CevapPuan == 4);
                var c3 = item.Count(x => x.CevapPuan == 3);
                var c2 = item.Count(x => x.CevapPuan == 2);
                var c1 = item.Count(x => x.CevapPuan == 1);

                ViewBag.d5 = (float)c5 * 5 / 5 * 100;
                ViewBag.d4 = (float)c4 * 4 / 5 * 100;
                ViewBag.d3 = (float)c3 * 3 / 5 * 100;
                ViewBag.d2 = (float)c2 * 2 / 5 * 100;
                ViewBag.d1 = (float)c1 * 1 / 5 * 100;

                // PuanÄ± hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanÄ±nÄ± yÃ¼zdesini bulur
                var p1 = p2 / soru;

                ViewBag.baslik1.Add(item.Key);
                ViewBag.puan1.Add(p1);
                ViewBag.adet1.Add(kisiSayisi);

            }
            foreach (var item in bul.AsEnumerable().GroupBy(x => x.User?.Departman?.DepartmanAdi ?? "Tanımsız"))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerÃ§ek kiÅŸi adedi

                var c5 = item.Count(x => x.CevapPuan == 5);
                var c4 = item.Count(x => x.CevapPuan == 4);
                var c3 = item.Count(x => x.CevapPuan == 3);
                var c2 = item.Count(x => x.CevapPuan == 2);
                var c1 = item.Count(x => x.CevapPuan == 1);

                ViewBag.d5 = (float)c5 * 5 / 5 * 100;
                ViewBag.d4 = (float)c4 * 4 / 5 * 100;
                ViewBag.d3 = (float)c3 * 3 / 5 * 100;
                ViewBag.d2 = (float)c2 * 2 / 5 * 100;
                ViewBag.d1 = (float)c1 * 1 / 5 * 100;

                // PuanÄ± hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanÄ±nÄ± yÃ¼zdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik2.Add(item.Key);
                ViewBag.puan2.Add(p1);
                ViewBag.adet2.Add(kisiSayisi);

            }
            foreach (var item in bul.AsEnumerable().GroupBy(x => x.User?.Cinsiyet?.CinsiyetAdi ?? "Tanımsız"))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerÃ§ek kiÅŸi adedi

                var c5 = item.Count(x => x.CevapPuan == 5);
                var c4 = item.Count(x => x.CevapPuan == 4);
                var c3 = item.Count(x => x.CevapPuan == 3);
                var c2 = item.Count(x => x.CevapPuan == 2);
                var c1 = item.Count(x => x.CevapPuan == 1);

                ViewBag.d5 = (float)c5 * 5 / 5 * 100;
                ViewBag.d4 = (float)c4 * 4 / 5 * 100;
                ViewBag.d3 = (float)c3 * 3 / 5 * 100;
                ViewBag.d2 = (float)c2 * 2 / 5 * 100;
                ViewBag.d1 = (float)c1 * 1 / 5 * 100;

                // PuanÄ± hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanÄ±nÄ± yÃ¼zdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik3.Add(item.Key);
                ViewBag.puan3.Add(p1);
                ViewBag.adet3.Add(kisiSayisi);

            }
            foreach (var item in bul.AsEnumerable().GroupBy(x => x.User?.Egitim?.EgitimAdi ?? "Tanımsız"))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerÃ§ek kiÅŸi adedi

                var c5 = item.Count(x => x.CevapPuan == 5);
                var c4 = item.Count(x => x.CevapPuan == 4);
                var c3 = item.Count(x => x.CevapPuan == 3);
                var c2 = item.Count(x => x.CevapPuan == 2);
                var c1 = item.Count(x => x.CevapPuan == 1);

                ViewBag.d5 = (float)c5 * 5 / 5 * 100;
                ViewBag.d4 = (float)c4 * 4 / 5 * 100;
                ViewBag.d3 = (float)c3 * 3 / 5 * 100;
                ViewBag.d2 = (float)c2 * 2 / 5 * 100;
                ViewBag.d1 = (float)c1 * 1 / 5 * 100;

                // PuanÄ± hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanÄ±nÄ± yÃ¼zdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik4.Add(item.Key);
                ViewBag.puan4.Add(p1);
                ViewBag.adet4.Add(kisiSayisi);


            }
            var bugun = DateTime.Today;

            // Ã–nce boÅŸ listeleri hazÄ±rla
            ViewBag.baslik5 = new List<string>();
            ViewBag.puan5 = new List<float>();

            foreach (var item in bul.AsEnumerable()
                .Where(x => x.User?.UserDogumTar != null)
                .GroupBy(x => x.User.UserDogumTar.Value.Year))
            {
                var dogumYili = item.Key;
                var yas = bugun.Year - dogumYili;
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerÃ§ek kiÅŸi adedi


                // EÄŸer doÄŸum gÃ¼nÃ¼ bu yÄ±l daha gelmediyse yaÅŸÄ± 1 azalt
                var ilkKayit = item.FirstOrDefault()?.User?.UserDogumTar;
                if (ilkKayit != null && bugun.DayOfYear < ilkKayit.Value.DayOfYear)
                {
                    yas--;
                }

                // Ortalama puanÄ± hesapla (kiÅŸiye gÃ¶re normalize)
                var ortalama = item
                    .GroupBy(x => x.UserId)
                    .Select(g => g.Average(y => y.CevapPuan))
                    .Average();

                var ortalamaYuzde = ortalama * 20; // 5 Ã¼zerinden 100'e Ã§evirme

                // ğŸ”¹ Burada artÄ±k Add et
                ViewBag.baslik5.Add(yas.ToString());       // X ekseni â†’ yaÅŸ
                ViewBag.puan5.Add((float)ortalamaYuzde);   // Y ekseni â†’ puan
                ViewBag.adet5.Add(kisiSayisi);   // Y ekseni â†’ puan
            }

            foreach (var item in bul.AsEnumerable().GroupBy(x => x.User?.Sehir?.SehiarAdi ?? "Tanımsız"))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerÃ§ek kiÅŸi adedi

                var c5 = item.Count(x => x.CevapPuan == 5);
                var c4 = item.Count(x => x.CevapPuan == 4);
                var c3 = item.Count(x => x.CevapPuan == 3);
                var c2 = item.Count(x => x.CevapPuan == 2);
                var c1 = item.Count(x => x.CevapPuan == 1);

                ViewBag.d5 = (float)c5 * 5 / 5 * 100;
                ViewBag.d4 = (float)c4 * 4 / 5 * 100;
                ViewBag.d3 = (float)c3 * 3 / 5 * 100;
                ViewBag.d2 = (float)c2 * 2 / 5 * 100;
                ViewBag.d1 = (float)c1 * 1 / 5 * 100;

                // PuanÄ± hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanÄ±nÄ± yÃ¼zdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik6.Add(item.Key);
                ViewBag.puan6.Add(p1);
                ViewBag.adet6.Add(kisiSayisi);

            }
            foreach (var item in bul.AsEnumerable().GroupBy(x => x.User?.Sube?.SubeAdi ?? "Tanımsız"))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerÃ§ek kiÅŸi adedi

                var c5 = item.Count(x => x.CevapPuan == 5);
                var c4 = item.Count(x => x.CevapPuan == 4);
                var c3 = item.Count(x => x.CevapPuan == 3);
                var c2 = item.Count(x => x.CevapPuan == 2);
                var c1 = item.Count(x => x.CevapPuan == 1);

                ViewBag.d5 = (float)c5 * 5 / 5 * 100;
                ViewBag.d4 = (float)c4 * 4 / 5 * 100;
                ViewBag.d3 = (float)c3 * 3 / 5 * 100;
                ViewBag.d2 = (float)c2 * 2 / 5 * 100;
                ViewBag.d1 = (float)c1 * 1 / 5 * 100;

                // PuanÄ± hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanÄ±nÄ± yÃ¼zdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik7.Add(item.Key);
                ViewBag.puan7.Add(p1);
                ViewBag.adet7.Add(kisiSayisi);

            }
            foreach (var item in bul.AsEnumerable().GroupBy(x => x.User?.Unvan?.UnvanAdi ?? "Tanımsız"))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerÃ§ek kiÅŸi adedi

                var c5 = item.Count(x => x.CevapPuan == 5);
                var c4 = item.Count(x => x.CevapPuan == 4);
                var c3 = item.Count(x => x.CevapPuan == 3);
                var c2 = item.Count(x => x.CevapPuan == 2);
                var c1 = item.Count(x => x.CevapPuan == 1);

                ViewBag.d5 = (float)c5 * 5 / 5 * 100;
                ViewBag.d4 = (float)c4 * 4 / 5 * 100;
                ViewBag.d3 = (float)c3 * 3 / 5 * 100;
                ViewBag.d2 = (float)c2 * 2 / 5 * 100;
                ViewBag.d1 = (float)c1 * 1 / 5 * 100;

                // PuanÄ± hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanÄ±nÄ± yÃ¼zdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik8.Add(item.Key);
                ViewBag.puan8.Add(p1);
                ViewBag.adet8.Add(kisiSayisi);

            }
            foreach (var item in bul.AsEnumerable().GroupBy(x => x.User?.Yaka?.YakaAdi ?? "Tanımsız"))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerÃ§ek kiÅŸi adedi

                var c5 = item.Count(x => x.CevapPuan == 5);
                var c4 = item.Count(x => x.CevapPuan == 4);
                var c3 = item.Count(x => x.CevapPuan == 3);
                var c2 = item.Count(x => x.CevapPuan == 2);
                var c1 = item.Count(x => x.CevapPuan == 1);

                ViewBag.d5 = (float)c5 * 5 / 5 * 100;
                ViewBag.d4 = (float)c4 * 4 / 5 * 100;
                ViewBag.d3 = (float)c3 * 3 / 5 * 100;
                ViewBag.d2 = (float)c2 * 2 / 5 * 100;
                ViewBag.d1 = (float)c1 * 1 / 5 * 100;

                // PuanÄ± hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanÄ±nÄ± yÃ¼zdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik9.Add(item.Key);
                ViewBag.puan9.Add(p1);
                ViewBag.adet9.Add(kisiSayisi);

            }
            foreach (var item in bul.AsEnumerable().GroupBy(x => new
            {
                Id = x.User?.UserYoneticisi ?? 0,
                Ad = x.User?.Yonetici?.YoneticiAdi ?? "Tanımsız"
            }))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerÃ§ek kiÅŸi adedi

                var c5 = item.Count(x => x.CevapPuan == 5);
                var c4 = item.Count(x => x.CevapPuan == 4);
                var c3 = item.Count(x => x.CevapPuan == 3);
                var c2 = item.Count(x => x.CevapPuan == 2);
                var c1 = item.Count(x => x.CevapPuan == 1);

                ViewBag.d5 = (float)c5 * 5 / 5 * 100;
                ViewBag.d4 = (float)c4 * 4 / 5 * 100;
                ViewBag.d3 = (float)c3 * 3 / 5 * 100;
                ViewBag.d2 = (float)c2 * 2 / 5 * 100;
                ViewBag.d1 = (float)c1 * 1 / 5 * 100;

                // PuanÄ± hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanÄ±nÄ± yÃ¼zdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik10.Add(item.Key.Ad);
                ViewBag.puan10.Add(p1);
                ViewBag.adet10.Add(kisiSayisi);
                ViewBag.yoneticiResim10.Add(item.Key.Id > 0 && yoneticiResimSozlugu.TryGetValue(item.Key.Id, out var yoneticiResim) ? yoneticiResim : "");

            }
            foreach (var item in bul.Where(x => x.Soru != null).GroupBy(x => x.Soru.SoruAdi))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerÃ§ek kiÅŸi adedi

                var c5 = item.Count(x => x.CevapPuan == 5);
                var c4 = item.Count(x => x.CevapPuan == 4);
                var c3 = item.Count(x => x.CevapPuan == 3);
                var c2 = item.Count(x => x.CevapPuan == 2);
                var c1 = item.Count(x => x.CevapPuan == 1);

                ViewBag.d5 = (float)c5 * 5 / 5 * 100;
                ViewBag.d4 = (float)c4 * 4 / 5 * 100;
                ViewBag.d3 = (float)c3 * 3 / 5 * 100;
                ViewBag.d2 = (float)c2 * 2 / 5 * 100;
                ViewBag.d1 = (float)c1 * 1 / 5 * 100;

                // PuanÄ± hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanÄ±nÄ± yÃ¼zdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik11.Add(item.Key);
                ViewBag.puan11.Add(p1);
                ViewBag.adet11.Add(kisiSayisi);

            }
            foreach (var item in bul.GroupBy(x => x.SoruGrup?.SoruGrupAdi ?? x.Soru?.SoruGrup?.SoruGrupAdi ?? "Grupsuz"))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerÃ§ek kiÅŸi adedi

                var c5 = item.Count(x => x.CevapPuan == 5);
                var c4 = item.Count(x => x.CevapPuan == 4);
                var c3 = item.Count(x => x.CevapPuan == 3);
                var c2 = item.Count(x => x.CevapPuan == 2);
                var c1 = item.Count(x => x.CevapPuan == 1);

                ViewBag.d5 = (float)c5 * 5 / 5 * 100;
                ViewBag.d4 = (float)c4 * 4 / 5 * 100;
                ViewBag.d3 = (float)c3 * 3 / 5 * 100;
                ViewBag.d2 = (float)c2 * 2 / 5 * 100;
                ViewBag.d1 = (float)c1 * 1 / 5 * 100;

                // PuanÄ± hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanÄ±nÄ± yÃ¼zdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik12.Add(item.Key);
                ViewBag.puan12.Add(p1);
                ViewBag.adet12.Add(kisiSayisi);
            }

            ViewBag.YoneticiResimleri = yoneticiResimSozlugu;

            return View("AnketSoruGrupIndex", bul);
        }
        public ActionResult AnketSoruGrupIndex(int id, int? ank)
        {
            if (Session["id"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }
            if (Session["admin"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }

            if (!AnketCalismaAlanindaMi(ank))
            {
                return RedirectToAction("AnketIndex");
            }

            var bul = RaporHavuzSorgusu(ank)
                .Where(x => x.SoruGrupId == id)
                .ToList();
            var adi = bul.FirstOrDefault();
            var ankadi = db.Anket.AsNoTracking().FirstOrDefault(x => x.AnketId == ank);
            if (ankadi != null)
            {
                ViewBag.adi = ankadi.AnketAdi;
                ViewBag.id = ankadi.AnketId;
                ViewBag.sinav = SinavTurundeMi(ankadi);

            }
            if (ank != null)
            {
                ViewBag.ank = ank;
            }
            if (adi != null)
            {
                ViewBag.anketadi = adi.SoruGrup.SoruGrupAdi;
            }



            // Genel
            ViewBag.baslik1 = new List<string>();
            ViewBag.puan1 = new List<float>();
            ViewBag.adet1 = new List<int>();

            // Departman
            ViewBag.baslik2 = new List<string>();
            ViewBag.puan2 = new List<float>();
            ViewBag.adet2 = new List<int>();

            // Cinsiyet
            ViewBag.baslik3 = new List<string>();
            ViewBag.puan3 = new List<float>();
            ViewBag.adet3 = new List<int>();

            // EÄŸitim
            ViewBag.baslik4 = new List<string>();
            ViewBag.puan4 = new List<float>();
            ViewBag.adet4 = new List<int>();

            // YaÅŸ
            ViewBag.baslik5 = new List<string>();  // int yerine string yapalÄ±m â†’ chart iÃ§in daha kolay
            ViewBag.puan5 = new List<float>();
            ViewBag.adet5 = new List<int>();

            // Åehir
            ViewBag.baslik6 = new List<string>();
            ViewBag.puan6 = new List<float>();
            ViewBag.adet6 = new List<int>();

            // Åube
            ViewBag.baslik7 = new List<string>();
            ViewBag.puan7 = new List<float>();
            ViewBag.adet7 = new List<int>();

            // Ãœnvan
            ViewBag.baslik8 = new List<string>();
            ViewBag.puan8 = new List<float>();
            ViewBag.adet8 = new List<int>();

            // Yaka
            ViewBag.baslik9 = new List<string>();
            ViewBag.puan9 = new List<float>();
            ViewBag.adet9 = new List<int>();

            // YÃ¶netici
            ViewBag.baslik10 = new List<string>();
            ViewBag.puan10 = new List<float>();
            ViewBag.adet10 = new List<int>();
            ViewBag.yoneticiResim10 = new List<string>();
            var yoneticiResimSozlugu = YoneticiResimSozlugu(bul.AsEnumerable().Select(x => x.User?.UserYoneticisi));

            // Soru
            ViewBag.baslik11 = new List<string>();
            ViewBag.puan11 = new List<float>();
            ViewBag.adet11 = new List<int>();

            // Soru Grup
            ViewBag.baslik12 = new List<string>();
            ViewBag.puan12 = new List<float>();
            ViewBag.adet12 = new List<int>();


            foreach (var item in bul.GroupBy(x => x.Anket.AnketAdi))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerÃ§ek kiÅŸi adedi

                var c5 = item.Count(x => x.CevapPuan == 5);
                var c4 = item.Count(x => x.CevapPuan == 4);
                var c3 = item.Count(x => x.CevapPuan == 3);
                var c2 = item.Count(x => x.CevapPuan == 2);
                var c1 = item.Count(x => x.CevapPuan == 1);

                ViewBag.d5 = (float)c5 * 5 / 5 * 100;
                ViewBag.d4 = (float)c4 * 4 / 5 * 100;
                ViewBag.d3 = (float)c3 * 3 / 5 * 100;
                ViewBag.d2 = (float)c2 * 2 / 5 * 100;
                ViewBag.d1 = (float)c1 * 1 / 5 * 100;

                // PuanÄ± hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanÄ±nÄ± yÃ¼zdesini bulur
                var p1 = p2 / soru;

                ViewBag.baslik1.Add(item.Key);
                ViewBag.puan1.Add(p1);
                ViewBag.adet1.Add(kisiSayisi);

            }
            foreach (var item in bul.AsEnumerable().GroupBy(x => x.User?.Departman?.DepartmanAdi ?? "Tanımsız"))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerÃ§ek kiÅŸi adedi

                var c5 = item.Count(x => x.CevapPuan == 5);
                var c4 = item.Count(x => x.CevapPuan == 4);
                var c3 = item.Count(x => x.CevapPuan == 3);
                var c2 = item.Count(x => x.CevapPuan == 2);
                var c1 = item.Count(x => x.CevapPuan == 1);

                ViewBag.d5 = (float)c5 * 5 / 5 * 100;
                ViewBag.d4 = (float)c4 * 4 / 5 * 100;
                ViewBag.d3 = (float)c3 * 3 / 5 * 100;
                ViewBag.d2 = (float)c2 * 2 / 5 * 100;
                ViewBag.d1 = (float)c1 * 1 / 5 * 100;

                // PuanÄ± hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanÄ±nÄ± yÃ¼zdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik2.Add(item.Key);
                ViewBag.puan2.Add(p1);
                ViewBag.adet2.Add(kisiSayisi);

            }
            foreach (var item in bul.AsEnumerable().GroupBy(x => x.User?.Cinsiyet?.CinsiyetAdi ?? "Tanımsız"))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerÃ§ek kiÅŸi adedi

                var c5 = item.Count(x => x.CevapPuan == 5);
                var c4 = item.Count(x => x.CevapPuan == 4);
                var c3 = item.Count(x => x.CevapPuan == 3);
                var c2 = item.Count(x => x.CevapPuan == 2);
                var c1 = item.Count(x => x.CevapPuan == 1);

                ViewBag.d5 = (float)c5 * 5 / 5 * 100;
                ViewBag.d4 = (float)c4 * 4 / 5 * 100;
                ViewBag.d3 = (float)c3 * 3 / 5 * 100;
                ViewBag.d2 = (float)c2 * 2 / 5 * 100;
                ViewBag.d1 = (float)c1 * 1 / 5 * 100;

                // PuanÄ± hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanÄ±nÄ± yÃ¼zdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik3.Add(item.Key);
                ViewBag.puan3.Add(p1);
                ViewBag.adet3.Add(kisiSayisi);

            }
            foreach (var item in bul.AsEnumerable().GroupBy(x => x.User?.Egitim?.EgitimAdi ?? "Tanımsız"))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerÃ§ek kiÅŸi adedi

                var c5 = item.Count(x => x.CevapPuan == 5);
                var c4 = item.Count(x => x.CevapPuan == 4);
                var c3 = item.Count(x => x.CevapPuan == 3);
                var c2 = item.Count(x => x.CevapPuan == 2);
                var c1 = item.Count(x => x.CevapPuan == 1);

                ViewBag.d5 = (float)c5 * 5 / 5 * 100;
                ViewBag.d4 = (float)c4 * 4 / 5 * 100;
                ViewBag.d3 = (float)c3 * 3 / 5 * 100;
                ViewBag.d2 = (float)c2 * 2 / 5 * 100;
                ViewBag.d1 = (float)c1 * 1 / 5 * 100;

                // PuanÄ± hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanÄ±nÄ± yÃ¼zdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik4.Add(item.Key);
                ViewBag.puan4.Add(p1);
                ViewBag.adet4.Add(kisiSayisi);


            }
            var bugun = DateTime.Today;

            // Ã–nce boÅŸ listeleri hazÄ±rla
            ViewBag.baslik5 = new List<string>();
            ViewBag.puan5 = new List<float>();

            foreach (var item in bul.AsEnumerable()
                .Where(x => x.User?.UserDogumTar != null)
                .GroupBy(x => x.User.UserDogumTar.Value.Year))
            {
                var dogumYili = item.Key;
                var yas = bugun.Year - dogumYili;
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerÃ§ek kiÅŸi adedi


                // EÄŸer doÄŸum gÃ¼nÃ¼ bu yÄ±l daha gelmediyse yaÅŸÄ± 1 azalt
                var ilkKayit = item.FirstOrDefault()?.User?.UserDogumTar;
                if (ilkKayit != null && bugun.DayOfYear < ilkKayit.Value.DayOfYear)
                {
                    yas--;
                }

                // Ortalama puanÄ± hesapla (kiÅŸiye gÃ¶re normalize)
                var ortalama = item
                    .GroupBy(x => x.UserId)
                    .Select(g => g.Average(y => y.CevapPuan))
                    .Average();

                var ortalamaYuzde = ortalama * 20; // 5 Ã¼zerinden 100'e Ã§evirme

                // ğŸ”¹ Burada artÄ±k Add et
                ViewBag.baslik5.Add(yas.ToString());       // X ekseni â†’ yaÅŸ
                ViewBag.puan5.Add((float)ortalamaYuzde);   // Y ekseni â†’ puan
                ViewBag.adet5.Add(kisiSayisi);   // Y ekseni â†’ puan
            }

            foreach (var item in bul.AsEnumerable().GroupBy(x => x.User?.Sehir?.SehiarAdi ?? "Tanımsız"))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerÃ§ek kiÅŸi adedi

                var c5 = item.Count(x => x.CevapPuan == 5);
                var c4 = item.Count(x => x.CevapPuan == 4);
                var c3 = item.Count(x => x.CevapPuan == 3);
                var c2 = item.Count(x => x.CevapPuan == 2);
                var c1 = item.Count(x => x.CevapPuan == 1);

                ViewBag.d5 = (float)c5 * 5 / 5 * 100;
                ViewBag.d4 = (float)c4 * 4 / 5 * 100;
                ViewBag.d3 = (float)c3 * 3 / 5 * 100;
                ViewBag.d2 = (float)c2 * 2 / 5 * 100;
                ViewBag.d1 = (float)c1 * 1 / 5 * 100;

                // PuanÄ± hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanÄ±nÄ± yÃ¼zdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik6.Add(item.Key);
                ViewBag.puan6.Add(p1);
                ViewBag.adet6.Add(kisiSayisi);

            }
            foreach (var item in bul.AsEnumerable().GroupBy(x => x.User?.Sube?.SubeAdi ?? "Tanımsız"))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerÃ§ek kiÅŸi adedi

                var c5 = item.Count(x => x.CevapPuan == 5);
                var c4 = item.Count(x => x.CevapPuan == 4);
                var c3 = item.Count(x => x.CevapPuan == 3);
                var c2 = item.Count(x => x.CevapPuan == 2);
                var c1 = item.Count(x => x.CevapPuan == 1);

                ViewBag.d5 = (float)c5 * 5 / 5 * 100;
                ViewBag.d4 = (float)c4 * 4 / 5 * 100;
                ViewBag.d3 = (float)c3 * 3 / 5 * 100;
                ViewBag.d2 = (float)c2 * 2 / 5 * 100;
                ViewBag.d1 = (float)c1 * 1 / 5 * 100;

                // PuanÄ± hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanÄ±nÄ± yÃ¼zdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik7.Add(item.Key);
                ViewBag.puan7.Add(p1);
                ViewBag.adet7.Add(kisiSayisi);

            }
            foreach (var item in bul.AsEnumerable().GroupBy(x => x.User?.Unvan?.UnvanAdi ?? "Tanımsız"))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerÃ§ek kiÅŸi adedi

                var c5 = item.Count(x => x.CevapPuan == 5);
                var c4 = item.Count(x => x.CevapPuan == 4);
                var c3 = item.Count(x => x.CevapPuan == 3);
                var c2 = item.Count(x => x.CevapPuan == 2);
                var c1 = item.Count(x => x.CevapPuan == 1);

                ViewBag.d5 = (float)c5 * 5 / 5 * 100;
                ViewBag.d4 = (float)c4 * 4 / 5 * 100;
                ViewBag.d3 = (float)c3 * 3 / 5 * 100;
                ViewBag.d2 = (float)c2 * 2 / 5 * 100;
                ViewBag.d1 = (float)c1 * 1 / 5 * 100;

                // PuanÄ± hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanÄ±nÄ± yÃ¼zdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik8.Add(item.Key);
                ViewBag.puan8.Add(p1);
                ViewBag.adet8.Add(kisiSayisi);

            }
            foreach (var item in bul.AsEnumerable().GroupBy(x => x.User?.Yaka?.YakaAdi ?? "Tanımsız"))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerÃ§ek kiÅŸi adedi

                var c5 = item.Count(x => x.CevapPuan == 5);
                var c4 = item.Count(x => x.CevapPuan == 4);
                var c3 = item.Count(x => x.CevapPuan == 3);
                var c2 = item.Count(x => x.CevapPuan == 2);
                var c1 = item.Count(x => x.CevapPuan == 1);

                ViewBag.d5 = (float)c5 * 5 / 5 * 100;
                ViewBag.d4 = (float)c4 * 4 / 5 * 100;
                ViewBag.d3 = (float)c3 * 3 / 5 * 100;
                ViewBag.d2 = (float)c2 * 2 / 5 * 100;
                ViewBag.d1 = (float)c1 * 1 / 5 * 100;

                // PuanÄ± hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanÄ±nÄ± yÃ¼zdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik9.Add(item.Key);
                ViewBag.puan9.Add(p1);
                ViewBag.adet9.Add(kisiSayisi);

            }
            foreach (var item in bul.AsEnumerable().GroupBy(x => new
            {
                Id = x.User?.UserYoneticisi ?? 0,
                Ad = x.User?.Yonetici?.YoneticiAdi ?? "Tanımsız"
            }))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerÃ§ek kiÅŸi adedi

                var c5 = item.Count(x => x.CevapPuan == 5);
                var c4 = item.Count(x => x.CevapPuan == 4);
                var c3 = item.Count(x => x.CevapPuan == 3);
                var c2 = item.Count(x => x.CevapPuan == 2);
                var c1 = item.Count(x => x.CevapPuan == 1);

                ViewBag.d5 = (float)c5 * 5 / 5 * 100;
                ViewBag.d4 = (float)c4 * 4 / 5 * 100;
                ViewBag.d3 = (float)c3 * 3 / 5 * 100;
                ViewBag.d2 = (float)c2 * 2 / 5 * 100;
                ViewBag.d1 = (float)c1 * 1 / 5 * 100;

                // PuanÄ± hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanÄ±nÄ± yÃ¼zdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik10.Add(item.Key.Ad);
                ViewBag.puan10.Add(p1);
                ViewBag.adet10.Add(kisiSayisi);
                ViewBag.yoneticiResim10.Add(item.Key.Id > 0 && yoneticiResimSozlugu.TryGetValue(item.Key.Id, out var yoneticiResim) ? yoneticiResim : "");

            }
            foreach (var item in bul.GroupBy(x => x.Soru.SoruAdi))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerÃ§ek kiÅŸi adedi

                var c5 = item.Count(x => x.CevapPuan == 5);
                var c4 = item.Count(x => x.CevapPuan == 4);
                var c3 = item.Count(x => x.CevapPuan == 3);
                var c2 = item.Count(x => x.CevapPuan == 2);
                var c1 = item.Count(x => x.CevapPuan == 1);

                ViewBag.d5 = (float)c5 * 5 / 5 * 100;
                ViewBag.d4 = (float)c4 * 4 / 5 * 100;
                ViewBag.d3 = (float)c3 * 3 / 5 * 100;
                ViewBag.d2 = (float)c2 * 2 / 5 * 100;
                ViewBag.d1 = (float)c1 * 1 / 5 * 100;

                // PuanÄ± hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanÄ±nÄ± yÃ¼zdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik11.Add(item.Key);
                ViewBag.puan11.Add(p1);
                ViewBag.adet11.Add(kisiSayisi);

            }
            foreach (var item in bul.GroupBy(x => x.SoruGrup.SoruGrupAdi))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerÃ§ek kiÅŸi adedi

                var c5 = item.Count(x => x.CevapPuan == 5);
                var c4 = item.Count(x => x.CevapPuan == 4);
                var c3 = item.Count(x => x.CevapPuan == 3);
                var c2 = item.Count(x => x.CevapPuan == 2);
                var c1 = item.Count(x => x.CevapPuan == 1);

                ViewBag.d5 = (float)c5 * 5 / 5 * 100;
                ViewBag.d4 = (float)c4 * 4 / 5 * 100;
                ViewBag.d3 = (float)c3 * 3 / 5 * 100;
                ViewBag.d2 = (float)c2 * 2 / 5 * 100;
                ViewBag.d1 = (float)c1 * 1 / 5 * 100;

                // PuanÄ± hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanÄ±nÄ± yÃ¼zdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik12.Add(item.Key);
                ViewBag.puan12.Add(p1);
                ViewBag.adet12.Add(kisiSayisi);
            }

            ViewBag.YoneticiResimleri = yoneticiResimSozlugu;

            return View(bul);
        }
        public ActionResult AnketAdIndex()
        {
            if (Session["id"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }
            if (Session["admin"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }

            return View(CalismaAlaniAnketleri());
        }
        public ActionResult AnketAdCreate()
        {
            if (Session["id"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }
            if (Session["admin"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }
            return RedirectToAction("AssessmentWizard");
        }
        [ValidateAntiForgeryToken()]
        [HttpPost]
        public ActionResult AnketAdCreate(Anket dgskn, DateTime? YayinBaslangicTarihi, DateTime? YayinBitisTarihi, string KatilimYontemi)
        {
            if (Session["id"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }
            if (Session["admin"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }

            if (YayinBaslangicTarihi.HasValue
                && YayinBitisTarihi.HasValue
                && YayinBaslangicTarihi.Value >= YayinBitisTarihi.Value)
            {
                ModelState.AddModelError("YayinBitisTarihi", "Bitis tarihi baslangictan sonra olmali.");
            }

            if (string.IsNullOrWhiteSpace(dgskn.AnketAdi))
            {
                ModelState.AddModelError("AnketAdi", "Calisma adi zorunlu.");
            }
            else if (CalismaAlaniAyniAnketAdiVar(dgskn.AnketAdi))
            {
                ModelState.AddModelError("AnketAdi", "Bu isimde bir calisma zaten var. Rapor ve katilim takibi karismamasi icin farkli bir ad kullanin.");
            }

            if (!ModelState.IsValid)
            {
                ViewBag.YayinBaslangicLocal = DateTimeLocalValue(YayinBaslangicTarihi);
                ViewBag.YayinBitisLocal = DateTimeLocalValue(YayinBitisTarihi);
                ViewBag.KatilimYontemi = NormalizeKatilimYontemi(KatilimYontemi);
                return View(dgskn);
            }

            if (!PaketKullanimKontrolu.AktifAnketEklenebilirMi(db, AktifCalismaAlaniId(), out var paketLimitMesaji))
            {
                ModelState.AddModelError("", paketLimitMesaji);
                ViewBag.YayinBaslangicLocal = DateTimeLocalValue(YayinBaslangicTarihi);
                ViewBag.YayinBitisLocal = DateTimeLocalValue(YayinBitisTarihi);
                ViewBag.KatilimYontemi = NormalizeKatilimYontemi(KatilimYontemi);
                return View(dgskn);
            }

            try
            {
                db.Anket.Add(dgskn);
                db.SaveChanges();
                var calismaAlaniId = AktifCalismaAlaniId();
                if (calismaAlaniId.HasValue)
                {
                    db.Database.ExecuteSqlCommand(
                        @"UPDATE dbo.Anket
                          SET CalismaAlaniId = @p0,
                              SahipPersonelId = @p1,
                              YayinDurumu = N'Taslak',
                              OlusturmaTarihi = GETDATE()
                          WHERE AnketId = @p2",
                        calismaAlaniId.Value,
                        Convert.ToInt32(Session["id"]),
                        dgskn.AnketId);
                }

                YayinAyarlariniKaydet(dgskn.AnketId, YayinBaslangicTarihi, YayinBitisTarihi);
                KatilimYonteminiKaydet(dgskn.AnketId, KatilimYontemi);
                return RedirectToAction("AnketAdIndex");

            }
            catch
            {
                ViewBag.YayinBaslangicLocal = DateTimeLocalValue(YayinBaslangicTarihi);
                ViewBag.YayinBitisLocal = DateTimeLocalValue(YayinBitisTarihi);
                ViewBag.KatilimYontemi = NormalizeKatilimYontemi(KatilimYontemi);
                return View(dgskn);
            }
        }
        public ActionResult AnketAdEdit(int id, string returnUrl = null)
        {
            if (Session["id"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }
            if (Session["admin"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }

            var anket = CalismaAlaniAnketGetir(id);
            if (anket == null) return NotFound();

            PrepareAnketAdLookups(id);
            ViewBag.ReturnUrl = CalismaAlaniDonusAdresi(returnUrl);

            return View(anket);
        }

        private void PrepareAnketAdLookups(int? anketId = null, DateTime? yayinBaslangic = null, DateTime? yayinBitis = null, bool postedValues = false)
        {
            ViewBag.DepartmanList = db.Departman.ToList();
            ViewBag.SehirList = db.Sehir.ToList();
            ViewBag.SubeList = db.Sube.ToList();
            ViewBag.UnvanList = db.Unvan.ToList();
            if (anketId.HasValue)
            {
                PrepareAnketYayinViewBag(anketId.Value, yayinBaslangic, yayinBitis, postedValues);
            }
            else
            {
                ViewBag.YayinBaslangicLocal = DateTimeLocalValue(yayinBaslangic);
                ViewBag.YayinBitisLocal = DateTimeLocalValue(yayinBitis);
            }

            if (ViewBag.KatilimYontemi == null)
            {
                ViewBag.KatilimYontemi = anketId.HasValue
                    ? KatilimYontemiGetir(anketId.Value)
                    : KatilimYontemiHerkeseAcik;
            }

            if (anketId.HasValue)
            {
                PrepareAnketPaylasimViewBag(anketId.Value);
            }
        }

        private void PrepareAnketPaylasimViewBag(int anketId)
        {
            var token = EnsureAnketPaylasimToken(anketId);
            var katilimYontemi = NormalizeKatilimYontemi(Convert.ToString(ViewBag.KatilimYontemi ?? KatilimYontemiGetir(anketId)));

            ViewBag.PaylasimToken = token;
            ViewBag.PaylasimUrl = string.IsNullOrWhiteSpace(token) ? string.Empty : KatilimPaylasimUrl(anketId);
            ViewBag.PaylasimQrUrl = string.IsNullOrWhiteSpace(token) ? string.Empty : Url.Action("KatilimQr", "Home", new { token });
            ViewBag.KatilimYontemiEtiketi = KatilimYontemiEtiketi(katilimYontemi);
            ViewBag.PaylasimAciklamasi = KatilimYontemiPaylasimAciklamasi(katilimYontemi);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult AnketAdEdit(Anket dgskn, IFormFile ImzaDosyasi, DateTime? YayinBaslangicTarihi, DateTime? YayinBitisTarihi, string KatilimYontemi, string returnUrl = null)
        {
            if (Session["id"] == null || Session["admin"] == null)
            {
                return RedirectToAction("Giris", "Home");
            }

            var katilimYontemi = NormalizeKatilimYontemi(KatilimYontemi);
            var donusAdresi = CalismaAlaniDonusAdresi(returnUrl);

            try
            {
                var anket = CalismaAlaniAnketGetir(dgskn.AnketId);
                if (anket == null) return NotFound();
                var isExamSubmission = dgskn.Sinav == true;

                var gonderilenAnketAdi = (dgskn.AnketAdi ?? string.Empty).Trim();
                var mevcutAnketAdi = (anket.AnketAdi ?? string.Empty).Trim();
                var anketAdiDegisti = !string.Equals(gonderilenAnketAdi, mevcutAnketAdi, StringComparison.CurrentCultureIgnoreCase);

                if (string.IsNullOrWhiteSpace(gonderilenAnketAdi))
                {
                    ModelState.AddModelError("AnketAdi", "Anket adi zorunlu.");
                }
                else if (anketAdiDegisti && CalismaAlaniAyniAnketAdiVar(gonderilenAnketAdi, anket.AnketId))
                {
                    ModelState.AddModelError("AnketAdi", "Bu adda baÅŸka bir Ã§alÄ±ÅŸma var. YalnÄ±zca Ã§alÄ±ÅŸma adÄ±nÄ± deÄŸiÅŸtirmek istiyorsanÄ±z farklÄ± bir ad kullanÄ±n.");
                }

                if (string.IsNullOrWhiteSpace(dgskn.EgitimVeren))
                {
                    ModelState.AddModelError("EgitimVeren", "Egitim duzenleyen bilgisi zorunlu.");
                }

                if (isExamSubmission && (!dgskn.Zaman.HasValue || dgskn.Zaman.Value <= 0))
                {
                    ModelState.AddModelError("Zaman", "Sinav suresi dakika cinsinden zorunlu.");
                }

                if (isExamSubmission
                    && (!dgskn.SertifikaNotu.HasValue || dgskn.SertifikaNotu.Value < 0 || dgskn.SertifikaNotu.Value > 100))
                {
                    ModelState.AddModelError("SertifikaNotu", "Sertifika/gecme notu 0 ile 100 arasinda olmali.");
                }

                if (YayinBaslangicTarihi.HasValue
                    && YayinBitisTarihi.HasValue
                    && YayinBaslangicTarihi.Value >= YayinBitisTarihi.Value)
                {
                    ModelState.AddModelError("YayinBitisTarihi", "Bitis tarihi baslangictan sonra olmali.");
                }

                if (!ModelState.IsValid)
                {
                    ViewBag.KatilimYontemi = katilimYontemi;
                    PrepareAnketAdLookups(anket.AnketId, YayinBaslangicTarihi, YayinBitisTarihi, true);
                    ViewBag.ReturnUrl = donusAdresi;
                    dgskn.Imza = anket.Imza;
                    return View(dgskn);
                }

                // AlanlarÄ± gÃ¼ncelle
                anket.AnketAdi = gonderilenAnketAdi;
                anket.EgitimVeren = dgskn.EgitimVeren;
                anket.Pasif = dgskn.Pasif;
                anket.Tanimsiz = dgskn.Tanimsiz;
                anket.Sinav = isExamSubmission;
                anket.Link = isExamSubmission ? dgskn.Link : null;
                anket.Zaman = isExamSubmission ? dgskn.Zaman : null;
                anket.Sonuc = dgskn.Sonuc;
                anket.SertifikaNotu = isExamSubmission ? dgskn.SertifikaNotu : null;

                anket.DepartmanId = dgskn.DepartmanId;
                anket.SehirId = dgskn.SehirId;
                anket.SubeId = dgskn.SubeId;
                anket.UnvanId = dgskn.UnvanId;

                // Ä°mza iÅŸlemi
                if (ImzaDosyasi != null && ImzaDosyasi.Length > 0)
                {
                    string fileName = Guid.NewGuid() + Path.GetExtension(ImzaDosyasi.FileName);
                    string path = Path.Combine(MapPath("~/uploads/imzalar"), fileName);
                    ImzaDosyasi.SaveAs(path);

                    anket.Imza = "/uploads/imzalar/" + fileName;
                }

                db.SaveChanges();
                YayinAyarlariniKaydet(anket.AnketId, YayinBaslangicTarihi, YayinBitisTarihi);
                KatilimYonteminiKaydet(anket.AnketId, katilimYontemi);

                TempData["AyarKayitMesaji"] = "Ã‡alÄ±ÅŸma ayarlarÄ± kaydedildi.";
                return RedirectToAction("AnketAdEdit", new { id = anket.AnketId, paylas = 1, returnUrl = donusAdresi });
            }
            catch
            {
                ViewBag.KatilimYontemi = katilimYontemi;
                PrepareAnketAdLookups(dgskn.AnketId, YayinBaslangicTarihi, YayinBitisTarihi, true);
                ViewBag.ReturnUrl = donusAdresi;
                return View(dgskn);
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult KatilimYontemiGuncelle(int anketId, string katilimYontemi)
        {
            if (Session["id"] == null || Session["admin"] == null)
            {
                return Json(new { success = false, message = "Oturum sureniz doldu. Lutfen tekrar giris yapin." });
            }

            var anket = CalismaAlaniAnketGetir(anketId);
            if (anket == null)
            {
                return Json(new { success = false, message = "Calisma bulunamadi." });
            }

            var normalized = NormalizeKatilimYontemi(katilimYontemi);
            if (!KatilimYonteminiKaydet(anket.AnketId, normalized))
            {
                return Json(new { success = false, message = "Katilim yontemi kaydedilemedi." });
            }

            return Json(new
            {
                success = true,
                katilimYontemi = normalized,
                label = KatilimYontemiEtiketi(normalized),
                subtitle = KatilimYontemiPaylasimAciklamasi(normalized)
            });
        }

        public ActionResult SertifikaWizard(int id, string returnUrl = null)
        {
            if (Session["id"] == null || Session["admin"] == null)
            {
                return RedirectToAction("Giris", "Home");
            }

            var anket = CalismaAlaniAnketGetir(id);
            if (anket == null) return NotFound();

            var ayar = SertifikaAyariniGetir(id);
            var model = new SertifikaWizardForm
            {
                AnketId = anket.AnketId,
                AnketAdi = anket.AnketAdi,
                ReturnUrl = CalismaAlaniDonusAdresi(returnUrl),
                Sinav = anket.Sinav == true,
                SertifikaAktif = ayar?.SertifikaAktif == true,
                SertifikaKatilimciErisimi = ayar?.SertifikaKatilimciErisimi != false,
                SertifikaVerilisZamani = NormalizeSertifikaZamani(ayar?.SertifikaVerilisZamani),
                SertifikaNotu = anket.SertifikaNotu ?? 70,
                EgitimVeren = anket.EgitimVeren,
                SertifikaBaslik = ayar?.SertifikaBaslik ?? "KatÄ±lÄ±m SertifikasÄ±",
                SertifikaMetni = ayar?.SertifikaMetni,
                SertifikaTema = NormalizeSertifikaTema(ayar?.SertifikaTema),
                SertifikaLogo = ayar?.SertifikaLogo,
                SertifikaVurguRengi = NormalizeSertifikaRengi(ayar?.SertifikaVurguRengi),
                SertifikaCerceve = NormalizeSertifikaCerceve(ayar?.SertifikaCerceve),
                SertifikaFont = NormalizeSertifikaFont(ayar?.SertifikaFont),
                SertifikaYaziPunto = NormalizeSertifikaPunto(ayar?.SertifikaYaziPunto, 17, 11, 28),
                SertifikaBaslikPunto = NormalizeSertifikaPunto(ayar?.SertifikaBaslikPunto, 44, 24, 72),
                Imza = anket.Imza
            };

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult SertifikaWizard(SertifikaWizardForm form, IFormFile ImzaDosyasi, IFormFile LogoDosyasi)
        {
            if (Session["id"] == null || Session["admin"] == null)
            {
                return RedirectToAction("Giris", "Home");
            }

            var anket = CalismaAlaniAnketGetir(form.AnketId);
            if (anket == null) return NotFound();

            form.ReturnUrl = CalismaAlaniDonusAdresi(form.ReturnUrl);
            form.AnketAdi = anket.AnketAdi;
            form.Sinav = anket.Sinav == true;
            form.SertifikaVerilisZamani = NormalizeSertifikaZamani(form.SertifikaVerilisZamani);
            form.SertifikaTema = NormalizeSertifikaTema(form.SertifikaTema);
            form.SertifikaVurguRengi = NormalizeSertifikaRengi(form.SertifikaVurguRengi);
            form.SertifikaCerceve = NormalizeSertifikaCerceve(form.SertifikaCerceve);
            form.SertifikaFont = NormalizeSertifikaFont(form.SertifikaFont);
            form.SertifikaYaziPunto = NormalizeSertifikaPunto(form.SertifikaYaziPunto, 17, 11, 28);
            form.SertifikaBaslikPunto = NormalizeSertifikaPunto(form.SertifikaBaslikPunto, 44, 24, 72);
            var mevcutSertifikaAyar = SertifikaAyariniGetir(form.AnketId);

            if (form.SertifikaAktif)
            {
                if (!form.SertifikaNotu.HasValue || form.SertifikaNotu.Value < 0 || form.SertifikaNotu.Value > 100)
                {
                    ModelState.AddModelError("SertifikaNotu", "GeÃ§me notu 0 ile 100 arasÄ±nda olmalÄ±.");
                }

                if (string.IsNullOrWhiteSpace(form.EgitimVeren))
                {
                    ModelState.AddModelError("EgitimVeren", "Sertifikada gÃ¶rÃ¼necek dÃ¼zenleyen zorunlu.");
                }
            }

            if (string.IsNullOrWhiteSpace(form.SertifikaBaslik))
            {
                form.SertifikaBaslik = "KatÄ±lÄ±m SertifikasÄ±";
            }

            if (ImzaDosyasi != null && ImzaDosyasi.Length > 0 && !SertifikaGorseliUzantisiGecerli(ImzaDosyasi.FileName))
            {
                ModelState.AddModelError("ImzaDosyasi", "Imza icin PNG, JPG veya WEBP dosyasi secin.");
            }

            if (LogoDosyasi != null && LogoDosyasi.Length > 0 && !SertifikaGorseliUzantisiGecerli(LogoDosyasi.FileName))
            {
                ModelState.AddModelError("LogoDosyasi", "Logo icin PNG, JPG veya WEBP dosyasi secin.");
            }

            if (!ModelState.IsValid)
            {
                form.Imza = anket.Imza;
                form.SertifikaLogo = string.IsNullOrWhiteSpace(form.SertifikaLogo) ? mevcutSertifikaAyar?.SertifikaLogo : form.SertifikaLogo;
                return View(form);
            }

            anket.Sonuc = form.SertifikaAktif;
            anket.SertifikaNotu = form.SertifikaAktif ? form.SertifikaNotu : null;
            anket.EgitimVeren = form.EgitimVeren;

            if (ImzaDosyasi != null && ImzaDosyasi.Length > 0)
            {
                var uploadDir = MapPath("~/uploads/imzalar");
                Directory.CreateDirectory(uploadDir);
                var fileName = Guid.NewGuid() + Path.GetExtension(ImzaDosyasi.FileName);
                var path = Path.Combine(uploadDir, fileName);
                ImzaDosyasi.SaveAs(path);
                anket.Imza = "/uploads/imzalar/" + fileName;
            }

            if (LogoDosyasi != null && LogoDosyasi.Length > 0)
            {
                var uploadDir = MapPath("~/uploads/sertifika-logolar");
                Directory.CreateDirectory(uploadDir);
                var fileName = Guid.NewGuid() + Path.GetExtension(LogoDosyasi.FileName);
                var path = Path.Combine(uploadDir, fileName);
                LogoDosyasi.SaveAs(path);
                form.SertifikaLogo = "/uploads/sertifika-logolar/" + fileName;
            }
            else if (string.IsNullOrWhiteSpace(form.SertifikaLogo))
            {
                form.SertifikaLogo = mevcutSertifikaAyar?.SertifikaLogo;
            }

            db.SaveChanges();
            SertifikaTasarimKolonlariniHazirla();

            try
            {
                db.Database.ExecuteSqlCommand(
                    @"UPDATE dbo.Anket
                      SET SertifikaAktif = @p0,
                          SertifikaKatilimciErisimi = @p1,
                          SertifikaVerilisZamani = @p2,
                          SertifikaBaslik = @p3,
                          SertifikaMetni = @p4
                      WHERE AnketId = @p5",
                    form.SertifikaAktif,
                    form.SertifikaKatilimciErisimi,
                    form.SertifikaVerilisZamani,
                    (object)form.SertifikaBaslik ?? DBNull.Value,
                    (object)form.SertifikaMetni ?? DBNull.Value,
                    form.AnketId);
            }
            catch
            {
                TempData["Mesaj"] = "Sertifika ayar kolonlarÄ± SQL tarafÄ±nda henÃ¼z eklenmemiÅŸ gÃ¶rÃ¼nÃ¼yor.";
            }

            try
            {
                db.Database.ExecuteSqlCommand(
                    @"UPDATE dbo.Anket
                      SET SertifikaTema = @p0,
                          SertifikaLogo = @p1,
                          SertifikaVurguRengi = @p2,
                          SertifikaCerceve = @p3,
                          SertifikaFont = @p4,
                          SertifikaYaziPunto = @p5,
                          SertifikaBaslikPunto = @p6
                      WHERE AnketId = @p7",
                    form.SertifikaTema,
                    (object)form.SertifikaLogo ?? DBNull.Value,
                    form.SertifikaVurguRengi,
                    form.SertifikaCerceve,
                    form.SertifikaFont,
                    form.SertifikaYaziPunto,
                    form.SertifikaBaslikPunto,
                    form.AnketId);
            }
            catch
            {
                TempData["Mesaj"] = "Sertifika tasarim kolonlari SQL tarafinda henuz eklenmemis gorunuyor.";
            }

            return LocalRedirect(form.ReturnUrl);
        }

        private class AnketSilmePaket
        {
            public Anket Anket { get; set; }
            public List<Havuz> KatilimKayitlari { get; set; } = new List<Havuz>();
            public List<Izledim> SureKayitlari { get; set; } = new List<Izledim>();
            public List<AnketGrup> AnketGruplari { get; set; } = new List<AnketGrup>();
            public List<SoruGrup> SoruGruplari { get; set; } = new List<SoruGrup>();
            public List<Soru> Sorular { get; set; } = new List<Soru>();
            public List<CevapGrup> CevapGruplari { get; set; } = new List<CevapGrup>();
            public List<Cevap> Cevaplar { get; set; } = new List<Cevap>();
            public List<string> KatilimciAdlari { get; set; } = new List<string>();
            public int KayitliKatilimciSayisi { get; set; }
            public int AnonimKatilimciSayisi { get; set; }
        }

        private AnketSilmePaket AnketSilmePaketiniHazirla(int id)
        {
            var paket = new AnketSilmePaket
            {
                Anket = CalismaAlaniAnketGetir(id)
            };

            if (paket.Anket == null)
            {
                return paket;
            }

            paket.KatilimKayitlari = db.Havuz.Where(x => x.AnketId == id).ToList();
            paket.SureKayitlari = db.Izledim.Where(x => x.AnketId == id).ToList();
            paket.AnketGruplari = db.AnketGrup.Where(x => x.AnketId == id).ToList();

            var soruGrupIds = paket.AnketGruplari
                .Where(x => x.SoruGrupId != null)
                .Select(x => x.SoruGrupId.Value)
                .Distinct()
                .ToList();

            var sadeceBuCalismadaKullanilanSoruGrupIds = soruGrupIds
                .Where(soruGrupId => !db.AnketGrup.Any(x => x.AnketId != id && x.SoruGrupId == soruGrupId))
                .ToList();

            if (sadeceBuCalismadaKullanilanSoruGrupIds.Any())
            {
                paket.Sorular = db.Soru
                    .Where(x => x.SoruGrupId != null && sadeceBuCalismadaKullanilanSoruGrupIds.Contains(x.SoruGrupId.Value))
                    .ToList();

                var soruIds = paket.Sorular.Select(x => x.SoruId).ToList();
                var cevapGrupIds = paket.Sorular
                    .Where(x => x.CevapGrupId != null)
                    .Select(x => x.CevapGrupId.Value)
                    .Distinct()
                    .ToList();

                var sadeceBuSorulardaKullanilanCevapGrupIds = cevapGrupIds
                    .Where(cevapGrupId => !db.Soru.Any(x => !soruIds.Contains(x.SoruId) && x.CevapGrupId == cevapGrupId))
                    .ToList();

                if (sadeceBuSorulardaKullanilanCevapGrupIds.Any())
                {
                    paket.Cevaplar = db.Cevap
                        .Where(x => x.CevapGrupId != null && sadeceBuSorulardaKullanilanCevapGrupIds.Contains(x.CevapGrupId.Value))
                        .ToList();

                    paket.CevapGruplari = db.CevapGrup
                        .Where(x => sadeceBuSorulardaKullanilanCevapGrupIds.Contains(x.CevapGrupId))
                        .ToList();
                }

                paket.SoruGruplari = db.SoruGrup
                    .Where(x => sadeceBuCalismadaKullanilanSoruGrupIds.Contains(x.SoruGrupId))
                    .ToList();
            }

            var kayitliKatilimciIds = paket.KatilimKayitlari
                .Where(x => x.UserId != null && x.UserId != 1)
                .Select(x => x.UserId.Value)
                .Distinct()
                .ToList();

            var anonimKatilimciIds = paket.KatilimKayitlari
                .Where(x => x.UserId == null && x.Isimsiz != null)
                .Select(x => x.Isimsiz.Value)
                .Distinct()
                .ToList();

            paket.KayitliKatilimciSayisi = kayitliKatilimciIds.Count;
            paket.AnonimKatilimciSayisi = anonimKatilimciIds.Count;

            if (kayitliKatilimciIds.Any())
            {
                paket.KatilimciAdlari = db.User
                    .Where(x => kayitliKatilimciIds.Contains(x.UserId))
                    .OrderBy(x => x.UserAdi)
                    .Take(8)
                    .ToList()
                    .Select(x => string.IsNullOrWhiteSpace(x.UserAdi)
                        ? (string.IsNullOrWhiteSpace(x.UserTc) ? ("KatÄ±lÄ±mcÄ± #" + x.UserId) : x.UserTc)
                        : x.UserAdi)
                    .ToList();
            }

            var kalanListeYeri = Math.Max(0, 8 - paket.KatilimciAdlari.Count);
            if (kalanListeYeri > 0)
            {
                paket.KatilimciAdlari.AddRange(anonimKatilimciIds
                    .Take(kalanListeYeri)
                    .Select(x => "KatÄ±lÄ±m kodu " + x));
            }

            return paket;
        }

        private ActionResult AnketSilmeOzetJson(AnketSilmePaket paket)
        {
            if (paket.Anket == null)
            {
                return Json(new { success = false, message = "Ã‡alÄ±ÅŸma bulunamadÄ±." });
            }

            var katilimciSayisi = paket.KayitliKatilimciSayisi + paket.AnonimKatilimciSayisi;
            var kalemler = new[]
            {
                new { label = "Ã‡alÄ±ÅŸma ana kaydÄ±", count = 1 },
                new { label = "KatÄ±lÄ±m cevaplarÄ±", count = paket.KatilimKayitlari.Count },
                new { label = "SÃ¼re takip kayÄ±tlarÄ±", count = paket.SureKayitlari.Count },
                new { label = "Rapor baÅŸlÄ±ÄŸÄ± baÄŸlantÄ±larÄ±", count = paket.AnketGruplari.Count },
                new { label = "YalnÄ±z bu Ã§alÄ±ÅŸmaya baÄŸlÄ± soru gruplarÄ±", count = paket.SoruGruplari.Count },
                new { label = "YalnÄ±z bu Ã§alÄ±ÅŸmaya baÄŸlÄ± sorular", count = paket.Sorular.Count },
                new { label = "YalnÄ±z bu Ã§alÄ±ÅŸmaya baÄŸlÄ± cevap gruplarÄ±", count = paket.CevapGruplari.Count },
                new { label = "YalnÄ±z bu Ã§alÄ±ÅŸmaya baÄŸlÄ± seÃ§enekler", count = paket.Cevaplar.Count }
            }.Where(x => x.count > 0).ToList();

            return Json(new
            {
                success = true,
                anketId = paket.Anket.AnketId,
                anketAdi = paket.Anket.AnketAdi,
                katilimKaydi = paket.KatilimKayitlari.Count,
                katilimciSayisi,
                sureKaydi = paket.SureKayitlari.Count,
                raporBasligi = paket.AnketGruplari.Count,
                soruGrubu = paket.SoruGruplari.Count,
                soru = paket.Sorular.Count,
                cevapGrubu = paket.CevapGruplari.Count,
                cevap = paket.Cevaplar.Count,
                katilimcilar = paket.KatilimciAdlari,
                katilimciFazla = Math.Max(0, katilimciSayisi - paket.KatilimciAdlari.Count),
                kalemler
            });
        }

        [HttpGet]
        public ActionResult AnketSilmeOzeti(int id)
        {
            if (Session["id"] == null || Session["admin"] == null)
            {
                return Json(new { success = false, message = "Oturum bulunamadÄ±." });
            }

            if (!AnketCalismaAlanindaMi(id))
            {
                return Json(new { success = false, message = "Bu Ã§alÄ±ÅŸmaya eriÅŸim yetkiniz yok." });
            }

            return AnketSilmeOzetJson(AnketSilmePaketiniHazirla(id));
        }

        [ValidateAntiForgeryToken]
        [HttpPost]
        public ActionResult AnketSil(int id)
        {
            if (Session["id"] == null || Session["admin"] == null)
            {
                return Json(new { success = false, message = "Oturum bulunamadÄ±." });
            }

            if (!AnketCalismaAlanindaMi(id))
            {
                return Json(new { success = false, message = "Bu Ã§alÄ±ÅŸmaya eriÅŸim yetkiniz yok." });
            }

            using (var tx = db.Database.BeginTransaction())
            {
                try
                {
                    var paket = AnketSilmePaketiniHazirla(id);
                    if (paket.Anket == null)
                    {
                        return Json(new { success = false, message = "Ã‡alÄ±ÅŸma bulunamadÄ±." });
                    }

                    db.Havuz.RemoveRange(paket.KatilimKayitlari);
                    db.Izledim.RemoveRange(paket.SureKayitlari);
                    db.SaveChanges();

                    db.AnketGrup.RemoveRange(paket.AnketGruplari);
                    db.SaveChanges();

                    db.Cevap.RemoveRange(paket.Cevaplar);
                    db.Soru.RemoveRange(paket.Sorular);
                    db.SaveChanges();

                    db.CevapGrup.RemoveRange(paket.CevapGruplari);
                    db.SoruGrup.RemoveRange(paket.SoruGruplari);
                    db.SaveChanges();

                    db.Anket.Remove(paket.Anket);
                    db.SaveChanges();

                    tx.Commit();

                    return Json(new
                    {
                        success = true,
                        message = "Ã‡alÄ±ÅŸma ve baÄŸlÄ± kayÄ±tlarÄ± silindi."
                    });
                }
                catch (Exception ex)
                {
                    tx.Rollback();
                    return Json(new
                    {
                        success = false,
                        message = "Silme iÅŸlemi tamamlanamadÄ±: " + ex.Message
                    });
                }
            }
        }

        public ActionResult AnketAdDelete(int id)
        {
            if (Session["id"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }
            if (Session["admin"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }
            var anket = CalismaAlaniAnketGetir(id);
            if (anket == null) return NotFound();

            return View(anket);
        }
        [ValidateAntiForgeryToken()]
        [HttpPost]
        public ActionResult AnketAdDelete(int? id)
        {
            if (Session["id"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }
            if (Session["admin"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }

            if (!AnketCalismaAlanindaMi(id))
            {
                return NotFound();
            }

            if (ModelState.IsValid)
            {
                //havuzda kaydÄ± mevcutsa silinmez
                if (db.AnketGrup.Any(x => x.AnketId == id))
                {
                    return RedirectToAction("Hata2", "Home", null);
                }
                if (db.Havuz.Any(x => x.AnketId == id))
                {
                    return RedirectToAction("Hata3", "Home", null);
                }
            }

            try
            {
                Anket unv = CalismaAlaniAnketGetir(id);
                if (unv == null) return NotFound();

                db.Anket.Remove(unv);
                db.SaveChanges();
                return RedirectToAction("AnketAdIndex");

            }
            catch
            {
                return View();
            }
        }
        public ActionResult AnketGrupIndex(int? id, string adi, string returnUrl = null)
        {
            if (Session["id"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }
            if (Session["admin"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }
            ViewBag.id = id;
            ViewBag.adi = adi;
            ViewBag.ReturnUrl = CalismaAlaniDonusAdresi(returnUrl);

            if (!AnketCalismaAlanindaMi(id))
            {
                return RedirectToAction("AnketAdIndex");
            }

            var gruplar = db.AnketGrup
                .Include("SoruGrup")
                .Where(x => x.AnketId == id)
                .OrderBy(x => x.SoruGrup.SoruGrupSira ?? int.MaxValue)
                .ThenBy(x => x.SoruGrup.SoruGrupAdi)
                .ToList();

            var soruGrupIds = gruplar
                .Where(x => x.SoruGrupId != null)
                .Select(x => x.SoruGrupId.Value)
                .ToList();

            var soruBilgileri = db.Soru
                .Where(x => x.SoruGrupId != null && soruGrupIds.Contains(x.SoruGrupId.Value))
                .GroupBy(x => x.SoruGrupId.Value)
                .Select(x => new
                {
                    SoruGrupId = x.Key,
                    SoruSayisi = x.Count(),
                    Puan = x.Sum(s => (double?)s.SoruPuan) ?? 0
                })
                .ToList();

            ViewBag.SoruSayilari = soruBilgileri.ToDictionary(x => x.SoruGrupId, x => x.SoruSayisi);
            ViewBag.GrupPuanlari = soruBilgileri.ToDictionary(x => x.SoruGrupId, x => x.Puan);
            ViewBag.ToplamSoru = soruBilgileri.Sum(x => x.SoruSayisi);
            ViewBag.ToplamPuan = soruBilgileri.Sum(x => x.Puan);

            return View(gruplar);
        }

        private string CalismaAlaniDonusAdresi(string returnUrl)
        {
            if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
            {
                return returnUrl;
            }

            return Url.Action("Indexgosterge", "Home") ?? "/";
        }

        private string DogruCevapDonusAdresi(string returnUrl)
        {
            return CalismaAlaniDonusAdresi(returnUrl);
        }

        private DogruCevapEditorModel DogruCevapEditorModelOlustur(int anketId)
        {
            var anket = CalismaAlaniAnketGetir(anketId);
            if (anket == null)
            {
                return null;
            }

            var soruGrupIds = db.AnketGrup
                .Where(x => x.AnketId == anketId && x.SoruGrupId != null)
                .Select(x => x.SoruGrupId.Value)
                .ToList();

            var sorular = db.Soru
                .Include("CevapGrup")
                .Where(x => x.SoruGrupId != null && soruGrupIds.Contains(x.SoruGrupId.Value))
                .OrderBy(x => x.SoruSira ?? int.MaxValue)
                .ThenBy(x => x.SoruId)
                .ToList();

            var cevapGrupIds = sorular
                .Where(x => x.CevapGrupId != null)
                .Select(x => x.CevapGrupId.Value)
                .Distinct()
                .ToList();

            var cevaplar = db.Cevap
                .Where(x => x.CevapGrupId != null && cevapGrupIds.Contains(x.CevapGrupId.Value))
                .OrderBy(x => x.CevapId)
                .ToList();

            return new DogruCevapEditorModel
            {
                AnketId = anket.AnketId,
                AnketAdi = anket.AnketAdi,
                Sorular = sorular.Select(soru =>
                {
                    var soruCevaplari = cevaplar
                        .Where(cevap => cevap.CevapGrupId == soru.CevapGrupId)
                        .Select(cevap => new DogruCevapAnswerModel
                        {
                            CevapId = cevap.CevapId,
                            CevapAdi = cevap.CevapAdi,
                            Dogru = cevap.Dogru == true
                        })
                        .ToList();

                    return new DogruCevapQuestionModel
                    {
                        SoruId = soru.SoruId,
                        SoruSira = soru.SoruSira,
                        SoruAdi = soru.SoruAdi,
                        SoruPuan = soru.SoruPuan ?? 0,
                        CevapGrupId = soru.CevapGrupId,
                        DogruCevapId = soruCevaplari.FirstOrDefault(x => x.Dogru)?.CevapId,
                        Cevaplar = soruCevaplari
                    };
                }).ToList()
            };
        }

        public ActionResult DogruCevapEditor(int id, string returnUrl = null)
        {
            if (Session["id"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }
            if (Session["admin"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }

            var model = DogruCevapEditorModelOlustur(id);
            if (model == null)
            {
                return RedirectToAction("AnketAdIndex", "Home");
            }

            model.ReturnUrl = DogruCevapDonusAdresi(returnUrl);

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult DogruCevapEditor(DogruCevapSaveModel form)
        {
            if (Session["id"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }
            if (Session["admin"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }

            var model = DogruCevapEditorModelOlustur(form.AnketId);
            if (model == null)
            {
                return RedirectToAction("AnketAdIndex", "Home");
            }

            model.ReturnUrl = DogruCevapDonusAdresi(form.ReturnUrl);

            if (form.DogruCevaplar == null)
            {
                form.DogruCevaplar = new Dictionary<int, int>();
            }

            var soruIds = model.Sorular.Select(x => x.SoruId).ToList();
            var sorular = db.Soru
                .Where(x => soruIds.Contains(x.SoruId))
                .ToList();
            var cevapGrupIds = sorular
                .Where(x => x.CevapGrupId != null)
                .Select(x => x.CevapGrupId.Value)
                .Distinct()
                .ToList();
            var cevaplar = db.Cevap
                .Where(x => x.CevapGrupId != null && cevapGrupIds.Contains(x.CevapGrupId.Value))
                .ToList();

            foreach (var soru in sorular)
            {
                if (!form.DogruCevaplar.TryGetValue(soru.SoruId, out var secilenCevapId))
                {
                    ModelState.AddModelError($"DogruCevaplar[{soru.SoruId}]", "Bu soru iÃ§in doÄŸru cevap seÃ§in.");
                    continue;
                }

                var secilenCevap = cevaplar.FirstOrDefault(x => x.CevapId == secilenCevapId);
                if (secilenCevap == null || secilenCevap.CevapGrupId != soru.CevapGrupId)
                {
                    ModelState.AddModelError($"DogruCevaplar[{soru.SoruId}]", "SeÃ§ilen cevap bu soruya ait deÄŸil.");
                }
            }

            var ortakCevapGrubuCakismalari = sorular
                .Where(x => x.CevapGrupId != null && form.DogruCevaplar.ContainsKey(x.SoruId))
                .GroupBy(x => x.CevapGrupId.Value)
                .Where(grup => grup
                    .Select(soru => form.DogruCevaplar[soru.SoruId])
                    .Distinct()
                    .Count() > 1)
                .ToList();

            if (ortakCevapGrubuCakismalari.Any())
            {
                ModelState.AddModelError("", "AynÄ± cevap grubunu kullanan sorularda farklÄ± doÄŸru cevap seÃ§ilemez. Bu sorular iÃ§in ayrÄ± cevap grubu oluÅŸturun.");
            }

            if (!ModelState.IsValid)
            {
                foreach (var soru in model.Sorular)
                {
                    if (form.DogruCevaplar.TryGetValue(soru.SoruId, out var secilenCevapId))
                    {
                        soru.DogruCevapId = secilenCevapId;
                        foreach (var cevap in soru.Cevaplar)
                        {
                            cevap.Dogru = cevap.CevapId == secilenCevapId;
                        }
                    }
                }

                return View(model);
            }

            foreach (var soru in sorular)
            {
                var secilenCevapId = form.DogruCevaplar[soru.SoruId];
                var soruPuani = soru.SoruPuan ?? 0;
                foreach (var cevap in cevaplar.Where(x => x.CevapGrupId == soru.CevapGrupId))
                {
                    cevap.Dogru = cevap.CevapId == secilenCevapId;
                    cevap.CevapPuan = cevap.Dogru == true ? soruPuani : 0;
                }
            }

            var havuzKayitlari = db.Havuz
                .Include("Cevap")
                .Include("Soru")
                .Where(x => x.AnketId == form.AnketId && x.SoruID != null && soruIds.Contains(x.SoruID.Value))
                .ToList();

            foreach (var havuz in havuzKayitlari)
            {
                var soruPuani = havuz.SoruPuan ?? havuz.Soru?.SoruPuan ?? 0;
                havuz.SoruPuan = soruPuani;
                havuz.CevapPuan = havuz.Cevap?.Dogru == true ? soruPuani : 0;
            }

            db.SaveChanges();
            TempData["DogruCevapMesaj"] = "DoÄŸru cevaplar kaydedildi ve mevcut katÄ±lÄ±m puanlarÄ± yeniden hesaplandÄ±.";

            return LocalRedirect(DogruCevapDonusAdresi(form.ReturnUrl));
        }

        public ActionResult AnketGrupCreate(int id, string adi)
        {
            if (Session["id"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }
            if (Session["admin"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }
            ViewBag.id = id;
            ViewBag.adi = adi;

            if (!AnketCalismaAlanindaMi(id))
            {
                return RedirectToAction("AnketAdIndex");
            }

            List<SelectListItem> an =
            (from i in CalismaAlaniAnketleri().OrderBy(x => x.AnketAdi).ToList()
             select new SelectListItem
             {
                 Text = i.AnketAdi,
                 Value = i.AnketId.ToString(),
             }).ToList();
            ViewBag.Ank = an;

            var mevcutGrupIds = db.AnketGrup
                .Where(x => x.AnketId == id && x.SoruGrupId.HasValue)
                .Select(x => x.SoruGrupId.Value)
                .ToList();
            var hav = CalismaAlaniBankaKayitlari<SoruGrup>("SoruGrup", "SoruGrupSira")
                .Where(x => !mevcutGrupIds.Contains(x.SoruGrupId))
                .ToList();
            List<SelectListItem> sr =
            (from i in hav.OrderBy(x => x.SoruGrupSira).ToList()
             select new SelectListItem
             {
                 Text = i.SoruGrupAdi,
                 Value = i.SoruGrupId.ToString(),
             }).ToList();
            ViewBag.Sor = sr;

            return View();
        }
        [ValidateAntiForgeryToken()]
        [HttpPost]
        public ActionResult AnketGrupCreate(AnketGrup dgskn, string adi)
        {
            if (Session["id"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }
            if (Session["admin"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }
            if (!AnketCalismaAlanindaMi(dgskn.AnketId))
            {
                return RedirectToAction("AnketAdIndex");
            }
            if (!CalismaAlaniBankaKaydiVarMi("SoruGrup", "SoruGrupId", dgskn.SoruGrupId))
            {
                return RedirectToAction("AnketGrupIndex", new { id = dgskn.AnketId, adi = adi });
            }

            if (db.AnketGrup.Any(x => x.SoruGrupId == dgskn.SoruGrupId && x.AnketId == dgskn.AnketId))
            {
                return RedirectToAction("Hata1", "Home", null);
            }
            try
            {
                db.AnketGrup.Add(dgskn);
                db.SaveChanges();
                return RedirectToAction("AnketGrupIndex", new { id = dgskn.AnketId, adi = adi });

            }
            catch
            {
                return View();
            }
        }
        public ActionResult AnketGrupEdit(int id, int? ank, string adi)
        {
            if (Session["id"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }
            if (Session["admin"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }
            ViewBag.id = ank;
            ViewBag.adi = adi;

            if (!AnketCalismaAlanindaMi(ank))
            {
                return RedirectToAction("AnketAdIndex");
            }

            List<SelectListItem> an =
            (from i in CalismaAlaniAnketleri().OrderBy(x => x.AnketAdi).ToList()
             select new SelectListItem
             {
                 Text = i.AnketAdi,
                 Value = i.AnketId.ToString(),
             }).ToList();
            ViewBag.Ank = an;

            var mevcutGrupIds = db.AnketGrup
                .Where(x => x.AnketId == ank && x.SoruGrupId.HasValue && x.AnketGupId != id)
                .Select(x => x.SoruGrupId.Value)
                .ToList();
            var hav = CalismaAlaniBankaKayitlari<SoruGrup>("SoruGrup", "SoruGrupSira")
                .Where(x => !mevcutGrupIds.Contains(x.SoruGrupId))
                .ToList();
            List<SelectListItem> sr =
            (from i in hav.OrderBy(x => x.SoruGrupSira).ToList()
             select new SelectListItem
             {
                 Text = i.SoruGrupAdi,
                 Value = i.SoruGrupId.ToString(),
             }).ToList();
            ViewBag.Sor = sr;

            return View(db.AnketGrup.Where(x => x.AnketGupId == id && x.AnketId == ank).FirstOrDefault());
        }
        [ValidateAntiForgeryToken()]
        [HttpPost]
        public ActionResult AnketGrupEdit(AnketGrup dgskn, int? sr, int? ank, string adi)
        {
            if (Session["id"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }
            if (Session["admin"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }
            if (!AnketCalismaAlanindaMi(ank) || !AnketCalismaAlanindaMi(dgskn.AnketId))
            {
                return RedirectToAction("AnketAdIndex");
            }
            if (!CalismaAlaniBankaKaydiVarMi("SoruGrup", "SoruGrupId", dgskn.SoruGrupId))
            {
                return RedirectToAction("AnketGrupIndex", new { id = ank, adi });
            }

            if (db.Havuz.Any(x => x.SoruGrupId == sr && x.AnketId == ank))
            {
                return RedirectToAction("Hata3", "Home", null);

            }

            try
            {
                {
                    db.Entry(dgskn).State = EntityState.Modified;
                    db.SaveChanges();
                }
                return RedirectToAction("AnketGrupIndex", new { id = dgskn.AnketId, adi });

            }
            catch
            {
                return View();
            }
        }
        public ActionResult AnketGrupDelete(int id, int? ank, string adi)
        {
            if (Session["id"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }
            if (Session["admin"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }
            ViewBag.id = ank;
            ViewBag.adi = adi;
            if (!AnketCalismaAlanindaMi(ank))
            {
                return RedirectToAction("AnketAdIndex");
            }

            return View(db.AnketGrup.Where(x => x.AnketGupId == id && x.AnketId == ank).FirstOrDefault());
        }
        [ValidateAntiForgeryToken()]
        [HttpPost]
        public ActionResult AnketGrupDelete(int? id, int? sr, int? ank, string adi)
        {
            if (Session["id"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }
            if (Session["admin"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }
            if (!AnketCalismaAlanindaMi(ank))
            {
                return RedirectToAction("AnketAdIndex");
            }

            if (db.Havuz.Any(x => x.SoruGrupId == sr && x.AnketId == ank))
            {
                return RedirectToAction("Hata3", "Home", null);

            }

            try
            {
                AnketGrup unv = db.AnketGrup.Where(x => x.AnketGupId == id && x.AnketId == ank).FirstOrDefault();
                if (unv == null) return NotFound();

                db.AnketGrup.Remove(unv);
                db.SaveChanges();
                return RedirectToAction("AnketGrupIndex", new { id = ank, adi });

            }
            catch
            {
                return View();
            }
        }
        private void SoruFormListeleriniHazirla()
        {
            List<SelectListItem> an =
            (from i in CalismaAlaniAnketleri().OrderBy(x => x.AnketAdi).ToList()
             select new SelectListItem
             {
                 Text = i.AnketAdi,
                 Value = i.AnketId.ToString(),
             }).ToList();
            ViewBag.Ank = an;

            List<SelectListItem> un =
            (from i in CalismaAlaniBankaKayitlari<CevapGrup>("CevapGrup", "CevapGrupAdi")
             select new SelectListItem
             {
                 Text = i.CevapGrupAdi,
                 Value = i.CevapGrupId.ToString(),
             }).ToList();
            ViewBag.Cev = un;

            List<SelectListItem> sr =
            (from i in CalismaAlaniBankaKayitlari<SoruGrup>("SoruGrup", "SoruGrupAdi")
             select new SelectListItem
             {
                 Text = i.SoruGrupAdi,
                 Value = i.SoruGrupId.ToString(),
             }).ToList();
            ViewBag.Sor = sr;
        }

        private void CevapFormListeleriniHazirla()
        {
            List<SelectListItem> un =
            (from i in CalismaAlaniBankaKayitlari<CevapGrup>("CevapGrup", "CevapGrupAdi")
             select new SelectListItem
             {
                 Text = i.CevapGrupAdi,
                 Value = i.CevapGrupId.ToString(),
             }).ToList();
            ViewBag.Gur = un;
        }

        public class SoruBankasiCevapFormModel
        {
            public int? CevapId { get; set; }
            public string Metin { get; set; }
            public string Gorsel { get; set; }
            public bool Dogru { get; set; }
            public double? Puan { get; set; }
            public bool Silinsin { get; set; }
        }

        public class SoruBankasiFormModel
        {
            public int? AnketId { get; set; }
            public int? SoruId { get; set; }
            public string SoruAdi { get; set; }
            public string SoruGorsel { get; set; }
            public int? SoruGrupId { get; set; }
            public int? CevapGrupId { get; set; }
            public string CevapGrupModu { get; set; } = "yeni";
            public string YeniCevapGrupAdi { get; set; }
            public int? SoruSira { get; set; }
            public double? SoruPuan { get; set; }
            public string PuanlamaModu { get; set; } = "sinav";
            public bool CevaplariGuncelle { get; set; } = true;
            public int? DogruCevapSatiri { get; set; }
            public List<SoruBankasiCevapFormModel> Cevaplar { get; set; } = new List<SoruBankasiCevapFormModel>();
        }

        public class AnketSoruYonetimModel
        {
            public int AnketId { get; set; }
            public string AnketAdi { get; set; }
            public string ReturnUrl { get; set; }
            public bool SinavMi { get; set; }
            public int KatilimSayisi { get; set; }
            public int SoruSayisi { get; set; }
            public int BaslikSayisi { get; set; }
            public double ToplamPuan { get; set; }
            public List<AnketSoruYonetimGrupModel> Gruplar { get; set; } = new List<AnketSoruYonetimGrupModel>();
        }

        public class AnketSoruYonetimGrupModel
        {
            public int AnketGrupId { get; set; }
            public int? SoruGrupId { get; set; }
            public string SoruGrupAdi { get; set; }
            public int SoruSayisi { get; set; }
            public double Puan { get; set; }
            public List<AnketSoruYonetimSoruModel> Sorular { get; set; } = new List<AnketSoruYonetimSoruModel>();
        }

        public class AnketSoruYonetimSoruModel
        {
            public int SoruId { get; set; }
            public string SoruAdi { get; set; }
            public string SoruGorsel { get; set; }
            public string CevapGrupAdi { get; set; }
            public int CevapSayisi { get; set; }
            public int KatilimKaydi { get; set; }
            public double Puan { get; set; }
            public bool Cikarilabilir { get; set; }
            public string CikarilamazMesaji { get; set; }
        }

        private static (string Metin, string Gorsel) SoruBankasiGorselliMetniCoz(string value)
        {
            var text = value ?? string.Empty;
            var match = Regex.Match(
                text,
                @"\s*\[\[gorsel:(?<file>[^\]]+)\]\]\s*$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

            if (!match.Success)
            {
                return (text.Trim(), string.Empty);
            }

            var cleanText = Regex.Replace(text, @"\s*\[\[gorsel:[^\]]+\]\]\s*$", "", RegexOptions.IgnoreCase).Trim();
            return (cleanText, WizardGorselDosyaAdi(match.Groups["file"].Value));
        }

        private List<SoruBankasiCevapFormModel> SoruBankasiVarsayilanCevaplari()
        {
            return new List<SoruBankasiCevapFormModel>
            {
                new SoruBankasiCevapFormModel { Metin = "A seÃ§eneÄŸi", Dogru = true, Puan = 5 },
                new SoruBankasiCevapFormModel { Metin = "B seÃ§eneÄŸi", Puan = 4 },
                new SoruBankasiCevapFormModel { Metin = "C seÃ§eneÄŸi", Puan = 3 },
                new SoruBankasiCevapFormModel { Metin = "D seÃ§eneÄŸi", Puan = 2 }
            };
        }

        private List<SoruBankasiCevapFormModel> SoruBankasiCevaplariniGetir(int? cevapGrupId)
        {
            if (!cevapGrupId.HasValue)
            {
                return SoruBankasiVarsayilanCevaplari();
            }

            var cevaplar = CalismaAlaniBankaKayitlari<Cevap>("Cevap", "CevapId")
                .Where(x => x.CevapGrupId == cevapGrupId.Value)
                .OrderBy(x => x.CevapId)
                .ToList();

            if (!cevaplar.Any())
            {
                return SoruBankasiVarsayilanCevaplari();
            }

            return cevaplar.Select((cevap, index) =>
            {
                var gorselli = SoruBankasiGorselliMetniCoz(cevap.CevapAdi);
                return new SoruBankasiCevapFormModel
                {
                    CevapId = cevap.CevapId,
                    Metin = gorselli.Metin,
                    Gorsel = gorselli.Gorsel,
                    Dogru = cevap.Dogru == true,
                    Puan = cevap.CevapPuan ?? (index == 0 ? 5 : 0)
                };
            }).ToList();
        }

        private SoruBankasiFormModel SoruBankasiFormuHazirla(Soru soru = null)
        {
            if (soru == null)
            {
                return new SoruBankasiFormModel
                {
                    SoruSira = (CalismaAlaniBankaKayitlari<Soru>("Soru", "SoruId")
                        .Select(x => x.SoruSira ?? 0)
                        .DefaultIfEmpty(0)
                        .Max()) + 1,
                    SoruPuan = 100,
                    Cevaplar = SoruBankasiVarsayilanCevaplari(),
                    DogruCevapSatiri = 0
                };
            }

            var gorselli = SoruBankasiGorselliMetniCoz(soru.SoruAdi);
            var cevaplar = SoruBankasiCevaplariniGetir(soru.CevapGrupId);
            var dogruSatir = cevaplar.FindIndex(x => x.Dogru);

            return new SoruBankasiFormModel
            {
                SoruId = soru.SoruId,
                SoruAdi = gorselli.Metin,
                SoruGorsel = gorselli.Gorsel,
                SoruGrupId = soru.SoruGrupId,
                CevapGrupId = soru.CevapGrupId,
                CevapGrupModu = "hazir",
                YeniCevapGrupAdi = soru.CevapGrup?.CevapGrupAdi,
                SoruSira = soru.SoruSira,
                SoruPuan = soru.SoruPuan,
                PuanlamaModu = cevaplar.Any(x => x.Dogru) ? "sinav" : "anket",
                CevaplariGuncelle = true,
                DogruCevapSatiri = dogruSatir >= 0 ? dogruSatir : (int?)null,
                Cevaplar = cevaplar
            };
        }

        private List<SoruBankasiCevapFormModel> SoruBankasiAktifCevaplar(SoruBankasiFormModel form)
        {
            return (form.Cevaplar ?? new List<SoruBankasiCevapFormModel>())
                .Select((cevap, index) =>
                {
                    cevap.Metin = (cevap.Metin ?? string.Empty).Trim();
                    cevap.Gorsel = WizardGorselDosyaAdi(cevap.Gorsel);
                    cevap.Dogru = string.Equals(form.PuanlamaModu, "sinav", StringComparison.OrdinalIgnoreCase)
                        && form.DogruCevapSatiri == index
                        && !cevap.Silinsin;
                    cevap.Puan = ClampSurveyScore(cevap.Puan);
                    return cevap;
                })
                .Where(x => !x.Silinsin && (!string.IsNullOrWhiteSpace(x.Metin) || !string.IsNullOrWhiteSpace(x.Gorsel)))
                .Take(12)
                .ToList();
        }

        private bool SoruBankasiFormunuDogrula(SoruBankasiFormModel form, bool yeniKayit, out bool yeniCevapGrubu, out List<SoruBankasiCevapFormModel> aktifCevaplar)
        {
            form.SoruGorsel = WizardGorselDosyaAdi(form.SoruGorsel);
            yeniCevapGrubu = !string.Equals(form.CevapGrupModu, "hazir", StringComparison.OrdinalIgnoreCase);
            aktifCevaplar = SoruBankasiAktifCevaplar(form);

            if (string.IsNullOrWhiteSpace(form.SoruAdi) && string.IsNullOrWhiteSpace(form.SoruGorsel))
            {
                ModelState.AddModelError(nameof(form.SoruAdi), "Soru metni veya gÃ¶rseli zorunlu.");
            }

            if (!form.SoruGrupId.HasValue)
            {
                ModelState.AddModelError(nameof(form.SoruGrupId), "BÃ¶lÃ¼m / kategori seÃ§in.");
            }

            if (!CalismaAlaniBankaSecimiGecerliMi("SoruGrup", "SoruGrupId", form.SoruGrupId))
            {
                ModelState.AddModelError(nameof(form.SoruGrupId), "SeÃ§ili kategori bu Ã§alÄ±ÅŸma alanÄ±na ait deÄŸil.");
            }

            if (yeniCevapGrubu)
            {
                if (aktifCevaplar.Count < 2)
                {
                    ModelState.AddModelError("", "Yeni seÃ§enek grubu iÃ§in en az iki cevap seÃ§eneÄŸi yazÄ±n.");
                }
            }
            else if (!CalismaAlaniBankaSecimiGecerliMi("CevapGrup", "CevapGrupId", form.CevapGrupId) || !form.CevapGrupId.HasValue)
            {
                ModelState.AddModelError(nameof(form.CevapGrupId), "GeÃ§erli bir seÃ§enek grubu seÃ§in.");
            }
            else if (form.CevaplariGuncelle && aktifCevaplar.Count < 2)
            {
                ModelState.AddModelError("", "SeÃ§enekleri gÃ¼ncellemek iÃ§in en az iki cevap seÃ§eneÄŸi yazÄ±n.");
            }

            var cevaplariKontrolEt = yeniCevapGrubu || form.CevaplariGuncelle;
            var sinavModu = string.Equals(form.PuanlamaModu, "sinav", StringComparison.OrdinalIgnoreCase);
            if (cevaplariKontrolEt && sinavModu && !aktifCevaplar.Any(x => x.Dogru))
            {
                ModelState.AddModelError("", "SÄ±nav sorusu iÃ§in doÄŸru cevabÄ± iÅŸaretleyin.");
            }

            if (form.SoruPuan.GetValueOrDefault() < 0)
            {
                ModelState.AddModelError(nameof(form.SoruPuan), "Puan negatif olamaz.");
            }

            if (!yeniKayit && (!form.SoruId.HasValue || !CalismaAlaniBankaKaydiVarMi("Soru", "SoruId", form.SoruId)))
            {
                ModelState.AddModelError("", "Bu soru aktif Ã§alÄ±ÅŸma alanÄ±nda bulunamadÄ±.");
            }

            return ModelState.IsValid;
        }

        private CevapGrup SoruBankasiCevapGrubuOlustur(SoruBankasiFormModel form)
        {
            var grupAdi = TrimWizardText(
                form.YeniCevapGrupAdi,
                TrimWizardText(form.SoruAdi, "Yeni Soru", 220) + " CevaplarÄ±",
                250);

            var cevapGrup = new CevapGrup { CevapGrupAdi = grupAdi };
            db.CevapGrup.Add(cevapGrup);
            db.SaveChanges();
            CalismaAlaniBankaKaydinaBagla("CevapGrup", "CevapGrupId", cevapGrup.CevapGrupId);
            return cevapGrup;
        }

        private void AnketSoruGrubunuBagla(int anketId, int? soruGrupId)
        {
            if (!soruGrupId.HasValue || !AnketCalismaAlanindaMi(anketId))
            {
                return;
            }

            if (!CalismaAlaniBankaSecimiGecerliMi("SoruGrup", "SoruGrupId", soruGrupId))
            {
                return;
            }

            if (db.AnketGrup.Any(x => x.AnketId == anketId && x.SoruGrupId == soruGrupId))
            {
                return;
            }

            db.AnketGrup.Add(new AnketGrup
            {
                AnketId = anketId,
                SoruGrupId = soruGrupId
            });
            db.SaveChanges();
        }

        private AnketSoruYonetimModel AnketSoruYonetimModeliOlustur(int anketId)
        {
            var anket = db.Anket.FirstOrDefault(x => x.AnketId == anketId);
            if (anket == null)
            {
                return null;
            }

            var gruplar = db.AnketGrup
                .Include("SoruGrup")
                .Where(x => x.AnketId == anketId)
                .OrderBy(x => x.SoruGrup.SoruGrupSira)
                .ThenBy(x => x.SoruGrup.SoruGrupAdi)
                .ToList();

            var grupIds = gruplar
                .Where(x => x.SoruGrupId.HasValue)
                .Select(x => x.SoruGrupId.Value)
                .ToList();

            var sorular = db.Soru
                .Include("CevapGrup")
                .Where(x => x.SoruGrupId.HasValue && grupIds.Contains(x.SoruGrupId.Value))
                .OrderBy(x => x.SoruGrupId)
                .ThenBy(x => x.SoruSira)
                .ThenBy(x => x.SoruId)
                .ToList();

            var cevapGrupIds = sorular
                .Where(x => x.CevapGrupId.HasValue)
                .Select(x => x.CevapGrupId.Value)
                .Distinct()
                .ToList();

            var cevapSayilari = db.Cevap
                .Where(x => x.CevapGrupId.HasValue && cevapGrupIds.Contains(x.CevapGrupId.Value))
                .GroupBy(x => x.CevapGrupId.Value)
                .ToDictionary(x => x.Key, x => x.Count());

            var soruIds = sorular.Select(x => x.SoruId).ToList();
            var katilimKayitlari = db.Havuz
                .Where(x => x.AnketId == anketId && x.SoruID.HasValue && soruIds.Contains(x.SoruID.Value))
                .GroupBy(x => x.SoruID.Value)
                .ToDictionary(x => x.Key, x => x.Count());

            var tumKatilimKayitlari = db.Havuz
                .Where(x => x.SoruID.HasValue && soruIds.Contains(x.SoruID.Value))
                .GroupBy(x => x.SoruID.Value)
                .ToDictionary(x => x.Key, x => x.Count());

            var baskaCalismadaGrupVarMi = db.AnketGrup
                .Where(x => x.AnketId != anketId && x.SoruGrupId.HasValue && grupIds.Contains(x.SoruGrupId.Value))
                .Select(x => x.SoruGrupId.Value)
                .Distinct()
                .ToList();

            var katilimSayisi = db.Havuz
                .Where(x => x.AnketId == anketId)
                .Select(x => new { x.UserId, x.Isimsiz, x.HavuzId })
                .ToList()
                .Select(x => x.UserId.HasValue ? "u:" + x.UserId.Value : "k:" + (x.Isimsiz ?? x.HavuzId))
                .Distinct()
                .Count();

            var model = new AnketSoruYonetimModel
            {
                AnketId = anket.AnketId,
                AnketAdi = anket.AnketAdi,
                SinavMi = SinavTurundeMi(anket),
                KatilimSayisi = katilimSayisi
            };

            foreach (var grup in gruplar)
            {
                var grupSorulari = sorular
                    .Where(x => x.SoruGrupId == grup.SoruGrupId)
                    .ToList();

                var grupModel = new AnketSoruYonetimGrupModel
                {
                    AnketGrupId = grup.AnketGupId,
                    SoruGrupId = grup.SoruGrupId,
                    SoruGrupAdi = grup.SoruGrup?.SoruGrupAdi ?? "TanÄ±msÄ±z baÅŸlÄ±k",
                    SoruSayisi = grupSorulari.Count,
                    Puan = grupSorulari.Sum(x => x.SoruPuan ?? 0)
                };

                foreach (var soru in grupSorulari)
                {
                    var gorselli = SoruBankasiGorselliMetniCoz(soru.SoruAdi);
                    var katilimKaydi = katilimKayitlari.ContainsKey(soru.SoruId) ? katilimKayitlari[soru.SoruId] : 0;
                    var tumKatilimKaydi = tumKatilimKayitlari.ContainsKey(soru.SoruId) ? tumKatilimKayitlari[soru.SoruId] : 0;
                    var grupBaskaCalismada = soru.SoruGrupId.HasValue && baskaCalismadaGrupVarMi.Contains(soru.SoruGrupId.Value);
                    var cikarilabilir = tumKatilimKaydi == 0 && !grupBaskaCalismada;
                    var cikarilamazMesaji = katilimKaydi > 0
                        ? "Kat\u0131l\u0131m kayd\u0131 oldu\u011fu i\u00e7in \u00e7\u0131kar\u0131lamaz."
                        : tumKatilimKaydi > 0
                            ? "Soru ge\u00e7mi\u015f kat\u0131l\u0131mda kullan\u0131lm\u0131\u015f."
                        : grupBaskaCalismada
                            ? "Bu ba\u015fl\u0131k ba\u015fka \u00e7al\u0131\u015fmada da kullan\u0131l\u0131yor."
                            : "";

                    grupModel.Sorular.Add(new AnketSoruYonetimSoruModel
                    {
                        SoruId = soru.SoruId,
                        SoruAdi = gorselli.Metin,
                        SoruGorsel = gorselli.Gorsel,
                        CevapGrupAdi = soru.CevapGrup?.CevapGrupAdi ?? "TanÄ±msÄ±z seÃ§enek grubu",
                        CevapSayisi = soru.CevapGrupId.HasValue && cevapSayilari.ContainsKey(soru.CevapGrupId.Value)
                            ? cevapSayilari[soru.CevapGrupId.Value]
                            : 0,
                        KatilimKaydi = katilimKaydi,
                        Puan = soru.SoruPuan ?? 0,
                        Cikarilabilir = cikarilabilir,
                        CikarilamazMesaji = cikarilamazMesaji
                    });
                }

                model.Gruplar.Add(grupModel);
            }

            model.SoruSayisi = model.Gruplar.Sum(x => x.SoruSayisi);
            model.BaslikSayisi = model.Gruplar.Count;
            model.ToplamPuan = model.Gruplar.Sum(x => x.Puan);
            return model;
        }

        public ActionResult AnketSoruYonetim(int id, string returnUrl = null)
        {
            if (Session["id"] == null || Session["admin"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }

            if (!AnketCalismaAlanindaMi(id))
            {
                return RedirectToAction("Indexgosterge");
            }

            var model = AnketSoruYonetimModeliOlustur(id);
            if (model == null)
            {
                return RedirectToAction("Indexgosterge");
            }

            model.ReturnUrl = CalismaAlaniDonusAdresi(returnUrl);

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult AnketSoruCikar(int id, int soruId)
        {
            if (Session["id"] == null || Session["admin"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }

            if (!AnketCalismaAlanindaMi(id))
            {
                return RedirectToAction("Indexgosterge");
            }

            var soru = db.Soru.FirstOrDefault(x => x.SoruId == soruId);
            if (soru == null || !soru.SoruGrupId.HasValue || !db.AnketGrup.Any(x => x.AnketId == id && x.SoruGrupId == soru.SoruGrupId))
            {
                TempData["AnketSoruYonetimMesaj"] = "Soru bu \u00e7al\u0131\u015fmada bulunamad\u0131.";
                return RedirectToAction("AnketSoruYonetim", new { id });
            }

            if (db.Havuz.Any(x => x.SoruID == soruId))
            {
                TempData["AnketSoruYonetimMesaj"] = "Bu soruda kat\u0131l\u0131m kayd\u0131 var. Rapor verisi bozulmas\u0131n diye \u00e7\u0131kar\u0131lamaz.";
                return RedirectToAction("AnketSoruYonetim", new { id });
            }

            if (db.AnketGrup.Any(x => x.AnketId != id && x.SoruGrupId == soru.SoruGrupId))
            {
                TempData["AnketSoruYonetimMesaj"] = "Bu soru ba\u015fl\u0131\u011f\u0131 ba\u015fka \u00e7al\u0131\u015fmada da kullan\u0131l\u0131yor. Ortak bankay\u0131 bozmamak i\u00e7in buradan \u00e7\u0131kar\u0131lamaz.";
                return RedirectToAction("AnketSoruYonetim", new { id });
            }

            var soruGrupId = soru.SoruGrupId;
            db.Soru.Remove(soru);
            db.SaveChanges();

            if (!db.Soru.Any(x => x.SoruGrupId == soruGrupId))
            {
                var bag = db.AnketGrup.FirstOrDefault(x => x.AnketId == id && x.SoruGrupId == soruGrupId);
                if (bag != null)
                {
                    db.AnketGrup.Remove(bag);
                    db.SaveChanges();
                }
            }

            TempData["AnketSoruYonetimMesaj"] = "Soru Ã§alÄ±ÅŸmadan Ã§Ä±karÄ±ldÄ±.";
            return RedirectToAction("AnketSoruYonetim", new { id });
        }

        private void SoruBankasiCevaplariniKaydet(int cevapGrupId, SoruBankasiFormModel form, List<SoruBankasiCevapFormModel> aktifCevaplar, bool mevcutGrubuGuncelle)
        {
            var sinavModu = string.Equals(form.PuanlamaModu, "sinav", StringComparison.OrdinalIgnoreCase);
            var soruPuani = form.SoruPuan.GetValueOrDefault(100);
            var mevcutCevaplar = mevcutGrubuGuncelle
                ? db.Cevap.Where(x => x.CevapGrupId == cevapGrupId).ToList()
                : new List<Cevap>();
            var kaydedilenCevaplar = new List<Cevap>();

            if (mevcutGrubuGuncelle)
            {
                var silinecekler = (form.Cevaplar ?? new List<SoruBankasiCevapFormModel>())
                    .Where(x => x.Silinsin && x.CevapId.HasValue)
                    .Select(x => x.CevapId.Value)
                    .ToList();

                foreach (var cevapId in silinecekler)
                {
                    var cevap = mevcutCevaplar.FirstOrDefault(x => x.CevapId == cevapId);
                    if (cevap != null && cevap.CevapGrupId == cevapGrupId)
                    {
                        if (db.Havuz.Any(x => x.CevapId == cevapId))
                        {
                            cevap.CevapGrupId = null;
                        }
                        else
                        {
                            db.Cevap.Remove(cevap);
                        }
                    }
                }
            }

            foreach (var cevapForm in aktifCevaplar)
            {
                var cevap = cevapForm.CevapId.HasValue
                    ? mevcutCevaplar.FirstOrDefault(x => x.CevapId == cevapForm.CevapId.Value)
                    : null;

                if (cevap == null)
                {
                    cevap = new Cevap { CevapGrupId = cevapGrupId };
                    db.Cevap.Add(cevap);
                }

                cevap.CevapAdi = WizardMetniGorselEtiketiyle(cevapForm.Metin, cevapForm.Gorsel, "Cevap", 250);
                cevap.CevapGrupId = cevapGrupId;
                cevap.Dogru = sinavModu ? cevapForm.Dogru : (bool?)null;
                cevap.CevapPuan = sinavModu ? (cevapForm.Dogru ? soruPuani : 0) : ClampSurveyScore(cevapForm.Puan);
                kaydedilenCevaplar.Add(cevap);
            }

            db.SaveChanges();

            foreach (var cevap in kaydedilenCevaplar)
            {
                CalismaAlaniBankaKaydinaBagla("Cevap", "CevapId", cevap.CevapId);
            }
        }

        public ActionResult SoruIndex()
        {
            if (Session["id"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }
            if (Session["admin"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }
            return View(CalismaAlaniBankaKayitlari<Soru>("Soru", "SoruAdi"));
        }
        public ActionResult SoruCreate(int? anketId)
        {
            if (Session["id"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }
            if (Session["admin"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }
            SoruFormListeleriniHazirla();

            var form = SoruBankasiFormuHazirla();
            if (anketId.HasValue && AnketCalismaAlanindaMi(anketId))
            {
                form.AnketId = anketId;
                form.SoruGrupId = db.AnketGrup
                    .Where(x => x.AnketId == anketId.Value && x.SoruGrupId.HasValue)
                    .OrderBy(x => x.SoruGrup.SoruGrupSira)
                    .Select(x => x.SoruGrupId)
                    .FirstOrDefault();
            }

            return View(form);
        }
        [ValidateAntiForgeryToken()]
        [HttpPost]
        public ActionResult SoruCreate(SoruBankasiFormModel form)
        {
            if (Session["id"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }
            if (Session["admin"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }

            try
            {
                form ??= new SoruBankasiFormModel();
                if (!SoruBankasiFormunuDogrula(form, true, out var yeniCevapGrubu, out var aktifCevaplar))
                {
                    SoruFormListeleriniHazirla();
                    return View(form);
                }

                using var tx = db.Database.BeginTransaction();
                var cevapGrupId = form.CevapGrupId.GetValueOrDefault();
                if (yeniCevapGrubu)
                {
                    var cevapGrup = SoruBankasiCevapGrubuOlustur(form);
                    cevapGrupId = cevapGrup.CevapGrupId;
                }

                if (yeniCevapGrubu || form.CevaplariGuncelle)
                {
                    SoruBankasiCevaplariniKaydet(cevapGrupId, form, aktifCevaplar, !yeniCevapGrubu);
                }

                var soru = new Soru
                {
                    SoruAdi = WizardMetniGorselEtiketiyle(form.SoruAdi, form.SoruGorsel, "Soru", 250),
                    SoruGrupId = form.SoruGrupId,
                    CevapGrupId = cevapGrupId,
                    SoruSira = form.SoruSira,
                    SoruPuan = form.SoruPuan
                };

                db.Soru.Add(soru);
                db.SaveChanges();
                CalismaAlaniBankaKaydinaBagla("Soru", "SoruId", soru.SoruId);
                if (form.AnketId.HasValue)
                {
                    AnketSoruGrubunuBagla(form.AnketId.Value, soru.SoruGrupId);
                }
                tx.Commit();
                return form.AnketId.HasValue
                    ? RedirectToAction("AnketSoruYonetim", new { id = form.AnketId.Value })
                    : RedirectToAction("SoruIndex");

            }
            catch (Exception ex)
            {
                ModelState.AddModelError("", ex.Message);
                SoruFormListeleriniHazirla();
                return View(form);
            }
        }
        public ActionResult SoruEdit(int id, int? anketId)
        {
            if (Session["id"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }
            if (Session["admin"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }
            SoruFormListeleriniHazirla();

            var kayit = CalismaAlaniBankaKaydiGetir<Soru>("Soru", "SoruId", id);
            if (kayit == null)
            {
                return RedirectToAction("SoruIndex");
            }

            var form = SoruBankasiFormuHazirla(kayit);
            if (anketId.HasValue && AnketCalismaAlanindaMi(anketId))
            {
                form.AnketId = anketId.Value;
            }

            return View(form);
        }
        [ValidateAntiForgeryToken()]
        [HttpPost]
        public ActionResult SoruEdit(SoruBankasiFormModel form)
        {
            if (Session["id"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }
            if (Session["admin"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }

            try
            {
                form ??= new SoruBankasiFormModel();
                if (!SoruBankasiFormunuDogrula(form, false, out var yeniCevapGrubu, out var aktifCevaplar))
                {
                    SoruFormListeleriniHazirla();
                    return View(form);
                }

                using var tx = db.Database.BeginTransaction();
                var kayit = CalismaAlaniBankaKaydiGetir<Soru>("Soru", "SoruId", form.SoruId.Value);
                if (kayit == null)
                {
                    tx.Rollback();
                    return RedirectToAction("SoruIndex");
                }

                var cevapGrupId = form.CevapGrupId.GetValueOrDefault();
                if (yeniCevapGrubu)
                {
                    var cevapGrup = SoruBankasiCevapGrubuOlustur(form);
                    cevapGrupId = cevapGrup.CevapGrupId;
                }

                if (yeniCevapGrubu || form.CevaplariGuncelle)
                {
                    SoruBankasiCevaplariniKaydet(cevapGrupId, form, aktifCevaplar, !yeniCevapGrubu);
                }

                kayit.SoruAdi = WizardMetniGorselEtiketiyle(form.SoruAdi, form.SoruGorsel, "Soru", 250);
                kayit.SoruGrupId = form.SoruGrupId;
                kayit.CevapGrupId = cevapGrupId;
                kayit.SoruSira = form.SoruSira;
                kayit.SoruPuan = form.SoruPuan;
                db.Entry(kayit).State = EntityState.Modified;
                db.SaveChanges();
                CalismaAlaniBankaKaydinaBagla("Soru", "SoruId", kayit.SoruId);
                if (form.AnketId.HasValue)
                {
                    AnketSoruGrubunuBagla(form.AnketId.Value, kayit.SoruGrupId);
                }
                tx.Commit();
                return form.AnketId.HasValue
                    ? RedirectToAction("AnketSoruYonetim", new { id = form.AnketId.Value })
                    : RedirectToAction("SoruIndex");

            }
            catch (Exception ex)
            {
                ModelState.AddModelError("", ex.Message);
                SoruFormListeleriniHazirla();
                return View(form);
            }
        }
        public ActionResult SoruDelete(int id)
        {
            if (Session["id"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }
            if (Session["admin"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }
            var kayit = CalismaAlaniBankaKaydiGetir<Soru>("Soru", "SoruId", id);
            if (kayit == null)
            {
                return RedirectToAction("SoruIndex");
            }

            return View(kayit);
        }
        [ValidateAntiForgeryToken()]
        [HttpPost]
        public ActionResult SoruDelete(int? id)
        {
            if (Session["id"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }
            if (Session["admin"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }
            if (!id.HasValue)
            {
                return RedirectToAction("SoruIndex");
            }

            if (ModelState.IsValid)
            {
                //havuzda kaydÄ± mevcutsa silinmez
                if (!CalismaAlaniBankaKaydiVarMi("Soru", "SoruId", id))
                {
                    return RedirectToAction("SoruIndex");
                }

                if (db.Havuz.Any(x => x.SoruID == id))
                {
                    return RedirectToAction("Hata1", "Ayar", null);
                }
            }

            try
            {
                Soru unv = CalismaAlaniBankaKaydiGetir<Soru>("Soru", "SoruId", id.Value);
                if (unv == null)
                {
                    return RedirectToAction("SoruIndex");
                }

                db.Soru.Remove(unv);
                db.SaveChanges();
                return RedirectToAction("SoruIndex");

            }
            catch
            {
                return View();
            }
        }
        public ActionResult SoruGrupIndex()
        {
            if (Session["id"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }
            if (Session["admin"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }
            return View(CalismaAlaniBankaKayitlari<SoruGrup>("SoruGrup", "SoruGrupAdi"));
        }
        public ActionResult SoruGrupCreate()
        {
            if (Session["id"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }
            if (Session["admin"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }
            return View();
        }
        [ValidateAntiForgeryToken()]
        [HttpPost]
        public ActionResult SoruGrupCreate(SoruGrup dgskn)
        {
            if (Session["id"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }
            if (Session["admin"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }

            try
            {
                db.SoruGrup.Add(dgskn);
                db.SaveChanges();
                CalismaAlaniBankaKaydinaBagla("SoruGrup", "SoruGrupId", dgskn.SoruGrupId);
                return RedirectToAction("SoruGrupIndex");

            }
            catch
            {
                return View();
            }
        }
        public ActionResult SoruGrupEdit(int id)
        {
            if (Session["id"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }
            if (Session["admin"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }

            var kayit = CalismaAlaniBankaKaydiGetir<SoruGrup>("SoruGrup", "SoruGrupId", id);
            if (kayit == null)
            {
                return RedirectToAction("SoruGrupIndex");
            }

            return View(kayit);
        }
        [ValidateAntiForgeryToken()]
        [HttpPost]
        public ActionResult SoruGrupEdit(SoruGrup dgskn)
        {
            if (Session["id"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }
            if (Session["admin"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }

            try
            {
                if (!CalismaAlaniBankaKaydiVarMi("SoruGrup", "SoruGrupId", dgskn.SoruGrupId))
                {
                    ModelState.AddModelError("", "Bu kategori aktif calisma alanina ait degil.");
                    return View(dgskn);
                }

                {
                    db.Entry(dgskn).State = EntityState.Modified;
                    db.SaveChanges();
                    CalismaAlaniBankaKaydinaBagla("SoruGrup", "SoruGrupId", dgskn.SoruGrupId);
                }
                return RedirectToAction("SoruGrupIndex");

            }
            catch
            {
                return View();
            }
        }
        public ActionResult SoruGrupDelete(int id)
        {
            if (Session["id"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }
            if (Session["admin"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }
            var kayit = CalismaAlaniBankaKaydiGetir<SoruGrup>("SoruGrup", "SoruGrupId", id);
            if (kayit == null)
            {
                return RedirectToAction("SoruGrupIndex");
            }

            return View(kayit);
        }
        [ValidateAntiForgeryToken()]
        [HttpPost]
        public ActionResult SoruGrupDelete(int? id)
        {
            if (Session["id"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }
            if (Session["admin"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }
            if (!id.HasValue)
            {
                return RedirectToAction("SoruGrupIndex");
            }

            if (ModelState.IsValid)
            {
                //havuzda kaydÄ± mevcutsa silinmez
                if (!CalismaAlaniBankaKaydiVarMi("SoruGrup", "SoruGrupId", id))
                {
                    return RedirectToAction("SoruGrupIndex");
                }

                if (db.Havuz.Any(x => x.SoruGrupId == id)
                    || db.AnketGrup.Any(x => x.SoruGrupId == id)
                    || db.Soru.Any(x => x.SoruGrupId == id))
                {
                    return RedirectToAction("Hata1", "Ayar", null);
                }
            }

            try
            {
                SoruGrup unv = CalismaAlaniBankaKaydiGetir<SoruGrup>("SoruGrup", "SoruGrupId", id.Value);
                if (unv == null)
                {
                    return RedirectToAction("SoruGrupIndex");
                }

                db.SoruGrup.Remove(unv);
                db.SaveChanges();
                return RedirectToAction("SoruGrupIndex");

            }
            catch
            {
                return View();
            }
        }
        public ActionResult CevapIndex()
        {
            if (Session["id"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }
            if (Session["admin"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }
            return View(CalismaAlaniBankaKayitlari<Cevap>("Cevap", "CevapAdi"));
        }
        public ActionResult CevapCreate()
        {
            if (Session["id"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }
            if (Session["admin"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }

            CevapFormListeleriniHazirla();

            return View();
        }
        [ValidateAntiForgeryToken()]
        [HttpPost]
        public ActionResult CevapCreate(Cevap dgskn)
        {
            if (Session["id"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }
            if (Session["admin"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }

            try
            {
                if (!CalismaAlaniBankaSecimiGecerliMi("CevapGrup", "CevapGrupId", dgskn.CevapGrupId))
                {
                    ModelState.AddModelError("", "Secili cevap grubu bu calisma alanina ait degil.");
                    CevapFormListeleriniHazirla();
                    return View(dgskn);
                }

                db.Cevap.Add(dgskn);
                db.SaveChanges();
                CalismaAlaniBankaKaydinaBagla("Cevap", "CevapId", dgskn.CevapId);
                return RedirectToAction("CevapIndex");

            }
            catch
            {
                CevapFormListeleriniHazirla();
                return View(dgskn);
            }
        }
        public ActionResult CevapEdit(int id)
        {
            if (Session["id"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }
            if (Session["admin"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }
            CevapFormListeleriniHazirla();

            var kayit = CalismaAlaniBankaKaydiGetir<Cevap>("Cevap", "CevapId", id);
            if (kayit == null)
            {
                return RedirectToAction("CevapIndex");
            }

            return View(kayit);
        }
        [ValidateAntiForgeryToken()]
        [HttpPost]
        public ActionResult CevapEdit(Cevap dgskn)
        {
            if (Session["id"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }
            if (Session["admin"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }

            try
            {
                if (!CalismaAlaniBankaKaydiVarMi("Cevap", "CevapId", dgskn.CevapId)
                    || !CalismaAlaniBankaSecimiGecerliMi("CevapGrup", "CevapGrupId", dgskn.CevapGrupId))
                {
                    ModelState.AddModelError("", "Bu cevap veya secili cevap grubu aktif calisma alanina ait degil.");
                    CevapFormListeleriniHazirla();
                    return View(dgskn);
                }

                {
                    db.Entry(dgskn).State = EntityState.Modified;
                    db.SaveChanges();
                    CalismaAlaniBankaKaydinaBagla("Cevap", "CevapId", dgskn.CevapId);
                }
                return RedirectToAction("CevapIndex");

            }
            catch
            {
                CevapFormListeleriniHazirla();
                return View(dgskn);
            }
        }
        public ActionResult CevapDelete(int id)
        {
            if (Session["id"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }
            if (Session["admin"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }
            var kayit = CalismaAlaniBankaKaydiGetir<Cevap>("Cevap", "CevapId", id);
            if (kayit == null)
            {
                return RedirectToAction("CevapIndex");
            }

            return View(kayit);
        }
        [ValidateAntiForgeryToken()]
        [HttpPost]
        public ActionResult CevapDelete(int? id)
        {
            if (Session["id"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }
            if (Session["admin"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }
            if (!id.HasValue)
            {
                return RedirectToAction("CevapIndex");
            }

            if (ModelState.IsValid)
            {
                //havuzda kaydÄ± mevcutsa silinmez
                if (!CalismaAlaniBankaKaydiVarMi("Cevap", "CevapId", id))
                {
                    return RedirectToAction("CevapIndex");
                }

                if (db.Havuz.Any(x => x.CevapId == id))
                {
                    return RedirectToAction("Hata1", "Ayar", null);
                }
            }

            try
            {
                Cevap unv = CalismaAlaniBankaKaydiGetir<Cevap>("Cevap", "CevapId", id.Value);
                if (unv == null)
                {
                    return RedirectToAction("CevapIndex");
                }

                db.Cevap.Remove(unv);
                db.SaveChanges();
                return RedirectToAction("CevapIndex");

            }
            catch
            {
                return View();
            }
        }
        public ActionResult CevapGrupIndex()
        {
            if (Session["id"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }
            if (Session["admin"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }
            return View(CalismaAlaniBankaKayitlari<CevapGrup>("CevapGrup", "CevapGrupAdi"));
        }
        public ActionResult CevapGrupCreate()
        {
            if (Session["id"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }
            if (Session["admin"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }
            return View();
        }
        [ValidateAntiForgeryToken()]
        [HttpPost]
        public ActionResult CevapGrupCreate(CevapGrup dgskn)
        {
            if (Session["id"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }
            if (Session["admin"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }

            try
            {
                dgskn.CevapGrupAdi = (dgskn.CevapGrupAdi ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(dgskn.CevapGrupAdi))
                {
                    ModelState.AddModelError(nameof(dgskn.CevapGrupAdi), "SeÃ§enek grubu adÄ± zorunlu.");
                }
                else if (dgskn.CevapGrupAdi.Length > 250)
                {
                    ModelState.AddModelError(nameof(dgskn.CevapGrupAdi), "SeÃ§enek grubu adÄ± en fazla 250 karakter olabilir.");
                }

                if (!ModelState.IsValid)
                {
                    return View(dgskn);
                }

                db.CevapGrup.Add(dgskn);
                db.SaveChanges();
                CalismaAlaniBankaKaydinaBagla("CevapGrup", "CevapGrupId", dgskn.CevapGrupId);
                return RedirectToAction("CevapGrupIndex");

            }
            catch
            {
                return View(dgskn);
            }
        }
        public ActionResult CevapGrupEdit(int id)
        {
            if (Session["id"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }
            if (Session["admin"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }

            var kayit = CalismaAlaniBankaKaydiGetir<CevapGrup>("CevapGrup", "CevapGrupId", id);
            if (kayit == null)
            {
                return RedirectToAction("CevapGrupIndex");
            }

            return View(kayit);
        }
        [ValidateAntiForgeryToken()]
        [HttpPost]
        public ActionResult CevapGrupEdit(CevapGrup dgskn)
        {
            if (Session["id"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }
            if (Session["admin"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }

            try
            {
                dgskn.CevapGrupAdi = (dgskn.CevapGrupAdi ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(dgskn.CevapGrupAdi))
                {
                    ModelState.AddModelError(nameof(dgskn.CevapGrupAdi), "SeÃ§enek grubu adÄ± zorunlu.");
                }
                else if (dgskn.CevapGrupAdi.Length > 250)
                {
                    ModelState.AddModelError(nameof(dgskn.CevapGrupAdi), "SeÃ§enek grubu adÄ± en fazla 250 karakter olabilir.");
                }

                if (!ModelState.IsValid)
                {
                    return View(dgskn);
                }

                if (!CalismaAlaniBankaKaydiVarMi("CevapGrup", "CevapGrupId", dgskn.CevapGrupId))
                {
                    ModelState.AddModelError("", "Bu cevap grubu aktif calisma alanina ait degil.");
                    return View(dgskn);
                }

                {
                    db.Entry(dgskn).State = EntityState.Modified;
                    db.SaveChanges();
                    CalismaAlaniBankaKaydinaBagla("CevapGrup", "CevapGrupId", dgskn.CevapGrupId);
                }
                return RedirectToAction("CevapGrupIndex");

            }
            catch
            {
                return View(dgskn);
            }
        }
        public ActionResult CevapGrupDelete(int id)
        {
            if (Session["id"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }
            if (Session["admin"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }
            var kayit = CalismaAlaniBankaKaydiGetir<CevapGrup>("CevapGrup", "CevapGrupId", id);
            if (kayit == null)
            {
                return RedirectToAction("CevapGrupIndex");
            }

            return View(kayit);
        }
        [ValidateAntiForgeryToken()]
        [HttpPost]
        public ActionResult CevapGrupDelete(int? id)
        {
            if (Session["id"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }
            if (Session["admin"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }
            if (!id.HasValue)
            {
                return RedirectToAction("CevapGrupIndex");
            }

            if (ModelState.IsValid)
            {
                //havuzda kaydÄ± mevcutsa silinmez
                if (!CalismaAlaniBankaKaydiVarMi("CevapGrup", "CevapGrupId", id))
                {
                    return RedirectToAction("CevapGrupIndex");
                }

                if (db.Havuz.Any(x => x.CevapGrupId == id)
                    || db.Soru.Any(x => x.CevapGrupId == id)
                    || db.Cevap.Any(x => x.CevapGrupId == id))
                {
                    return RedirectToAction("Hata1", "Ayar", null);
                }
            }

            try
            {
                CevapGrup unv = CalismaAlaniBankaKaydiGetir<CevapGrup>("CevapGrup", "CevapGrupId", id.Value);
                if (unv == null)
                {
                    return RedirectToAction("CevapGrupIndex");
                }

                db.CevapGrup.Remove(unv);
                db.SaveChanges();
                return RedirectToAction("CevapGrupIndex");

            }
            catch
            {
                return View();
            }
        }
        public ActionResult AnketGirisIndex(int? kod, int? id)
        {
            if (Session["id"] == null && kod == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }

            ViewBag.kod = kod;
            ViewBag.id = Session["id"];

            // KullanÄ±cÄ±yÄ± Ã§ekelim
            var user = db.User.FirstOrDefault(u => u.UserId == id);
            var adi = user?.UserAdi ?? "TanÄ±msÄ±z KullanÄ±cÄ±";

            if (user == null && kod == null)
                return RedirectToAction("Giris", "Home");

            // KullanÄ±cÄ±nÄ±n Havuzdaki anketleri
            var userAnketIds = db.Havuz
                    .Where(h => h.UserId == id)
                    .Select(h => h.AnketId)
                    .Distinct()
                    .ToList();

            // Ortak query: hem Havuz eÅŸleÅŸmesi hem de Anket filtreleri
            if (kod != null)
            {
                ViewBag.sertifika = null;
                return View(db.Anket.Where(a => a.Pasif != true && a.Tanimsiz != true));

            }
            else
            {


                var sertifika = db.Anket
                        .Where(a => userAnketIds.Contains(a.AnketId))
                        .ToList();
                var anketler1 = db.Anket
                    .Where(a =>
                        (a.DepartmanId == null || a.DepartmanId == user.UserDepartman) &&
                        (a.UnvanId == null || a.UnvanId == user.UserUnvan) &&
                        (a.SehirId == null || a.SehirId == user.UserSehir) &&
                        (a.SubeId == null || a.SubeId == user.UserSube)
                    )
                    .Where(a => a.Pasif != true)
                    .ToList();


                ViewBag.sertifika = sertifika;
                ViewBag.adi = adi;
                return View(anketler1);
            }
        }

        public ActionResult Katilim(int? id, string token)
        {
            var anketId = AnketIdFromKatilimToken(token);
            if (!anketId.HasValue && id.HasValue && Session["admin"] != null)
            {
                return Redirect(KatilimPaylasimUrl(id.Value));
            }

            if (!anketId.HasValue)
            {
                TempData["Mesaj"] = "Katilim baglantisi gecersiz ya da yenilenmis.";
                return RedirectToAction("Giris", "Home");
            }

            var anket = db.Anket.FirstOrDefault(x => x.AnketId == anketId.Value);
            if (anket == null || anket.Pasif == true)
            {
                TempData["Mesaj"] = "Bu Ã§alÄ±ÅŸma yayÄ±nda deÄŸil.";
                return RedirectToAction("Giris", "Home");
            }

            int? kod = null;
            int? publicUserId = null;
            var katilimYontemi = KatilimYontemiGetir(anket.AnketId);
            var kayitliZorunlu = katilimYontemi == KatilimYontemiKayitli || katilimYontemi == KatilimYontemiKisiyeOzel;
            var sessionUserId = Session["admin"] == null ? SessionUserId() : null;

            if (kayitliZorunlu)
            {
                if (!sessionUserId.HasValue)
                {
                    return RedirectToAction("KatilimciDogrula", "Home", new { token });
                }

                if (!KayitliKatilimciCalismayaUygunMu(anket, sessionUserId.Value, out var uygunlukMesaji))
                {
                    TempData["Mesaj"] = uygunlukMesaji;
                    return RedirectToAction("KatilimciDogrula", "Home", new { token });
                }

                publicUserId = sessionUserId.Value;
            }
            else if (katilimYontemi == KatilimYontemiBilgiFormu)
            {
                var bilgiFormuToken = Convert.ToString(Session[BilgiFormuKatilimTokenSessionKey]);
                if (!sessionUserId.HasValue || !string.Equals(bilgiFormuToken, token, StringComparison.Ordinal))
                {
                    return RedirectToAction("KatilimciDogrula", "Home", new { token });
                }

                publicUserId = sessionUserId.Value;
            }
            else if (sessionUserId.HasValue && KayitliKatilimciCalismayaUygunMu(anket, sessionUserId.Value, out _))
            {
                publicUserId = sessionUserId.Value;
            }
            else
            {
                kod = KatilimciKoduAlVeyaOlustur();
                publicUserId = kod;
            }

            if (!AnketKatilimaAcikMi(anket.AnketId, out var yayinMesaji))
            {
                TempData["Mesaj"] = yayinMesaji;
                return RedirectToAction("KatilimPortal", "Home", new { kod, user = publicUserId });
            }

            if (!string.IsNullOrWhiteSpace(anket.Link))
            {
                return RedirectToAction("AnketGirisCreate1", "Home", new { id = anket.AnketId, kod, user = publicUserId });
            }

            return RedirectToAction("AnketGirisCreate2", "Home", new { id = anket.AnketId, kod, user = publicUserId });
        }

        public ActionResult KatilimQr(string token)
        {
            var anketId = AnketIdFromKatilimToken(token);
            if (!anketId.HasValue)
            {
                return NotFound();
            }

            var anket = db.Anket.FirstOrDefault(x => x.AnketId == anketId.Value);
            if (anket == null || anket.Pasif == true)
            {
                return NotFound();
            }

            using var qrGenerator = new QRCodeGenerator();
            using var qrData = qrGenerator.CreateQrCode(KatilimPaylasimUrl(anket.AnketId), QRCodeGenerator.ECCLevel.Q);
            var qrCode = new SvgQRCode(qrData);
            var svg = qrCode.GetGraphic(6, "#152033", "#ffffff", true);

            return Content(svg, "image/svg+xml", Encoding.UTF8);
        }

        private string KatilimPaylasimUrl(int anketId)
        {
            var token = EnsureAnketPaylasimToken(anketId);
            var path = Url.Action("Katilim", "Home", new { token }) ?? $"/Home/Katilim?token={WebUtility.UrlEncode(token)}";
            var request = HttpContext?.Request;
            if (request == null)
            {
                return path;
            }

            return $"{request.Scheme}://{request.Host}{path}";
        }

        private static bool KatilimciKoduGecerliMi(int kod)
        {
            return kod >= 100000000 && kod <= 999999999;
        }

        private int? KatilimciKoduCookieOku()
        {
            if (Request?.Cookies == null)
            {
                return null;
            }

            if (Request.Cookies.TryGetValue(KatilimciKoduCookieAdi, out var raw)
                && int.TryParse(raw, out var kod)
                && KatilimciKoduGecerliMi(kod))
            {
                return kod;
            }

            return null;
        }

        private void KatilimciKoduCookieYaz(int kod)
        {
            if (!KatilimciKoduGecerliMi(kod) || Response?.Cookies == null)
            {
                return;
            }

            Response.Cookies.Append(KatilimciKoduCookieAdi, kod.ToString(), new CookieOptions
            {
                HttpOnly = true,
                Secure = Request?.IsHttps == true,
                SameSite = SameSiteMode.Lax,
                Expires = DateTimeOffset.UtcNow.AddMonths(12)
            });
        }

        private int YeniKatilimciKodu()
        {
            for (var i = 0; i < 20; i++)
            {
                var kod = RandomNumberGenerator.GetInt32(100000000, 999999999);
                var kullanimda = db.Havuz.Any(x => x.Isimsiz == kod || x.UserId == kod)
                    || db.Izledim.Any(x => x.UseId == kod);

                if (!kullanimda)
                {
                    return kod;
                }
            }

            return RandomNumberGenerator.GetInt32(100000000, 999999999);
        }

        private int KatilimciKoduAlVeyaOlustur()
        {
            var mevcutKod = KatilimciKoduCookieOku();
            if (mevcutKod.HasValue)
            {
                return mevcutKod.Value;
            }

            var yeniKod = YeniKatilimciKodu();
            KatilimciKoduCookieYaz(yeniKod);
            return yeniKod;
        }

        private void KatilimciKoduHatirla(int? kod)
        {
            if (kod.HasValue && KatilimciKoduGecerliMi(kod.Value))
            {
                KatilimciKoduCookieYaz(kod.Value);
            }
        }

        private async Task OturumuKapatAsync()
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            Session.Clear();
            Response.Cookies.Delete(KatilimciKoduCookieAdi);
            Response.Cookies.Delete(".ASPXAUTH");
        }

        private int? SessionUserId()
        {
            if (Session["id"] != null && int.TryParse(Session["id"].ToString(), out var sessionUserId))
            {
                return sessionUserId;
            }

            return null;
        }

        private static bool KatilimKoduVarMi(int? kod)
        {
            return kod.HasValue && kod.Value > 0;
        }

        private int KatilimKimligiCoz(int? user, int? kod)
        {
            if (KatilimKoduVarMi(kod))
            {
                return kod.Value;
            }

            return SessionUserId() ?? user.GetValueOrDefault();
        }

        private static string KatilimciKimlikTemizle(string value)
        {
            return Regex.Replace((value ?? string.Empty).Trim(), @"\s+", string.Empty);
        }

        private static string KatilimciMetinTemizle(string value)
        {
            return (value ?? string.Empty).Trim();
        }

        private IQueryable<User> KayitliKatilimciSorgusu()
        {
            return db.User.Where(x =>
                x.Pasif != true
                && (x.UserAdres == null || !x.UserAdres.StartsWith(BilgiFormuKullaniciAdresOnEki)));
        }

        private int? AnketCalismaAlaniIdGetir(int anketId)
        {
            try
            {
                var calismaAlaniId = db.Database.SqlQuery<int?>(
                    "SELECT TOP 1 CalismaAlaniId FROM dbo.Anket WHERE AnketId = @p0",
                    anketId).FirstOrDefault();

                return calismaAlaniId.HasValue && calismaAlaniId.Value > 0 ? calismaAlaniId : null;
            }
            catch
            {
                return null;
            }
        }

        private User CalismaAlaniKatilimcisiGetir(int? calismaAlaniId, string tcKimlikNo, string eposta)
        {
            if (!calismaAlaniId.HasValue)
            {
                return null;
            }

            var bilgiFormuIsareti = BilgiFormuKullaniciAdresOnEki + "%";

            if (!string.IsNullOrWhiteSpace(tcKimlikNo))
            {
                return db.User.SqlQuery(
                    @"SELECT TOP 1 u.*
                      FROM dbo.[User] u
                      WHERE u.UserTc = @p0
                        AND u.CalismaAlaniId = @p1
                        AND ISNULL(u.Pasif, 0) = 0
                        AND (u.UserAdres IS NULL OR u.UserAdres NOT LIKE @p2)
                      ORDER BY u.UserId",
                    tcKimlikNo,
                    calismaAlaniId.Value,
                    bilgiFormuIsareti).FirstOrDefault();
            }

            if (!string.IsNullOrWhiteSpace(eposta))
            {
                return db.User.SqlQuery(
                    @"SELECT TOP 1 u.*
                      FROM dbo.[User] u
                      WHERE u.UserMail = @p0
                        AND u.CalismaAlaniId = @p1
                        AND ISNULL(u.Pasif, 0) = 0
                        AND (u.UserAdres IS NULL OR u.UserAdres NOT LIKE @p2)
                      ORDER BY u.UserId",
                    eposta,
                    calismaAlaniId.Value,
                    bilgiFormuIsareti).FirstOrDefault();
            }

            return null;
        }

        private User CalismaAlaniKatilimcisiGetir(int? calismaAlaniId, int userId)
        {
            if (!calismaAlaniId.HasValue || userId <= 0)
            {
                return null;
            }

            return db.User.SqlQuery(
                @"SELECT TOP 1 u.*
                  FROM dbo.[User] u
                  WHERE u.UserId = @p0
                    AND u.CalismaAlaniId = @p1
                    AND ISNULL(u.Pasif, 0) = 0
                    AND (u.UserAdres IS NULL OR u.UserAdres NOT LIKE @p2)",
                userId,
                calismaAlaniId.Value,
                BilgiFormuKullaniciAdresOnEki + "%").FirstOrDefault();
        }

        private void KatilimciOturumuAc(User user)
        {
            if (user == null)
            {
                return;
            }

            Session.Clear();
            FormsAuthentication.SetAuthCookie(user.UserAdi ?? user.UserTc ?? user.UserMail ?? ("KatÄ±lÄ±mcÄ± " + user.UserId), false);
            Session["id"] = user.UserId;
            Session["adi"] = user.UserAdi;
            Session["tc"] = user.UserTc;
            Session["ipadres"] = GetClientIp();
        }

        private ActionResult KatilimciDogrulamaView(Anket anket, string token, KatilimciDogrulamaFormu form = null, string uyari = null)
        {
            var katilimYontemi = KatilimYontemiGetir(anket.AnketId);
            ViewBag.AnketAdi = anket.AnketAdi;
            ViewBag.KatilimYontemi = katilimYontemi;
            ViewBag.BilgiFormu = katilimYontemi == KatilimYontemiBilgiFormu;
            ViewBag.KayitliZorunlu = katilimYontemi == KatilimYontemiKayitli || katilimYontemi == KatilimYontemiKisiyeOzel;
            ViewBag.Uyari = uyari;

            form ??= new KatilimciDogrulamaFormu();
            form.Token = token;
            return View("KatilimciDogrula", form);
        }

        public ActionResult KatilimciDogrula(string token)
        {
            var anketId = AnketIdFromKatilimToken(token);
            if (!anketId.HasValue)
            {
                TempData["Mesaj"] = "KatÄ±lÄ±m baÄŸlantÄ±sÄ± geÃ§ersiz ya da yenilenmiÅŸ.";
                return RedirectToAction("Giris", "Home");
            }

            var anket = db.Anket.FirstOrDefault(x => x.AnketId == anketId.Value);
            if (anket == null || anket.Pasif == true)
            {
                TempData["Mesaj"] = "Bu Ã§alÄ±ÅŸma yayÄ±nda deÄŸil.";
                return RedirectToAction("Giris", "Home");
            }

            var katilimYontemi = KatilimYontemiGetir(anket.AnketId);
            if (katilimYontemi == KatilimYontemiHerkeseAcik)
            {
                return RedirectToAction("Katilim", "Home", new { token });
            }

            return KatilimciDogrulamaView(anket, token);
        }

        [ValidateAntiForgeryToken(), HttpPost]
        public ActionResult KatilimciDogrula(KatilimciDogrulamaFormu form)
        {
            form ??= new KatilimciDogrulamaFormu();
            form.Token = KatilimciMetinTemizle(form.Token);

            var anketId = AnketIdFromKatilimToken(form.Token);
            if (!anketId.HasValue)
            {
                TempData["Mesaj"] = "KatÄ±lÄ±m baÄŸlantÄ±sÄ± geÃ§ersiz ya da yenilenmiÅŸ.";
                return RedirectToAction("Giris", "Home");
            }

            var anket = db.Anket.FirstOrDefault(x => x.AnketId == anketId.Value);
            if (anket == null || anket.Pasif == true)
            {
                TempData["Mesaj"] = "Bu Ã§alÄ±ÅŸma yayÄ±nda deÄŸil.";
                return RedirectToAction("Giris", "Home");
            }

            var katilimYontemi = KatilimYontemiGetir(anket.AnketId);
            var bilgiFormu = katilimYontemi == KatilimYontemiBilgiFormu;
            var kayitliZorunlu = katilimYontemi == KatilimYontemiKayitli || katilimYontemi == KatilimYontemiKisiyeOzel;
            if (!bilgiFormu && !kayitliZorunlu)
            {
                return RedirectToAction("Katilim", "Home", new { token = form.Token });
            }

            var tcKimlikNo = KatilimciKimlikTemizle(form.TcKimlikNo);
            var adSoyad = KatilimciMetinTemizle(form.AdSoyad);
            var eposta = KatilimciMetinTemizle(form.Eposta);
            var telefon = KatilimciMetinTemizle(form.Telefon);

            if (string.IsNullOrWhiteSpace(tcKimlikNo) && kayitliZorunlu)
            {
                return KatilimciDogrulamaView(anket, form.Token, form, "LÃ¼tfen kayÄ±tlÄ± TC / katÄ±lÄ±mcÄ± numaranÄ±zÄ± yazÄ±n.");
            }

            if (bilgiFormu && string.IsNullOrWhiteSpace(adSoyad))
            {
                return KatilimciDogrulamaView(anket, form.Token, form, "LÃ¼tfen ad soyad bilginizi yazÄ±n.");
            }

            if (bilgiFormu && string.IsNullOrWhiteSpace(tcKimlikNo) && string.IsNullOrWhiteSpace(eposta))
            {
                return KatilimciDogrulamaView(anket, form.Token, form, "KatÄ±lÄ±mÄ± takip edebilmek iÃ§in TC / katÄ±lÄ±mcÄ± numarasÄ± veya e-posta alanlarÄ±ndan en az birini yazÄ±n.");
            }

            User user = null;
            if (kayitliZorunlu)
            {
                user = CalismaAlaniKatilimcisiGetir(
                    AnketCalismaAlaniIdGetir(anket.AnketId),
                    tcKimlikNo,
                    eposta);
            }

            if (kayitliZorunlu && user == null)
            {
                return KatilimciDogrulamaView(anket, form.Token, form, "Bu bilgiyle kayÄ±tlÄ± katÄ±lÄ±mcÄ± bulunamadÄ±. LÃ¼tfen TC / katÄ±lÄ±mcÄ± numarasÄ±nÄ± kontrol edin.");
            }

            if (kayitliZorunlu && !KayitliKatilimciCalismayaUygunMu(anket, user.UserId, out var uygunlukMesaji))
            {
                return KatilimciDogrulamaView(anket, form.Token, form, uygunlukMesaji);
            }

            if (bilgiFormu)
            {
                user = new User
                {
                    UserAdi = adSoyad,
                    UserTc = string.IsNullOrWhiteSpace(tcKimlikNo) ? null : tcKimlikNo,
                    UserMail = string.IsNullOrWhiteSpace(eposta) ? null : eposta,
                    UserTelefon = string.IsNullOrWhiteSpace(telefon) ? null : telefon,
                    UserAdres = BilgiFormuKullaniciAdresOnEki + anket.AnketId,
                    Pasif = false,
                    KayitTarihi = DateTime.Now
                };

                db.User.Add(user);
                db.SaveChanges();
            }

            KatilimciOturumuAc(user);
            if (bilgiFormu)
            {
                Session[BilgiFormuKatilimTokenSessionKey] = form.Token;
            }

            return RedirectToAction("Katilim", "Home", new { token = form.Token });
        }

        private bool KayitliKatilimciCalismayaUygunMu(Anket anket, int userId, out string mesaj)
        {
            mesaj = string.Empty;
            if (anket == null || userId <= 0)
            {
                mesaj = "Bu calisma icin katilimci bilgisi bulunamadi.";
                return false;
            }

            var user = CalismaAlaniKatilimcisiGetir(AnketCalismaAlaniIdGetir(anket.AnketId), userId);
            if (user == null)
            {
                mesaj = "Bu calisma kayitli katilimcilara ozel. Lutfen katilimci hesabi ile giris yapin.";
                return false;
            }

            if (anket.DepartmanId.HasValue && user.UserDepartman != anket.DepartmanId)
            {
                mesaj = "Bu calisma departman hedefi nedeniyle hesabiniz icin acik degil.";
                return false;
            }

            if (anket.UnvanId.HasValue && user.UserUnvan != anket.UnvanId)
            {
                mesaj = "Bu calisma unvan hedefi nedeniyle hesabiniz icin acik degil.";
                return false;
            }

            if (anket.SehirId.HasValue && user.UserSehir != anket.SehirId)
            {
                mesaj = "Bu calisma sehir hedefi nedeniyle hesabiniz icin acik degil.";
                return false;
            }

            if (anket.SubeId.HasValue && user.UserSube != anket.SubeId)
            {
                mesaj = "Bu calisma sube hedefi nedeniyle hesabiniz icin acik degil.";
                return false;
            }

            return true;
        }

        private int ResolveKatilimUseId(int? user, int? kod)
        {
            if (user.HasValue && user.Value > 0)
            {
                return user.Value;
            }

            if (kod.HasValue && kod.Value > 0)
            {
                return kod.Value;
            }

            return SessionUserId() ?? 1;
        }

        private List<Havuz> KatilimHavuzlariniGetir(int anketId, int? kod, int? user)
        {
            var query = db.Havuz
                .Include("Cevap")
                .Include("Soru")
                .Include("User")
                .Include("User.Cinsiyet")
                .Include("User.Departman")
                .Include("User.Egitim")
                .Include("User.Sehir")
                .Include("User.Sube")
                .Include("User.Unvan")
                .Include("User.Yaka")
                .Include("User.Yonetici")
                .Where(x => x.AnketId == anketId);

            if (kod.HasValue && kod.Value > 0 && user.HasValue && user.Value > 0)
            {
                return query.Where(x => x.Isimsiz == kod.Value || x.UserId == user.Value).ToList();
            }

            if (kod.HasValue && kod.Value > 0)
            {
                return query.Where(x => x.Isimsiz == kod.Value).ToList();
            }

            if (user.HasValue && user.Value > 0)
            {
                return query.Where(x => x.UserId == user.Value).ToList();
            }

            return new List<Havuz>();
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

        private Dictionary<int, string> YoneticiResimSozlugu(IEnumerable<int?> yoneticiIds)
        {
            var sonuc = new Dictionary<int, string>();
            var ids = (yoneticiIds ?? Enumerable.Empty<int?>())
                .Where(x => x.HasValue && x.Value > 0)
                .Select(x => x.Value)
                .Distinct()
                .ToList();

            if (!ids.Any() || !YoneticiResimAlaniVarMi())
            {
                return sonuc;
            }

            foreach (var id in ids)
            {
                var resim = db.Database
                    .SqlQuery<string>("SELECT YoneticiResim FROM dbo.Yonetici WHERE YoneticiId = @p0", id)
                    .FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(resim))
                {
                    sonuc[id] = resim;
                }
            }

            return sonuc;
        }

        private void RaporYoneticiResimleriHazirla(IEnumerable<Havuz> kayitlar)
        {
            var liste = (kayitlar ?? Enumerable.Empty<Havuz>()).ToList();
            var sozluk = YoneticiResimSozlugu(liste.Select(x => x.User?.UserYoneticisi));

            ViewBag.YoneticiResimleri = sozluk;
            ViewBag.yoneticiResim10 = liste
                .GroupBy(x => new
                {
                    Id = x.User?.UserYoneticisi ?? 0,
                    Ad = x.User?.Yonetici?.YoneticiAdi ?? "Tanımsız"
                })
                .Select(g => g.Key.Id > 0 && sozluk.TryGetValue(g.Key.Id, out var resim) ? resim : "")
                .ToList();
        }

        private static List<Havuz> SonSoruCevaplari(IEnumerable<Havuz> cevaplar)
        {
            return (cevaplar ?? Enumerable.Empty<Havuz>())
                .Where(x => x.SoruID != null)
                .GroupBy(x => x.SoruID.Value)
                .Select(g => g
                    .OrderByDescending(x => x.KayitTar ?? DateTime.MinValue)
                    .ThenByDescending(x => x.HavuzId)
                    .First())
                .ToList();
        }

        private static double SoruPuaniAl(Havuz cevap)
        {
            var puan = cevap?.SoruPuan ?? cevap?.Soru?.SoruPuan ?? 0;
            return puan > 0 ? puan : 1;
        }

        private static bool AnketTurundeMi(Anket anket)
        {
            if (anket == null)
            {
                return false;
            }

            if (anket.Sinav != true)
            {
                return true;
            }

            var normalized = NormalizeSurveyScoreText(anket.AnketAdi);
            return normalized.Contains("anket")
                || normalized.Contains("memnuniyet")
                || normalized.Contains("geri bildirim")
                || normalized.Contains("geribildirim")
                || normalized.Contains("nabiz")
                || normalized.Contains("algi");
        }

        private static bool SinavTurundeMi(Anket anket)
        {
            return !AnketTurundeMi(anket);
        }

        private static string CalismaTipEtiketi(Anket anket)
        {
            if (AnketTurundeMi(anket))
            {
                return "Anket";
            }

            return string.IsNullOrWhiteSpace(anket?.Link) ? "SÄ±nav" : "EÄŸitim";
        }

        private static double AnketCevapPuaniAl(Havuz cevap)
        {
            var puan = cevap?.Cevap?.CevapPuan ?? cevap?.CevapPuan ?? 0;
            return Math.Max(0, Math.Min(5, puan));
        }

        private double KatilimPuaniHesapla(Anket anket, IEnumerable<Havuz> cevaplar)
        {
            var cevapListesi = SonSoruCevaplari(cevaplar);
            if (!cevapListesi.Any())
            {
                return 0;
            }

            if (SinavTurundeMi(anket))
            {
                var toplamPuan = cevapListesi.Sum(SoruPuaniAl);
                var kazanilanPuan = cevapListesi
                    .Where(x => x.Cevap != null && x.Cevap.Dogru == true)
                    .Sum(SoruPuaniAl);

                return toplamPuan <= 0
                    ? 0
                    : Math.Round(kazanilanPuan / toplamPuan * 100, 2);
            }

            var toplamAgirlik = cevapListesi.Sum(SoruPuaniAl);
            var agirlikliPuan = cevapListesi.Sum(x =>
                AnketCevapPuaniAl(x) * SoruPuaniAl(x));

            return toplamAgirlik <= 0
                ? 0
                : Math.Round(agirlikliPuan / toplamAgirlik * 20, 2);
        }

        private int AnketSoruSayisi(int anketId)
        {
            var soruGrupIds = db.AnketGrup
                .Where(x => x.AnketId == anketId && x.SoruGrupId != null)
                .Select(x => x.SoruGrupId)
                .ToList();

            return db.Soru.Count(x => soruGrupIds.Contains(x.SoruGrupId));
        }

        public ActionResult KatilimPortal(int? kod, int? user)
        {
            if (!KatilimKoduVarMi(kod) && !SessionUserId().HasValue)
            {
                return RedirectToAction("Giris", "Home", new { panel = "participant" });
            }

            var sessionUserId = KatilimKoduVarMi(kod) ? null : SessionUserId();
            var portalUserId = KatilimKoduVarMi(kod) ? null : sessionUserId;
            var effectiveUseId = KatilimKimligiCoz(portalUserId, kod);
            KatilimciKoduHatirla(kod);

            var katilimAnketIds = new List<int>();
            IQueryable<Havuz> havuzKimlikQuery = db.Havuz.Where(x => false);

            if (kod.HasValue && kod.Value > 0 && portalUserId.HasValue && portalUserId.Value > 0)
            {
                havuzKimlikQuery = db.Havuz.Where(x => x.Isimsiz == kod.Value || x.UserId == portalUserId.Value);
            }
            else if (kod.HasValue && kod.Value > 0)
            {
                havuzKimlikQuery = db.Havuz.Where(x => x.Isimsiz == kod.Value);
            }
            else if (portalUserId.HasValue && portalUserId.Value > 0)
            {
                havuzKimlikQuery = db.Havuz.Where(x => x.UserId == portalUserId.Value);
            }

            katilimAnketIds.AddRange(havuzKimlikQuery
                .Where(x => x.AnketId != null)
                .Select(x => x.AnketId.Value)
                .Distinct()
                .ToList());

            katilimAnketIds.AddRange(db.Izledim
                .Where(x => x.UseId == effectiveUseId && x.AnketId != null)
                .Select(x => x.AnketId.Value)
                .Distinct()
                .ToList());

            katilimAnketIds = katilimAnketIds.Distinct().ToList();

            var anketler = db.Anket
                .Where(x => katilimAnketIds.Contains(x.AnketId))
                .OrderByDescending(x => x.AnketId)
                .ToList();

            var model = new KatilimPortalModel
            {
                Kod = kod,
                User = portalUserId,
                KatilimciAdi = Session["adi"]?.ToString() ?? (kod.HasValue ? $"KatÄ±lÄ±mcÄ± #{kod}" : "KatÄ±lÄ±mcÄ±")
            };

            foreach (var anket in anketler)
            {
                var anketTurunde = AnketTurundeMi(anket);
                var sinavTurunde = !anketTurunde;
                var videoGerekli = !string.IsNullOrWhiteSpace(anket.Link);
                var cevaplar = KatilimHavuzlariniGetir(anket.AnketId, kod, portalUserId);
                var toplamSoru = AnketSoruSayisi(anket.AnketId);
                var cevaplanan = cevaplar.Select(x => x.SoruID).Where(x => x != null).Distinct().Count();
                var yayinAcik = AnketKatilimaAcikMi(anket.AnketId, out var yayinMesaji);
                var izleme = db.Izledim
                    .Where(x => x.AnketId == anket.AnketId && x.UseId == effectiveUseId)
                    .OrderByDescending(x => x.IzleId)
                    .FirstOrDefault();
                var puan = KatilimPuaniHesapla(anket, cevaplar);
                var gecmeNotu = sinavTurunde ? (anket.SertifikaNotu ?? 0) : 0;
                var sertifikaAyar = SertifikaAyariniGetir(anket.AnketId);
                var sertifikaVar = sinavTurunde && sertifikaAyar?.SertifikaAktif == true;
                var katilimciSertifikaAlabilir = sertifikaAyar?.SertifikaKatilimciErisimi != false;
                var sureDevamEdiyor = izleme?.BitisZaman != null && izleme.BitisZaman > DateTime.Now;
                var suresiDoldu = izleme?.BitisZaman != null && izleme.BitisZaman <= DateTime.Now;
                var tumSorularTamamlandi = toplamSoru == 0
                    ? izleme != null
                    : cevaplanan >= toplamSoru;
                var tamamlandi = tumSorularTamamlandi;
                var sertifikaZamaniGeldi = SertifikaZamaniGeldiMi(sertifikaAyar, sureDevamEdiyor, tumSorularTamamlandi, out var sertifikaZamanMesaji);
                var sertifikaPuanYeterli = puan >= gecmeNotu;
                var sertifikaDurumMesaji = string.Empty;

                if (sertifikaVar)
                {
                    if (!sertifikaPuanYeterli)
                    {
                        sertifikaDurumMesaji = $"Sertifika iÃ§in puan yetersiz. Gerekli: {gecmeNotu:n0}, mevcut: {puan:n2}.";
                    }
                    else if (!sertifikaZamaniGeldi)
                    {
                        sertifikaDurumMesaji = sertifikaZamanMesaji;
                    }
                    else if (!katilimciSertifikaAlabilir)
                    {
                        sertifikaDurumMesaji = "Sertifika yÃ¶netici tarafÄ±ndan paylaÅŸÄ±lacak.";
                    }
                    else
                    {
                        sertifikaDurumMesaji = "Sertifika hazÄ±r.";
                    }
                }

                model.Calismalar.Add(new KatilimPortalItem
                {
                    AnketId = anket.AnketId,
                    AnketAdi = anket.AnketAdi,
                    Sinav = sinavTurunde,
                    VideoGerekli = videoGerekli,
                    TipEtiketi = CalismaTipEtiketi(anket),
                    SertifikaVar = sertifikaVar,
                    SertifikaHazir = sertifikaVar && sertifikaPuanYeterli && sertifikaZamaniGeldi && katilimciSertifikaAlabilir,
                    SertifikaDurumMesaji = sertifikaDurumMesaji,
                    DevamEdiyor = sureDevamEdiyor,
                    Tamamlandi = tamamlandi,
                    SuresiDoldu = suresiDoldu,
                    YayinAcik = yayinAcik,
                    YayinMesaji = yayinMesaji,
                    Puan = puan,
                    GecmeNotu = gecmeNotu,
                    Cevaplanan = cevaplanan,
                    ToplamSoru = toplamSoru,
                    KatilimTarihi = cevaplar.Max(x => x.KayitTar) ?? izleme?.IzTarih,
                    DurumMetni = !yayinAcik
                        ? yayinMesaji
                        : (sureDevamEdiyor
                            ? "Devam ediyor"
                            : (suresiDoldu ? "SÃ¼re doldu" : (tamamlandi ? "TamamlandÄ±" : "BaÅŸlandÄ±")))
                });
            }

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult KatilimTamamla(int anketId, int? kod, int? user)
        {
            if (!KatilimKoduVarMi(kod) && !SessionUserId().HasValue)
            {
                return Json(new { success = false, message = "KatÄ±lÄ±m bilgisi bulunamadÄ±." });
            }

            var effectiveUseId = KatilimKimligiCoz(user, kod);
            var izleme = db.Izledim.FirstOrDefault(x => x.AnketId == anketId && x.UseId == effectiveUseId);

            if (izleme == null)
            {
                izleme = new Izledim
                {
                    AnketId = anketId,
                    UseId = effectiveUseId,
                    Izledi = true,
                    IzTarih = DateTime.Now,
                    BitisZaman = DateTime.Now
                };
                db.Izledim.Add(izleme);
            }
            else
            {
                izleme.Izledi = true;
                izleme.IzTarih ??= DateTime.Now;
                if (izleme.BitisZaman == null || izleme.BitisZaman > DateTime.Now)
                {
                    izleme.BitisZaman = DateTime.Now;
                }
            }

            db.SaveChanges();

            return Json(new { success = true });
        }

        public ActionResult Anketsertifika(int id, int? kod, int ank, string ankadi)
        {
            if (Session["id"] == null && kod == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }

            var ankadi1 = db.Anket.FirstOrDefault(x => x.AnketId == ank);
            if (ankadi1 == null) return NotFound();

            var sertifikaAyar = SertifikaAyariniGetir(ank);
            var puani = ankadi1.SertifikaNotu ?? 0;
            var userId = id > 0 ? id : (int?)null;
            var effectiveUseId = ResolveKatilimUseId(userId, kod);
            var izll = db.Izledim
                .Where(x => x.AnketId == ank && x.UseId == effectiveUseId)
                .OrderByDescending(x => x.IzleId)
                .FirstOrDefault();
            var bul = KatilimHavuzlariniGetir(ank, kod, userId);

            double p1;
            if (ankadi1.Sinav == true)
            {
                p1 = KatilimPuaniHesapla(ankadi1, bul);
            }
            else
            {
                var soru = bul.Count(); // toplam Soru
                var c5 = bul.Count(x => x.CevapPuan == 5);
                var c4 = bul.Count(x => x.CevapPuan == 4);
                var c3 = bul.Count(x => x.CevapPuan == 3);
                var c2 = bul.Count(x => x.CevapPuan == 2);
                var c1 = bul.Count(x => x.CevapPuan == 1);

                ViewBag.d5 = (float)c5 * 5 / 5 * 100;
                ViewBag.d4 = (float)c4 * 4 / 5 * 100;
                ViewBag.d3 = (float)c3 * 3 / 5 * 100;
                ViewBag.d2 = (float)c2 * 2 / 5 * 100;
                ViewBag.d1 = (float)c1 * 1 / 5 * 100;

                // PuanÄ± hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;
                p1 = soru > 0 ? p2 / soru : 0; // bÃ¶lme hatasÄ±nÄ± engelle
            }

            // VarsayÄ±lan deÄŸerler
            ViewBag.puan = false;
            ViewBag.not = p1;
            ViewBag.snotu = puani;
            ViewBag.sertifikaBaslik = sertifikaAyar?.SertifikaBaslik;
            ViewBag.sertifikaMetni = sertifikaAyar?.SertifikaMetni;
            ViewBag.sertifikaTema = NormalizeSertifikaTema(sertifikaAyar?.SertifikaTema);
            ViewBag.sertifikaLogo = sertifikaAyar?.SertifikaLogo;
            ViewBag.sertifikaVurguRengi = NormalizeSertifikaRengi(sertifikaAyar?.SertifikaVurguRengi);
            ViewBag.sertifikaCerceve = NormalizeSertifikaCerceve(sertifikaAyar?.SertifikaCerceve);
            ViewBag.sertifikaFont = NormalizeSertifikaFont(sertifikaAyar?.SertifikaFont);
            ViewBag.sertifikaYaziPunto = NormalizeSertifikaPunto(sertifikaAyar?.SertifikaYaziPunto, 17, 11, 28);
            ViewBag.sertifikaBaslikPunto = NormalizeSertifikaPunto(sertifikaAyar?.SertifikaBaslikPunto, 44, 24, 72);

            var toplamSoru = AnketSoruSayisi(ank);
            var cevaplanan = bul
                .Where(x => x.SoruID != null)
                .Select(x => x.SoruID.Value)
                .Distinct()
                .Count();
            var tumSorularTamamlandi = toplamSoru == 0 ? izll != null : cevaplanan >= toplamSoru;
            var sureDevamEdiyor = izll?.BitisZaman != null && izll.BitisZaman > DateTime.Now;
            var sertifikaAktif = sertifikaAyar?.SertifikaAktif == true;
            var katilimciSertifikaAlabilir = sertifikaAyar?.SertifikaKatilimciErisimi != false;
            var sertifikaZamaniGeldi = SertifikaZamaniGeldiMi(sertifikaAyar, sureDevamEdiyor, tumSorularTamamlandi, out var sertifikaZamanMesaji);

            if (!sertifikaAktif)
            {
                ViewBag.puan = false;
                ViewBag.mesaj = "Bu Ã§alÄ±ÅŸma iÃ§in sertifika yayÄ±nÄ± kapalÄ±.";
                ViewBag.hideScore = true;
            }
            else if (izll == null)
            {
                ViewBag.puan = false;
                ViewBag.mesaj = "Bu eÄŸitimi bitirmediÄŸiniz iÃ§in sertifika alamazsÄ±nÄ±z.";
            }
            else if (!tumSorularTamamlandi)
            {
                ViewBag.puan = false;
                ViewBag.mesaj = "Sertifika iÃ§in tÃ¼m sorularÄ±n tamamlanmasÄ± gerekiyor.";
                ViewBag.hideScore = true;
            }
            else if (!sertifikaZamaniGeldi)
            {
                ViewBag.puan = false;
                ViewBag.mesaj = sertifikaZamanMesaji;
                ViewBag.hideScore = sureDevamEdiyor;
            }
            else if (p1 < puani)
            {
                ViewBag.puan = false;
                ViewBag.mesaj = "Notunuz yeterli deÄŸil. Sertifika alamazsÄ±nÄ±z.";
                ViewBag.hideScore = false;
            }
            else if (!katilimciSertifikaAlabilir && Session["admin"] == null)
            {
                ViewBag.puan = false;
                ViewBag.mesaj = "Sertifika hazÄ±r; ancak katÄ±lÄ±mcÄ± indirme yetkisi kapalÄ±. Sertifika yÃ¶netici tarafÄ±ndan paylaÅŸÄ±lacak.";
                ViewBag.hideScore = false;
            }
            else
            {
                ViewBag.puan = true;
                ViewBag.mesaj = null;
                ViewBag.hideScore = false;
            }

            var tar = bul.OrderByDescending(a => a.KayitTar).FirstOrDefault();

            ViewBag.tarih = tar?.KayitTar;
            ViewBag.anket = ankadi;
            ViewBag.adSoyad = Session["adi"]?.ToString() ?? (kod.HasValue ? $"KatÄ±lÄ±mcÄ± #{kod}" : "KatÄ±lÄ±mcÄ±");
            if (ankadi1 != null)
            {
                ViewBag.egitimveren = ankadi1.EgitimVeren;
                ViewBag.imza = ankadi1.Imza;
            }

            return View();
        }

        public ActionResult AnketGirisCreate(int id, int? kod, int? user)
        {
            if (!KatilimKoduVarMi(kod) && !SessionUserId().HasValue)
                return RedirectToAction("Giris", "Home", new { panel = "participant" });

            var ank = db.Anket.FirstOrDefault(x => x.AnketId == id);
            if (ank == null) return NotFound();

            var effectiveUseId = KatilimKimligiCoz(user, kod);
            if (!AnketKatilimaAcikMi(id, out var yayinMesaji))
            {
                TempData["Mesaj"] = yayinMesaji;
                return RedirectToAction("KatilimPortal", "Home", new { kod, user = effectiveUseId });
            }

            ViewBag.zaman = ank.Zaman;
            ViewBag.ankadi = ank.AnketAdi;

            // Ä°zledim tablosuna baÅŸlangÄ±Ã§ kaydÄ± yoksa ekle
            var mevcutIzleme = db.Izledim.FirstOrDefault(x => x.AnketId == id && x.UseId == effectiveUseId);
            if (mevcutIzleme == null)
            {
                var iz = new Izledim
                {
                    AnketId = id,
                    UseId = effectiveUseId,
                    Izledi = true,
                    IzTarih = DateTime.Now,
                    BitisZaman = ank.Zaman.HasValue && ank.Zaman.Value > 0
                        ? DateTime.Now.AddMinutes(ank.Zaman.Value)
                        : (DateTime?)null
                };
                db.Izledim.Add(iz);
                db.SaveChanges();
                mevcutIzleme = iz;
            }

            ViewBag.KalanSure = mevcutIzleme.BitisZaman - DateTime.Now;
            ViewBag.sureSinirsiz = ank.Zaman == null || ank.Zaman <= 0;

            // SÃ¼resi bitmiÅŸ mi?
            if (mevcutIzleme.BitisZaman.HasValue && mevcutIzleme.BitisZaman.Value <= DateTime.Now)
            {
                TempData["Mesaj"] = "SÃ¼reniz dolmuÅŸtur. Bu ankete tekrar giriÅŸ yapamazsÄ±nÄ±z.";
                return RedirectToAction("AnketGirisIndex", "Home", new {id = user});
            }


            var sr = db.AnketGrup.Where(x => x.AnketId == id);
            var st = from detay in sr
                     join master in db.Soru on detay.SoruGrupId equals master.SoruGrupId
                     select master;

            ViewBag.kod = kod;
            ViewBag.id = id;

            var hv = db.Havuz.Include("Cevap").Where(x => x.AnketId == id);

            Tumcontroller model = new Tumcontroller()
            {
                AnkGrp = sr,
                Sor = st,
                Hav = hv,
                Cev = db.Cevap
            };

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult AnketGirisCreate(Havuz hav)
        {
            if (Session["id"] == null && (hav.Isimsiz == null || hav.Isimsiz == 0))
                return Json(new { success = false, message = "Oturum bulunamadÄ±" });

            if (!TryApplyTrustedAnswerValues(hav, out var answerError))
            {
                return Json(new { success = false, message = answerError });
            }

            if (hav.AnketId.HasValue && !AnketKatilimaAcikMi(hav.AnketId.Value, out var yayinMesaji))
            {
                return Json(new { success = false, expired = true, message = yayinMesaji });
            }

            int? currentUserId = null;
            if (Session["id"] != null && int.TryParse(Session["id"].ToString(), out var sessionUserId))
            {
                currentUserId = sessionUserId;
            }

            if (currentUserId != null)
            {
                hav.UserId = currentUserId;
                hav.Isimsiz = null;
            }
            else
            {
                hav.UserId = null;
                if (hav.Isimsiz == null || hav.Isimsiz == 0)
                {
                    hav.Isimsiz = 1;
                }
            }

            var mevcut = db.Havuz.FirstOrDefault(x =>
                x.AnketId == hav.AnketId &&
                x.SoruID == hav.SoruID &&
                (
                    (currentUserId != null && x.UserId == currentUserId) ||
                    (currentUserId == null && x.Isimsiz == hav.Isimsiz)
                ));

            if (mevcut != null)
            {
                mevcut.CevapId = hav.CevapId;
                mevcut.CevapGrupId = hav.CevapGrupId;
                mevcut.CevapPuan = hav.CevapPuan;
                mevcut.SoruPuan = hav.SoruPuan;
                mevcut.SoruGrupPuan = hav.SoruGrupPuan;
                mevcut.SoruGrupId = hav.SoruGrupId;
                mevcut.KayitTar = DateTime.Now;
            }
            else
            {
                hav.KayitTar = DateTime.Now;
                db.Havuz.Add(hav);
            }

            db.SaveChanges();

            return Json(new { success = true, havuzId = mevcut?.HavuzId ?? hav.HavuzId, cevapId = hav.CevapId });
        }


        [HttpPost]
        public ActionResult AnketGirisDelete(int id)
        {
            var kayit = db.Havuz.Find(id);
            if (kayit != null)
            {
                db.Havuz.Remove(kayit);
                db.SaveChanges();
                return Json(new { success = true });
            }
            return Json(new { success = false, message = "KayÄ±t bulunamadÄ±" });
        }
        public ActionResult AnketGirisCreate2(int id, int? kod, int? user)
        {
            if (!KatilimKoduVarMi(kod) && !SessionUserId().HasValue)
            {
                return RedirectToAction("Giris", "Home", new { panel = "participant" });
            }
            int effectiveUseId = KatilimKimligiCoz(user, kod);
            if (effectiveUseId <= 0)
            {
                effectiveUseId = 1;
            }

            var izl = db.Izledim.Where(x => x.AnketId == id && x.UseId == effectiveUseId).FirstOrDefault();
            var sr = db.AnketGrup.Where(x => x.AnketId == id);
            var ank = db.Anket.Where(x => x.AnketId == id).FirstOrDefault();
            if (ank == null) return NotFound();

            if (!AnketKatilimaAcikMi(id, out var yayinMesaji))
            {
                TempData["Mesaj"] = yayinMesaji;
                return RedirectToAction("KatilimPortal", "Home", new { kod, user = effectiveUseId });
            }

            var mevcutIzleme = db.Izledim.FirstOrDefault(x => x.AnketId == id && x.UseId == effectiveUseId);
            if (mevcutIzleme == null)
            {
                mevcutIzleme = new Izledim
                {
                    AnketId = id,
                    UseId = effectiveUseId,
                    Izledi = true,
                    IzTarih = DateTime.Now,
                    BitisZaman = ank.Zaman.HasValue && ank.Zaman.Value > 0
                        ? DateTime.Now.AddMinutes(ank.Zaman.Value)
                        : (DateTime?)null
                };
                db.Izledim.Add(mevcutIzleme);
                db.SaveChanges();
            }

            ViewBag.sinav = SinavTurundeMi(ank);
            ViewBag.calismaTuru = CalismaTipEtiketi(ank);
            ViewBag.link = ank.Link;
            ViewBag.zaman = ank.Zaman;
            ViewBag.user = effectiveUseId;
            ViewBag.ankadi = ank.AnketAdi;
            var katilimci = user.HasValue && user.Value > 0
                ? db.User.Include("Cinsiyet").FirstOrDefault(x => x.UserId == user.Value)
                : (!KatilimKoduVarMi(kod) && SessionUserId().HasValue
                    ? db.User.Include("Cinsiyet").FirstOrDefault(x => x.UserId == SessionUserId().Value)
                    : null);
            ViewBag.katilimciAdi = katilimci?.UserAdi;
            ViewBag.katilimciResim = katilimci?.UserResim;
            ViewBag.katilimciCinsiyet = katilimci?.Cinsiyet?.CinsiyetAdi;

            if (izl != null)
            {
                ViewBag.bit = izl.BitisZaman;
                ViewBag.bas = DateTime.Now;
                TimeSpan kalanSure = ViewBag.bit - ViewBag.bas;
                ViewBag.KalanSure = kalanSure;

            }


            var st = from detay in sr
                     join master in db.Soru on detay.SoruGrupId equals master.SoruGrupId
                     select master;


            ViewBag.kod = kod;
            ViewBag.id = id;
            var hv = db.Havuz.Where(x => x.AnketId == id);

            ViewBag.KalanSure = mevcutIzleme.BitisZaman - DateTime.Now;
            ViewBag.sureSinirsiz = ank.Zaman == null || ank.Zaman <= 0;

            // SÃ¼resi bitmiÅŸ mi?
            if (mevcutIzleme.BitisZaman.HasValue && mevcutIzleme.BitisZaman.Value <= DateTime.Now)
            {
                TempData["Mesaj"] = "SÃ¼reniz dolmuÅŸtur. Bu Ã§alÄ±ÅŸmaya tekrar giriÅŸ yapamazsÄ±nÄ±z.";
                return RedirectToAction("KatilimPortal", "Home", new { kod, user = effectiveUseId });
            }

            Tumcontroller model = new Tumcontroller()
            {
                AnkGrp = db.AnketGrup.Where(x => x.AnketId == id),
                Sor = st,
                Hav = hv,
                Cev = db.Cevap,
            };
            return View(model);


        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult AnketGirisCreate2(Havuz hav, int? user)
        {
            // 1) KullanÄ±cÄ± doÄŸrulama
            var publicKod = hav.Isimsiz.HasValue && hav.Isimsiz.Value > 0;
            if (!publicKod && !SessionUserId().HasValue)
                return Json(new { success = false, message = "Oturum bulunamadÄ±" });

            // 2) Ä°zleme kontrolÃ¼
            if (!TryApplyTrustedAnswerValues(hav, out var answerError))
            {
                return Json(new { success = false, message = answerError });
            }

            if (hav.AnketId.HasValue && !AnketKatilimaAcikMi(hav.AnketId.Value, out var yayinMesaji))
            {
                return Json(new { success = false, expired = true, message = yayinMesaji });
            }

            var effectiveUseId = KatilimKimligiCoz(user, publicKod ? hav.Isimsiz : null);
            var izl = db.Izledim.FirstOrDefault(x => x.AnketId == hav.AnketId && x.UseId == effectiveUseId);
            if (izl == null)
            {
                return Json(new { success = false, message = "SÄ±nav oturumu bulunamadÄ±. LÃ¼tfen katÄ±lÄ±m alanÄ±nÄ±zdan tekrar deneyin." });
            }

            if (izl.BitisZaman.HasValue && izl.BitisZaman.Value <= DateTime.Now)
            {
                return Json(new { success = false, expired = true, message = "SÃ¼reniz dolmuÅŸtur. Bu sÄ±nava cevap gÃ¶nderemezsiniz." });
            }

            if (izl != null)
            {
                if (izl.Izledi != true)
                    return Json(new { success = false, message = "Ä°zleme tamamlanmamÄ±ÅŸ" });
            }

            // 3) UserId belirle
            int? currentUserId = null;
            if (!publicKod && Session["id"] != null)
            {
                if (int.TryParse(Session["id"].ToString(), out var uid))
                    currentUserId = uid;
            }
            if (!publicKod && currentUserId == null && user != null && user > 0)
            {
                currentUserId = user;
            }

            // 4) AynÄ± soruya verilmiÅŸ cevabÄ± ara
            var mevcut = db.Havuz.FirstOrDefault(x =>
                x.AnketId == hav.AnketId &&
                x.SoruID == hav.SoruID &&
                (
                    (currentUserId != null && x.UserId == currentUserId) ||
                    (currentUserId == null && x.Isimsiz == hav.Isimsiz)
                )
            );

            if (mevcut != null)
            {
                // GÃ¼ncelle
                mevcut.CevapId = hav.CevapId;
                mevcut.CevapGrupId = hav.CevapGrupId;
                mevcut.CevapPuan = hav.CevapPuan;
                mevcut.SoruPuan = hav.SoruPuan;
                mevcut.SoruGrupPuan = hav.SoruGrupPuan;
                mevcut.SoruGrupId = hav.SoruGrupId;
                mevcut.KayitTar = DateTime.Now;

                if (currentUserId != null)
                {
                    mevcut.UserId = currentUserId;
                    mevcut.Isimsiz = null;
                }
                else
                {
                    mevcut.UserId = null;
                    if (hav.Isimsiz == null || hav.Isimsiz == 0)
                    {
                        hav.Isimsiz = 1;
                    }
                    mevcut.Isimsiz = hav.Isimsiz;
                }
                db.SaveChanges();

                return Json(new { success = true, havuzId = mevcut.HavuzId, cevapId = mevcut.CevapId });
            }
            else
            {
                // Yeni kayÄ±t
                hav.KayitTar = DateTime.Now;
                if (currentUserId != null)
                {
                    hav.UserId = currentUserId;
                    hav.Isimsiz = null;
                }
                else
                {
                    hav.UserId = null;
                    if (hav.Isimsiz == null || hav.Isimsiz == 0)
                    {
                        hav.Isimsiz = 1;
                    }
                }

                db.Havuz.Add(hav);
                db.SaveChanges();

                return Json(new { success = true, havuzId = hav.HavuzId, cevapId = hav.CevapId });
            }
        }
        public ActionResult AnketGirisCreate1(int id, int? kod, int? user)
        {

            if (!KatilimKoduVarMi(kod) && !SessionUserId().HasValue)
            {
                return RedirectToAction("Giris", "Home", new { panel = "participant" });
            }

            var sr = db.AnketGrup.Where(x => x.AnketId == id);
            var effectiveUseId = KatilimKimligiCoz(user, kod);
            var izl = db.Izledim.Any(x => x.UseId == effectiveUseId && x.AnketId == id);
            var mevcutIzleme = db.Izledim.FirstOrDefault(x => x.AnketId == id && x.UseId == effectiveUseId);

            // SÃ¼resi bitmiÅŸ mi?
            var ank = db.Anket.Where(x => x.AnketId == id).FirstOrDefault();
            if (ank == null) return NotFound();

            if (!AnketKatilimaAcikMi(id, out var yayinMesaji))
            {
                TempData["Mesaj"] = yayinMesaji;
                return RedirectToAction("KatilimPortal", "Home", new { kod, user = effectiveUseId });
            }

            if (mevcutIzleme?.BitisZaman != null && mevcutIzleme.BitisZaman.Value <= DateTime.Now)
            {
                TempData["Mesaj"] = "SÃ¼reniz dolmuÅŸtur. Bu Ã§alÄ±ÅŸmaya tekrar giriÅŸ yapamazsÄ±nÄ±z.";
                return RedirectToAction("KatilimPortal", "Home", new { kod, user = effectiveUseId });
            }

            if (izl == true)
            {
                ViewBag.izledi = true;
            }
            ViewBag.sinav = SinavTurundeMi(ank);
            ViewBag.calismaTuru = CalismaTipEtiketi(ank);
            ViewBag.link = ank.Link;
            ViewBag.zaman = ank.Zaman;
            ViewBag.user = effectiveUseId;
            ViewBag.ankadi = ank.AnketAdi;
            var katilimci = user.HasValue && user.Value > 0
                ? db.User.Include("Cinsiyet").FirstOrDefault(x => x.UserId == user.Value)
                : (!KatilimKoduVarMi(kod) && SessionUserId().HasValue
                    ? db.User.Include("Cinsiyet").FirstOrDefault(x => x.UserId == SessionUserId().Value)
                    : null);
            ViewBag.katilimciAdi = katilimci?.UserAdi;
            ViewBag.katilimciResim = katilimci?.UserResim;
            ViewBag.katilimciCinsiyet = katilimci?.Cinsiyet?.CinsiyetAdi;

            var izll = db.Izledim.Where(x => x.AnketId == id && x.UseId == effectiveUseId).FirstOrDefault();


            var st = from detay in sr
                     join master in db.Soru on detay.SoruGrupId equals master.SoruGrupId
                     select master;

            var anketGruplari = sr.ToList();
            var sorular = st
                .OrderBy(x => x.SoruSira ?? int.MaxValue)
                .ThenBy(x => x.SoruId)
                .ToList();

            ViewBag.soruSayisi = sorular.Select(x => x.SoruId).Distinct().Count();
            ViewBag.grupSayisi = anketGruplari
                .Where(x => x.SoruGrupId.HasValue)
                .Select(x => x.SoruGrupId.Value)
                .Distinct()
                .Count();
            ViewBag.toplamPuan = sorular.Sum(x => x.SoruPuan ?? 0);
            ViewBag.sertifikaNotu = ank.SertifikaNotu;
            ViewBag.sonucAcik = ank.Sonuc == true;
            ViewBag.egitimVeren = ank.EgitimVeren;
            ViewBag.izlemeBaslangic = mevcutIzleme?.IzTarih;
            ViewBag.izlemeBitis = mevcutIzleme?.BitisZaman;

            ViewBag.kod = kod;
            ViewBag.id = id;
            var hv = db.Havuz.Where(x => x.AnketId == id);
            Tumcontroller model = new Tumcontroller()
            {
                AnkGrp = anketGruplari,
                Sor = sorular,
                Hav = hv,
                Cev = db.Cevap,
            };
            return View(model);


        }

        [ValidateAntiForgeryToken()]
        [HttpPost]
        public ActionResult Izledim(Izledim izl, int? kod, int? user)
        {
            if (!KatilimKoduVarMi(kod) && !SessionUserId().HasValue)
            {
                return RedirectToAction("Giris", "Home", new { panel = "participant" });
            }

            var effectiveUserId = KatilimKimligiCoz(user, kod);
            if (effectiveUserId <= 0)
            {
                effectiveUserId = 1;
            }

            var anket = db.Anket.FirstOrDefault(x => x.AnketId == izl.AnketId);
            if (izl.AnketId.HasValue && !AnketKatilimaAcikMi(izl.AnketId.Value, out var yayinMesaji))
            {
                TempData["Mesaj"] = yayinMesaji;
                return RedirectToAction("KatilimPortal", "Home", new { kod, user = effectiveUserId });
            }

            DateTime? yeniBitisZaman = anket?.Zaman != null && anket.Zaman > 0
                ? DateTime.Now.AddMinutes(anket.Zaman.Value)
                : null;

            var mevcut = db.Izledim.FirstOrDefault(x => x.AnketId == izl.AnketId && x.UseId == effectiveUserId);
            if (mevcut == null)
            {
                izl.UseId = effectiveUserId;
                izl.IzTarih = DateTime.Now;
                izl.BitisZaman = yeniBitisZaman;
                db.Izledim.Add(izl);
            }
            else
            {
                if (mevcut.BitisZaman.HasValue && mevcut.BitisZaman.Value <= DateTime.Now)
                {
                    TempData["Mesaj"] = "SÃ¼reniz dolmuÅŸtur. Bu sÄ±nava tekrar giriÅŸ yapamazsÄ±nÄ±z.";
                    return RedirectToAction("KatilimPortal", "Home", new { kod, user = effectiveUserId });
                }

                mevcut.Izledi = true;
                if (mevcut.IzTarih == null)
                {
                    mevcut.IzTarih = DateTime.Now;
                }
                if (mevcut.BitisZaman == null)
                {
                    mevcut.BitisZaman = yeniBitisZaman;
                }
            }

            db.SaveChanges();

            return RedirectToAction("AnketGirisCreate2", "Home", new { id = izl.AnketId, user = effectiveUserId, kod });

        }
        public ActionResult AnketGirisEdit(int id)
        {
            if (Session["id"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }

            return View(db.Havuz.Where(x => x.HavuzId == id).FirstOrDefault());
        }
        [ValidateAntiForgeryToken()]
        [HttpPost]
        public ActionResult AnketGirisEdit(Havuz dgskn)
        {
            if (Session["id"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }

            try
            {
                {
                    db.Entry(dgskn).State = EntityState.Modified;
                    db.SaveChanges();
                }
                return RedirectToAction("AnketGirisCreate");
            }
            catch
            {
                return View();
            }
        }

        public ActionResult AnketGirisDelete(int id, int? kod, int? ank)
        {
            if (Session["id"] == null && kod == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }

            Havuz hav = db.Havuz.Where(x => x.HavuzId == id).FirstOrDefault();
            db.Havuz.Remove(hav);
            db.SaveChanges();
            return RedirectToAction("AnketGirisCreate", "Home", new { id = ank, kod });
        }
        public ActionResult AnketGirisDelete2(int id, int? kod, int? ank, int? user)
        {
            if (Session["id"] == null && kod == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }

            var izll = db.Izledim.Where(x => x.AnketId == ank && x.UseId == user).FirstOrDefault();

            if (izll != null)
            {
                if (izll.BitisZaman < DateTime.Now)
                {
                    return RedirectToAction("Hata5", "Home", null);
                }
            }


            Havuz hav = db.Havuz.Where(x => x.HavuzId == id).FirstOrDefault();
            db.Havuz.Remove(hav);
            db.SaveChanges();
            if (user != null)
            {
                return RedirectToAction("AnketGirisCreate2", "Home", new { id = ank, kod, user });
            }
            else
            {
                return RedirectToAction("AnketGirisCreate2", "Home", new { id = ank, kod, user = 1 });
            }

        }

        public ActionResult AnketHavuzIndex(int id)
        {
            if (Session["id"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }
            if (Session["admin"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }
            ViewBag.id = id;
            var anketIdleri = CalismaAlaniAnketleri().Select(x => (int?)x.AnketId).ToList();
            var havuz = db.Havuz
                .Include("Anket")
                .Include("User")
                .Where(x => anketIdleri.Contains(x.AnketId))
                .ToList();

            return View(havuz);
        }
        public ActionResult AnketHavuzIndex2(int id)
        {
            if (Session["id"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }
            if (Session["admin"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }
            if (!AnketCalismaAlanindaMi(id))
            {
                return RedirectToAction("AnketAdIndex");
            }

            var ad = db.Havuz.Include("Anket").Where(x => x.AnketId == id).FirstOrDefault();
            if (ad != null)
            {
                ViewBag.ankadi = ad.Anket != null ? ad.Anket.AnketAdi : null;
            }
            if (string.IsNullOrWhiteSpace(Convert.ToString(ViewBag.ankadi)))
            {
                var anket = CalismaAlaniAnketGetir(id);
                ViewBag.ankadi = anket != null ? anket.AnketAdi : "Ã‡alÄ±ÅŸma";
            }
            ViewBag.id = id;

            return View(db.Havuz
                .Include("Anket")
                .Include("Cevap")
                .Include("Soru")
                .Include("SoruGrup")
                .Include("User")
                .Where(x => x.AnketId == id)
                .ToList());
        }
        public ActionResult AnketHavuzIndex1(int id, int? ank, int? sor)
        {
            if (Session["id"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }
            if (Session["admin"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }
            if (!AnketCalismaAlanindaMi(ank))
            {
                return RedirectToAction("AnketAdIndex");
            }

            var ad = db.Havuz.Include("Anket").Where(x => x.AnketId == ank).FirstOrDefault();
            if (ad != null)
            {
                ViewBag.ankadi = ad.Anket != null ? ad.Anket.AnketAdi : null;
            }
            if (string.IsNullOrWhiteSpace(Convert.ToString(ViewBag.ankadi)) && ank.HasValue)
            {
                var anket = CalismaAlaniAnketGetir(ank.Value);
                ViewBag.ankadi = anket != null ? anket.AnketAdi : "Ã‡alÄ±ÅŸma";
            }

            var so = db.Havuz
                .Include("Anket")
                .Include("Cevap")
                .Include("Soru")
                .Include("SoruGrup")
                .Include("User")
                .Where(x => x.AnketId == ank);
            var soo = so.Where(x => x.SoruGrupId == id);
            var grup = db.SoruGrup.Find(id);

            ViewBag.id = id;
            ViewBag.ank = ank;
            ViewBag.sor = sor;
            ViewBag.grupadi = grup != null ? grup.SoruGrupAdi : "Soru grubu";
            return View(soo.ToList());
        }
        public ActionResult AnketHavuzDelete(int id)
        {
            if (Session["id"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }
            if (Session["admin"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }
            if (!AnketCalismaAlanindaMi(id))
            {
                return RedirectToAction("AnketAdIndex");
            }

            var bul = db.Havuz.Where(x => x.AnketId == id);

            var ad = CalismaAlaniAnketGetir(id);
            ViewBag.adi = ad != null ? ad.AnketAdi : "Ã‡alÄ±ÅŸma";

            var item3 = bul.Where(x => x.UserId != 1);
            var item4 = bul.Where(x => x.Isimsiz != null);

            var ktll = item3.GroupBy(x => x.UserId);
            ViewBag.tanimli = ktll.Count();

            var ktll1 = item4.GroupBy(x => x.Isimsiz);
            ViewBag.tanimsiz = ktll1.Count();

            ViewBag.id = new List<int>();   //AdÄ±
            ViewBag.ank = id;

            foreach (var item in bul)
            {
                ViewBag.id.Add(item.HavuzId);
            }
            return View(db.Havuz
                .Include("Anket")
                .Where(x => x.AnketId == id)
                .FirstOrDefault());

        }
        [ValidateAntiForgeryToken()]
        [HttpPost]
        public ActionResult AnketHavuzDelete(int[] ids)
        {
            if (Session["id"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }
            if (Session["admin"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }
            try
            {

                // Her bir ID iÃ§in tabloyu bul ve sil
                foreach (var id in ids ?? Array.Empty<int>())
                {
                    var tablo = db.Havuz.Find(id);
                    if (tablo != null)
                    {
                        if (!AnketCalismaAlanindaMi(tablo.AnketId))
                        {
                            return NotFound();
                        }

                        db.Havuz.Remove(tablo);
                    }
                }

                db.SaveChanges();
                return RedirectToAction("AnketHavuzIndex", new { id = Session["id"] });

            }
            catch
            {
                return View();
            }

        }
        public ActionResult AnketHavuzDelete2(int id, int? ank)
        {
            if (Session["id"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }
            if (Session["admin"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }
            if (!AnketCalismaAlanindaMi(ank))
            {
                return RedirectToAction("AnketIndex");
            }

            var bul1 = db.Havuz.Where(x => x.AnketId == ank);
            var bul = bul1.Where(x => x.SoruGrupId == id);

            ViewBag.id = new List<int>();   //AdÄ±
            ViewBag.ank = ank;

            foreach (var item in bul)
            {
                ViewBag.id.Add(item.HavuzId);
            }
            return View(bul
                .Include("SoruGrup")
                .Include("Anket")
                .FirstOrDefault());

        }
        [ValidateAntiForgeryToken()]
        [HttpPost]
        public ActionResult AnketHavuzDelete2(int[] ids, int? ank)
        {
            if (Session["id"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }
            if (Session["admin"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }
            try
            {
                // Her bir ID iÃ§in tabloyu bul ve sil
                foreach (var id in ids ?? Array.Empty<int>())
                {
                    var tablo = db.Havuz.Find(id);
                    if (tablo != null)
                    {
                        db.Havuz.Remove(tablo);
                    }
                }

                db.SaveChanges();
                return RedirectToAction("AnketHavuzIndex2", new { id = ank });

            }
            catch
            {
                return View();
            }

        }
        public ActionResult AnketHavuzDelete1(int id, int? ank, int? sor)
        {
            if (Session["id"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }
            if (Session["admin"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }

            ViewBag.id = id;
            ViewBag.ank = ank;
            ViewBag.sor = sor;

            return View(db.Havuz
                .Include("Soru")
                .Include("Cevap")
                .Include("SoruGrup")
                .Include("Anket")
                .Include("User")
                .Where(x => x.HavuzId == id)
                .FirstOrDefault());
        }
        [ValidateAntiForgeryToken()]
        [HttpPost]
        public ActionResult AnketHavuzDelete1(int? id, int? ank, int? sor)
        {
            if (Session["id"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }
            if (Session["admin"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }

            try
            {
                Havuz hav = db.Havuz.Where(x => x.HavuzId == id).FirstOrDefault();
                if (hav != null)
                {
                    db.Havuz.Remove(hav);
                    db.SaveChanges();
                }
                return RedirectToAction("AnketHavuzIndex1", "Home", new { id = sor, ank });

            }
            catch
            {
                return View();
            }
        }

        public ActionResult AnketZamanIndex()
        {
            if (Session["id"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }
            if (Session["admin"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }

            var anketler = CalismaAlaniAnketleri();
            var anketIdleri = anketler.Select(x => (int?)x.AnketId).ToList();
            var izlemeKayitlari = db.Izledim
                .Where(x => anketIdleri.Contains(x.AnketId))
                .ToList();

            ViewBag.ZamanToplam = izlemeKayitlari
                .Where(x => x.AnketId.HasValue)
                .GroupBy(x => x.AnketId.Value)
                .ToDictionary(x => x.Key, x => x.Count());
            ViewBag.ZamanDevam = izlemeKayitlari
                .Where(x => x.AnketId.HasValue && x.BitisZaman.HasValue && x.BitisZaman.Value > DateTime.Now)
                .GroupBy(x => x.AnketId.Value)
                .ToDictionary(x => x.Key, x => x.Count());
            ViewBag.ZamanBitti = izlemeKayitlari
                .Where(x => x.AnketId.HasValue && x.BitisZaman.HasValue && x.BitisZaman.Value <= DateTime.Now)
                .GroupBy(x => x.AnketId.Value)
                .ToDictionary(x => x.Key, x => x.Count());
            ViewBag.ZamanCevapliKatilim = db.Havuz
                .Where(x => anketIdleri.Contains(x.AnketId))
                .ToList()
                .Where(x => x.AnketId.HasValue)
                .GroupBy(x => x.AnketId.Value)
                .ToDictionary(
                    x => x.Key,
                    x => x
                        .Select(k => k.UserId.HasValue && k.UserId.Value != 1
                            ? "u:" + k.UserId.Value
                            : k.Isimsiz.HasValue
                                ? "k:" + k.Isimsiz.Value
                                : null)
                        .Where(k => !string.IsNullOrWhiteSpace(k))
                        .Distinct()
                        .Count());

            var izlenenAnketIdleri = izlemeKayitlari
                .Where(x => x.AnketId.HasValue)
                .Select(x => x.AnketId.Value)
                .Distinct()
                .ToList();

            return View(anketler
                .Where(x => izlenenAnketIdleri.Contains(x.AnketId))
                .OrderByDescending(x => x.AnketId)
                .ToList());
        }

        public ActionResult AnketZamanIndex1(int id)
        {
            if (Session["id"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }
            if (Session["admin"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }
            var a = CalismaAlaniAnketleri().FirstOrDefault(x => x.AnketId == id);
            if (a == null)
            {
                return NotFound();
            }

            ViewBag.zaman = a.Zaman;
            ViewBag.anketAdi = a.AnketAdi;
            ViewBag.anketId = a.AnketId;
            ViewBag.CevapliKatilimKeys = new HashSet<string>(
                db.Havuz
                    .Where(x => x.AnketId == id)
                    .ToList()
                    .SelectMany(x =>
                    {
                        var keys = new List<string>();
                        if (x.UserId.HasValue && x.UserId.Value != 1)
                        {
                            keys.Add("u:" + x.UserId.Value);
                        }
                        if (x.Isimsiz.HasValue)
                        {
                            keys.Add("k:" + x.Isimsiz.Value);
                        }
                        return keys;
                    }),
                StringComparer.Ordinal);


            return View(db.Izledim
                .Include("User")
                .Include("Anket")
                .Where(x => x.AnketId == id)
                .OrderByDescending(x => x.IzTarih)
                .ToList());
        }
        public ActionResult AnketZamanDelete(int id, string adi, string ank, int ankid)
        {
            if (Session["id"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }
            if (Session["admin"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }
            ViewBag.id = id;
            ViewBag.adi = adi;
            ViewBag.ank = ank;
            ViewBag.ankid = ankid;
            var kayit = db.Izledim
                .Include("User")
                .Include("Anket")
                .FirstOrDefault(x => x.IzleId == id);

            if (kayit == null)
            {
                return NotFound();
            }

            ViewBag.adi = kayit.User?.UserAdi ?? adi;
            ViewBag.ank = kayit.Anket?.AnketAdi ?? ank;
            ViewBag.ankid = kayit.AnketId ?? ankid;
            return View(kayit);
        }
        [ValidateAntiForgeryToken()]
        [HttpPost]
        public ActionResult AnketZamanDelete(int id, int ank)
        {
            if (Session["id"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }
            if (Session["admin"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }

            try
            {
                Izledim unv = db.Izledim.Where(x => x.IzleId == id).FirstOrDefault();
                if (unv == null)
                {
                    return RedirectToAction("AnketZamanIndex1", new { id = ank });
                }

                var anketId = unv.AnketId ?? ank;
                db.Izledim.Remove(unv);
                db.SaveChanges();
                return RedirectToAction("AnketZamanIndex1", new { id = anketId });

            }
            catch
            {
                return View();
            }
        }

        public ActionResult Hata()
        {
            return View();

        }
        public ActionResult Hata1()
        {
            return View();

        }
        public ActionResult Hata2()
        {
            return View();

        }
        public ActionResult Hata3()
        {
            return View();

        }
        public ActionResult Hata4()
        {
            return View();

        }
        public ActionResult Hata5()
        {
            return View();

        }
        public ActionResult Hata6()
        {
            return View();

        }

        public ActionResult Hakkinda(int id)
        {
            if (Session["id"] == null || Session["id"].ToString() != id.ToString())
            {
                return RedirectToAction("Giris", "Home", null);
            }
            var sor = db.LisansKyn.Where(x => x.LisansId.Equals(1)).FirstOrDefault();
            var sor1 = dbl.Lisans.Where(x => x.IpAdress == sor.IpAdress).FirstOrDefault();
            if (sor1 != null)
            {
                ViewBag.hak = sor1.ModulSayi;
            }
            else
            {
                ViewBag.hak = 0;
            }

            return View();
        }


    }
}
