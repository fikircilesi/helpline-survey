# Codex Sync Notu

Bu dosya evdeki ve isteki Codex'in ayni proje durumunu anlamasi icin tutulur.

## Proje

- GitHub repo: `https://github.com/fikircilesi/helpline-survey.git`
- Lokal proje klasoru: `C:\Users\DeLL\Documents\helpline-survey`
- Visual Studio solution: `C:\Users\DeLL\Documents\helpline-survey\survey\survey.sln`
- Uygulama adi: `Helpline Survey`
- Planlanan adres: `anket.aslana.com.tr`

## Her Bilgisayarda Baslarken

```powershell
cd C:\Users\DeLL\Documents\helpline-survey
git pull
```

Visual Studio'da `survey\survey.sln` dosyasini ac.

## Is Bitince

```powershell
cd C:\Users\DeLL\Documents\helpline-survey
git status
git add .
git commit -m "calisma guncellemesi"
git push
```

## SQL Senkron

GitHub kodu tasir, SQL Server veritabanini otomatik tasimaz.

SQL icin iki yontem var:

1. Tablo yapisi degisiklikleri icin `survey\DatabaseScripts` altina tarihli `.sql` script ekle.
2. Tum veritabani gerekiyorsa SSMS ile `Survey` icin `.bak` yedegi al ve OneDrive/Google Drive ile diger bilgisayara tasi.

Su an is bilgisayarinda:

- SQL Server Developer kurulu.
- SSMS 22 kurulu.
- `Survey` veritabani attach edildi.
- `EnvanterTakipLisans` su an ana konu degil, dokunulmadan duruyor.

Evde SQL degisikligi yapildiysa ise gelince once `Survey.bak` getirilmeli, sonra burada restore edilmeli.

## Lokal Gizli Dosyalar

Asagidaki dosyalar GitHub'a gitmez ve her bilgisayarda lokal kalir:

- `survey\appsettings.json`
- `survey\appsettings.Development.json`
- `survey\.vs\`
- `survey\bin\`
- `survey\obj\`

`appsettings.json` icindeki sifre ve connection string bilgileri GitHub'a gonderilmez.

## Codex'e Yazilacak Kisa Komutlar

Evde is bitince:

```text
Kod degisikliklerini kontrol et, commit ve push yap. SQL degisikligi varsa Survey icin yedek/script hazirla.
```

Iste baslarken:

```text
Kaldigimiz yerden devam. Git pull yap, CODEX-SYNC.md dosyasina gore proje ve SQL durumunu kontrol et.
```

Yedek gelince:

```text
Survey .bak dosyasini restore et, sonra uygulamayi VS/dotnet ile test et.
```
