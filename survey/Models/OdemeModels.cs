namespace survey.Models;

public sealed class OdemePaketiModel
{
    public int OdemePaketiId { get; set; }
    public string PaketKodu { get; set; }
    public string PaketAdi { get; set; }
    public string Aciklama { get; set; }
    public decimal Tutar { get; set; }
    public string ParaBirimi { get; set; }
    public int SureGun { get; set; }
    public int KullaniciLimiti { get; set; }
    public int AktifAnketLimiti { get; set; }
    public int AylikYanitLimiti { get; set; }
    public bool MarkaIziGoster { get; set; }
    public bool PdfRaporAktif { get; set; }
    public bool GelismisRaporAktif { get; set; }
    public bool DisaAktarmaAktif { get; set; }
    public bool YapayZekaOzetAktif { get; set; }
    public int SiraNo { get; set; }
}

public sealed class AbonelikDurumuModel
{
    public int CalismaAlaniId { get; set; }
    public int? OdemePaketiId { get; set; }
    public string PaketKodu { get; set; }
    public string PaketAdi { get; set; }
    public string AbonelikDurumu { get; set; }
    public DateTime? BaslangicTarihi { get; set; }
    public DateTime? BitisTarihi { get; set; }
    public int KalanGun { get; set; }
    public int KullaniciLimiti { get; set; }
    public int AktifAnketLimiti { get; set; }
    public int AylikYanitLimiti { get; set; }
    public bool MarkaIziGoster { get; set; }
    public bool PdfRaporAktif { get; set; }
    public bool GelismisRaporAktif { get; set; }
    public bool DisaAktarmaAktif { get; set; }
    public bool YapayZekaOzetAktif { get; set; }
}

public sealed class OdemeIslemiModel
{
    public int OdemeIslemiId { get; set; }
    public int CalismaAlaniId { get; set; }
    public int PersonelId { get; set; }
    public int OdemePaketiId { get; set; }
    public string PaketAdi { get; set; }
    public string SiparisNo { get; set; }
    public decimal Tutar { get; set; }
    public string ParaBirimi { get; set; }
    public string OdemeDurumu { get; set; }
    public string TamiJeton { get; set; }
    public string TamiOdemeSayfasi { get; set; }
    public string TamiHataKodu { get; set; }
    public string TamiHataMesaji { get; set; }
    public string TamiOdemeDurumu { get; set; }
    public string TamiIslemDurumu { get; set; }
    public DateTime KayitTarihi { get; set; }
    public DateTime? TamamlanmaTarihi { get; set; }
}

public sealed class OdemePaketleriSayfaModel
{
    public AbonelikDurumuModel Abonelik { get; set; }
    public List<OdemePaketiModel> Paketler { get; set; } = new();
    public List<OdemeIslemiModel> SonOdemeler { get; set; } = new();
    public bool TamiHazir { get; set; }
    public string Mesaj { get; set; }
}
