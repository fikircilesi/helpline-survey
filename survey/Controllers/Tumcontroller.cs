using System.Collections.Generic;


namespace survey.Models
{
    public class Tumcontroller
    {
        public IEnumerable<survey.Models.Havuz> Hav { get; set; }
        public IEnumerable<survey.Models.Anket> Ank { get; set; }
        public IEnumerable<survey.Models.AnketGrup> AnkGrp { get; set; }
        public IEnumerable<survey.Models.Soru> Sor { get; set; }
        public IEnumerable<survey.Models.Cevap> Cev { get; set; }
        public IEnumerable<survey.Models.SoruGrup> SorGrp { get; set; }
        public IEnumerable<survey.Models.CevapGrup> CevGrp { get; set; }
        public IEnumerable<survey.Models.User> Usr { get; set; }
        public IEnumerable<survey.Models.Unvan> Unv { get; set; }
        public IEnumerable<survey.Models.Cinsiyet> Cin { get; set; }
        public IEnumerable<survey.Models.Egitim> Egi { get; set; }
        public IEnumerable<survey.Models.Yonetici> Yon { get; set; }
        public IEnumerable<survey.Models.Bolum> Bol { get; set; }
        public IEnumerable<survey.Models.Sube> Sub { get; set; }
        public IEnumerable<survey.Models.Yaka> Yak { get; set; }
        public IEnumerable<survey.Models.Bolge> Blg { get; set; }
        public IEnumerable<survey.Models.Sehir> Seh { get; set; }
        public IEnumerable<survey.Models.Departman> Dep { get; set; }

    }
}