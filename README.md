# 👁️ VPN Bypass — Zero-Lag DPI Unblocker

<p align="center">
  <a href="https://github.com/SeriesFiftyeight/VPNBypass/releases/latest">
    <img src="https://img.shields.io/badge/DOWNLOAD-VPN_Bypass_v2.0-00FF9C?style=for-the-badge&logo=windows&logoColor=black" alt="Download" />
  </a>
  <img src="https://img.shields.io/badge/Platform-Windows_10_%2F_11-informational?style=for-the-badge&logo=windows" alt="Windows" />
  <img src="https://img.shields.io/badge/.NET-10-512BD4?style=for-the-badge" alt=".NET 10" />
  <img src="https://img.shields.io/badge/License-MIT-green?style=for-the-badge" alt="MIT" />
</p>

İnternet sağlayıcının **DPI (Deep Packet Inspection)** engellerini paket seviyesinde aşar.
Trafiğin yabancı bir sunucuya yönlendirilmediği için **ek ping yoktur.** Bir VPN gibi çalışır,
ama VPN değildir: bağlantın doğrudan hedef sunucuya gider.

> v2.0 sıfırdan yazıldı: yeni isim, hacker temalı arayüz, canlı ping matrisi ve
> Discord'a bağlı olmayan genel kullanım.

---

## 📥 Hızlı Başlangıç

1. **[Releases](https://github.com/SeriesFiftyeight/VPNBypass/releases/latest)** sayfasından `VPNBypass-v2.0.0.zip` dosyasını indir ve bir klasöre çıkar.
2. `VPNBypass.exe` dosyasına çift tıkla (yönetici izni ister).
3. **BAĞLAN**'a bas. Göz kırmızıdan yeşile döndüğünde bypass aktiftir.

**Gereksinim:** Windows 10/11 x64 ve [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0).

---

## ✨ Özellikler

| | |
|---|---|
| 👁️ **Canlı göz göstergesi** | Hiç kırpmadan sağa sola tarayan göz. Kırmızı: kapalı, yeşil: aktif |
| ⚡ **Canlı ping matrisi** | Birden çok hedefe paralel ping, geçmiş grafiği, ortalama ve paket kaybı |
| 🎚️ **Ayarlanabilir ping limitleri** | İyi / kritik eşikleri sen belirlersin. Renkler, grafik çizgileri ve uyarılar buna göre değişir |
| 🔔 **Kritik ping uyarısı** | Ortalama ping 3 ölçüm boyunca kritik eşiği aşarsa bildirim gösterir |
| 🌐 **8 bypass profili** | Türk Telekom, TurkNet, Kablonet, Vodafone, Superonline, Agresif, Cloudflare DNS, Turbo |
| 🚀 **Turbo / hızlandırma** | `--max-payload` ile büyük paketler (indirme, video) DPI işleminden muaf tutulur, CPU ve gecikme düşer |
| 🖥️ **Terminal günlüğü** | Motorun çıktısı canlı akar |
| 📥 **Tepsi modu** | Arka planda sessizce çalışır. Tepsi menüsünden bağlan / kes |
| 🔁 **Windows ile başlat** | Görev Zamanlayıcı üzerinden, UAC sorusu olmadan |
| 🌍 **TR / EN** | Anında dil değişimi |

---

## 🗂️ Profiller

| Kod | Profil | Kime göre |
|---|---|---|
| `STD-TR` ★ | Standart TR | Türk Telekom, TurkNet, Kablonet, Vodafone |
| `SOL-4` ★ | Superonline Mod 4 | Superonline fiber |
| `SOL-1` | Superonline Mod 1 · TTL 3 | Superonline alternatif |
| `SOL-2` | Superonline Mod 2 · -5 | DNS yönlendirmesiz |
| `SOL-3` | Superonline Mod 3 · TTL 3 + DNS | Superonline alternatif |
| `MAX-9` | Agresif · Maksimum | Zorlu filtreler |
| `CF-DNS` | Cloudflare DNS · TTL 5 | Cloudflare 1.1.1.1 tercih edenler |
| `TURBO` | Saf Parçalama | En düşük gecikme, DNS'e dokunmaz |

---

## 🛠️ Kaynaktan derleme

```powershell
# Geliştirme
dotnet build src/VpnBypass

# Yayın paketi (release/VPNBypass-v2.0.0.zip)
powershell -ExecutionPolicy Bypass -File build/publish.ps1
```

Proje yapısı:

```text
src/VpnBypass/
├── Core/         # Motor (GoodbyeDPI), profiller, ayarlar, ping izleyici
├── Controls/     # Göz animasyonu, matrix yağmuru, ping grafiği
├── Themes/       # Hacker teması (renkler, stiller)
├── Lang/         # tr.xaml / en.xaml
└── MainWindow    # Kontrol · Profiller · Ayarlar
engine/           # goodbyedpi.exe + WinDivert sürücüleri
build/            # publish.ps1, make-icon.ps1
```

---

## ⚖️ Lisanslar

- **VPN Bypass**: MIT, © 2026 SeriesFiftyeight
- **GoodbyeDPI**: ValdikSS, Apache License 2.0
- **WinDivert**: LGPLv3
