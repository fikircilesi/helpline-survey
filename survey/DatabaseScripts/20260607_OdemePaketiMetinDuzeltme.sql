USE Survey;
GO

DECLARE @UcretsizAdi NVARCHAR(120) = NCHAR(220) + N'cretsiz';
DECLARE @BaslangicAdi NVARCHAR(120) = N'Ba' + NCHAR(351) + N'lang' + NCHAR(305) + NCHAR(231);
DECLARE @UcretsizAciklama NVARCHAR(600) = N'K' + NCHAR(252) + NCHAR(231) + NCHAR(252) + N'k i' + NCHAR(351) + N'letmelerin temel anket ihtiyac' + NCHAR(305) + N'n' + NCHAR(305) + N' ger' + NCHAR(231) + N'ekten ' + NCHAR(231) + NCHAR(246) + N'zer. Survey by Aslana Teknoloji marka izi g' + NCHAR(246) + N'r' + NCHAR(252) + N'n' + NCHAR(252) + N'r.';
DECLARE @BaslangicAciklama NVARCHAR(600) = N'D' + NCHAR(252) + N'zenli anket kullanan k' + NCHAR(252) + NCHAR(231) + NCHAR(252) + N'k ekipler i' + NCHAR(231) + N'in daha fazla kapasite ve marka izi kald' + NCHAR(305) + N'rma.';
DECLARE @ProfesyonelAciklama NVARCHAR(600) = N'Raporlama, d' + NCHAR(305) + NCHAR(351) + N'a aktarma ve b' + NCHAR(252) + N'y' + NCHAR(252) + N'yen ekip kullan' + NCHAR(305) + N'm' + NCHAR(305) + N' i' + NCHAR(231) + N'in ana paket.';
DECLARE @KurumsalAciklama NVARCHAR(600) = N'Y' + NCHAR(252) + N'ksek hacimli kurumlar, ' + NCHAR(246) + N'zel destek ve y' + NCHAR(305) + N'll' + NCHAR(305) + N'k kullan' + NCHAR(305) + N'm i' + NCHAR(231) + N'in.';

UPDATE dbo.OdemePaketi
SET PaketAdi = @UcretsizAdi,
    Aciklama = @UcretsizAciklama
WHERE PaketKodu = N'UCRETSIZ';

UPDATE dbo.OdemePaketi
SET PaketAdi = @BaslangicAdi,
    Aciklama = @BaslangicAciklama
WHERE PaketKodu = N'BASLANGIC';

UPDATE dbo.OdemePaketi
SET PaketAdi = N'Profesyonel',
    Aciklama = @ProfesyonelAciklama
WHERE PaketKodu = N'PROFESYONEL';

UPDATE dbo.OdemePaketi
SET PaketAdi = N'Kurumsal',
    Aciklama = @KurumsalAciklama
WHERE PaketKodu = N'KURUMSAL';
GO
