using System.Data.SqlClient;
using System.Net;

namespace survey.Infrastructure;

public sealed class LisansKontrolMiddleware
{
    private const string VarsayilanClientId = "survey";
    private const string VarsayilanLisansAnahtari = "SURVE-20026-SURVE";
    private const string VarsayilanDomain = "survey.aslana.com.tr";

    private static readonly SemaphoreSlim CacheKilidi = new(1, 1);
    private static LisansKontrolSonucu SonSonuc;

    private readonly RequestDelegate _sonraki;
    private readonly IConfiguration _ayarlar;
    private readonly IWebHostEnvironment _ortam;

    public LisansKontrolMiddleware(RequestDelegate sonraki, IConfiguration ayarlar, IWebHostEnvironment ortam)
    {
        _sonraki = sonraki;
        _ayarlar = ayarlar;
        _ortam = ortam;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!_ayarlar.GetValue("LisansKontrol:Aktif", true))
        {
            await _sonraki(context);
            return;
        }

        var mevcutDomain = DomainTemizle(context.Request.Host.Host);

        if (GelisitirmeOrtamindaSerbest(context, mevcutDomain))
        {
            await _sonraki(context);
            return;
        }

        var sonuc = await LisansGecerliMiAsync(mevcutDomain);
        if (sonuc.Gecerli)
        {
            await _sonraki(context);
            return;
        }

        await LisanssizEkranYazAsync(context, sonuc.Mesaj);
    }

    private async Task<LisansKontrolSonucu> LisansGecerliMiAsync(string mevcutDomain)
    {
        var cacheDakika = Math.Max(1, _ayarlar.GetValue("LisansKontrol:CacheDakika", 5));
        var simdi = DateTimeOffset.UtcNow;

        if (SonSonuc is not null &&
            SonSonuc.Domain == mevcutDomain &&
            SonSonuc.GecerlilikZamani > simdi)
        {
            return SonSonuc;
        }

        await CacheKilidi.WaitAsync();
        try
        {
            if (SonSonuc is not null &&
                SonSonuc.Domain == mevcutDomain &&
                SonSonuc.GecerlilikZamani > simdi)
            {
                return SonSonuc;
            }

            var sonuc = await LisansSorgulaAsync(mevcutDomain);
            SonSonuc = sonuc with { GecerlilikZamani = simdi.AddMinutes(cacheDakika) };
            return SonSonuc;
        }
        finally
        {
            CacheKilidi.Release();
        }
    }

    private async Task<LisansKontrolSonucu> LisansSorgulaAsync(string mevcutDomain)
    {
        var beklenenDomain = DomainTemizle(_ayarlar["LisansKontrol:AllowedDomain"] ?? VarsayilanDomain);
        if (!string.Equals(mevcutDomain, beklenenDomain, StringComparison.OrdinalIgnoreCase))
        {
            return LisansKontrolSonucu.Gecersiz(mevcutDomain, "Bu site sadece Aslana Teknoloji sirketine aittir. Yetkisiz domain kullanimi engellenmistir.");
        }

        var baglanti = _ayarlar.GetConnectionString("LicensingConnection") ?? _ayarlar["LicensingConnection"];
        if (string.IsNullOrWhiteSpace(baglanti))
        {
            return LisansKontrolSonucu.Gecersiz(mevcutDomain, "Lisans baglantisi tanimli degil. Bu site sadece Aslana Teknoloji sirketine aittir.");
        }

        var clientId = _ayarlar["LisansKontrol:ClientId"] ?? VarsayilanClientId;
        var lisansAnahtari = _ayarlar["LisansKontrol:LicenseKey"] ?? VarsayilanLisansAnahtari;

        const string sorgu = @"
SELECT TOP (1) 1
FROM dbo.Licenses WITH (NOLOCK)
WHERE LicenseKey = @LicenseKey
  AND ClientId = @ClientId
  AND AllowedDomain = @AllowedDomain
  AND Status = N'Aktif'
  AND ActivationDate <= GETDATE()
  AND (ExpirationDate IS NULL OR ExpirationDate >= GETDATE());";

        try
        {
            await using var sqlBaglanti = new SqlConnection(baglanti);
            await using var komut = new SqlCommand(sorgu, sqlBaglanti);
            komut.Parameters.AddWithValue("@LicenseKey", lisansAnahtari);
            komut.Parameters.AddWithValue("@ClientId", clientId);
            komut.Parameters.AddWithValue("@AllowedDomain", mevcutDomain);

            await sqlBaglanti.OpenAsync();
            var kayitVar = await komut.ExecuteScalarAsync();

            return kayitVar is null
                ? LisansKontrolSonucu.Gecersiz(mevcutDomain, "Gecerli lisans bulunamadi. Bu site sadece Aslana Teknoloji sirketine aittir.")
                : LisansKontrolSonucu.GecerliSonuc(mevcutDomain);
        }
        catch
        {
            return LisansKontrolSonucu.Gecersiz(mevcutDomain, "Lisans sunucusuna ulasilamadi. Bu site sadece Aslana Teknoloji sirketine aittir.");
        }
    }

    private bool GelisitirmeOrtamindaSerbest(HttpContext context, string mevcutDomain)
    {
        if (!_ayarlar.GetValue("LisansKontrol:GelisitirmeSerbest", true))
        {
            return false;
        }

        if (mevcutDomain is not ("localhost" or "127.0.0.1" or "::1"))
        {
            return false;
        }

        var uzakAdres = context.Connection.RemoteIpAddress;
        if (uzakAdres is not null && uzakAdres.IsIPv4MappedToIPv6)
        {
            uzakAdres = uzakAdres.MapToIPv4();
        }

        return uzakAdres is null || IPAddress.IsLoopback(uzakAdres) || _ortam.IsDevelopment();
    }

    private static string DomainTemizle(string domain)
    {
        if (string.IsNullOrWhiteSpace(domain))
        {
            return string.Empty;
        }

        var temiz = domain.Trim().TrimEnd('.').ToLowerInvariant();
        return temiz.StartsWith("www.", StringComparison.OrdinalIgnoreCase)
            ? temiz[4..]
            : temiz;
    }

    private static async Task LisanssizEkranYazAsync(HttpContext context, string mesaj)
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        context.Response.ContentType = "text/html; charset=utf-8";

        var guvenliMesaj = WebUtility.HtmlEncode(mesaj);
        await context.Response.WriteAsync($$"""
<!doctype html>
<html lang="tr">
<head>
    <meta charset="utf-8">
    <meta name="viewport" content="width=device-width, initial-scale=1">
    <title>Lisans Kontrolu</title>
    <style>
        :root { color-scheme: dark; }
        * { box-sizing: border-box; }
        body {
            margin: 0;
            min-height: 100vh;
            display: grid;
            place-items: center;
            font-family: Arial, Helvetica, sans-serif;
            background: #101820;
            color: #f8fafc;
        }
        .panel {
            width: min(92vw, 560px);
            padding: 34px;
            border: 1px solid rgba(255,255,255,.16);
            border-radius: 14px;
            background: rgba(255,255,255,.07);
            box-shadow: 0 28px 80px rgba(0,0,0,.35);
        }
        .etiket {
            display: inline-flex;
            margin-bottom: 18px;
            padding: 7px 10px;
            border-radius: 999px;
            background: rgba(127, 240, 198, .15);
            color: #8ff0ca;
            font-size: 13px;
            font-weight: 700;
        }
        h1 {
            margin: 0 0 12px;
            font-size: clamp(28px, 5vw, 42px);
            line-height: 1.08;
        }
        p {
            margin: 0;
            color: #cbd5e1;
            font-size: 16px;
            line-height: 1.6;
        }
    </style>
</head>
<body>
    <main class="panel">
        <div class="etiket">Aslana Survey Studio</div>
        <h1>Lisans dogrulanamadi</h1>
        <p>{{guvenliMesaj}}</p>
    </main>
</body>
</html>
""");
    }

    private sealed record LisansKontrolSonucu(string Domain, bool Gecerli, string Mesaj, DateTimeOffset GecerlilikZamani)
    {
        public static LisansKontrolSonucu GecerliSonuc(string domain)
            => new(domain, true, string.Empty, DateTimeOffset.MinValue);

        public static LisansKontrolSonucu Gecersiz(string domain, string mesaj)
            => new(domain, false, mesaj, DateTimeOffset.MinValue);
    }
}
