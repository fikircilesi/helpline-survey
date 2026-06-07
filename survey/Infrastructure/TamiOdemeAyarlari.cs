namespace survey.Infrastructure;

public sealed class TamiOdemeAyarlari
{
    public bool Aktif { get; set; }
    public bool TestModu { get; set; } = true;
    public string UyeIsyeriNumarasi { get; set; }
    public string TerminalNumarasi { get; set; }
    public string GuvenlikAnahtari { get; set; }
    public string KidDegeri { get; set; }
    public string KDegeri { get; set; }
    public string MusteriTelefonu { get; set; }
    public string DonusUrlKoku { get; set; }

    public string ApiUrlKoku => TestModu
        ? "https://sandbox-paymentapi.tami.com.tr"
        : "https://paymentapi.tami.com.tr";

    public string PortalUrlKoku => TestModu
        ? "https://sandbox-portal.tami.com.tr"
        : "https://portal.tami.com.tr";

    public bool HostedOdemeHazirMi()
        => Aktif
           && !string.IsNullOrWhiteSpace(UyeIsyeriNumarasi)
           && !string.IsNullOrWhiteSpace(TerminalNumarasi)
           && !string.IsNullOrWhiteSpace(GuvenlikAnahtari)
           && !string.IsNullOrWhiteSpace(MusteriTelefonu);

    public bool SorgulamaHazirMi()
        => HostedOdemeHazirMi()
           && !string.IsNullOrWhiteSpace(KidDegeri)
           && !string.IsNullOrWhiteSpace(KDegeri);
}
