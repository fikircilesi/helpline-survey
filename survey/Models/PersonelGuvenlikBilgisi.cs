namespace survey.Models
{
    public class PersonelGuvenlikBilgisi
    {
        public int PersonelId { get; set; }
        public bool MailOnaylandi { get; set; }
        public string MailOnayKodu { get; set; }
        public DateTime? MailOnayKoduTarihi { get; set; }
        public string GoogleKimlikId { get; set; }
        public string GirisKaynagi { get; set; }
    }
}
