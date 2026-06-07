using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace survey.Infrastructure;

public sealed class TamiOdemeClient
{
    private static readonly JsonSerializerOptions JsonAyar = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly TamiOdemeAyarlari _ayarlar;

    public TamiOdemeClient(TamiOdemeAyarlari ayarlar)
    {
        _ayarlar = ayarlar;
    }

    public async Task<TamiHostedTokenSonucu> HostedTokenOlusturAsync(TamiHostedTokenIstegi istek)
    {
        if (!_ayarlar.HostedOdemeHazirMi())
        {
            return TamiHostedTokenSonucu.OlusturBasarisiz("Tami ayarlari eksik. Uye isyeri, terminal, guvenlik anahtari ve musteri telefonu girilmeli.");
        }

        var payload = new Dictionary<string, object>
        {
            ["amount"] = istek.Tutar,
            ["orderId"] = istek.SiparisNo,
            ["successCallbackUrl"] = istek.BasariliDonusUrl,
            ["failCallbackUrl"] = istek.BasarisizDonusUrl,
            ["mobilePhoneNumber"] = _ayarlar.MusteriTelefonu,
            ["data"] = new Dictionary<string, string>
            {
                ["calismaAlaniId"] = istek.CalismaAlaniId.ToString(CultureInfo.InvariantCulture),
                ["odemePaketiId"] = istek.OdemePaketiId.ToString(CultureInfo.InvariantCulture)
            }
        };

        using var http = YeniHttpClient();
        using var response = await http.PostAsync(
            $"{_ayarlar.ApiUrlKoku}/api/v0/hosted/create-one-time-hosted-token",
            JsonIcerik(payload));

        var raw = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            return TamiHostedTokenSonucu.OlusturBasarisiz($"Tami hosted token alinamadi. HTTP {(int)response.StatusCode}: {raw}");
        }

        try
        {
            using var document = JsonDocument.Parse(raw);
            if (document.RootElement.TryGetProperty("oneTimeToken", out var tokenElement))
            {
                var token = tokenElement.GetString();
                if (!string.IsNullOrWhiteSpace(token))
                {
                    return TamiHostedTokenSonucu.OlusturBasarili(token, HostedOdemeSayfasi(token));
                }
            }

            return TamiHostedTokenSonucu.OlusturBasarisiz("Tami yanitinda oneTimeToken bulunamadi.");
        }
        catch (JsonException ex)
        {
            return TamiHostedTokenSonucu.OlusturBasarisiz("Tami token yaniti okunamadi: " + ex.Message);
        }
    }

    public async Task<TamiOdemeSorguSonucu> OdemeSorgulaAsync(string siparisNo)
    {
        if (!_ayarlar.SorgulamaHazirMi())
        {
            return TamiOdemeSorguSonucu.Basarisiz("Tami sorgulama ayarlari eksik. Kid ve K degeri girilmeli.");
        }

        var payload = new Dictionary<string, object>
        {
            ["orderId"] = siparisNo,
            ["isTransactionDetail"] = "true"
        };
        payload["securityHash"] = SecurityHashOlustur(payload);

        using var http = YeniHttpClient();
        using var response = await http.PostAsync(
            $"{_ayarlar.ApiUrlKoku}/api/v0/payment/query",
            JsonIcerik(payload));

        var raw = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            return TamiOdemeSorguSonucu.Basarisiz($"Tami sorgu basarisiz. HTTP {(int)response.StatusCode}: {raw}");
        }

        try
        {
            using var document = JsonDocument.Parse(raw);
            var root = document.RootElement;
            var success = JsonBool(root, "success");
            var errorCode = JsonString(root, "errorCode");
            var errorMessage = JsonString(root, "errorMessage");
            var paymentStatus = JsonString(root, "paymentStatus");
            var orderStatus = JsonString(root, "orderStatus");

            if (string.IsNullOrWhiteSpace(paymentStatus) && root.TryGetProperty("order", out var order))
            {
                paymentStatus = JsonString(order, "paymentStatus");
                orderStatus = JsonString(order, "orderStatus");
            }

            return new TamiOdemeSorguSonucu
            {
                Basarili = success,
                OdemeBasarili = string.Equals(paymentStatus, "SUCCESS", StringComparison.OrdinalIgnoreCase),
                OdemeDurumu = paymentStatus,
                IslemDurumu = orderStatus,
                HataKodu = errorCode,
                HataMesaji = errorMessage,
                HamYanit = raw
            };
        }
        catch (JsonException ex)
        {
            return TamiOdemeSorguSonucu.Basarisiz("Tami sorgu yaniti okunamadi: " + ex.Message, raw);
        }
    }

    private HttpClient YeniHttpClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        http.DefaultRequestHeaders.Add("correlationId", Guid.NewGuid().ToString("N"));
        http.DefaultRequestHeaders.Add("PG-Auth-Token", PgAuthTokenOlustur());
        http.DefaultRequestHeaders.Add("PG-Api-Version", "v2");
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return http;
    }

    private string PgAuthTokenOlustur()
    {
        var text = _ayarlar.UyeIsyeriNumarasi + _ayarlar.TerminalNumarasi + _ayarlar.GuvenlikAnahtari;
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return $"{_ayarlar.UyeIsyeriNumarasi}:{_ayarlar.TerminalNumarasi}:{Convert.ToBase64String(hash)}";
    }

    private string SecurityHashOlustur(Dictionary<string, object> payload)
    {
        var imzalanacak = payload
            .Where(x => !string.Equals(x.Key, "securityHash", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(x => x.Key, x => x.Value);

        var headerJson = JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["alg"] = "HS512",
            ["typ"] = "JWT",
            ["kid"] = _ayarlar.KidDegeri
        }, JsonAyar);

        var payloadJson = JsonSerializer.Serialize(imzalanacak, JsonAyar);
        var signingInput = Base64Url(Encoding.UTF8.GetBytes(headerJson)) + "." + Base64Url(Encoding.UTF8.GetBytes(payloadJson));
        var key = TamiKDegeriCoz(_ayarlar.KDegeri);
        using var hmac = new HMACSHA512(key);
        var signature = Base64Url(hmac.ComputeHash(Encoding.UTF8.GetBytes(signingInput)));
        return signingInput + "." + signature;
    }

    private string HostedOdemeSayfasi(string token)
        => $"{_ayarlar.PortalUrlKoku}/hostedPaymentPage?token={token}";

    private static StringContent JsonIcerik(object payload)
        => new(JsonSerializer.Serialize(payload, JsonAyar), Encoding.UTF8, "application/json");

    private static byte[] TamiKDegeriCoz(string deger)
    {
        var temiz = (deger ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(temiz))
        {
            return Array.Empty<byte>();
        }

        try
        {
            var base64 = temiz.Replace('-', '+').Replace('_', '/');
            base64 = base64.PadRight(base64.Length + ((4 - base64.Length % 4) % 4), '=');
            return Convert.FromBase64String(base64);
        }
        catch (FormatException)
        {
            return Encoding.UTF8.GetBytes(temiz);
        }
    }

    private static string Base64Url(byte[] bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static bool JsonBool(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return false;
        }

        return property.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String => string.Equals(property.GetString(), "true", StringComparison.OrdinalIgnoreCase) || property.GetString() == "1",
            JsonValueKind.Number => property.TryGetInt32(out var value) && value == 1,
            _ => false
        };
    }

    private static string JsonString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        return property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : property.ToString();
    }
}

public sealed class TamiHostedTokenIstegi
{
    public int CalismaAlaniId { get; set; }
    public int OdemePaketiId { get; set; }
    public string SiparisNo { get; set; }
    public decimal Tutar { get; set; }
    public string BasariliDonusUrl { get; set; }
    public string BasarisizDonusUrl { get; set; }
}

public sealed class TamiHostedTokenSonucu
{
    public bool Basarili { get; set; }
    public string Token { get; set; }
    public string OdemeSayfasi { get; set; }
    public string HataMesaji { get; set; }

    public static TamiHostedTokenSonucu OlusturBasarili(string token, string odemeSayfasi)
        => new() { Basarili = true, Token = token, OdemeSayfasi = odemeSayfasi };

    public static TamiHostedTokenSonucu OlusturBasarisiz(string hataMesaji)
        => new() { Basarili = false, HataMesaji = hataMesaji };
}

public sealed class TamiOdemeSorguSonucu
{
    public bool Basarili { get; set; }
    public bool OdemeBasarili { get; set; }
    public string OdemeDurumu { get; set; }
    public string IslemDurumu { get; set; }
    public string HataKodu { get; set; }
    public string HataMesaji { get; set; }
    public string HamYanit { get; set; }

    public static TamiOdemeSorguSonucu Basarisiz(string hataMesaji, string hamYanit = null)
        => new() { Basarili = false, HataMesaji = hataMesaji, HamYanit = hamYanit };
}
