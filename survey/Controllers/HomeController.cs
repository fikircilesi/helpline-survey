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
using System.Web.Security;

namespace survey.Controllers
{
    public class HomeController : LegacyController
    {
        readonly SurveyEntities db = new SurveyEntities();
        readonly EnvanterTakipLisansEntities dbl = new EnvanterTakipLisansEntities();
        const int MailOnayKoduGecerlilikDakika = 20;
        const string KatilimciKoduCookieAdi = "evalio_participant_code";

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
            public Dictionary<int, int> DogruCevaplar { get; set; } = new Dictionary<int, int>();
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
            ViewBag.RecaptchaAktif = GoogleRecaptchaAktifMi();
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

            var token = Request.Form["g-recaptcha-response"].FirstOrDefault();
            if (string.IsNullOrWhiteSpace(token))
            {
                return new RecaptchaDogrulamaSonucu
                {
                    Basarili = false,
                    Mesaj = "Ben robot değilim kutusunu işaretleyin."
                };
            }

            if (LocalhostGelistirmeOrtamiMi())
            {
                return new RecaptchaDogrulamaSonucu { Basarili = true };
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
                        Mesaj = "Google reCAPTCHA servisine ulaşılamadı. HTTP " + (int)response.StatusCode
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
                    Mesaj = "Google reCAPTCHA doğrulaması tamamlanamadı: " + ex.Message
                };
            }
        }

        private static string RecaptchaHataMesaji(List<string> hataKodlari)
        {
            if (hataKodlari == null || !hataKodlari.Any())
            {
                return "Google reCAPTCHA doğrulaması başarısız oldu. Kutuyu tekrar işaretleyin.";
            }

            if (hataKodlari.Contains("timeout-or-duplicate"))
            {
                return "reCAPTCHA süresi doldu. Kutuyu tekrar işaretleyin.";
            }

            if (hataKodlari.Contains("invalid-input-secret"))
            {
                return "reCAPTCHA gizli anahtarı geçersiz. appsettings.json içindeki SecretKey kontrol edilmeli.";
            }

            if (hataKodlari.Contains("invalid-input-response") || hataKodlari.Contains("missing-input-response"))
            {
                return "reCAPTCHA cevabı Google tarafından kabul edilmedi. Site Key ve Secret Key eşleşmesini, localhost domain ayarını kontrol edin. Kod: " + string.Join(", ", hataKodlari);
            }

            return "Google reCAPTCHA doğrulaması başarısız oldu: " + string.Join(", ", hataKodlari);
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
                    string.IsNullOrWhiteSpace(calismaAlaniAdi) ? "Çalışma Alanım" : calismaAlaniAdi,
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
            Session["admin"] = personel.Admin;
            Session["resim"] = personel.Resim;
            Session["ipadres"] = GetClientIp();

            var calismaAlaniId = CalismaAlaniHazirla(
                personel.PersonelId,
                personel.Adres,
                $"{personel.PersonelAdi} Çalışma Alanı");

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
                // Güvenlik kolonları SQL tarafında henüz eklenmemişse giriş akışını kırmayalım.
            }
        }

        private int? AktifCalismaAlaniId()
        {
            var deger = Session["CalismaAlaniId"] as string;
            if (int.TryParse(deger, out var calismaAlaniId) && calismaAlaniId > 0)
            {
                return calismaAlaniId;
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
                hataMesaji = "SMTP ayarı bulunamadı.";
                return false;
            }

            try
            {
                using var msg = new MailMessage();
                msg.To.Add(email);
                msg.From = new MailAddress(smtpAyar.Gonderen);
                msg.Subject = "Aslana Survey Studio e-posta doğrulama kodu";
                msg.IsBodyHtml = true;
                msg.BodyEncoding = Encoding.UTF8;
                msg.Body =
                    $"<h2>Merhaba {WebUtility.HtmlEncode(adSoyad)},</h2>" +
                    "<p>Aslana Survey Studio hesabınızı aktifleştirmek için doğrulama kodunuz:</p>" +
                    $"<h1 style=\"letter-spacing:6px\">{kod}</h1>" +
                    $"<p>Bu kod {MailOnayKoduGecerlilikDakika} dakika geçerlidir.</p>";

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

        public ActionResult Giris()
        {
            Session.Clear();
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
                ViewBag.Uyari = "Güvenlik sorusunu kontrol edin.";
                CaptchaYenile();
                return View();
            }

            var obj = db.Personel.Where(a => a.KullaniciAdi.Equals(objUser) && a.Sifre.Equals(objUser1)).FirstOrDefault();

            if (obj != null && obj.KullaniciAdi == objUser && obj.Sifre == objUser1 && obj.Pasif != true)
            {
                var guvenlik = GuvenlikBilgisiGetir(obj.PersonelId);
                if (guvenlik?.MailOnaylandi == false)
                {
                    ViewBag.Uyari = "E-posta onayınız tamamlanmamış. Mail adresinize gelen kodu onaylayın.";
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
                ViewBag.Uyari = "Kullanıcı Adı veya Şifreyi Kontrol Ediniz";
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
                ViewBag.KatilimSorguError = "Katılım kodu 9 haneli olmalı.";
                ViewBag.ActiveAuthPanel = "participant";
                CaptchaYenile();
                return View("Giris");
            }

            var kodKullanilmis = db.Havuz.Any(x => x.Isimsiz == kod || x.UserId == kod)
                || db.Izledim.Any(x => x.UseId == kod);

            if (!kodKullanilmis)
            {
                ViewBag.KatilimSorguError = "Bu koda ait katılım kaydı bulunamadı.";
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
                ViewBag.RegisterError = "Güvenlik sorusunu kontrol edin.";
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
                ViewBag.RegisterError = "Lütfen zorunlu alanları doldurun.";
                ViewBag.ActiveAuthPanel = "register";
                CaptchaYenile();
                return View("Giris");
            }

            if (!string.Equals(password, passwordConfirm, StringComparison.Ordinal))
            {
                ViewBag.RegisterError = "Şifreler birbiriyle aynı olmalı.";
                ViewBag.ActiveAuthPanel = "register";
                CaptchaYenile();
                return View("Giris");
            }

            username = username.Trim();
            email = email.Trim();

            var kullaniciVar = db.Personel.Any(x => x.KullaniciAdi == username || x.Mail == email);
            if (kullaniciVar)
            {
                ViewBag.RegisterError = "Bu kullanıcı adı veya e-posta ile kayıt var.";
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
                ViewBag.RegisterError = "Güvenlik kolonları SQL tarafında henüz eklenmemiş görünüyor.";
                ViewBag.ActiveAuthPanel = "register";
                CaptchaYenile();
                return View("Giris");
            }

            if (!MailOnayKoduGonder(email, fullName, onayKodu, out var mailHatasi))
            {
                ViewBag.RegisterError = $"Hesap oluşturuldu ama doğrulama maili gönderilemedi: {mailHatasi}";
                ViewBag.ActiveAuthPanel = "verify";
                ViewBag.VerifyEmail = email;
                CaptchaYenile();
                return View("Giris");
            }

            ViewBag.RegisterSuccess = "Doğrulama kodu mail adresinize gönderildi.";
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
                ViewBag.VerifyError = "Güvenlik sorusunu kontrol edin.";
                ViewBag.ActiveAuthPanel = "verify";
                ViewBag.VerifyEmail = email;
                CaptchaYenile();
                return View("Giris");
            }

            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(onayKodu))
            {
                ViewBag.VerifyError = "E-posta ve doğrulama kodu zorunludur.";
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
                ViewBag.VerifyError = "Bu e-posta adresiyle kayıt bulunamadı.";
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
                ViewBag.VerifyError = "Doğrulama kodu hatalı veya süresi dolmuş.";
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
                ViewBag.Uyari = "Google giriş için ClientId ve ClientSecret appsettings.json içine eklenmeli.";
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
                ViewBag.Uyari = "Google hesabı doğrulanamadı.";
                CaptchaYenile();
                return View("Giris");
            }

            var email = sonuc.Principal.FindFirstValue(ClaimTypes.Email);
            var adSoyad = sonuc.Principal.FindFirstValue(ClaimTypes.Name);
            var googleKimlikId = sonuc.Principal.FindFirstValue(ClaimTypes.NameIdentifier);

            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(googleKimlikId))
            {
                ViewBag.Uyari = "Google hesabından e-posta bilgisi alınamadı.";
                CaptchaYenile();
                return View("Giris");
            }

            if (email.Length > 50)
            {
                ViewBag.Uyari = "E-posta adresi 50 karakterden uzun. Personel.Mail alanını büyütmemiz gerekiyor.";
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
                ViewBag.Uyari = "Google güvenlik kolonları SQL tarafında henüz eklenmemiş görünüyor.";
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
                    Adres = "Google Hesabı",
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
            Session.Clear();
            ViewBag.kod = DateTime.Now.ToString("fffffff");
            return View();

        }
        [ValidateAntiForgeryToken(), HttpPost]
        public ActionResult AnketGiris(string objUser)
        {
            var obj = db.User.Where(a => a.UserTc.Equals(objUser)).FirstOrDefault();

            if (obj != null && obj.UserTc == objUser && obj.Pasif != true)
            {
                FormsAuthentication.SetAuthCookie(obj.UserAdi, false);
                Session["id"] = obj.UserId;
                Session["adi"] = obj.UserAdi;
                Session["tc"] = obj.UserTc;

                // ip adresini almak
                string ip = GetClientIp();
                if (string.IsNullOrEmpty(ip))
                {
                    ip = GetClientIp();
                }
                Session["ipadres"] = ip;

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
        public ActionResult GirisCikis()
        {
            return RedirectToAction("Giris", "Home");
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
                return Json(new { success = false, message = "AI uretimi ayarlarda pasif gorunuyor." });
            }

            if (string.IsNullOrWhiteSpace(aiAyar.ApiKey)
                || aiAyar.ApiKey.Contains("BURAYA_OPENAI_API_KEY", StringComparison.OrdinalIgnoreCase))
            {
                return Json(new { success = false, message = "OpenAI API anahtari tanimli degil. Ayarlar > Yapay Zeka Ayarlari ekranindan girin." });
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
                return Json(new { success = false, message = "Oturum süreniz doldu. Lütfen tekrar giriş yapın." });
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
                return Json(new { success = false, message = "Taslak okunamadı. Sayfayı yenileyip tekrar deneyin." });
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

            var title = TrimWizardText(draft.Title, "Yeni Değerlendirme", 250);
            var owner = TrimWizardText(draft.Owner, "İnsan Kaynakları", 250);
            var isSurvey = string.Equals(draft.Mode, "survey", StringComparison.OrdinalIgnoreCase);
            var duration = ClampWizardNumber(draft.Duration, 0, 240);
            var passScore = ClampWizardNumber(draft.PassScore, 0, 100);
            var rawQuestionPoints = questions.Select(x => Math.Max(0, x.Points)).ToList();
            var publishQuestionPoints = isSurvey
                ? rawQuestionPoints.Select(x => (double)x).ToList()
                : NormalizeExamPoints(rawQuestionPoints);
            var personelId = Convert.ToInt32(Session["id"]);

            if (!isSurvey && questions.Any(x => RequiresCorrectAnswer(x) && x.Answers?.Any(a => a.Correct) != true))
            {
                return Json(new { success = false, message = "Yayına almadan önce her sınav sorusunda doğru cevabı işaretleyin." });
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
                            string.IsNullOrWhiteSpace(question.Group) ? title + " Soruları" : question.Group,
                            title + " Soruları",
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
                    db.AnketGrup.Add(new AnketGrup
                    {
                        AnketId = anket.AnketId,
                        SoruGrupId = soruGrup.SoruGrupId
                    });
                }

                var order = 1;
                for (var questionIndex = 0; questionIndex < publishQuestions.Count; questionIndex++)
                {
                    var publishQuestion = publishQuestions[questionIndex];
                    var question = publishQuestion.Question;
                    var questionPoints = publishQuestion.Points;
                    var soruGrup = soruGruplari[publishQuestion.GroupName];
                    var answers = NormalizeWizardAnswers(question, isSurvey);
                    var cevapGrup = new CevapGrup
                    {
                        CevapGrupAdi = TrimWizardText(question.Title + " Cevapları", "Cevap Grubu", 250)
                    };
                    db.CevapGrup.Add(cevapGrup);
                    db.SaveChanges();

                    db.Soru.Add(new Soru
                    {
                        SoruAdi = TrimWizardText(question.Title, "Soru", 250),
                        SoruSira = order++,
                        SoruGrupId = soruGrup.SoruGrupId,
                        CevapGrupId = cevapGrup.CevapGrupId,
                        SoruPuan = questionPoints
                    });

                    foreach (var answer in answers)
                    {
                        db.Cevap.Add(new Cevap
                        {
                            CevapAdi = TrimWizardText(answer.Text, "Cevap", 250),
                            CevapGrupId = cevapGrup.CevapGrupId,
                            Dogru = isSurvey ? (bool?)null : answer.Correct,
                            CevapPuan = isSurvey ? ClampSurveyScore(answer.Score) : (answer.Correct ? questionPoints : 0)
                        });
                    }
                }

                db.SaveChanges();
                tx.Commit();

                return Json(new
                {
                    success = true,
                    anketId = anket.AnketId,
                    redirectUrl = Url.Action("AnketGrupIndex", "Home", new { id = anket.AnketId, adi = anket.AnketAdi })
                });
            }
            catch (Exception ex)
            {
                tx.Rollback();
                return Json(new { success = false, message = "Değerlendirme kaydedilemedi: " + ex.Message });
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
                      WHERE Aktif = 1
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
                @"\bşaka\b",
                @"\bespri\b",
                @"\bsohbet\b",
                @"\bhikaye\b",
                @"\bşiir\b",
                @"\bfilm\b",
                @"\bmaç\b",
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
                "egitim", "eğitim", "sinav", "sınav", "anket", "degerlendirme", "değerlendirme",
                "test", "quiz", "oryantasyon", "farkindalik", "farkındalık", "prosedur", "prosedür",
                "talimat", "politika", "surec", "süreç", "kalite", "guvenlik", "güvenlik",
                "isg", "iş sağlığı", "is sagligi", "hijyen", "gida", "gıda", "haccp", "kkn",
                "alerjen", "helal", "brcgs", "iso", "kvkk", "gdpr", "bilgi güvenliği",
                "siber", "yangin", "yangın", "ilk yardim", "ilk yardım", "musteri", "müşteri",
                "satis", "satış", "insan kaynaklari", "insan kaynakları", "ise alim", "işe alım",
                "yetkinlik", "liderlik", "operasyon", "uretim", "üretim", "bakim", "bakım",
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

            if (Regex.IsMatch(topic.Trim(), @"^[A-ZÇĞİÖŞÜ0-9 .\-]{3,18}$"))
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
                .Replace('ı', 'i');
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
        {{ ""text"": ""Cok olumlu cevap"", ""correct"": false, ""score"": 5 }},
        {{ ""text"": ""Orta cevap"", ""correct"": false, ""score"": 3 }},
        {{ ""text"": ""Olumsuz cevap"", ""correct"": false, ""score"": 1 }},
        {{ ""text"": ""Bilmiyorum"", ""correct"": false, ""score"": 0 }}
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
                        Text = x.Text.Trim(),
                        Correct = !isSurvey && x.Correct,
                        Score = isSurvey ? InferSurveyAnswerScore(x, answerIndex, item.Answers?.Count ?? 0) : x.Score
                    })
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
                .Replace('ı', 'i')
                .Replace('İ', 'i')
                .Replace('ç', 'c')
                .Replace('ğ', 'g')
                .Replace('ö', 'o')
                .Replace('ş', 's')
                .Replace('ü', 'u');
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
                    .Equals("Yanlış", StringComparison.OrdinalIgnoreCase) != true;

            return new List<AssessmentWizardAnswer>
            {
                new AssessmentWizardAnswer { Text = "Doğru", Correct = correctIsTrue },
                new AssessmentWizardAnswer { Text = "Yanlış", Correct = !correctIsTrue }
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
                message = "Soru veya cevap kaydı bulunamadı.";
                return false;
            }

            if (soru.CevapGrupId != cevap.CevapGrupId)
            {
                message = "Seçilen cevap bu soruya ait değil.";
                return false;
            }

            var soruAnketeAit = db.AnketGrup.Any(x =>
                x.AnketId == hav.AnketId.Value &&
                x.SoruGrupId == soru.SoruGrupId);

            if (!soruAnketeAit)
            {
                message = "Seçilen soru bu çalışmaya ait değil.";
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
            public int Points { get; set; }
            public bool Required { get; set; }
            public List<AssessmentWizardAnswer> Answers { get; set; }
        }

        public class AssessmentWizardAnswer
        {
            public string Text { get; set; }
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

        private void KatilimYonteminiKaydet(int anketId, string yontem)
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
            }
            catch
            {
                // Kolon yoksa ya da yetki yoksa ana kayit akisini kirmayalim.
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
                // Veritabanı kullanıcısının DDL yetkisi yoksa SQL scripti elle çalıştırılabilir.
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
                             ISNULL(NULLIF(SertifikaBaslik, N''), N'Katılım Sertifikası') AS SertifikaBaslik,
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
                        // Yeni tasarım kolonları eklenmemişse sertifika eski ayarlarla çalışmaya devam eder.
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
                    SertifikaBaslik = "Katılım Sertifikası",
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
                return "Katılım Sertifikası";
            }

            var baslik = value.Trim();
            return baslik == "KatÄ±lÄ±m SertifikasÄ±" ? "Katılım Sertifikası" : baslik;
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

                mesaj = "Sertifika tüm sorular tamamlanınca açılacak.";
                return false;
            }

            if (zaman == "YayinBitince")
            {
                if (ayar?.YayinBitisTarihi != null && DateTime.Now >= ayar.YayinBitisTarihi.Value)
                {
                    return true;
                }

                mesaj = ayar?.YayinBitisTarihi != null
                    ? $"Sertifika yayın bitişinde açılacak: {ayar.YayinBitisTarihi.Value:dd.MM.yyyy HH:mm}."
                    : "Sertifika yayın bitişinde açılacak; önce çalışma bitiş tarihi belirlenmeli.";
                return false;
            }

            if (!sureDevamEdiyor)
            {
                return true;
            }

            mesaj = "Sertifika sınav süresi kapandıktan sonra açılacak.";
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
            ViewBag.KatilimTokenMap = EnsureAnketPaylasimTokenlari(anketler.Select(x => x.AnketId));
            ViewBag.KatilimYontemiMap = KatilimYontemleriGetir(anketler.Select(x => x.AnketId));
            Tumcontroller model = new Tumcontroller()
            {
                Ank = anketler,
                Hav = db.Havuz.Where(x => anketIdleri.Contains(x.AnketId)),
                AnkGrp = db.AnketGrup.Where(x => anketIdleri.Contains(x.AnketId)),
                Sor = db.Soru,
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
            List<SelectListItem> unv =
            (from i in db.Unvan.ToList()
             select new SelectListItem
             {
                 Text = i.UnvanAdi,
                 Value = i.UnvanId.ToString(),
             }).ToList();
            ViewBag.Unv = unv;

            ViewBag.KayitTar = DateTime.Now.ToString("yyyy-MM-dd HH:mm");


            return View(db.Personel.Where(x => x.PersonelId == id).FirstOrDefault());
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
            try
            {
                if (uploadfile != null)
                {
                    string ResimAdi = Path.GetFileName(uploadfile.FileName);
                    string adres = MapPath("~/Content/Personel/" + ResimAdi);
                    uploadfile.SaveAs(adres);

                    Personel.Resim = Request.Form["Resim"];
                    Personel.Resim = ResimAdi;

                    db.Entry(Personel).State = EntityState.Modified;
                    db.SaveChanges();
                    return RedirectToAction("Indexgosterge", "Home", new { id = Session["id"] });
                }
                else
                {
                    db.Entry(Personel).State = EntityState.Modified;
                    db.SaveChanges();
                    return RedirectToAction("Indexgosterge", "Home", new { id = Session["id"] });

                }
            }
            catch
            {
                return View();
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
        public ActionResult AnketIndex1(int id, int? filterEgitimId = null)
        {
            if (Session["id"] == null || Session["admin"] == null)
                return RedirectToAction("Giris", "Home");

            var anket = CalismaAlaniAnketGetir(id);
            if (anket == null)
                return RedirectToAction("AnketIndex");

            ViewBag.adi = anket.AnketAdi;
            ViewBag.anketadi = filterEgitimId != null
                ? db.Egitim.FirstOrDefault(x => x.EgitimId == filterEgitimId)?.EgitimAdi
                : "Tümü";
            ViewBag.id = anket.AnketId;
            ViewBag.sinav = SinavTurundeMi(anket);
            ViewBag.FilterEgitim = filterEgitimId;

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

            var bul = db.Havuz
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
                .Where(x => x.AnketId == id);
            var adi = db.Havuz.Where(x => x.AnketId == id).FirstOrDefault();
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

            // Eğitim
            ViewBag.baslik4 = new List<string>();
            ViewBag.puan4 = new List<float>();
            ViewBag.adet4 = new List<int>();

            // Yaş
            ViewBag.baslik5 = new List<string>();  // int yerine string yapalım → chart için daha kolay
            ViewBag.puan5 = new List<float>();
            ViewBag.adet5 = new List<int>();

            // Şehir
            ViewBag.baslik6 = new List<string>();
            ViewBag.puan6 = new List<float>();
            ViewBag.adet6 = new List<int>();

            // Şube
            ViewBag.baslik7 = new List<string>();
            ViewBag.puan7 = new List<float>();
            ViewBag.adet7 = new List<int>();

            // Ünvan
            ViewBag.baslik8 = new List<string>();
            ViewBag.puan8 = new List<float>();
            ViewBag.adet8 = new List<int>();

            // Yaka
            ViewBag.baslik9 = new List<string>();
            ViewBag.puan9 = new List<float>();
            ViewBag.adet9 = new List<int>();

            // Yönetici
            ViewBag.baslik10 = new List<string>();
            ViewBag.puan10 = new List<float>();
            ViewBag.adet10 = new List<int>();

            // Soru
            ViewBag.baslik11 = new List<string>();
            ViewBag.puan11 = new List<float>();
            ViewBag.adet11 = new List<int>();
            ViewBag.soruGrup11 = new List<string>();


            // Soru Grup
            ViewBag.baslik12 = new List<string>();
            ViewBag.puan12 = new List<float>();
            ViewBag.adet12 = new List<int>();

            foreach (var item in bul.GroupBy(x => x.Anket.AnketAdi))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerçek kişi adedi

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

                // Puanı hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanını yüzdesini bulur
                var p1 = p2 / soru;

                ViewBag.baslik1.Add(item.Key);
                ViewBag.puan1.Add(p1);
                ViewBag.adet1.Add(kisiSayisi);
            }
            foreach (var item in bul.GroupBy(x => x.User.Departman.DepartmanAdi))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerçek kişi adedi


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

                // Puanı hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanını yüzdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik2.Add(item.Key);
                ViewBag.puan2.Add(p1);
                ViewBag.adet2.Add(kisiSayisi);

            }
            foreach (var item in bul.GroupBy(x => x.User.Cinsiyet.CinsiyetAdi))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerçek kişi adedi

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

                // Puanı hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanını yüzdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik3.Add(item.Key);
                ViewBag.puan3.Add(p1);
                ViewBag.adet3.Add(kisiSayisi);

            }
            foreach (var item in bul.GroupBy(x => x.User.Egitim.EgitimAdi))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerçek kişi adedi

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

                // Puanı hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanını yüzdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik4.Add(item.Key);
                ViewBag.puan4.Add(p1);
                ViewBag.adet4.Add(kisiSayisi);

            }
            var bugun = DateTime.Today;

            // Önce boş listeleri hazırla
            ViewBag.baslik5 = new List<string>();
            ViewBag.puan5 = new List<float>();

            foreach (var item in bul
                .Where(x => x.User.UserDogumTar != null)
                .GroupBy(x => x.User.UserDogumTar.Value.Year))
            {
                var dogumYili = item.Key;
                var yas = bugun.Year - dogumYili;
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerçek kişi adedi


                // Eğer doğum günü bu yıl daha gelmediyse yaşı 1 azalt
                var ilkKayit = item.First().User.UserDogumTar;
                if (ilkKayit != null && bugun.DayOfYear < ilkKayit.Value.DayOfYear)
                {
                    yas--;
                }

                // Ortalama puanı hesapla (kişiye göre normalize)
                var ortalama = item
                    .GroupBy(x => x.UserId)
                    .Select(g => g.Average(y => y.CevapPuan))
                    .Average();

                var ortalamaYuzde = ortalama * 20; // 5 üzerinden 100'e çevirme

                // 🔹 Burada artık Add et
                ViewBag.baslik5.Add(yas.ToString());       // X ekseni → yaş
                ViewBag.puan5.Add((float)ortalamaYuzde);   // Y ekseni → puan
                ViewBag.adet5.Add(kisiSayisi);   // Y ekseni → puan
            }

            foreach (var item in bul.GroupBy(x => x.User.Sehir.SehiarAdi))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerçek kişi adedi

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

                // Puanı hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanını yüzdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik6.Add(item.Key);
                ViewBag.puan6.Add(p1);
                ViewBag.adet6.Add(kisiSayisi);

            }
            foreach (var item in bul.GroupBy(x => x.User.Sube.SubeAdi))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerçek kişi adedi

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

                // Puanı hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanını yüzdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik7.Add(item.Key);
                ViewBag.puan7.Add(p1);
                ViewBag.adet7.Add(kisiSayisi);

            }
            foreach (var item in bul.GroupBy(x => x.User.Unvan.UnvanAdi))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerçek kişi adedi

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

                // Puanı hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanını yüzdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik8.Add(item.Key);
                ViewBag.puan8.Add(p1);
                ViewBag.adet8.Add(kisiSayisi);

            }
            foreach (var item in bul.GroupBy(x => x.User.Yaka.YakaAdi))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerçek kişi adedi

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

                // Puanı hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanını yüzdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik9.Add(item.Key);
                ViewBag.puan9.Add(p1);
                ViewBag.adet9.Add(kisiSayisi);

            }
            foreach (var item in bul.GroupBy(x => x.User.Yonetici.YoneticiAdi))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerçek kişi adedi

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

                // Puanı hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanını yüzdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik10.Add(item.Key);
                ViewBag.puan10.Add(p1);
                ViewBag.adet10.Add(kisiSayisi);

            }
            // Soru + Soru Grup

            foreach (var item in bul.GroupBy(x => new { x.Soru.SoruAdi, x.SoruGrup.SoruGrupAdi }))
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

            foreach (var item in bul.GroupBy(x => x.SoruGrup.SoruGrupAdi))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerçek kişi adedi

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

                // Puanı hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanını yüzdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik12.Add(item.Key);
                ViewBag.puan12.Add(p1);
                ViewBag.adet12.Add(kisiSayisi);

            }

            return View(bul);
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

            var bul1 = db.Havuz.Where(x => x.AnketId == ank);
            var bul = bul1.Where(x => x.User.UserDepartman == id);
            var adi = db.Havuz.Where(x => x.User.UserDepartman == id).FirstOrDefault();
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
                ViewBag.anketadi = adi.User.Departman.DepartmanAdi;
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

            // Eğitim
            ViewBag.baslik4 = new List<string>();
            ViewBag.puan4 = new List<float>();
            ViewBag.adet4 = new List<int>();

            // Yaş
            ViewBag.baslik5 = new List<string>();  // int yerine string yapalım → chart için daha kolay
            ViewBag.puan5 = new List<float>();
            ViewBag.adet5 = new List<int>();

            // Şehir
            ViewBag.baslik6 = new List<string>();
            ViewBag.puan6 = new List<float>();
            ViewBag.adet6 = new List<int>();

            // Şube
            ViewBag.baslik7 = new List<string>();
            ViewBag.puan7 = new List<float>();
            ViewBag.adet7 = new List<int>();

            // Ünvan
            ViewBag.baslik8 = new List<string>();
            ViewBag.puan8 = new List<float>();
            ViewBag.adet8 = new List<int>();

            // Yaka
            ViewBag.baslik9 = new List<string>();
            ViewBag.puan9 = new List<float>();
            ViewBag.adet9 = new List<int>();

            // Yönetici
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
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerçek kişi adedi

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

                // Puanı hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanını yüzdesini bulur
                var p1 = p2 / soru;

                ViewBag.baslik1.Add(item.Key);
                ViewBag.puan1.Add(p1);
                ViewBag.adet1.Add(kisiSayisi);

            }
            foreach (var item in bul.GroupBy(x => x.User.Departman.DepartmanAdi))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerçek kişi adedi

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

                // Puanı hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanını yüzdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik2.Add(item.Key);
                ViewBag.puan2.Add(p1);
                ViewBag.adet2.Add(kisiSayisi);

            }
            foreach (var item in bul.GroupBy(x => x.User.Cinsiyet.CinsiyetAdi))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerçek kişi adedi

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

                // Puanı hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanını yüzdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik3.Add(item.Key);
                ViewBag.puan3.Add(p1);
                ViewBag.adet3.Add(kisiSayisi);

            }
            foreach (var item in bul.GroupBy(x => x.User.Egitim.EgitimAdi))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerçek kişi adedi

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

                // Puanı hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanını yüzdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik4.Add(item.Key);
                ViewBag.puan4.Add(p1);
                ViewBag.adet4.Add(kisiSayisi);


            }
            var bugun = DateTime.Today;

            // Önce boş listeleri hazırla
            ViewBag.baslik5 = new List<string>();
            ViewBag.puan5 = new List<float>();

            foreach (var item in bul
                .Where(x => x.User.UserDogumTar != null)
                .GroupBy(x => x.User.UserDogumTar.Value.Year))
            {
                var dogumYili = item.Key;
                var yas = bugun.Year - dogumYili;
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerçek kişi adedi


                // Eğer doğum günü bu yıl daha gelmediyse yaşı 1 azalt
                var ilkKayit = item.First().User.UserDogumTar;
                if (ilkKayit != null && bugun.DayOfYear < ilkKayit.Value.DayOfYear)
                {
                    yas--;
                }

                // Ortalama puanı hesapla (kişiye göre normalize)
                var ortalama = item
                    .GroupBy(x => x.UserId)
                    .Select(g => g.Average(y => y.CevapPuan))
                    .Average();

                var ortalamaYuzde = ortalama * 20; // 5 üzerinden 100'e çevirme

                // 🔹 Burada artık Add et
                ViewBag.baslik5.Add(yas.ToString());       // X ekseni → yaş
                ViewBag.puan5.Add((float)ortalamaYuzde);   // Y ekseni → puan
                ViewBag.adet5.Add(kisiSayisi);   // Y ekseni → puan
            }

            foreach (var item in bul.GroupBy(x => x.User.Sehir.SehiarAdi))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerçek kişi adedi

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

                // Puanı hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanını yüzdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik6.Add(item.Key);
                ViewBag.puan6.Add(p1);
                ViewBag.adet6.Add(kisiSayisi);

            }
            foreach (var item in bul.GroupBy(x => x.User.Sube.SubeAdi))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerçek kişi adedi

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

                // Puanı hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanını yüzdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik7.Add(item.Key);
                ViewBag.puan7.Add(p1);
                ViewBag.adet7.Add(kisiSayisi);

            }
            foreach (var item in bul.GroupBy(x => x.User.Unvan.UnvanAdi))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerçek kişi adedi

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

                // Puanı hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanını yüzdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik8.Add(item.Key);
                ViewBag.puan8.Add(p1);
                ViewBag.adet8.Add(kisiSayisi);

            }
            foreach (var item in bul.GroupBy(x => x.User.Yaka.YakaAdi))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerçek kişi adedi

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

                // Puanı hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanını yüzdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik9.Add(item.Key);
                ViewBag.puan9.Add(p1);
                ViewBag.adet9.Add(kisiSayisi);

            }
            foreach (var item in bul.GroupBy(x => x.User.Yonetici.YoneticiAdi))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerçek kişi adedi

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

                // Puanı hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanını yüzdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik10.Add(item.Key);
                ViewBag.puan10.Add(p1);
                ViewBag.adet10.Add(kisiSayisi);

            }
            foreach (var item in bul.GroupBy(x => x.Soru.SoruAdi))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerçek kişi adedi

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

                // Puanı hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanını yüzdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik11.Add(item.Key);
                ViewBag.puan11.Add(p1);
                ViewBag.adet11.Add(kisiSayisi);

            }
            foreach (var item in bul.GroupBy(x => x.SoruGrup.SoruGrupAdi))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerçek kişi adedi

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

                // Puanı hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanını yüzdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik12.Add(item.Key);
                ViewBag.puan12.Add(p1);
                ViewBag.adet12.Add(kisiSayisi);
            }

            return View(bul);
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

            var bul1 = db.Havuz.Where(x => x.AnketId == ank);
            var bul = bul1.Where(x => x.User.UserCinsiyet == id);
            var adi = db.Havuz.Where(x => x.User.UserCinsiyet == id).FirstOrDefault();
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
                ViewBag.anketadi = adi.User.Cinsiyet.CinsiyetAdi;
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

            // Eğitim
            ViewBag.baslik4 = new List<string>();
            ViewBag.puan4 = new List<float>();
            ViewBag.adet4 = new List<int>();

            // Yaş
            ViewBag.baslik5 = new List<string>();  // int yerine string yapalım → chart için daha kolay
            ViewBag.puan5 = new List<float>();
            ViewBag.adet5 = new List<int>();

            // Şehir
            ViewBag.baslik6 = new List<string>();
            ViewBag.puan6 = new List<float>();
            ViewBag.adet6 = new List<int>();

            // Şube
            ViewBag.baslik7 = new List<string>();
            ViewBag.puan7 = new List<float>();
            ViewBag.adet7 = new List<int>();

            // Ünvan
            ViewBag.baslik8 = new List<string>();
            ViewBag.puan8 = new List<float>();
            ViewBag.adet8 = new List<int>();

            // Yaka
            ViewBag.baslik9 = new List<string>();
            ViewBag.puan9 = new List<float>();
            ViewBag.adet9 = new List<int>();

            // Yönetici
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
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerçek kişi adedi

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

                // Puanı hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanını yüzdesini bulur
                var p1 = p2 / soru;

                ViewBag.baslik1.Add(item.Key);
                ViewBag.puan1.Add(p1);
                ViewBag.adet1.Add(kisiSayisi);

            }
            foreach (var item in bul.GroupBy(x => x.User.Departman.DepartmanAdi))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerçek kişi adedi

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

                // Puanı hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanını yüzdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik2.Add(item.Key);
                ViewBag.puan2.Add(p1);
                ViewBag.adet2.Add(kisiSayisi);

            }
            foreach (var item in bul.GroupBy(x => x.User.Cinsiyet.CinsiyetAdi))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerçek kişi adedi

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

                // Puanı hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanını yüzdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik3.Add(item.Key);
                ViewBag.puan3.Add(p1);
                ViewBag.adet3.Add(kisiSayisi);

            }
            foreach (var item in bul.GroupBy(x => x.User.Egitim.EgitimAdi))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerçek kişi adedi

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

                // Puanı hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanını yüzdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik4.Add(item.Key);
                ViewBag.puan4.Add(p1);
                ViewBag.adet4.Add(kisiSayisi);


            }
            var bugun = DateTime.Today;

            // Önce boş listeleri hazırla
            ViewBag.baslik5 = new List<string>();
            ViewBag.puan5 = new List<float>();

            foreach (var item in bul
                .Where(x => x.User.UserDogumTar != null)
                .GroupBy(x => x.User.UserDogumTar.Value.Year))
            {
                var dogumYili = item.Key;
                var yas = bugun.Year - dogumYili;
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerçek kişi adedi


                // Eğer doğum günü bu yıl daha gelmediyse yaşı 1 azalt
                var ilkKayit = item.First().User.UserDogumTar;
                if (ilkKayit != null && bugun.DayOfYear < ilkKayit.Value.DayOfYear)
                {
                    yas--;
                }

                // Ortalama puanı hesapla (kişiye göre normalize)
                var ortalama = item
                    .GroupBy(x => x.UserId)
                    .Select(g => g.Average(y => y.CevapPuan))
                    .Average();

                var ortalamaYuzde = ortalama * 20; // 5 üzerinden 100'e çevirme

                // 🔹 Burada artık Add et
                ViewBag.baslik5.Add(yas.ToString());       // X ekseni → yaş
                ViewBag.puan5.Add((float)ortalamaYuzde);   // Y ekseni → puan
                ViewBag.adet5.Add(kisiSayisi);   // Y ekseni → puan
            }

            foreach (var item in bul.GroupBy(x => x.User.Sehir.SehiarAdi))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerçek kişi adedi

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

                // Puanı hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanını yüzdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik6.Add(item.Key);
                ViewBag.puan6.Add(p1);
                ViewBag.adet6.Add(kisiSayisi);

            }
            foreach (var item in bul.GroupBy(x => x.User.Sube.SubeAdi))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerçek kişi adedi

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

                // Puanı hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanını yüzdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik7.Add(item.Key);
                ViewBag.puan7.Add(p1);
                ViewBag.adet7.Add(kisiSayisi);

            }
            foreach (var item in bul.GroupBy(x => x.User.Unvan.UnvanAdi))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerçek kişi adedi

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

                // Puanı hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanını yüzdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik8.Add(item.Key);
                ViewBag.puan8.Add(p1);
                ViewBag.adet8.Add(kisiSayisi);

            }
            foreach (var item in bul.GroupBy(x => x.User.Yaka.YakaAdi))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerçek kişi adedi

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

                // Puanı hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanını yüzdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik9.Add(item.Key);
                ViewBag.puan9.Add(p1);
                ViewBag.adet9.Add(kisiSayisi);

            }
            foreach (var item in bul.GroupBy(x => x.User.Yonetici.YoneticiAdi))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerçek kişi adedi

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

                // Puanı hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanını yüzdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik10.Add(item.Key);
                ViewBag.puan10.Add(p1);
                ViewBag.adet10.Add(kisiSayisi);

            }
            foreach (var item in bul.GroupBy(x => x.Soru.SoruAdi))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerçek kişi adedi

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

                // Puanı hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanını yüzdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik11.Add(item.Key);
                ViewBag.puan11.Add(p1);
                ViewBag.adet11.Add(kisiSayisi);

            }
            foreach (var item in bul.GroupBy(x => x.SoruGrup.SoruGrupAdi))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerçek kişi adedi

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

                // Puanı hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanını yüzdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik12.Add(item.Key);
                ViewBag.puan12.Add(p1);
                ViewBag.adet12.Add(kisiSayisi);
            }

            return View(bul);
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

            var bul1 = db.Havuz.Where(x => x.AnketId == ank);
            var bul = bul1.Where(x => x.User.UserEgitim == id);
            var adi = db.Havuz.Where(x => x.User.UserEgitim == id).FirstOrDefault();
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
                ViewBag.anketadi = adi.User.Egitim.EgitimAdi;
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

            // Eğitim
            ViewBag.baslik4 = new List<string>();
            ViewBag.puan4 = new List<float>();
            ViewBag.adet4 = new List<int>();

            // Yaş
            ViewBag.baslik5 = new List<string>();  // int yerine string yapalım → chart için daha kolay
            ViewBag.puan5 = new List<float>();
            ViewBag.adet5 = new List<int>();

            // Şehir
            ViewBag.baslik6 = new List<string>();
            ViewBag.puan6 = new List<float>();
            ViewBag.adet6 = new List<int>();

            // Şube
            ViewBag.baslik7 = new List<string>();
            ViewBag.puan7 = new List<float>();
            ViewBag.adet7 = new List<int>();

            // Ünvan
            ViewBag.baslik8 = new List<string>();
            ViewBag.puan8 = new List<float>();
            ViewBag.adet8 = new List<int>();

            // Yaka
            ViewBag.baslik9 = new List<string>();
            ViewBag.puan9 = new List<float>();
            ViewBag.adet9 = new List<int>();

            // Yönetici
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
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerçek kişi adedi

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

                // Puanı hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanını yüzdesini bulur
                var p1 = p2 / soru;

                ViewBag.baslik1.Add(item.Key);
                ViewBag.puan1.Add(p1);
                ViewBag.adet1.Add(kisiSayisi);

            }
            foreach (var item in bul.GroupBy(x => x.User.Departman.DepartmanAdi))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerçek kişi adedi

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

                // Puanı hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanını yüzdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik2.Add(item.Key);
                ViewBag.puan2.Add(p1);
                ViewBag.adet2.Add(kisiSayisi);

            }
            foreach (var item in bul.GroupBy(x => x.User.Cinsiyet.CinsiyetAdi))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerçek kişi adedi

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

                // Puanı hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanını yüzdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik3.Add(item.Key);
                ViewBag.puan3.Add(p1);
                ViewBag.adet3.Add(kisiSayisi);

            }
            foreach (var item in bul.GroupBy(x => x.User.Egitim.EgitimAdi))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerçek kişi adedi

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

                // Puanı hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanını yüzdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik4.Add(item.Key);
                ViewBag.puan4.Add(p1);
                ViewBag.adet4.Add(kisiSayisi);


            }
            var bugun = DateTime.Today;

            // Önce boş listeleri hazırla
            ViewBag.baslik5 = new List<string>();
            ViewBag.puan5 = new List<float>();

            foreach (var item in bul
                .Where(x => x.User.UserDogumTar != null)
                .GroupBy(x => x.User.UserDogumTar.Value.Year))
            {
                var dogumYili = item.Key;
                var yas = bugun.Year - dogumYili;
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerçek kişi adedi


                // Eğer doğum günü bu yıl daha gelmediyse yaşı 1 azalt
                var ilkKayit = item.First().User.UserDogumTar;
                if (ilkKayit != null && bugun.DayOfYear < ilkKayit.Value.DayOfYear)
                {
                    yas--;
                }

                // Ortalama puanı hesapla (kişiye göre normalize)
                var ortalama = item
                    .GroupBy(x => x.UserId)
                    .Select(g => g.Average(y => y.CevapPuan))
                    .Average();

                var ortalamaYuzde = ortalama * 20; // 5 üzerinden 100'e çevirme

                // 🔹 Burada artık Add et
                ViewBag.baslik5.Add(yas.ToString());       // X ekseni → yaş
                ViewBag.puan5.Add((float)ortalamaYuzde);   // Y ekseni → puan
                ViewBag.adet5.Add(kisiSayisi);   // Y ekseni → puan
            }

            foreach (var item in bul.GroupBy(x => x.User.Sehir.SehiarAdi))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerçek kişi adedi

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

                // Puanı hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanını yüzdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik6.Add(item.Key);
                ViewBag.puan6.Add(p1);
                ViewBag.adet6.Add(kisiSayisi);

            }
            foreach (var item in bul.GroupBy(x => x.User.Sube.SubeAdi))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerçek kişi adedi

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

                // Puanı hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanını yüzdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik7.Add(item.Key);
                ViewBag.puan7.Add(p1);
                ViewBag.adet7.Add(kisiSayisi);

            }
            foreach (var item in bul.GroupBy(x => x.User.Unvan.UnvanAdi))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerçek kişi adedi

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

                // Puanı hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanını yüzdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik8.Add(item.Key);
                ViewBag.puan8.Add(p1);
                ViewBag.adet8.Add(kisiSayisi);

            }
            foreach (var item in bul.GroupBy(x => x.User.Yaka.YakaAdi))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerçek kişi adedi

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

                // Puanı hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanını yüzdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik9.Add(item.Key);
                ViewBag.puan9.Add(p1);
                ViewBag.adet9.Add(kisiSayisi);

            }
            foreach (var item in bul.GroupBy(x => x.User.Yonetici.YoneticiAdi))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerçek kişi adedi

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

                // Puanı hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanını yüzdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik10.Add(item.Key);
                ViewBag.puan10.Add(p1);
                ViewBag.adet10.Add(kisiSayisi);

            }
            foreach (var item in bul.GroupBy(x => x.Soru.SoruAdi))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerçek kişi adedi

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

                // Puanı hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanını yüzdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik11.Add(item.Key);
                ViewBag.puan11.Add(p1);
                ViewBag.adet11.Add(kisiSayisi);

            }
            foreach (var item in bul.GroupBy(x => x.SoruGrup.SoruGrupAdi))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerçek kişi adedi

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

                // Puanı hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanını yüzdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik12.Add(item.Key);
                ViewBag.puan12.Add(p1);
                ViewBag.adet12.Add(kisiSayisi);
            }

            return View(bul);
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

            var bul1 = db.Havuz.Where(x => x.AnketId == ank);
            var bul = bul1.Where(x => x.User.UserDogumTar.Value.Year == id);
            var adi = db.Havuz.Where(x => x.User.UserDogumTar.Value.Year == id).FirstOrDefault();
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

            // Eğitim
            ViewBag.baslik4 = new List<string>();
            ViewBag.puan4 = new List<float>();
            ViewBag.adet4 = new List<int>();

            // Yaş
            ViewBag.baslik5 = new List<string>();  // int yerine string yapalım → chart için daha kolay
            ViewBag.puan5 = new List<float>();
            ViewBag.adet5 = new List<int>();

            // Şehir
            ViewBag.baslik6 = new List<string>();
            ViewBag.puan6 = new List<float>();
            ViewBag.adet6 = new List<int>();

            // Şube
            ViewBag.baslik7 = new List<string>();
            ViewBag.puan7 = new List<float>();
            ViewBag.adet7 = new List<int>();

            // Ünvan
            ViewBag.baslik8 = new List<string>();
            ViewBag.puan8 = new List<float>();
            ViewBag.adet8 = new List<int>();

            // Yaka
            ViewBag.baslik9 = new List<string>();
            ViewBag.puan9 = new List<float>();
            ViewBag.adet9 = new List<int>();

            // Yönetici
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
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerçek kişi adedi

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

                // Puanı hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanını yüzdesini bulur
                var p1 = p2 / soru;

                ViewBag.baslik1.Add(item.Key);
                ViewBag.puan1.Add(p1);
                ViewBag.adet1.Add(kisiSayisi);

            }
            foreach (var item in bul.GroupBy(x => x.User.Departman.DepartmanAdi))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerçek kişi adedi

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

                // Puanı hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanını yüzdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik2.Add(item.Key);
                ViewBag.puan2.Add(p1);
                ViewBag.adet2.Add(kisiSayisi);

            }
            foreach (var item in bul.GroupBy(x => x.User.Cinsiyet.CinsiyetAdi))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerçek kişi adedi

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

                // Puanı hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanını yüzdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik3.Add(item.Key);
                ViewBag.puan3.Add(p1);
                ViewBag.adet3.Add(kisiSayisi);

            }
            foreach (var item in bul.GroupBy(x => x.User.Egitim.EgitimAdi))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerçek kişi adedi

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

                // Puanı hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanını yüzdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik4.Add(item.Key);
                ViewBag.puan4.Add(p1);
                ViewBag.adet4.Add(kisiSayisi);


            }
            var bugun = DateTime.Today;

            // Önce boş listeleri hazırla
            ViewBag.baslik5 = new List<string>();
            ViewBag.puan5 = new List<float>();

            foreach (var item in bul
                .Where(x => x.User.UserDogumTar != null)
                .GroupBy(x => x.User.UserDogumTar.Value.Year))
            {
                var dogumYili = item.Key;
                var yas = bugun.Year - dogumYili;
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerçek kişi adedi


                // Eğer doğum günü bu yıl daha gelmediyse yaşı 1 azalt
                var ilkKayit = item.First().User.UserDogumTar;
                if (ilkKayit != null && bugun.DayOfYear < ilkKayit.Value.DayOfYear)
                {
                    yas--;
                }

                // Ortalama puanı hesapla (kişiye göre normalize)
                var ortalama = item
                    .GroupBy(x => x.UserId)
                    .Select(g => g.Average(y => y.CevapPuan))
                    .Average();

                var ortalamaYuzde = ortalama * 20; // 5 üzerinden 100'e çevirme

                // 🔹 Burada artık Add et
                ViewBag.baslik5.Add(yas.ToString());       // X ekseni → yaş
                ViewBag.puan5.Add((float)ortalamaYuzde);   // Y ekseni → puan
                ViewBag.adet5.Add(kisiSayisi);   // Y ekseni → puan
            }

            foreach (var item in bul.GroupBy(x => x.User.Sehir.SehiarAdi))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerçek kişi adedi

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

                // Puanı hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanını yüzdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik6.Add(item.Key);
                ViewBag.puan6.Add(p1);
                ViewBag.adet6.Add(kisiSayisi);

            }
            foreach (var item in bul.GroupBy(x => x.User.Sube.SubeAdi))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerçek kişi adedi

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

                // Puanı hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanını yüzdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik7.Add(item.Key);
                ViewBag.puan7.Add(p1);
                ViewBag.adet7.Add(kisiSayisi);

            }
            foreach (var item in bul.GroupBy(x => x.User.Unvan.UnvanAdi))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerçek kişi adedi

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

                // Puanı hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanını yüzdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik8.Add(item.Key);
                ViewBag.puan8.Add(p1);
                ViewBag.adet8.Add(kisiSayisi);

            }
            foreach (var item in bul.GroupBy(x => x.User.Yaka.YakaAdi))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerçek kişi adedi

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

                // Puanı hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanını yüzdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik9.Add(item.Key);
                ViewBag.puan9.Add(p1);
                ViewBag.adet9.Add(kisiSayisi);

            }
            foreach (var item in bul.GroupBy(x => x.User.Yonetici.YoneticiAdi))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerçek kişi adedi

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

                // Puanı hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanını yüzdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik10.Add(item.Key);
                ViewBag.puan10.Add(p1);
                ViewBag.adet10.Add(kisiSayisi);

            }
            foreach (var item in bul.GroupBy(x => x.Soru.SoruAdi))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerçek kişi adedi

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

                // Puanı hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanını yüzdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik11.Add(item.Key);
                ViewBag.puan11.Add(p1);
                ViewBag.adet11.Add(kisiSayisi);

            }
            foreach (var item in bul.GroupBy(x => x.SoruGrup.SoruGrupAdi))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerçek kişi adedi

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

                // Puanı hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanını yüzdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik12.Add(item.Key);
                ViewBag.puan12.Add(p1);
                ViewBag.adet12.Add(kisiSayisi);
            }

            return View(bul);
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

            var bul1 = db.Havuz.Where(x => x.AnketId == ank);
            var bul = bul1.Where(x => x.User.UserSehir == id);
            var adi = db.Havuz.Where(x => x.User.UserSehir == id).FirstOrDefault();
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
                ViewBag.anketadi = adi.User.Sehir.SehiarAdi;
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

            // Eğitim
            ViewBag.baslik4 = new List<string>();
            ViewBag.puan4 = new List<float>();
            ViewBag.adet4 = new List<int>();

            // Yaş
            ViewBag.baslik5 = new List<string>();  // int yerine string yapalım → chart için daha kolay
            ViewBag.puan5 = new List<float>();
            ViewBag.adet5 = new List<int>();

            // Şehir
            ViewBag.baslik6 = new List<string>();
            ViewBag.puan6 = new List<float>();
            ViewBag.adet6 = new List<int>();

            // Şube
            ViewBag.baslik7 = new List<string>();
            ViewBag.puan7 = new List<float>();
            ViewBag.adet7 = new List<int>();

            // Ünvan
            ViewBag.baslik8 = new List<string>();
            ViewBag.puan8 = new List<float>();
            ViewBag.adet8 = new List<int>();

            // Yaka
            ViewBag.baslik9 = new List<string>();
            ViewBag.puan9 = new List<float>();
            ViewBag.adet9 = new List<int>();

            // Yönetici
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
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerçek kişi adedi

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

                // Puanı hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanını yüzdesini bulur
                var p1 = p2 / soru;

                ViewBag.baslik1.Add(item.Key);
                ViewBag.puan1.Add(p1);
                ViewBag.adet1.Add(kisiSayisi);

            }
            foreach (var item in bul.GroupBy(x => x.User.Departman.DepartmanAdi))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerçek kişi adedi

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

                // Puanı hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanını yüzdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik2.Add(item.Key);
                ViewBag.puan2.Add(p1);
                ViewBag.adet2.Add(kisiSayisi);

            }
            foreach (var item in bul.GroupBy(x => x.User.Cinsiyet.CinsiyetAdi))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerçek kişi adedi

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

                // Puanı hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanını yüzdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik3.Add(item.Key);
                ViewBag.puan3.Add(p1);
                ViewBag.adet3.Add(kisiSayisi);

            }
            foreach (var item in bul.GroupBy(x => x.User.Egitim.EgitimAdi))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerçek kişi adedi

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

                // Puanı hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanını yüzdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik4.Add(item.Key);
                ViewBag.puan4.Add(p1);
                ViewBag.adet4.Add(kisiSayisi);


            }
            var bugun = DateTime.Today;

            // Önce boş listeleri hazırla
            ViewBag.baslik5 = new List<string>();
            ViewBag.puan5 = new List<float>();

            foreach (var item in bul
                .Where(x => x.User.UserDogumTar != null)
                .GroupBy(x => x.User.UserDogumTar.Value.Year))
            {
                var dogumYili = item.Key;
                var yas = bugun.Year - dogumYili;
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerçek kişi adedi


                // Eğer doğum günü bu yıl daha gelmediyse yaşı 1 azalt
                var ilkKayit = item.First().User.UserDogumTar;
                if (ilkKayit != null && bugun.DayOfYear < ilkKayit.Value.DayOfYear)
                {
                    yas--;
                }

                // Ortalama puanı hesapla (kişiye göre normalize)
                var ortalama = item
                    .GroupBy(x => x.UserId)
                    .Select(g => g.Average(y => y.CevapPuan))
                    .Average();

                var ortalamaYuzde = ortalama * 20; // 5 üzerinden 100'e çevirme

                // 🔹 Burada artık Add et
                ViewBag.baslik5.Add(yas.ToString());       // X ekseni → yaş
                ViewBag.puan5.Add((float)ortalamaYuzde);   // Y ekseni → puan
                ViewBag.adet5.Add(kisiSayisi);   // Y ekseni → puan
            }

            foreach (var item in bul.GroupBy(x => x.User.Sehir.SehiarAdi))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerçek kişi adedi

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

                // Puanı hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanını yüzdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik6.Add(item.Key);
                ViewBag.puan6.Add(p1);
                ViewBag.adet6.Add(kisiSayisi);

            }
            foreach (var item in bul.GroupBy(x => x.User.Sube.SubeAdi))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerçek kişi adedi

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

                // Puanı hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanını yüzdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik7.Add(item.Key);
                ViewBag.puan7.Add(p1);
                ViewBag.adet7.Add(kisiSayisi);

            }
            foreach (var item in bul.GroupBy(x => x.User.Unvan.UnvanAdi))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerçek kişi adedi

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

                // Puanı hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanını yüzdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik8.Add(item.Key);
                ViewBag.puan8.Add(p1);
                ViewBag.adet8.Add(kisiSayisi);

            }
            foreach (var item in bul.GroupBy(x => x.User.Yaka.YakaAdi))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerçek kişi adedi

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

                // Puanı hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanını yüzdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik9.Add(item.Key);
                ViewBag.puan9.Add(p1);
                ViewBag.adet9.Add(kisiSayisi);

            }
            foreach (var item in bul.GroupBy(x => x.User.Yonetici.YoneticiAdi))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerçek kişi adedi

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

                // Puanı hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanını yüzdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik10.Add(item.Key);
                ViewBag.puan10.Add(p1);
                ViewBag.adet10.Add(kisiSayisi);

            }
            foreach (var item in bul.GroupBy(x => x.Soru.SoruAdi))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerçek kişi adedi

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

                // Puanı hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanını yüzdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik11.Add(item.Key);
                ViewBag.puan11.Add(p1);
                ViewBag.adet11.Add(kisiSayisi);

            }
            foreach (var item in bul.GroupBy(x => x.SoruGrup.SoruGrupAdi))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerçek kişi adedi

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

                // Puanı hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanını yüzdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik12.Add(item.Key);
                ViewBag.puan12.Add(p1);
                ViewBag.adet12.Add(kisiSayisi);
            }

            return View(bul);
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

            var bul1 = db.Havuz.Where(x => x.AnketId == ank);
            var bul = bul1.Where(x => x.User.UserSube == id);
            var adi = db.Havuz.Where(x => x.User.UserSube == id).FirstOrDefault();
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
                ViewBag.anketadi = adi.User.Sube.SubeAdi;
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

            // Eğitim
            ViewBag.baslik4 = new List<string>();
            ViewBag.puan4 = new List<float>();
            ViewBag.adet4 = new List<int>();

            // Yaş
            ViewBag.baslik5 = new List<string>();  // int yerine string yapalım → chart için daha kolay
            ViewBag.puan5 = new List<float>();
            ViewBag.adet5 = new List<int>();

            // Şehir
            ViewBag.baslik6 = new List<string>();
            ViewBag.puan6 = new List<float>();
            ViewBag.adet6 = new List<int>();

            // Şube
            ViewBag.baslik7 = new List<string>();
            ViewBag.puan7 = new List<float>();
            ViewBag.adet7 = new List<int>();

            // Ünvan
            ViewBag.baslik8 = new List<string>();
            ViewBag.puan8 = new List<float>();
            ViewBag.adet8 = new List<int>();

            // Yaka
            ViewBag.baslik9 = new List<string>();
            ViewBag.puan9 = new List<float>();
            ViewBag.adet9 = new List<int>();

            // Yönetici
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
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerçek kişi adedi

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

                // Puanı hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanını yüzdesini bulur
                var p1 = p2 / soru;

                ViewBag.baslik1.Add(item.Key);
                ViewBag.puan1.Add(p1);
                ViewBag.adet1.Add(kisiSayisi);

            }
            foreach (var item in bul.GroupBy(x => x.User.Departman.DepartmanAdi))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerçek kişi adedi

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

                // Puanı hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanını yüzdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik2.Add(item.Key);
                ViewBag.puan2.Add(p1);
                ViewBag.adet2.Add(kisiSayisi);

            }
            foreach (var item in bul.GroupBy(x => x.User.Cinsiyet.CinsiyetAdi))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerçek kişi adedi

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

                // Puanı hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanını yüzdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik3.Add(item.Key);
                ViewBag.puan3.Add(p1);
                ViewBag.adet3.Add(kisiSayisi);

            }
            foreach (var item in bul.GroupBy(x => x.User.Egitim.EgitimAdi))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerçek kişi adedi

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

                // Puanı hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanını yüzdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik4.Add(item.Key);
                ViewBag.puan4.Add(p1);
                ViewBag.adet4.Add(kisiSayisi);


            }
            var bugun = DateTime.Today;

            // Önce boş listeleri hazırla
            ViewBag.baslik5 = new List<string>();
            ViewBag.puan5 = new List<float>();

            foreach (var item in bul
                .Where(x => x.User.UserDogumTar != null)
                .GroupBy(x => x.User.UserDogumTar.Value.Year))
            {
                var dogumYili = item.Key;
                var yas = bugun.Year - dogumYili;
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerçek kişi adedi


                // Eğer doğum günü bu yıl daha gelmediyse yaşı 1 azalt
                var ilkKayit = item.First().User.UserDogumTar;
                if (ilkKayit != null && bugun.DayOfYear < ilkKayit.Value.DayOfYear)
                {
                    yas--;
                }

                // Ortalama puanı hesapla (kişiye göre normalize)
                var ortalama = item
                    .GroupBy(x => x.UserId)
                    .Select(g => g.Average(y => y.CevapPuan))
                    .Average();

                var ortalamaYuzde = ortalama * 20; // 5 üzerinden 100'e çevirme

                // 🔹 Burada artık Add et
                ViewBag.baslik5.Add(yas.ToString());       // X ekseni → yaş
                ViewBag.puan5.Add((float)ortalamaYuzde);   // Y ekseni → puan
                ViewBag.adet5.Add(kisiSayisi);   // Y ekseni → puan
            }

            foreach (var item in bul.GroupBy(x => x.User.Sehir.SehiarAdi))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerçek kişi adedi

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

                // Puanı hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanını yüzdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik6.Add(item.Key);
                ViewBag.puan6.Add(p1);
                ViewBag.adet6.Add(kisiSayisi);

            }
            foreach (var item in bul.GroupBy(x => x.User.Sube.SubeAdi))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerçek kişi adedi

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

                // Puanı hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanını yüzdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik7.Add(item.Key);
                ViewBag.puan7.Add(p1);
                ViewBag.adet7.Add(kisiSayisi);

            }
            foreach (var item in bul.GroupBy(x => x.User.Unvan.UnvanAdi))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerçek kişi adedi

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

                // Puanı hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanını yüzdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik8.Add(item.Key);
                ViewBag.puan8.Add(p1);
                ViewBag.adet8.Add(kisiSayisi);

            }
            foreach (var item in bul.GroupBy(x => x.User.Yaka.YakaAdi))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerçek kişi adedi

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

                // Puanı hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanını yüzdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik9.Add(item.Key);
                ViewBag.puan9.Add(p1);
                ViewBag.adet9.Add(kisiSayisi);

            }
            foreach (var item in bul.GroupBy(x => x.User.Yonetici.YoneticiAdi))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerçek kişi adedi

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

                // Puanı hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanını yüzdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik10.Add(item.Key);
                ViewBag.puan10.Add(p1);
                ViewBag.adet10.Add(kisiSayisi);

            }
            foreach (var item in bul.GroupBy(x => x.Soru.SoruAdi))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerçek kişi adedi

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

                // Puanı hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanını yüzdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik11.Add(item.Key);
                ViewBag.puan11.Add(p1);
                ViewBag.adet11.Add(kisiSayisi);

            }
            foreach (var item in bul.GroupBy(x => x.SoruGrup.SoruGrupAdi))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerçek kişi adedi

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

                // Puanı hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanını yüzdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik12.Add(item.Key);
                ViewBag.puan12.Add(p1);
                ViewBag.adet12.Add(kisiSayisi);
            }

            return View(bul);
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

            var bul1 = db.Havuz.Where(x => x.AnketId == ank);
            var bul = bul1.Where(x => x.User.UserUnvan == id);
            var adi = db.Havuz.Where(x => x.User.UserUnvan == id).FirstOrDefault();
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
                ViewBag.anketadi = adi.User.Unvan.UnvanAdi;
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

            // Eğitim
            ViewBag.baslik4 = new List<string>();
            ViewBag.puan4 = new List<float>();
            ViewBag.adet4 = new List<int>();

            // Yaş
            ViewBag.baslik5 = new List<string>();  // int yerine string yapalım → chart için daha kolay
            ViewBag.puan5 = new List<float>();
            ViewBag.adet5 = new List<int>();

            // Şehir
            ViewBag.baslik6 = new List<string>();
            ViewBag.puan6 = new List<float>();
            ViewBag.adet6 = new List<int>();

            // Şube
            ViewBag.baslik7 = new List<string>();
            ViewBag.puan7 = new List<float>();
            ViewBag.adet7 = new List<int>();

            // Ünvan
            ViewBag.baslik8 = new List<string>();
            ViewBag.puan8 = new List<float>();
            ViewBag.adet8 = new List<int>();

            // Yaka
            ViewBag.baslik9 = new List<string>();
            ViewBag.puan9 = new List<float>();
            ViewBag.adet9 = new List<int>();

            // Yönetici
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
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerçek kişi adedi

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

                // Puanı hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanını yüzdesini bulur
                var p1 = p2 / soru;

                ViewBag.baslik1.Add(item.Key);
                ViewBag.puan1.Add(p1);
                ViewBag.adet1.Add(kisiSayisi);

            }
            foreach (var item in bul.GroupBy(x => x.User.Departman.DepartmanAdi))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerçek kişi adedi

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

                // Puanı hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanını yüzdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik2.Add(item.Key);
                ViewBag.puan2.Add(p1);
                ViewBag.adet2.Add(kisiSayisi);

            }
            foreach (var item in bul.GroupBy(x => x.User.Cinsiyet.CinsiyetAdi))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerçek kişi adedi

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

                // Puanı hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanını yüzdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik3.Add(item.Key);
                ViewBag.puan3.Add(p1);
                ViewBag.adet3.Add(kisiSayisi);

            }
            foreach (var item in bul.GroupBy(x => x.User.Egitim.EgitimAdi))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerçek kişi adedi

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

                // Puanı hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanını yüzdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik4.Add(item.Key);
                ViewBag.puan4.Add(p1);
                ViewBag.adet4.Add(kisiSayisi);


            }
            var bugun = DateTime.Today;

            // Önce boş listeleri hazırla
            ViewBag.baslik5 = new List<string>();
            ViewBag.puan5 = new List<float>();

            foreach (var item in bul
                .Where(x => x.User.UserDogumTar != null)
                .GroupBy(x => x.User.UserDogumTar.Value.Year))
            {
                var dogumYili = item.Key;
                var yas = bugun.Year - dogumYili;
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerçek kişi adedi


                // Eğer doğum günü bu yıl daha gelmediyse yaşı 1 azalt
                var ilkKayit = item.First().User.UserDogumTar;
                if (ilkKayit != null && bugun.DayOfYear < ilkKayit.Value.DayOfYear)
                {
                    yas--;
                }

                // Ortalama puanı hesapla (kişiye göre normalize)
                var ortalama = item
                    .GroupBy(x => x.UserId)
                    .Select(g => g.Average(y => y.CevapPuan))
                    .Average();

                var ortalamaYuzde = ortalama * 20; // 5 üzerinden 100'e çevirme

                // 🔹 Burada artık Add et
                ViewBag.baslik5.Add(yas.ToString());       // X ekseni → yaş
                ViewBag.puan5.Add((float)ortalamaYuzde);   // Y ekseni → puan
                ViewBag.adet5.Add(kisiSayisi);   // Y ekseni → puan
            }

            foreach (var item in bul.GroupBy(x => x.User.Sehir.SehiarAdi))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerçek kişi adedi

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

                // Puanı hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanını yüzdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik6.Add(item.Key);
                ViewBag.puan6.Add(p1);
                ViewBag.adet6.Add(kisiSayisi);

            }
            foreach (var item in bul.GroupBy(x => x.User.Sube.SubeAdi))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerçek kişi adedi

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

                // Puanı hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanını yüzdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik7.Add(item.Key);
                ViewBag.puan7.Add(p1);
                ViewBag.adet7.Add(kisiSayisi);

            }
            foreach (var item in bul.GroupBy(x => x.User.Unvan.UnvanAdi))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerçek kişi adedi

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

                // Puanı hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanını yüzdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik8.Add(item.Key);
                ViewBag.puan8.Add(p1);
                ViewBag.adet8.Add(kisiSayisi);

            }
            foreach (var item in bul.GroupBy(x => x.User.Yaka.YakaAdi))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerçek kişi adedi

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

                // Puanı hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanını yüzdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik9.Add(item.Key);
                ViewBag.puan9.Add(p1);
                ViewBag.adet9.Add(kisiSayisi);

            }
            foreach (var item in bul.GroupBy(x => x.User.Yonetici.YoneticiAdi))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerçek kişi adedi

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

                // Puanı hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanını yüzdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik10.Add(item.Key);
                ViewBag.puan10.Add(p1);
                ViewBag.adet10.Add(kisiSayisi);

            }
            foreach (var item in bul.GroupBy(x => x.Soru.SoruAdi))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerçek kişi adedi

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

                // Puanı hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanını yüzdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik11.Add(item.Key);
                ViewBag.puan11.Add(p1);
                ViewBag.adet11.Add(kisiSayisi);

            }
            foreach (var item in bul.GroupBy(x => x.SoruGrup.SoruGrupAdi))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerçek kişi adedi

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

                // Puanı hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanını yüzdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik12.Add(item.Key);
                ViewBag.puan12.Add(p1);
                ViewBag.adet12.Add(kisiSayisi);
            }

            return View(bul);
        }
        public ActionResult AnketUserIndex(int id, int? ank, int? user)
        {
            if (Session["id"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }

            var izll = db.Izledim.Where(x => x.AnketId == ank && x.UseId == id).FirstOrDefault();

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

            var bul1 = db.Havuz.Where(x => x.AnketId == ank);
            var bul = db.Havuz.Where(x => x.AnketId == ank).Where(x => x.UserId == id);
            var adi = db.Havuz
                .Where(x => x.AnketId == ank && x.UserId == id)
                .OrderByDescending(x => x.KayitTar)
                .FirstOrDefault();
            var ankadi = db.Anket.Where(x => x.AnketId == ank).FirstOrDefault();
            if (adi != null)
            {
                ViewBag.adisoyadi = adi.User.UserAdi;
                ViewBag.unvan = adi.User.Unvan.UnvanAdi;
                ViewBag.egitim = adi.User.Egitim.EgitimAdi;
                ViewBag.cinsiyet = adi.User.Cinsiyet.CinsiyetAdi;
                ViewBag.departman = adi.User.Departman.DepartmanAdi;
                ViewBag.giris = adi.User.UserIseGirisTarihi;
                ViewBag.kayit = adi.KayitTar;
                ViewBag.yonetici = adi.User.Yonetici.YoneticiAdi;
                ViewBag.yaka = adi.User.Yaka.YakaAdi;
                ViewBag.sehir = adi.User.Sehir.SehiarAdi;
                ViewBag.sube = adi.User.Sube.SubeAdi;
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
                ViewBag.anketadi = adi.User.UserAdi;
            }

            if (ankadi.Sinav != true)
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

                // Puanı hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanını yüzdesini bulur
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

            var bul1 = db.Havuz.Where(x => x.AnketId == ank);
            var bul = bul1.Where(x => x.User.UserYaka == id);
            var adi = db.Havuz.Where(x => x.User.UserYaka == id).FirstOrDefault();
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
                ViewBag.anketadi = adi.User.Yaka.YakaAdi;
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

            // Eğitim
            ViewBag.baslik4 = new List<string>();
            ViewBag.puan4 = new List<float>();
            ViewBag.adet4 = new List<int>();

            // Yaş
            ViewBag.baslik5 = new List<string>();  // int yerine string yapalım → chart için daha kolay
            ViewBag.puan5 = new List<float>();
            ViewBag.adet5 = new List<int>();

            // Şehir
            ViewBag.baslik6 = new List<string>();
            ViewBag.puan6 = new List<float>();
            ViewBag.adet6 = new List<int>();

            // Şube
            ViewBag.baslik7 = new List<string>();
            ViewBag.puan7 = new List<float>();
            ViewBag.adet7 = new List<int>();

            // Ünvan
            ViewBag.baslik8 = new List<string>();
            ViewBag.puan8 = new List<float>();
            ViewBag.adet8 = new List<int>();

            // Yaka
            ViewBag.baslik9 = new List<string>();
            ViewBag.puan9 = new List<float>();
            ViewBag.adet9 = new List<int>();

            // Yönetici
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
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerçek kişi adedi

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

                // Puanı hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanını yüzdesini bulur
                var p1 = p2 / soru;

                ViewBag.baslik1.Add(item.Key);
                ViewBag.puan1.Add(p1);
                ViewBag.adet1.Add(kisiSayisi);

            }
            foreach (var item in bul.GroupBy(x => x.User.Departman.DepartmanAdi))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerçek kişi adedi

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

                // Puanı hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanını yüzdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik2.Add(item.Key);
                ViewBag.puan2.Add(p1);
                ViewBag.adet2.Add(kisiSayisi);

            }
            foreach (var item in bul.GroupBy(x => x.User.Cinsiyet.CinsiyetAdi))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerçek kişi adedi

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

                // Puanı hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanını yüzdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik3.Add(item.Key);
                ViewBag.puan3.Add(p1);
                ViewBag.adet3.Add(kisiSayisi);

            }
            foreach (var item in bul.GroupBy(x => x.User.Egitim.EgitimAdi))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerçek kişi adedi

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

                // Puanı hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanını yüzdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik4.Add(item.Key);
                ViewBag.puan4.Add(p1);
                ViewBag.adet4.Add(kisiSayisi);


            }
            var bugun = DateTime.Today;

            // Önce boş listeleri hazırla
            ViewBag.baslik5 = new List<string>();
            ViewBag.puan5 = new List<float>();

            foreach (var item in bul
                .Where(x => x.User.UserDogumTar != null)
                .GroupBy(x => x.User.UserDogumTar.Value.Year))
            {
                var dogumYili = item.Key;
                var yas = bugun.Year - dogumYili;
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerçek kişi adedi


                // Eğer doğum günü bu yıl daha gelmediyse yaşı 1 azalt
                var ilkKayit = item.First().User.UserDogumTar;
                if (ilkKayit != null && bugun.DayOfYear < ilkKayit.Value.DayOfYear)
                {
                    yas--;
                }

                // Ortalama puanı hesapla (kişiye göre normalize)
                var ortalama = item
                    .GroupBy(x => x.UserId)
                    .Select(g => g.Average(y => y.CevapPuan))
                    .Average();

                var ortalamaYuzde = ortalama * 20; // 5 üzerinden 100'e çevirme

                // 🔹 Burada artık Add et
                ViewBag.baslik5.Add(yas.ToString());       // X ekseni → yaş
                ViewBag.puan5.Add((float)ortalamaYuzde);   // Y ekseni → puan
                ViewBag.adet5.Add(kisiSayisi);   // Y ekseni → puan
            }

            foreach (var item in bul.GroupBy(x => x.User.Sehir.SehiarAdi))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerçek kişi adedi

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

                // Puanı hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanını yüzdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik6.Add(item.Key);
                ViewBag.puan6.Add(p1);
                ViewBag.adet6.Add(kisiSayisi);

            }
            foreach (var item in bul.GroupBy(x => x.User.Sube.SubeAdi))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerçek kişi adedi

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

                // Puanı hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanını yüzdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik7.Add(item.Key);
                ViewBag.puan7.Add(p1);
                ViewBag.adet7.Add(kisiSayisi);

            }
            foreach (var item in bul.GroupBy(x => x.User.Unvan.UnvanAdi))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerçek kişi adedi

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

                // Puanı hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanını yüzdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik8.Add(item.Key);
                ViewBag.puan8.Add(p1);
                ViewBag.adet8.Add(kisiSayisi);

            }
            foreach (var item in bul.GroupBy(x => x.User.Yaka.YakaAdi))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerçek kişi adedi

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

                // Puanı hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanını yüzdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik9.Add(item.Key);
                ViewBag.puan9.Add(p1);
                ViewBag.adet9.Add(kisiSayisi);

            }
            foreach (var item in bul.GroupBy(x => x.User.Yonetici.YoneticiAdi))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerçek kişi adedi

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

                // Puanı hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanını yüzdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik10.Add(item.Key);
                ViewBag.puan10.Add(p1);
                ViewBag.adet10.Add(kisiSayisi);

            }
            foreach (var item in bul.GroupBy(x => x.Soru.SoruAdi))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerçek kişi adedi

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

                // Puanı hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanını yüzdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik11.Add(item.Key);
                ViewBag.puan11.Add(p1);
                ViewBag.adet11.Add(kisiSayisi);

            }
            foreach (var item in bul.GroupBy(x => x.SoruGrup.SoruGrupAdi))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerçek kişi adedi

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

                // Puanı hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanını yüzdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik12.Add(item.Key);
                ViewBag.puan12.Add(p1);
                ViewBag.adet12.Add(kisiSayisi);
            }

            return View(bul);
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

            var bul1 = db.Havuz.Where(x => x.AnketId == ank);
            var bul = bul1.Where(x => x.User.UserYoneticisi == id);
            var adi = db.Havuz.Where(x => x.User.UserYoneticisi == id).FirstOrDefault();
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
                ViewBag.anketadi = adi.User.Yonetici.YoneticiAdi;
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

            // Eğitim
            ViewBag.baslik4 = new List<string>();
            ViewBag.puan4 = new List<float>();
            ViewBag.adet4 = new List<int>();

            // Yaş
            ViewBag.baslik5 = new List<string>();  // int yerine string yapalım → chart için daha kolay
            ViewBag.puan5 = new List<float>();
            ViewBag.adet5 = new List<int>();

            // Şehir
            ViewBag.baslik6 = new List<string>();
            ViewBag.puan6 = new List<float>();
            ViewBag.adet6 = new List<int>();

            // Şube
            ViewBag.baslik7 = new List<string>();
            ViewBag.puan7 = new List<float>();
            ViewBag.adet7 = new List<int>();

            // Ünvan
            ViewBag.baslik8 = new List<string>();
            ViewBag.puan8 = new List<float>();
            ViewBag.adet8 = new List<int>();

            // Yaka
            ViewBag.baslik9 = new List<string>();
            ViewBag.puan9 = new List<float>();
            ViewBag.adet9 = new List<int>();

            // Yönetici
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
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerçek kişi adedi

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

                // Puanı hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanını yüzdesini bulur
                var p1 = p2 / soru;

                ViewBag.baslik1.Add(item.Key);
                ViewBag.puan1.Add(p1);
                ViewBag.adet1.Add(kisiSayisi);

            }
            foreach (var item in bul.GroupBy(x => x.User.Departman.DepartmanAdi))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerçek kişi adedi

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

                // Puanı hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanını yüzdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik2.Add(item.Key);
                ViewBag.puan2.Add(p1);
                ViewBag.adet2.Add(kisiSayisi);

            }
            foreach (var item in bul.GroupBy(x => x.User.Cinsiyet.CinsiyetAdi))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerçek kişi adedi

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

                // Puanı hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanını yüzdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik3.Add(item.Key);
                ViewBag.puan3.Add(p1);
                ViewBag.adet3.Add(kisiSayisi);

            }
            foreach (var item in bul.GroupBy(x => x.User.Egitim.EgitimAdi))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerçek kişi adedi

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

                // Puanı hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanını yüzdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik4.Add(item.Key);
                ViewBag.puan4.Add(p1);
                ViewBag.adet4.Add(kisiSayisi);


            }
            var bugun = DateTime.Today;

            // Önce boş listeleri hazırla
            ViewBag.baslik5 = new List<string>();
            ViewBag.puan5 = new List<float>();

            foreach (var item in bul
                .Where(x => x.User.UserDogumTar != null)
                .GroupBy(x => x.User.UserDogumTar.Value.Year))
            {
                var dogumYili = item.Key;
                var yas = bugun.Year - dogumYili;
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerçek kişi adedi


                // Eğer doğum günü bu yıl daha gelmediyse yaşı 1 azalt
                var ilkKayit = item.First().User.UserDogumTar;
                if (ilkKayit != null && bugun.DayOfYear < ilkKayit.Value.DayOfYear)
                {
                    yas--;
                }

                // Ortalama puanı hesapla (kişiye göre normalize)
                var ortalama = item
                    .GroupBy(x => x.UserId)
                    .Select(g => g.Average(y => y.CevapPuan))
                    .Average();

                var ortalamaYuzde = ortalama * 20; // 5 üzerinden 100'e çevirme

                // 🔹 Burada artık Add et
                ViewBag.baslik5.Add(yas.ToString());       // X ekseni → yaş
                ViewBag.puan5.Add((float)ortalamaYuzde);   // Y ekseni → puan
                ViewBag.adet5.Add(kisiSayisi);   // Y ekseni → puan
            }

            foreach (var item in bul.GroupBy(x => x.User.Sehir.SehiarAdi))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerçek kişi adedi

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

                // Puanı hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanını yüzdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik6.Add(item.Key);
                ViewBag.puan6.Add(p1);
                ViewBag.adet6.Add(kisiSayisi);

            }
            foreach (var item in bul.GroupBy(x => x.User.Sube.SubeAdi))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerçek kişi adedi

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

                // Puanı hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanını yüzdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik7.Add(item.Key);
                ViewBag.puan7.Add(p1);
                ViewBag.adet7.Add(kisiSayisi);

            }
            foreach (var item in bul.GroupBy(x => x.User.Unvan.UnvanAdi))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerçek kişi adedi

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

                // Puanı hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanını yüzdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik8.Add(item.Key);
                ViewBag.puan8.Add(p1);
                ViewBag.adet8.Add(kisiSayisi);

            }
            foreach (var item in bul.GroupBy(x => x.User.Yaka.YakaAdi))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerçek kişi adedi

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

                // Puanı hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanını yüzdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik9.Add(item.Key);
                ViewBag.puan9.Add(p1);
                ViewBag.adet9.Add(kisiSayisi);

            }
            foreach (var item in bul.GroupBy(x => x.User.Yonetici.YoneticiAdi))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerçek kişi adedi

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

                // Puanı hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanını yüzdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik10.Add(item.Key);
                ViewBag.puan10.Add(p1);
                ViewBag.adet10.Add(kisiSayisi);

            }
            foreach (var item in bul.GroupBy(x => x.Soru.SoruAdi))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerçek kişi adedi

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

                // Puanı hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanını yüzdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik11.Add(item.Key);
                ViewBag.puan11.Add(p1);
                ViewBag.adet11.Add(kisiSayisi);

            }
            foreach (var item in bul.GroupBy(x => x.SoruGrup.SoruGrupAdi))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerçek kişi adedi

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

                // Puanı hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanını yüzdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik12.Add(item.Key);
                ViewBag.puan12.Add(p1);
                ViewBag.adet12.Add(kisiSayisi);
            }

            return View(bul);
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

            var bul = db.Havuz
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
                .Where(x => x.AnketId == ank && x.SoruID == id)
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
                ViewBag.anketadi = adi.Soru?.SoruAdi ?? "Soru detayı";
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

            // Eğitim
            ViewBag.baslik4 = new List<string>();
            ViewBag.puan4 = new List<float>();
            ViewBag.adet4 = new List<int>();

            // Yaş
            ViewBag.baslik5 = new List<string>();  // int yerine string yapalım → chart için daha kolay
            ViewBag.puan5 = new List<float>();
            ViewBag.adet5 = new List<int>();

            // Şehir
            ViewBag.baslik6 = new List<string>();
            ViewBag.puan6 = new List<float>();
            ViewBag.adet6 = new List<int>();

            // Şube
            ViewBag.baslik7 = new List<string>();
            ViewBag.puan7 = new List<float>();
            ViewBag.adet7 = new List<int>();

            // Ünvan
            ViewBag.baslik8 = new List<string>();
            ViewBag.puan8 = new List<float>();
            ViewBag.adet8 = new List<int>();

            // Yaka
            ViewBag.baslik9 = new List<string>();
            ViewBag.puan9 = new List<float>();
            ViewBag.adet9 = new List<int>();

            // Yönetici
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


            foreach (var item in bul.GroupBy(x => x.Anket?.AnketAdi ?? "Çalışma"))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerçek kişi adedi

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

                // Puanı hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanını yüzdesini bulur
                var p1 = p2 / soru;

                ViewBag.baslik1.Add(item.Key);
                ViewBag.puan1.Add(p1);
                ViewBag.adet1.Add(kisiSayisi);

            }
            foreach (var item in bul.Where(x => x.User?.Departman != null).GroupBy(x => x.User.Departman.DepartmanAdi))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerçek kişi adedi

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

                // Puanı hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanını yüzdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik2.Add(item.Key);
                ViewBag.puan2.Add(p1);
                ViewBag.adet2.Add(kisiSayisi);

            }
            foreach (var item in bul.Where(x => x.User?.Cinsiyet != null).GroupBy(x => x.User.Cinsiyet.CinsiyetAdi))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerçek kişi adedi

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

                // Puanı hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanını yüzdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik3.Add(item.Key);
                ViewBag.puan3.Add(p1);
                ViewBag.adet3.Add(kisiSayisi);

            }
            foreach (var item in bul.Where(x => x.User?.Egitim != null).GroupBy(x => x.User.Egitim.EgitimAdi))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerçek kişi adedi

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

                // Puanı hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanını yüzdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik4.Add(item.Key);
                ViewBag.puan4.Add(p1);
                ViewBag.adet4.Add(kisiSayisi);


            }
            var bugun = DateTime.Today;

            // Önce boş listeleri hazırla
            ViewBag.baslik5 = new List<string>();
            ViewBag.puan5 = new List<float>();

            foreach (var item in bul
                .Where(x => x.User?.UserDogumTar != null)
                .GroupBy(x => x.User.UserDogumTar.Value.Year))
            {
                var dogumYili = item.Key;
                var yas = bugun.Year - dogumYili;
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerçek kişi adedi


                // Eğer doğum günü bu yıl daha gelmediyse yaşı 1 azalt
                var ilkKayit = item.FirstOrDefault()?.User?.UserDogumTar;
                if (ilkKayit != null && bugun.DayOfYear < ilkKayit.Value.DayOfYear)
                {
                    yas--;
                }

                // Ortalama puanı hesapla (kişiye göre normalize)
                var ortalama = item
                    .GroupBy(x => x.UserId)
                    .Select(g => g.Average(y => y.CevapPuan))
                    .Average();

                var ortalamaYuzde = ortalama * 20; // 5 üzerinden 100'e çevirme

                // 🔹 Burada artık Add et
                ViewBag.baslik5.Add(yas.ToString());       // X ekseni → yaş
                ViewBag.puan5.Add((float)ortalamaYuzde);   // Y ekseni → puan
                ViewBag.adet5.Add(kisiSayisi);   // Y ekseni → puan
            }

            foreach (var item in bul.Where(x => x.User?.Sehir != null).GroupBy(x => x.User.Sehir.SehiarAdi))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerçek kişi adedi

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

                // Puanı hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanını yüzdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik6.Add(item.Key);
                ViewBag.puan6.Add(p1);
                ViewBag.adet6.Add(kisiSayisi);

            }
            foreach (var item in bul.Where(x => x.User?.Sube != null).GroupBy(x => x.User.Sube.SubeAdi))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerçek kişi adedi

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

                // Puanı hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanını yüzdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik7.Add(item.Key);
                ViewBag.puan7.Add(p1);
                ViewBag.adet7.Add(kisiSayisi);

            }
            foreach (var item in bul.Where(x => x.User?.Unvan != null).GroupBy(x => x.User.Unvan.UnvanAdi))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerçek kişi adedi

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

                // Puanı hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanını yüzdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik8.Add(item.Key);
                ViewBag.puan8.Add(p1);
                ViewBag.adet8.Add(kisiSayisi);

            }
            foreach (var item in bul.Where(x => x.User?.Yaka != null).GroupBy(x => x.User.Yaka.YakaAdi))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerçek kişi adedi

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

                // Puanı hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanını yüzdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik9.Add(item.Key);
                ViewBag.puan9.Add(p1);
                ViewBag.adet9.Add(kisiSayisi);

            }
            foreach (var item in bul.Where(x => x.User?.Yonetici != null).GroupBy(x => x.User.Yonetici.YoneticiAdi))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerçek kişi adedi

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

                // Puanı hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanını yüzdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik10.Add(item.Key);
                ViewBag.puan10.Add(p1);
                ViewBag.adet10.Add(kisiSayisi);

            }
            foreach (var item in bul.Where(x => x.Soru != null).GroupBy(x => x.Soru.SoruAdi))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerçek kişi adedi

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

                // Puanı hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanını yüzdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik11.Add(item.Key);
                ViewBag.puan11.Add(p1);
                ViewBag.adet11.Add(kisiSayisi);

            }
            foreach (var item in bul.GroupBy(x => x.SoruGrup?.SoruGrupAdi ?? x.Soru?.SoruGrup?.SoruGrupAdi ?? "Grupsuz"))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerçek kişi adedi

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

                // Puanı hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanını yüzdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik12.Add(item.Key);
                ViewBag.puan12.Add(p1);
                ViewBag.adet12.Add(kisiSayisi);
            }

            return View(bul);
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

            var bul1 = db.Havuz.Where(x => x.AnketId == ank);
            var bul = bul1.Where(x => x.SoruGrupId == id);
            var adi = db.Havuz.Where(x => x.SoruGrupId == id).FirstOrDefault();
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

            // Eğitim
            ViewBag.baslik4 = new List<string>();
            ViewBag.puan4 = new List<float>();
            ViewBag.adet4 = new List<int>();

            // Yaş
            ViewBag.baslik5 = new List<string>();  // int yerine string yapalım → chart için daha kolay
            ViewBag.puan5 = new List<float>();
            ViewBag.adet5 = new List<int>();

            // Şehir
            ViewBag.baslik6 = new List<string>();
            ViewBag.puan6 = new List<float>();
            ViewBag.adet6 = new List<int>();

            // Şube
            ViewBag.baslik7 = new List<string>();
            ViewBag.puan7 = new List<float>();
            ViewBag.adet7 = new List<int>();

            // Ünvan
            ViewBag.baslik8 = new List<string>();
            ViewBag.puan8 = new List<float>();
            ViewBag.adet8 = new List<int>();

            // Yaka
            ViewBag.baslik9 = new List<string>();
            ViewBag.puan9 = new List<float>();
            ViewBag.adet9 = new List<int>();

            // Yönetici
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
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerçek kişi adedi

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

                // Puanı hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanını yüzdesini bulur
                var p1 = p2 / soru;

                ViewBag.baslik1.Add(item.Key);
                ViewBag.puan1.Add(p1);
                ViewBag.adet1.Add(kisiSayisi);

            }
            foreach (var item in bul.GroupBy(x => x.User.Departman.DepartmanAdi))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerçek kişi adedi

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

                // Puanı hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanını yüzdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik2.Add(item.Key);
                ViewBag.puan2.Add(p1);
                ViewBag.adet2.Add(kisiSayisi);

            }
            foreach (var item in bul.GroupBy(x => x.User.Cinsiyet.CinsiyetAdi))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerçek kişi adedi

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

                // Puanı hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanını yüzdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik3.Add(item.Key);
                ViewBag.puan3.Add(p1);
                ViewBag.adet3.Add(kisiSayisi);

            }
            foreach (var item in bul.GroupBy(x => x.User.Egitim.EgitimAdi))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerçek kişi adedi

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

                // Puanı hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanını yüzdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik4.Add(item.Key);
                ViewBag.puan4.Add(p1);
                ViewBag.adet4.Add(kisiSayisi);


            }
            var bugun = DateTime.Today;

            // Önce boş listeleri hazırla
            ViewBag.baslik5 = new List<string>();
            ViewBag.puan5 = new List<float>();

            foreach (var item in bul
                .Where(x => x.User.UserDogumTar != null)
                .GroupBy(x => x.User.UserDogumTar.Value.Year))
            {
                var dogumYili = item.Key;
                var yas = bugun.Year - dogumYili;
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerçek kişi adedi


                // Eğer doğum günü bu yıl daha gelmediyse yaşı 1 azalt
                var ilkKayit = item.First().User.UserDogumTar;
                if (ilkKayit != null && bugun.DayOfYear < ilkKayit.Value.DayOfYear)
                {
                    yas--;
                }

                // Ortalama puanı hesapla (kişiye göre normalize)
                var ortalama = item
                    .GroupBy(x => x.UserId)
                    .Select(g => g.Average(y => y.CevapPuan))
                    .Average();

                var ortalamaYuzde = ortalama * 20; // 5 üzerinden 100'e çevirme

                // 🔹 Burada artık Add et
                ViewBag.baslik5.Add(yas.ToString());       // X ekseni → yaş
                ViewBag.puan5.Add((float)ortalamaYuzde);   // Y ekseni → puan
                ViewBag.adet5.Add(kisiSayisi);   // Y ekseni → puan
            }

            foreach (var item in bul.GroupBy(x => x.User.Sehir.SehiarAdi))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerçek kişi adedi

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

                // Puanı hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanını yüzdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik6.Add(item.Key);
                ViewBag.puan6.Add(p1);
                ViewBag.adet6.Add(kisiSayisi);

            }
            foreach (var item in bul.GroupBy(x => x.User.Sube.SubeAdi))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerçek kişi adedi

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

                // Puanı hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanını yüzdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik7.Add(item.Key);
                ViewBag.puan7.Add(p1);
                ViewBag.adet7.Add(kisiSayisi);

            }
            foreach (var item in bul.GroupBy(x => x.User.Unvan.UnvanAdi))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerçek kişi adedi

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

                // Puanı hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanını yüzdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik8.Add(item.Key);
                ViewBag.puan8.Add(p1);
                ViewBag.adet8.Add(kisiSayisi);

            }
            foreach (var item in bul.GroupBy(x => x.User.Yaka.YakaAdi))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerçek kişi adedi

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

                // Puanı hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanını yüzdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik9.Add(item.Key);
                ViewBag.puan9.Add(p1);
                ViewBag.adet9.Add(kisiSayisi);

            }
            foreach (var item in bul.GroupBy(x => x.User.Yonetici.YoneticiAdi))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerçek kişi adedi

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

                // Puanı hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanını yüzdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik10.Add(item.Key);
                ViewBag.puan10.Add(p1);
                ViewBag.adet10.Add(kisiSayisi);

            }
            foreach (var item in bul.GroupBy(x => x.Soru.SoruAdi))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerçek kişi adedi

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

                // Puanı hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanını yüzdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik11.Add(item.Key);
                ViewBag.puan11.Add(p1);
                ViewBag.adet11.Add(kisiSayisi);

            }
            foreach (var item in bul.GroupBy(x => x.SoruGrup.SoruGrupAdi))
            {
                var soru = item.Count(); // toplam Soru
                var kisiSayisi = item.Select(x => x.UserId).Distinct().Count(); // gerçek kişi adedi

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

                // Puanı hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;   // ortalama memnuniyet puanını yüzdesini bulur
                var p1 = p2 / soru;
                ViewBag.baslik12.Add(item.Key);
                ViewBag.puan12.Add(p1);
                ViewBag.adet12.Add(kisiSayisi);
            }

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
            ViewBag.YayinBaslangicLocal = string.Empty;
            ViewBag.YayinBitisLocal = string.Empty;
            ViewBag.KatilimYontemi = KatilimYontemiHerkeseAcik;
            return View();
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
        public ActionResult AnketAdEdit(int id)
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
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult AnketAdEdit(Anket dgskn, IFormFile ImzaDosyasi, DateTime? YayinBaslangicTarihi, DateTime? YayinBitisTarihi, string KatilimYontemi)
        {
            if (Session["id"] == null || Session["admin"] == null)
            {
                return RedirectToAction("Giris", "Home");
            }

            var katilimYontemi = NormalizeKatilimYontemi(KatilimYontemi);

            try
            {
                var anket = CalismaAlaniAnketGetir(dgskn.AnketId);
                if (anket == null) return NotFound();
                var isExamSubmission = dgskn.Sinav == true;

                if (string.IsNullOrWhiteSpace(dgskn.AnketAdi))
                {
                    ModelState.AddModelError("AnketAdi", "Anket adi zorunlu.");
                }
                else if (CalismaAlaniAyniAnketAdiVar(dgskn.AnketAdi, dgskn.AnketId))
                {
                    ModelState.AddModelError("AnketAdi", "Bu isimde bir calisma zaten var. Rapor ve katilim takibi karismamasi icin farkli bir ad kullanin.");
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
                    dgskn.Imza = anket.Imza;
                    return View(dgskn);
                }

                // Alanları güncelle
                anket.AnketAdi = dgskn.AnketAdi;
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

                // İmza işlemi
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

                TempData["AyarKayitMesaji"] = "Çalışma ayarları kaydedildi.";
                return RedirectToAction("AnketAdEdit", new { id = anket.AnketId });
            }
            catch
            {
                ViewBag.KatilimYontemi = katilimYontemi;
                PrepareAnketAdLookups(dgskn.AnketId, YayinBaslangicTarihi, YayinBitisTarihi, true);
                return View(dgskn);
            }
        }

        public ActionResult SertifikaWizard(int id)
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
                Sinav = anket.Sinav == true,
                SertifikaAktif = ayar?.SertifikaAktif == true,
                SertifikaKatilimciErisimi = ayar?.SertifikaKatilimciErisimi != false,
                SertifikaVerilisZamani = NormalizeSertifikaZamani(ayar?.SertifikaVerilisZamani),
                SertifikaNotu = anket.SertifikaNotu ?? 70,
                EgitimVeren = anket.EgitimVeren,
                SertifikaBaslik = ayar?.SertifikaBaslik ?? "Katılım Sertifikası",
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
                    ModelState.AddModelError("SertifikaNotu", "Geçme notu 0 ile 100 arasında olmalı.");
                }

                if (string.IsNullOrWhiteSpace(form.EgitimVeren))
                {
                    ModelState.AddModelError("EgitimVeren", "Sertifikada görünecek düzenleyen zorunlu.");
                }
            }

            if (string.IsNullOrWhiteSpace(form.SertifikaBaslik))
            {
                form.SertifikaBaslik = "Katılım Sertifikası";
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
                TempData["Mesaj"] = "Sertifika ayar kolonları SQL tarafında henüz eklenmemiş görünüyor.";
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

            return RedirectToAction("Indexgosterge");
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
                //havuzda kaydı mevcutsa silinmez
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
        public ActionResult AnketGrupIndex(int? id, string adi)
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

        public ActionResult DogruCevapEditor(int id)
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
                    ModelState.AddModelError($"DogruCevaplar[{soru.SoruId}]", "Bu soru için doğru cevap seçin.");
                    continue;
                }

                var secilenCevap = cevaplar.FirstOrDefault(x => x.CevapId == secilenCevapId);
                if (secilenCevap == null || secilenCevap.CevapGrupId != soru.CevapGrupId)
                {
                    ModelState.AddModelError($"DogruCevaplar[{soru.SoruId}]", "Seçilen cevap bu soruya ait değil.");
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
                ModelState.AddModelError("", "Aynı cevap grubunu kullanan sorularda farklı doğru cevap seçilemez. Bu sorular için ayrı cevap grubu oluşturun.");
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
            TempData["DogruCevapMesaj"] = "Doğru cevaplar kaydedildi ve mevcut katılım puanları yeniden hesaplandı.";

            return RedirectToAction("DogruCevapEditor", new { id = form.AnketId });
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

            var aa = db.AnketGrup.Where(x => x.AnketId == id);
            var hav =
            (from a in db.SoruGrup
             join c in aa on a.SoruGrupId equals c.SoruGrupId into lrs
             from lr in lrs.DefaultIfEmpty()
             where lr == null
             select a).ToList();
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

            var aa = db.AnketGrup.Where(x => x.AnketId == ank);
            var hav =
            (from a in db.SoruGrup
             join c in aa on a.SoruGrupId equals c.SoruGrupId into lrs
             from lr in lrs.DefaultIfEmpty()
             where lr == null
             select a).ToList();
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
            return View(db.Soru);
        }
        public ActionResult SoruCreate()
        {
            if (Session["id"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }
            if (Session["admin"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }
            List<SelectListItem> an =
            (from i in db.Anket.OrderBy(x => x.AnketAdi).ToList()
             select new SelectListItem
             {
                 Text = i.AnketAdi,
                 Value = i.AnketId.ToString(),
             }).ToList();
            ViewBag.Ank = an;

            List<SelectListItem> un =
            (from i in db.CevapGrup.OrderBy(x => x.CevapGrupAdi).ToList()
             select new SelectListItem
             {
                 Text = i.CevapGrupAdi,
                 Value = i.CevapGrupId.ToString(),
             }).ToList();
            ViewBag.Cev = un;

            List<SelectListItem> sr =
            (from i in db.SoruGrup.OrderBy(x => x.SoruGrupAdi).ToList()
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
        public ActionResult SoruCreate(Soru dgskn)
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
                db.Soru.Add(dgskn);
                db.SaveChanges();
                return RedirectToAction("SoruIndex");

            }
            catch
            {
                return View();
            }
        }
        public ActionResult SoruEdit(int id)
        {
            if (Session["id"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }
            if (Session["admin"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }
            List<SelectListItem> an =
            (from i in db.Anket.OrderBy(x => x.AnketAdi).ToList()
             select new SelectListItem
             {
                 Text = i.AnketAdi,
                 Value = i.AnketId.ToString(),
             }).ToList();
            ViewBag.Ank = an;

            List<SelectListItem> un =
            (from i in db.CevapGrup.OrderBy(x => x.CevapGrupAdi).ToList()
             select new SelectListItem
             {
                 Text = i.CevapGrupAdi,
                 Value = i.CevapGrupId.ToString(),
             }).ToList();
            ViewBag.Cev = un;

            List<SelectListItem> sr =
            (from i in db.SoruGrup.OrderBy(x => x.SoruGrupAdi).ToList()
             select new SelectListItem
             {
                 Text = i.SoruGrupAdi,
                 Value = i.SoruGrupId.ToString(),
             }).ToList();
            ViewBag.Sor = sr;


            return View(db.Soru.Where(x => x.SoruId == id).FirstOrDefault());
        }
        [ValidateAntiForgeryToken()]
        [HttpPost]
        public ActionResult SoruEdit(Soru dgskn)
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
                {
                    db.Entry(dgskn).State = EntityState.Modified;
                    db.SaveChanges();
                }
                return RedirectToAction("SoruIndex");

            }
            catch
            {
                return View();
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
            return View(db.Soru.Where(x => x.SoruId == id).FirstOrDefault());
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

            if (ModelState.IsValid)
            {
                //havuzda kaydı mevcutsa silinmez
                if (db.Havuz.Any(x => x.SoruID == id))
                {
                    return RedirectToAction("Hata1", "Ayar", null);
                }
            }

            try
            {
                Soru unv = db.Soru.Where(x => x.SoruId == id).FirstOrDefault();
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
            return View(db.SoruGrup);
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

            return View(db.SoruGrup.Where(x => x.SoruGrupId == id).FirstOrDefault());
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
                {
                    db.Entry(dgskn).State = EntityState.Modified;
                    db.SaveChanges();
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
            return View(db.SoruGrup.Where(x => x.SoruGrupId == id).FirstOrDefault());
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

            if (ModelState.IsValid)
            {
                //havuzda kaydı mevcutsa silinmez
                if (db.Havuz.Any(x => x.SoruGrupId == id))
                {
                    return RedirectToAction("Hata1", "Ayar", null);
                }
            }

            try
            {
                SoruGrup unv = db.SoruGrup.Where(x => x.SoruGrupId == id).FirstOrDefault();
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
            return View(db.Cevap);
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

            List<SelectListItem> un =
            (from i in db.CevapGrup.OrderBy(x => x.CevapGrupAdi).ToList()
             select new SelectListItem
             {
                 Text = i.CevapGrupAdi,
                 Value = i.CevapGrupId.ToString(),
             }).ToList();
            ViewBag.Gur = un;

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
                db.Cevap.Add(dgskn);
                db.SaveChanges();
                return RedirectToAction("CevapIndex");

            }
            catch
            {
                return View();
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
            List<SelectListItem> un =
            (from i in db.CevapGrup.OrderBy(x => x.CevapGrupAdi).ToList()
             select new SelectListItem
             {
                 Text = i.CevapGrupAdi,
                 Value = i.CevapGrupId.ToString(),
             }).ToList();
            ViewBag.Gur = un;


            return View(db.Cevap.Where(x => x.CevapId == id).FirstOrDefault());
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
                {
                    db.Entry(dgskn).State = EntityState.Modified;
                    db.SaveChanges();
                }
                return RedirectToAction("CevapIndex");

            }
            catch
            {
                return View();
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
            return View(db.Cevap.Where(x => x.CevapId == id).FirstOrDefault());
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

            if (ModelState.IsValid)
            {
                //havuzda kaydı mevcutsa silinmez
                if (db.Havuz.Any(x => x.CevapId == id))
                {
                    return RedirectToAction("Hata1", "Ayar", null);
                }
            }

            try
            {
                Cevap unv = db.Cevap.Where(x => x.CevapId == id).FirstOrDefault();
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
            return View(db.CevapGrup);
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
                db.CevapGrup.Add(dgskn);
                db.SaveChanges();
                return RedirectToAction("CevapGrupIndex");

            }
            catch
            {
                return View();
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

            return View(db.CevapGrup.Where(x => x.CevapGrupId == id).FirstOrDefault());
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
                {
                    db.Entry(dgskn).State = EntityState.Modified;
                    db.SaveChanges();
                }
                return RedirectToAction("CevapGrupIndex");

            }
            catch
            {
                return View();
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
            return View(db.CevapGrup.Where(x => x.CevapGrupId == id).FirstOrDefault());
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

            if (ModelState.IsValid)
            {
                //havuzda kaydı mevcutsa silinmez
                if (db.Havuz.Any(x => x.CevapGrupId == id))
                {
                    return RedirectToAction("Hata1", "Ayar", null);
                }
            }

            try
            {
                CevapGrup unv = db.CevapGrup.Where(x => x.CevapGrupId == id).FirstOrDefault();
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

            // Kullanıcıyı çekelim
            var user = db.User.FirstOrDefault(u => u.UserId == id);
            var adi = user?.UserAdi ?? "Tanımsız Kullanıcı";

            if (user == null && kod == null)
                return RedirectToAction("Giris", "Home");

            // Kullanıcının Havuzdaki anketleri
            var userAnketIds = db.Havuz
                    .Where(h => h.UserId == id)
                    .Select(h => h.AnketId)
                    .Distinct()
                    .ToList();

            // Ortak query: hem Havuz eşleşmesi hem de Anket filtreleri
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
                TempData["Mesaj"] = "Bu çalışma yayında değil.";
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
                    TempData["Mesaj"] = "Bu calisma kayitli katilimcilara ozel. Lutfen katilimci hesabi ile giris yapin.";
                    return RedirectToAction("Giris", "Home");
                }

                if (!KayitliKatilimciCalismayaUygunMu(anket, sessionUserId.Value, out var uygunlukMesaji))
                {
                    TempData["Mesaj"] = uygunlukMesaji;
                    return RedirectToAction("Giris", "Home");
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

        private int? SessionUserId()
        {
            if (Session["id"] != null && int.TryParse(Session["id"].ToString(), out var sessionUserId))
            {
                return sessionUserId;
            }

            return null;
        }

        private bool KayitliKatilimciCalismayaUygunMu(Anket anket, int userId, out string mesaj)
        {
            mesaj = string.Empty;
            if (anket == null || userId <= 0)
            {
                mesaj = "Bu calisma icin katilimci bilgisi bulunamadi.";
                return false;
            }

            var user = db.User.FirstOrDefault(x => x.UserId == userId && x.Pasif != true);
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

            return string.IsNullOrWhiteSpace(anket?.Link) ? "Sınav" : "Eğitim";
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
            if (Session["id"] == null && kod == null && user == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }

            var sessionUserId = kod.HasValue ? null : SessionUserId();
            var portalUserId = user ?? sessionUserId;
            var effectiveUseId = ResolveKatilimUseId(portalUserId, kod);
            KatilimciKoduHatirla(kod ?? portalUserId);

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
                KatilimciAdi = Session["adi"]?.ToString() ?? (kod.HasValue ? $"Katılımcı #{kod}" : "Katılımcı")
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
                        sertifikaDurumMesaji = $"Sertifika için puan yetersiz. Gerekli: {gecmeNotu:n0}, mevcut: {puan:n2}.";
                    }
                    else if (!sertifikaZamaniGeldi)
                    {
                        sertifikaDurumMesaji = sertifikaZamanMesaji;
                    }
                    else if (!katilimciSertifikaAlabilir)
                    {
                        sertifikaDurumMesaji = "Sertifika yönetici tarafından paylaşılacak.";
                    }
                    else
                    {
                        sertifikaDurumMesaji = "Sertifika hazır.";
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
                            : (suresiDoldu ? "Süre doldu" : (tamamlandi ? "Tamamlandı" : "Başlandı")))
                });
            }

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult KatilimTamamla(int anketId, int? kod, int? user)
        {
            if (Session["id"] == null && kod == null && user == null)
            {
                return Json(new { success = false, message = "Katılım bilgisi bulunamadı." });
            }

            var effectiveUseId = ResolveKatilimUseId(user, kod);
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

                // Puanı hesapla
                var p2 = ViewBag.d1 + ViewBag.d2 + ViewBag.d3 + ViewBag.d4 + ViewBag.d5;
                p1 = soru > 0 ? p2 / soru : 0; // bölme hatasını engelle
            }

            // Varsayılan değerler
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
                ViewBag.mesaj = "Bu çalışma için sertifika yayını kapalı.";
                ViewBag.hideScore = true;
            }
            else if (izll == null)
            {
                ViewBag.puan = false;
                ViewBag.mesaj = "Bu eğitimi bitirmediğiniz için sertifika alamazsınız.";
            }
            else if (!tumSorularTamamlandi)
            {
                ViewBag.puan = false;
                ViewBag.mesaj = "Sertifika için tüm soruların tamamlanması gerekiyor.";
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
                ViewBag.mesaj = "Notunuz yeterli değil. Sertifika alamazsınız.";
                ViewBag.hideScore = false;
            }
            else if (!katilimciSertifikaAlabilir && Session["admin"] == null)
            {
                ViewBag.puan = false;
                ViewBag.mesaj = "Sertifika hazır; ancak katılımcı indirme yetkisi kapalı. Sertifika yönetici tarafından paylaşılacak.";
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
            ViewBag.adSoyad = Session["adi"]?.ToString() ?? (kod.HasValue ? $"Katılımcı #{kod}" : "Katılımcı");
            if (ankadi1 != null)
            {
                ViewBag.egitimveren = ankadi1.EgitimVeren;
                ViewBag.imza = ankadi1.Imza;
            }

            return View();
        }

        public ActionResult AnketGirisCreate(int id, int? kod, int? user)
        {
            if (Session["id"] == null && kod == null)
                return RedirectToAction("Giris", "Home", null);

            var ank = db.Anket.FirstOrDefault(x => x.AnketId == id);
            if (ank == null) return NotFound();

            var effectiveUseId = ResolveKatilimUseId(user, kod);
            if (!AnketKatilimaAcikMi(id, out var yayinMesaji))
            {
                TempData["Mesaj"] = yayinMesaji;
                return RedirectToAction("KatilimPortal", "Home", new { kod, user = effectiveUseId });
            }

            ViewBag.zaman = ank.Zaman;
            ViewBag.ankadi = ank.AnketAdi;

            // İzledim tablosuna başlangıç kaydı yoksa ekle
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

            // Süresi bitmiş mi?
            if (mevcutIzleme.BitisZaman.HasValue && mevcutIzleme.BitisZaman.Value <= DateTime.Now)
            {
                TempData["Mesaj"] = "Süreniz dolmuştur. Bu ankete tekrar giriş yapamazsınız.";
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
                return Json(new { success = false, message = "Oturum bulunamadı" });

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
            return Json(new { success = false, message = "Kayıt bulunamadı" });
        }
        public ActionResult AnketGirisCreate2(int id, int? kod, int? user)
        {
            if (Session["id"] == null && kod == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }
            int effectiveUseId = user.GetValueOrDefault();
            if (effectiveUseId <= 0 && Session["id"] != null && int.TryParse(Session["id"].ToString(), out var sessionUseId))
            {
                effectiveUseId = sessionUseId;
            }
            if (effectiveUseId <= 0 && kod.HasValue)
            {
                effectiveUseId = kod.Value;
            }
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

            // Süresi bitmiş mi?
            if (mevcutIzleme.BitisZaman.HasValue && mevcutIzleme.BitisZaman.Value <= DateTime.Now)
            {
                TempData["Mesaj"] = "Süreniz dolmuştur. Bu çalışmaya tekrar giriş yapamazsınız.";
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
            // 1) Kullanıcı doğrulama
            if (Session["id"] == null && hav.Isimsiz == null)
                return Json(new { success = false, message = "Oturum bulunamadı" });

            // 2) İzleme kontrolü
            if (!TryApplyTrustedAnswerValues(hav, out var answerError))
            {
                return Json(new { success = false, message = answerError });
            }

            if (hav.AnketId.HasValue && !AnketKatilimaAcikMi(hav.AnketId.Value, out var yayinMesaji))
            {
                return Json(new { success = false, expired = true, message = yayinMesaji });
            }

            var publicKod = hav.Isimsiz.HasValue && hav.Isimsiz.Value > 0;
            var effectiveUseId = ResolveKatilimUseId(user, hav.Isimsiz);
            var izl = db.Izledim.FirstOrDefault(x => x.AnketId == hav.AnketId && x.UseId == effectiveUseId);
            if (izl == null)
            {
                return Json(new { success = false, message = "Sınav oturumu bulunamadı. Lütfen katılım alanınızdan tekrar deneyin." });
            }

            if (izl.BitisZaman.HasValue && izl.BitisZaman.Value <= DateTime.Now)
            {
                return Json(new { success = false, expired = true, message = "Süreniz dolmuştur. Bu sınava cevap gönderemezsiniz." });
            }

            if (izl != null)
            {
                if (izl.Izledi != true)
                    return Json(new { success = false, message = "İzleme tamamlanmamış" });
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

            // 4) Aynı soruya verilmiş cevabı ara
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
                // Güncelle
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
                // Yeni kayıt
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

            if (Session["id"] == null && kod == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }

            var sr = db.AnketGrup.Where(x => x.AnketId == id);
            var effectiveUseId = ResolveKatilimUseId(user, kod);
            var izl = db.Izledim.Any(x => x.UseId == effectiveUseId && x.AnketId == id);
            var mevcutIzleme = db.Izledim.FirstOrDefault(x => x.AnketId == id && x.UseId == effectiveUseId);

            // Süresi bitmiş mi?
            var ank = db.Anket.Where(x => x.AnketId == id).FirstOrDefault();
            if (ank == null) return NotFound();

            if (!AnketKatilimaAcikMi(id, out var yayinMesaji))
            {
                TempData["Mesaj"] = yayinMesaji;
                return RedirectToAction("KatilimPortal", "Home", new { kod, user = effectiveUseId });
            }

            if (mevcutIzleme?.BitisZaman != null && mevcutIzleme.BitisZaman.Value <= DateTime.Now)
            {
                TempData["Mesaj"] = "Süreniz dolmuştur. Bu çalışmaya tekrar giriş yapamazsınız.";
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

            var izll = db.Izledim.Where(x => x.AnketId == id && x.UseId == effectiveUseId).FirstOrDefault();


            var st = from detay in sr
                     join master in db.Soru on detay.SoruGrupId equals master.SoruGrupId
                     select master;


            ViewBag.kod = kod;
            ViewBag.id = id;
            var hv = db.Havuz.Where(x => x.AnketId == id);
            Tumcontroller model = new Tumcontroller()
            {
                AnkGrp = db.AnketGrup.Where(x => x.AnketId == id),
                Sor = st,
                Hav = hv,
                Cev = db.Cevap,
            };
            return View(model);


        }

        [ValidateAntiForgeryToken()]
        [HttpPost]
        public ActionResult Izledim(Izledim izl, int? kod, int? user)
        {
            var effectiveUserId = user.GetValueOrDefault();
            if (effectiveUserId <= 0 && izl.UseId.HasValue && izl.UseId.Value > 0)
            {
                effectiveUserId = izl.UseId.Value;
            }
            if (effectiveUserId <= 0 && Session["id"] != null && int.TryParse(Session["id"].ToString(), out var sessionUserId))
            {
                effectiveUserId = sessionUserId;
            }
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
                    TempData["Mesaj"] = "Süreniz dolmuştur. Bu sınava tekrar giriş yapamazsınız.";
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
                ViewBag.ankadi = anket != null ? anket.AnketAdi : "Çalışma";
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
                ViewBag.ankadi = anket != null ? anket.AnketAdi : "Çalışma";
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
            ViewBag.adi = ad != null ? ad.AnketAdi : "Çalışma";

            var item3 = bul.Where(x => x.UserId != 1);
            var item4 = bul.Where(x => x.Isimsiz != null);

            var ktll = item3.GroupBy(x => x.UserId);
            ViewBag.tanimli = ktll.Count();

            var ktll1 = item4.GroupBy(x => x.Isimsiz);
            ViewBag.tanimsiz = ktll1.Count();

            ViewBag.id = new List<int>();   //Adı
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

                // Her bir ID için tabloyu bul ve sil
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

            ViewBag.id = new List<int>();   //Adı
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
                // Her bir ID için tabloyu bul ve sil
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
