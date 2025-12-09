# MenuBu Printer Agent v2.0

Modern, stabil ve güvenli Windows yazıcı ajanı.

## Özellikler

- 🔐 **Token tabanlı kimlik doğrulama** - Şifre saklanmaz
- ⚡ **WebSocket anlık baskı** - Sipariş gelince anında yazdır
- 🔔 **Masaüstü bildirimleri** - Site kapalıyken de bildirim al
- 🖨️ **Çoklu yazıcı desteği** - Mutfak/bar/kasa ayrı yazıcılar
- 📝 **Detaylı loglama** - Sorun çözmek kolay
- 🎨 **Modern arayüz** - Kolay kullanım
- 📦 **Self-contained** - Kurulum gerektirmez (.NET dahil)
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

## Lisans

© 2024 MenuBu - Tüm hakları saklıdır.
