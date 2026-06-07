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
    public bool PaketYonetimiAktif { get; set; }
    public string Mesaj { get; set; }
}

public sealed class PlatformOdemeRaporuModel
{
    public int Gun { get; set; }
    public DateTime? DonemBaslangicTarihi { get; set; }
    public DateTime DonemBitisTarihi { get; set; } = DateTime.Today;
    public PlatformOdemeRaporuOzetModel Ozet { get; set; } = new();
    public List<PlatformPaketDagilimModel> PaketDagilimi { get; set; } = new();
    public List<PlatformAylikGelirModel> AylikGelir { get; set; } = new();
    public List<PlatformMusteriRaporSatiriModel> Musteriler { get; set; } = new();
    public List<PlatformOdemeRaporSatiriModel> SonOdemeler { get; set; } = new();
}

public sealed class PlatformOdemeRaporuOzetModel
{
    public int ToplamCalismaAlani { get; set; }
    public int AktifCalismaAlani { get; set; }
    public int UcretsizMusteri { get; set; }
    public int UcretliMusteri { get; set; }
    public int PaketsizMusteri { get; set; }
    public int ToplamPanelKullanicisi { get; set; }
    public int ToplamKatilimci { get; set; }
    public int ToplamAnket { get; set; }
    public int YayindaAnket { get; set; }
    public int UcretsizDenemeKaydi { get; set; }
    public int DonemYeniCalismaAlani { get; set; }
    public int DonemUcretsizBaslangic { get; set; }
    public int DonemOdemeSayisi { get; set; }
    public int BekleyenOdemeSayisi { get; set; }
    public int HataOdemeSayisi { get; set; }
    public int BasariliOdemeSayisi { get; set; }
    public decimal ToplamGelir { get; set; }
    public decimal DonemGelir { get; set; }
    public decimal DonusumOrani { get; set; }
}

public sealed class PlatformPaketDagilimModel
{
    public int OdemePaketiId { get; set; }
    public string PaketKodu { get; set; }
    public string PaketAdi { get; set; }
    public int AktifMusteriSayisi { get; set; }
    public int BasariliOdemeSayisi { get; set; }
    public decimal Gelir { get; set; }
    public decimal OrtalamaTutar { get; set; }
}

public sealed class PlatformAylikGelirModel
{
    public string AyEtiketi { get; set; }
    public int OdemeSayisi { get; set; }
    public decimal Gelir { get; set; }
}

public sealed class PlatformMusteriRaporSatiriModel
{
    public int CalismaAlaniId { get; set; }
    public string CalismaAlaniAdi { get; set; }
    public string FirmaAdi { get; set; }
    public string SahipAdi { get; set; }
    public string SahipEposta { get; set; }
    public string SahipTelefon { get; set; }
    public DateTime? KayitTarihi { get; set; }
    public string PaketKodu { get; set; }
    public string PaketAdi { get; set; }
    public string AbonelikDurumu { get; set; }
    public DateTime? BaslangicTarihi { get; set; }
    public DateTime? BitisTarihi { get; set; }
    public int KalanGun { get; set; }
    public bool UcretliMi { get; set; }
    public int KullaniciLimiti { get; set; }
    public int AktifAnketLimiti { get; set; }
    public int AylikYanitLimiti { get; set; }
    public int PanelKullanicisi { get; set; }
    public int KatilimciSayisi { get; set; }
    public int AnketSayisi { get; set; }
    public int YayindaAnketSayisi { get; set; }
    public decimal? SonOdemeTutari { get; set; }
    public string SonOdemeParaBirimi { get; set; }
    public string SonOdemeDurumu { get; set; }
    public DateTime? SonOdemeTarihi { get; set; }
    public string FirsatNotu { get; set; }
}

public sealed class PlatformOdemeRaporSatiriModel
{
    public int OdemeIslemiId { get; set; }
    public int CalismaAlaniId { get; set; }
    public string CalismaAlaniAdi { get; set; }
    public string FirmaAdi { get; set; }
    public string PaketAdi { get; set; }
    public string SiparisNo { get; set; }
    public decimal Tutar { get; set; }
    public string ParaBirimi { get; set; }
    public string OdemeDurumu { get; set; }
    public string TamiHataMesaji { get; set; }
    public DateTime KayitTarihi { get; set; }
    public DateTime? TamamlanmaTarihi { get; set; }
}
