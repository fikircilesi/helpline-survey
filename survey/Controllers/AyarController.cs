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

        public ActionResult index()
        {
            return View();
        }

        public ActionResult AiAyar()
        {
            if (Session["id"] == null || Session["admin"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
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
            if (Session["id"] == null || Session["admin"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
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

            TempData["AiAyarMesaj"] = "Yapay zeka ayarlari kaydedildi.";
            return RedirectToAction("AiAyar");
        }

        private bool AiAyarTablosuVarMi()
        {
            try
            {
                return db.Database.SqlQuery<int>("SELECT CASE WHEN OBJECT_ID(N'dbo.AiAyar', N'U') IS NULL THEN 0 ELSE 1 END")
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
            return View(db.Unvan);
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

            return View(db.Unvan.Where(x => x.UnvanId == id).FirstOrDefault());
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
            return View(db.Unvan.Where(x => x.UnvanId == id).FirstOrDefault());
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
                if (db.Personel.Any(x => x.Unvan.UnvanId.Equals(id)))
                {
                    return RedirectToAction("Hata1", "Ayar", null);
                }
                if (db.User.Any(x => x.Unvan.UnvanId.Equals(id)))
                {
                    return RedirectToAction("Hata1", "Ayar", null);
                }
            }

            try
            {
                Unvan unv = db.Unvan.Where(x => x.UnvanId == id).FirstOrDefault();
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
            return View(db.Departman);
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

            return View(db.Departman.Where(x => x.DepartmanId == id).FirstOrDefault());
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
            return View(db.Departman.Where(x => x.DepartmanId == id).FirstOrDefault());
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
                if (db.User.Any(x => x.UserDepartman == id))
                {
                    return RedirectToAction("Hata1", "Ayar", null);
                }
            }

            try
            {
                Departman unv = db.Departman.Where(x => x.DepartmanId == id).FirstOrDefault();
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
            return View(db.Bolge);
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

            return View(db.Bolge.Where(x => x.BolgeId == id).FirstOrDefault());
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
            return View(db.Bolge.Where(x => x.BolgeId == id).FirstOrDefault());
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
                if (db.User.Any(x => x.UserBolge == id))
                {
                    return RedirectToAction("Hata1", "Ayar", null);
                }
            }

            try
            {
                Bolge unv = db.Bolge.Where(x => x.BolgeId == id).FirstOrDefault();
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
            return View(db.Sehir);
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

            return View(db.Sehir.Where(x => x.SehirId == id).FirstOrDefault());
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
            return View(db.Sehir.Where(x => x.SehirId == id).FirstOrDefault());
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
                if (db.User.Any(x => x.UserSehir == id))
                {
                    return RedirectToAction("Hata1", "Ayar", null);
                }
            }

            try
            {
                Sehir unv = db.Sehir.Where(x => x.SehirId == id).FirstOrDefault();
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
            return View(db.Sube);
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

            return View(db.Sube.Where(x => x.SubeId == id).FirstOrDefault());
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
            return View(db.Sube.Where(x => x.SubeId == id).FirstOrDefault());
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
                if (db.User.Any(x => x.UserSube == id))
                {
                    return RedirectToAction("Hata1", "Ayar", null);
                }
            }

            try
            {
                Sube unv = db.Sube.Where(x => x.SubeId == id).FirstOrDefault();
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
            return View(db.Bolum);
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

            return View(db.Bolum.Where(x => x.BolumId == id).FirstOrDefault());
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
            return View(db.Bolum.Where(x => x.BolumId == id).FirstOrDefault());
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
                if (db.User.Any(x => x.UserBolumu == id))
                {
                    return RedirectToAction("Hata1", "Ayar", null);
                }
            }

            try
            {
                Bolum unv = db.Bolum.Where(x => x.BolumId == id).FirstOrDefault();
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
            return View(db.Yonetici);
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

            return View(db.Yonetici.Where(x => x.YoneticiId == id).FirstOrDefault());
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
            return View(db.Yonetici.Where(x => x.YoneticiId == id).FirstOrDefault());
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
                if (db.User.Any(x => x.UserYoneticisi == id))
                {
                    return RedirectToAction("Hata1", "Ayar", null);
                }
            }

            try
            {
                Yonetici unv = db.Yonetici.Where(x => x.YoneticiId == id).FirstOrDefault();
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

            return View(db.User);

        }
        public ActionResult UserIndex1(int id)
        {
            if (Session["id"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }

            var anket = db.Anket.FirstOrDefault(x => x.AnketId == id);
            ViewBag.AnketId = id;
            ViewBag.AnketAdi = anket?.AnketAdi ?? "Çalışma";

            var havuzUserIds = db.Havuz
                .Where(h => h.AnketId == id) // AnketId'si 11 olmayanları filtrele
                .Where(h => h.UserId != null)
                .Select(h => h.UserId)
                .ToList();

            // db.User'dan havuzda olmayan kullanıcıları filtrele
            var users = db.User
                .Include("Unvan")
                .Include("Bolum")
                .Include("Cinsiyet")
                .Include("Egitim")
                .Include("Sube")
                .Include("Bolge")
                .Include("Departman")
                .Include("Yonetici")
                .Include("Yaka")
                .Include("Sehir")
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
            var anket = db.Anket.FirstOrDefault(x => x.AnketId == id);
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
            ViewBag.KayitTar = DateTime.Now.ToString("yyyy-MM-dd HH:mm");

            List<SelectListItem> un =
            (from i in db.Unvan.OrderBy(x => x.UnvanAdi).ToList()
             select new SelectListItem
             {
                 Text = i.UnvanAdi,
                 Value = i.UnvanId.ToString(),
             }).ToList();
            ViewBag.Unv = un;

            List<SelectListItem> cn =
            (from i in db.Cinsiyet.OrderBy(x => x.CinsiyetAdi).ToList()
             select new SelectListItem
             {
                 Text = i.CinsiyetAdi,
                 Value = i.CinsiyetId.ToString(),
             }).ToList();
            ViewBag.Cin = cn;


            List<SelectListItem> bo =
            (from i in db.Bolum.OrderBy(x => x.BolumAdi).ToList()
             select new SelectListItem
             {
                 Text = i.BolumAdi,
                 Value = i.BolumId.ToString(),
             }).ToList();
            ViewBag.Bol = bo;

            List<SelectListItem> ci =
            (from i in db.Cinsiyet.OrderBy(x => x.CinsiyetAdi).ToList()
             select new SelectListItem
             {
                 Text = i.CinsiyetAdi,
                 Value = i.CinsiyetId.ToString(),
             }).ToList();
            ViewBag.Cin = ci;

            List<SelectListItem> eg =
            (from i in db.Egitim.OrderBy(x => x.EgitimAdi).ToList()
             select new SelectListItem
             {
                 Text = i.EgitimAdi,
                 Value = i.EgitimId.ToString(),
             }).ToList();
            ViewBag.Egi = eg;

            List<SelectListItem> bl =
            (from i in db.Bolge.OrderBy(x => x.BolgeAdi).ToList()
             select new SelectListItem
             {
                 Text = i.BolgeAdi,
                 Value = i.BolgeId.ToString(),
             }).ToList();
            ViewBag.Blg = bl;

            List<SelectListItem> se =
            (from i in db.Sehir.OrderBy(x => x.SehiarAdi).ToList()
             select new SelectListItem
             {
                 Text = i.SehiarAdi,
                 Value = i.SehirId.ToString(),
             }).ToList();
            ViewBag.Seh = se;

            List<SelectListItem> su =
            (from i in db.Sube.OrderBy(x => x.SubeAdi).ToList()
             select new SelectListItem
             {
                 Text = i.SubeAdi,
                 Value = i.SubeId.ToString(),
             }).ToList();
            ViewBag.Sub = su;

            List<SelectListItem> de =
            (from i in db.Departman.OrderBy(x => x.DepartmanAdi).ToList()
             select new SelectListItem
             {
                 Text = i.DepartmanAdi,
                 Value = i.DepartmanId.ToString(),
             }).ToList();
            ViewBag.Dep = de;

            List<SelectListItem> yo =
            (from i in db.Yonetici.OrderBy(x => x.YoneticiAdi).ToList()
             select new SelectListItem
             {
                 Text = i.YoneticiAdi,
                 Value = i.YoneticiId.ToString(),
             }).ToList();
            ViewBag.Yon = yo;

            List<SelectListItem> ya =
            (from i in db.Yaka.OrderBy(x => x.YakaAdi).ToList()
             select new SelectListItem
             {
                 Text = i.YakaAdi,
                 Value = i.YakaId.ToString(),
             }).ToList();
            ViewBag.Yak = ya;

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
                return RedirectToAction("UserIndex");

            }
            catch
            {
                return View();
            }
        }
        public ActionResult UserEdit(int id)
        {
            if (Session["id"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }

            ViewBag.KayitTar = DateTime.Now.ToString("yyyy-MM-dd HH:mm");

            List<SelectListItem> un =
            (from i in db.Unvan.OrderBy(x => x.UnvanAdi).ToList()
             select new SelectListItem
             {
                 Text = i.UnvanAdi,
                 Value = i.UnvanId.ToString(),
             }).ToList();
            ViewBag.Unv = un;

            List<SelectListItem> cn =
            (from i in db.Cinsiyet.OrderBy(x => x.CinsiyetAdi).ToList()
             select new SelectListItem
             {
                 Text = i.CinsiyetAdi,
                 Value = i.CinsiyetId.ToString(),
             }).ToList();
            ViewBag.Cin = cn;

            List<SelectListItem> bo =
            (from i in db.Bolum.OrderBy(x => x.BolumAdi).ToList()
             select new SelectListItem
             {
                 Text = i.BolumAdi,
                 Value = i.BolumId.ToString(),
             }).ToList();
            ViewBag.Bol = bo;

            List<SelectListItem> ci =
            (from i in db.Cinsiyet.OrderBy(x => x.CinsiyetAdi).ToList()
             select new SelectListItem
             {
                 Text = i.CinsiyetAdi,
                 Value = i.CinsiyetId.ToString(),
             }).ToList();
            ViewBag.Cin = ci;

            List<SelectListItem> eg =
            (from i in db.Egitim.OrderBy(x => x.EgitimAdi).ToList()
             select new SelectListItem
             {
                 Text = i.EgitimAdi,
                 Value = i.EgitimId.ToString(),
             }).ToList();
            ViewBag.Egi = eg;


            List<SelectListItem> bl =
            (from i in db.Bolge.OrderBy(x => x.BolgeAdi).ToList()
             select new SelectListItem
             {
                 Text = i.BolgeAdi,
                 Value = i.BolgeId.ToString(),
             }).ToList();
            ViewBag.Blg = bl;

            List<SelectListItem> se =
            (from i in db.Sehir.OrderBy(x => x.SehiarAdi).ToList()
             select new SelectListItem
             {
                 Text = i.SehiarAdi,
                 Value = i.SehirId.ToString(),
             }).ToList();
            ViewBag.Seh = se;
            List<SelectListItem> su =
            (from i in db.Sube.OrderBy(x => x.SubeAdi).ToList()
             select new SelectListItem
             {
                 Text = i.SubeAdi,
                 Value = i.SubeId.ToString(),
             }).ToList();
            ViewBag.Sub = su;

            List<SelectListItem> de =
            (from i in db.Departman.OrderBy(x => x.DepartmanAdi).ToList()
             select new SelectListItem
             {
                 Text = i.DepartmanAdi,
                 Value = i.DepartmanId.ToString(),
             }).ToList();
            ViewBag.Dep = de;

            List<SelectListItem> yo =
            (from i in db.Yonetici.OrderBy(x => x.YoneticiAdi).ToList()
             select new SelectListItem
             {
                 Text = i.YoneticiAdi,
                 Value = i.YoneticiId.ToString(),
             }).ToList();
            ViewBag.Yon = yo;

            List<SelectListItem> ya =
            (from i in db.Yaka.OrderBy(x => x.YakaAdi).ToList()
             select new SelectListItem
             {
                 Text = i.YakaAdi,
                 Value = i.YakaId.ToString(),
             }).ToList();
            ViewBag.Yak = ya;

            return View(db.User.Where(x => x.UserId == id).FirstOrDefault());
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
                {
                    db.Entry(dgskn).State = EntityState.Modified;
                    db.SaveChanges();
                }
                return RedirectToAction("UserIndex");

            }
            catch
            {
                return View();
            }
        }
        public ActionResult UserDelete(int id)
        {
            if (Session["id"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }
            return View(db.User.Where(x => x.UserId == id).FirstOrDefault());
        }
        [ValidateAntiForgeryToken()]
        [HttpPost]
        public ActionResult UserDelete(int? id)
        {
            if (Session["id"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }
            try
            {
                User unv = db.User.Where(x => x.UserId == id).FirstOrDefault();
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
            return View(db.Personel);
        }
        public ActionResult PersonelCreate()
        {
            if (Session["id"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }
            ViewBag.KayitTar = DateTime.Now.ToString("yyyy-MM-dd HH:mm");

            List<SelectListItem> un =
            (from i in db.Unvan.OrderBy(x => x.UnvanAdi).ToList()
             select new SelectListItem
             {
                 Text = i.UnvanAdi,
                 Value = i.UnvanId.ToString(),
             }).ToList();
            ViewBag.Unv = un;

            return View();
        }

        [ValidateAntiForgeryToken()]
        [HttpPost]
        public ActionResult PersonelCreate(Personel dgskn)
        {
            if (Session["id"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }

            try
            {
                db.Personel.Add(dgskn);
                db.SaveChanges();
                return RedirectToAction("PersonelIndex");

            }
            catch
            {
                return View();
            }
        }
        public ActionResult PersonelEdit(int id)
        {
            if (Session["id"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }
            ViewBag.KayitTar = DateTime.Now.ToString("yyyy-MM-dd HH:mm");

            List<SelectListItem> un =
            (from i in db.Unvan.OrderBy(x => x.UnvanAdi).ToList()
             select new SelectListItem
             {
                 Text = i.UnvanAdi,
                 Value = i.UnvanId.ToString(),
             }).ToList();
            ViewBag.Unv = un;


            return View(db.Personel.Where(x => x.PersonelId == id).FirstOrDefault());
        }

        [ValidateAntiForgeryToken()]
        [HttpPost]
        public ActionResult PersonelEdit(Personel dgskn)
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
                return RedirectToAction("PersonelIndex");

            }
            catch
            {
                return View();
            }
        }
        public ActionResult PersonelDelete(int id)
        {
            if (Session["id"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }
            return View(db.Personel.Where(x => x.PersonelId == id).FirstOrDefault());
        }
        [ValidateAntiForgeryToken()]
        [HttpPost]
        public ActionResult PersonelDelete(int? id)
        {
            if (Session["id"] == null)
            {
                return RedirectToAction("Giris", "Home", null);
            }
            try
            {
                Personel unv = db.Personel.Where(x => x.PersonelId == id).FirstOrDefault();
                db.Personel.Remove(unv);
                db.SaveChanges();
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

                List<smtpayar> emails = db.smtpayar
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

                return View(db.smtpayar.Where(x => x.MailId == id).FirstOrDefault());
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
