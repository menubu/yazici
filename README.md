# MenuBu Printer Agent v2.2.8

Modern, stabil ve güvenli Windows yazıcı ajanı.

## Özellikler

- 🔐 **Token tabanlı kimlik doğrulama** - Şifre saklanmaz
- ⚡ **WebSocket anlık baskı** - Sipariş gelince anında yazdır
- 🔔 **Masaüstü bildirimleri** - Site kapalıyken de bildirim al
- 🖨️ **Çoklu yazıcı desteği** - Mutfak/bar/kasa ayrı yazıcılar
- 🔒 **Güvenlik / gizlilik onayı** - İlk açılışta zorunlu onay akışı
- 📝 **Detaylı loglama** - Sorun çözmek kolay
- 🎨 **Modern arayüz** - Kolay kullanım
- 📦 **Kurulumlu dağıtım** - Program Ekle/Kaldır destekli setup
- 🔄 **Otomatik güncelleme** - Yeni sürüm kontrolü

## Gereksinimler

- Windows 10/11 (64-bit)
- İnternet bağlantısı
- Termal yazıcı (58mm veya 80mm)

## Kurulum

1. `MenuBuPrinterAgent-Setup.exe` dosyasını indirin
2. Çalıştırın ve kurulumu tamamlayın
3. Sistem tepsisinden giriş yapın
4. Yazıcınızı seçin

## Derleme

```bash
dotnet publish -c Release -r win-x64 --self-contained true
```

## Kurulumlu Paket (Program Ekle/Kaldır)

`installer/MenuBuPrinterAgent.iss` ile MSI benzeri klasik Windows kurulum paketi üretilir.

1. Windows ortamında publish alın:
```powershell
dotnet publish .\src\MenuBuPrinterAgent.csproj -c Release -r win-x64 --self-contained true -o .\publish\win-x64
```
2. (Opsiyonel) `installer/dependencies/` altına `MicrosoftEdgeWebView2RuntimeInstallerX64.exe` koyun.
3. Inno Setup ile `installer/MenuBuPrinterAgent.iss` dosyasını derleyin.

Çıktı: `dist/MenuBuPrinterAgent-Setup.exe`  
Bu kurulum, uygulamayı Program Ekle/Kaldır listesine ekler ve uninstall desteği sağlar.

## Lisans

© 2025 MenuBu - Tüm hakları saklıdır.
