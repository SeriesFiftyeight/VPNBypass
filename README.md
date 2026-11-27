# VPN Bypass: Zero-Lag DPI Unblocker

<p align="center">
  <a href="https://github.com/SeriesFiftyeight/VPNBypass/releases/latest">
    <img src="https://img.shields.io/badge/DOWNLOAD-VPN_Bypass-00FF9C?style=for-the-badge&logo=windows&logoColor=black" alt="Download" />
  </a>
  <img src="https://img.shields.io/badge/Platform-Windows_10_%2F_11-informational?style=for-the-badge&logo=windows" alt="Windows" />
  <img src="https://img.shields.io/badge/Admin-Not_required-00E5FF?style=for-the-badge" alt="No admin" />
  <img src="https://img.shields.io/badge/License-MIT-green?style=for-the-badge" alt="MIT" />
</p>

İnternet sağlayıcının **DPI (Deep Packet Inspection)** engellerini aşar. Trafiğin yabancı bir sunucuya
gitmediği için **ek ping yoktur.** VPN gibi çalışır, ama VPN değildir: bağlantın doğrudan hedef sunucuya gider.

v2.1'den itibaren **tamamen kendi motorunu** kullanır. GoodbyeDPI, WinDivert ya da başka bir sürücü
veya program yoktur. **Yönetici izni gerekmez.**

---

## Hızlı Başlangıç

1. **[Releases](https://github.com/SeriesFiftyeight/VPNBypass/releases/latest)** sayfasından son zip'i indir ve bir klasöre çıkar.
2. `VPNBypass.exe` dosyasını çalıştır.
3. Ortadaki büyük düğmeye bas. **KORUMA AÇIK** yazısı çıkınca bypass aktiftir.
4. Bir site hâlâ açılmıyorsa **PROFİL → En iyi yöntemi otomatik bul** düğmesine bas.

**Gereksinim:** Windows 10/11 x64 ve [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0).

---

## Nasıl çalışır?

```text
Tarayıcı / Discord ──► VPN Bypass motoru (127.0.0.1) ──► hedef sunucu
                          │
                          ├─ Şifreli DNS (DoH): site adını ISS'ye sormadan çözer
                          └─ Desync: ilk paketi böler, DPI site adını göremez
```

1. **Sistem proxy'si:** Uygulama, Windows'un kendi proxy API'siyle kullanıcı proxy'sini yerel motora
   yönlendirir. Tarayıcılar ve Discord bunu anında görür. Görünmez bir bekçi süreç ana uygulamayı izler:
   uygulama çökse ya da Görev Yöneticisi'nden kapatılsa bile eski ayarlarını geri yükler,
   **internetin hiçbir zaman kesilmez.**
2. **Şifreli DNS:** Site adları Cloudflare, Google, Quad9 veya AdGuard üzerinden HTTPS ile çözülür.
   ISS'nin DNS zehirlemesi etkisiz kalır.
3. **Desync:** DPI cihazları site adını bağlantının ilk paketinden okur (TLS SNI / HTTP Host). Motor bu
   paketi, site adı hiçbir zaman tek parça görünmeyecek şekilde yeniden düzenler. Sunucu ise geçerli
   bir bağlantı alır.

---

## Yöntemler

| Kod | Yöntem | Teknik | Kime göre |
|---|---|---|---|
| `STD` | Standart | TLS kaydı bölme + TCP bölme | Türk Telekom, TurkNet, Vodafone, Kablonet |
| `DSR` | Sıra Bozma | TLS bölme + ilk bayt + TTL=1 sıra bozma | Superonline, sıkı filtreler |
| `MAX` | Agresif | Hepsi birden | Diğerleri çalışmazsa |
| `TLS` | TLS Bölme | Sadece TLS kaydı bölme | En hafif |
| `TCP` | TCP Bölme | Sadece TCP paketini bölme | Alternatif |

**Otomatik bul**, her yöntemi engelli sitelere karşı dener ve en çok siteyi en hızlı açanı seçer.

---

## Özellikler

- **Tek tık bağlan / kes** ile uzun ince, sade panel
- **Canlı ping:** Birden çok hedef, geçmiş grafiği, ortalama, paket kaybı ve trafik sayacı
- **Ayarlanabilir ping limitleri** ve kritik ping bildirimi
- **IPv4 kesinti algılama:** Modem adres veremediğinde ne yapılacağını söyler
- **Tepsi modu**, Windows ile başlatma, TR / EN

---

## Kaynaktan derleme

```powershell
dotnet build src/VpnBypass

# Yayın paketi (release/VPNBypass-v2.1.1.zip)
powershell -ExecutionPolicy Bypass -File build/publish.ps1
# .NET kurulumu gerektirmeyen paket
powershell -ExecutionPolicy Bypass -File build/publish.ps1 -SelfContained
```

```text
src/VpnBypass/
├── Core/
│   ├── ProxyServer.cs   # Yerel HTTP/CONNECT proxy
│   ├── Desync.cs        # SNI bulma, TLS kaydı bölme, TCP bölme, sıra bozma
│   ├── DohResolver.cs   # Kendi DNS-over-HTTPS çözümleyicimiz
│   ├── SystemProxy.cs   # Windows proxy ayarı (WinINET API) + yedek/geri yükleme
│   ├── AutoTuner.cs     # En iyi yöntemi bulur
│   └── PingMonitor.cs   # Canlı ping ve limitler
├── Themes/ Lang/ Controls/
└── MainWindow           # Bağlan · Ping · Profil · Günlük · Ayarlar
```

---

## Lisans

MIT, © 2026 SeriesFiftyeight. Üçüncü parti motor veya sürücü içermez.
