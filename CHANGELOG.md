# Changelog

## v2.0.0

Sıfırdan yeniden yazıldı. Proje adı **DC Bypass Pro → VPN Bypass** oldu.

### Yeni
- Konsol menüsünün yerine WPF ile yazılmış, hacker temalı grafik arayüz geldi: matrix yağmuru, CRT tarama çizgileri ve glitch logo.
- Hiç kırpmadan sağa sola bakan, duruma göre renk değiştiren animasyonlu göz göstergesi.
- Canlı ping matrisi: paralel ölçüm, hedef başına geçmiş grafiği, ortalama ve paket kaybı.
- Ayarlanabilir ping limitleri (iyi / kritik), ölçüm aralığı ve kritik ping bildirimi.
- Ping hedefleri arayüzden düzenlenebiliyor.
- `TURBO` profili ve `--max-payload` ile daha düşük CPU kullanımı ve gecikme.
- Terminal tarzı canlı olay günlüğü.

### Değişen
- Discord'a özel özellikler kaldırıldı (gri ekran düzeltici, Discord başlatıcı). Uygulama artık her site için genel amaçlı.
- Windows ile başlatma artık Registry Run yerine Görev Zamanlayıcı ile yapılıyor (v1'de yönetici yetkisi nedeniyle çalışmıyordu).
- Ayarlar `vpnbypass.json` dosyasında tutuluyor.
- `START.bat` ve `START_TRAY.vbs` kaldırıldı. Exe kendisi yönetici izni istiyor ve `--tray` parametresi destekleniyor.
- Windows servis modu kaldırıldı.
- Repodan derlenmiş dosyalar (`dist/`) çıkarıldı. Paket artık `build/publish.ps1` ile üretilip Releases'a yükleniyor.

## v1.0.0

- İlk sürüm: DC Bypass Pro (konsol arayüzü + tepsi ikonu).
