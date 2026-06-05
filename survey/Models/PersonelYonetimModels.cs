using System;

namespace survey.Models
{
    public class PersonelListeSatiri
    {
        public int PersonelId { get; set; }
        public string PersonelAdi { get; set; }
        public string Tc { get; set; }
        public int? Unvani { get; set; }
        public string UnvanAdi { get; set; }
        public string Mail { get; set; }
        public string Resim { get; set; }
        public string Telefon { get; set; }
        public string Adres { get; set; }
        public string KullaniciAdi { get; set; }
        public bool? Pasif { get; set; }
        public DateTime? KayitTarihi { get; set; }
        public bool? Admin { get; set; }
        public bool? MailOnaylandi { get; set; }
        public string GoogleKimlikId { get; set; }
        public string GirisKaynagi { get; set; }
        public DateTime? SonGirisTarihi { get; set; }

        public bool HesapAktif => Pasif != true;
        public bool Yetkili => Admin == true;
        public bool MailOnayli => MailOnaylandi != false;
    }

    public class PersonelYonetimForm
    {
        public int PersonelId { get; set; }
        public string PersonelAdi { get; set; }
        public string Tc { get; set; }
        public int? Unvani { get; set; }
        public string Mail { get; set; }
        public string Resim { get; set; }
        public string Telefon { get; set; }
        public string Adres { get; set; }
        public string KullaniciAdi { get; set; }
        public string Sifre { get; set; }
        public bool HesapAktif { get; set; } = true;
        public DateTime? KayitTarihi { get; set; }
        public bool Admin { get; set; }
        public bool MailOnaylandi { get; set; } = true;
        public string GoogleKimlikId { get; set; }
        public string GirisKaynagi { get; set; }
        public DateTime? MailOnayKoduTarihi { get; set; }
        public DateTime? SonGirisTarihi { get; set; }
    }

    public class PersonelPanelBilgisi
    {
        public int PersonelId { get; set; }
        public bool? MailOnaylandi { get; set; }
        public string GoogleKimlikId { get; set; }
        public string GirisKaynagi { get; set; }
        public DateTime? MailOnayKoduTarihi { get; set; }
        public DateTime? SonGirisTarihi { get; set; }
    }
}
