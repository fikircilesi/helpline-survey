# Helpline Survey

Bu repo `C:\inetpub\vhosts\helpline.zone\Survey` klasorundeki Survey projesi icin hazirlandi.

Git'e alinmayan dosyalar:

- `bin/`, `obj/`, `.vs/` build ve IDE dosyalari
- `appsettings*.json` yerel baglanti bilgileri ve gizli ayarlar
- `*.log`, `*.err` calisma zamani loglari
- `wwwroot/uploads/` kullanici yuklemeleri
- `_codex_backup_*`, `_codex_backups/` eski yedekler

Evde devam etmek icin:

1. Private GitHub reposunu klonla.
2. `survey/survey.sln` dosyasini Visual Studio ile ac.
3. Yerel/gizli `appsettings.json` dosyasini `survey/` klasorune koy.
4. `dotnet restore` ve `dotnet build` ile projeyi hazirla.

Not: `appsettings.json` dosyalari bilerek GitHub'a gonderilmez.
