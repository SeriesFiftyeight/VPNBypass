# Changelog

## v2.1.1

### Düzeltmeler
- Engelli olmayan bazı sitelerin açılmaması düzeltildi. Bölünmüş ilk paketi reddeden ya da cevapsız bırakan katı sunucular (e-Devlet, bankalar, meb.gov.tr) için otomatik geri dönüş eklendi: motor normal paketle yeniden bağlanır ve siteyi hatırlar.
- Happy Eyeballs: IPv4 ve IPv6 adresleri paralel denenir, ilk bağlanan kullanılır. Bir adres cevap vermediğinde artık saniyelerce beklenmiyor.

## v2.1.0

### Kendi motorumuz
- GoodbyeDPI ve WinDivert tamamen kaldırıldı. Yerlerine uygulamanın içinde çalışan kendi motorumuz geldi: yerel proxy, TLS kaydı bölme, TCP bölme ve TTL ile sıra bozma.
- Kendi şifreli DNS çözümleyicimiz eklendi (DoH: Cloudflare, Google, Quad9, AdGuard). ISS DNS zehirlemesi ve IPv6-only ağlardaki DNS çökmesi çözüldü.
- **Yönetici izni artık gerekmiyor.** Sürücü yok, antivirüs uyarısı yok.
- Windows proxy ayarı resmi WinINET API'siyle yapılıyor, böylece Chrome ve Discord da motoru kullanıyor. Bekçi süreç sayesinde uygulama çökse ya da zorla kapatılsa bile internet kesilmiyor. Açılışta kalıntı ayarlar temizleniyor.
- "En iyi yöntemi otomatik bul": tüm yöntemleri engelli sitelere karşı dener ve en iyisini seçer.
- Bağlıyken yöntem değiştirmek motoru yeniden başlatmıyor, anında geçerli oluyor.

### Arayüz
- Uzun ince, sade panel: büyük bağlan/kes düğmesi, PING · PROFİL · GÜNLÜK · AYARLAR sekmeleri.
- Göz animasyonu ve matrix efekti kaldırıldı. Daha az CPU ve bellek kullanılıyor.
- Trafik sayacı eklendi.

### Düzeltmeler
- IPv4 olmayan ağlarda ping satırları "IPv4 YOK" gösteriyor ve paket kaybına sayılmıyor.
- Ping artık kendi DNS'imizle çözülüyor. Zehirli DNS yüzünden engel sayfasına ping atılmıyor.
- Varsayılan ping hedefleri IPv4 ve IPv6 ile çalışan alan adları oldu.
- Windows ile başlat artık her durumda çalışıyor (kullanıcı Run anahtarı).

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
