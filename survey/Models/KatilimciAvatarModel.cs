namespace survey.Models
{
    public class KatilimciAvatarModel
    {
        public string Ad { get; set; }
        public string Resim { get; set; }
        public string Cinsiyet { get; set; }
        public string Size { get; set; } = "md";
        public string ClassName { get; set; }
        public string Klasor { get; set; } = "Katilimci";
    }
}
