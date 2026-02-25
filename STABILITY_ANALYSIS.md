# MenuBu Printer Agent V2 - Stabilizasyon Analizi

Bu doküman 25 Şubat 2026 tarihinde mevcut kod ve loglar üzerinden hazırlanmıştır.

## 1) Gözlenen Problemler

- Bazı makinelerde WebSocket bağlantısı sağlıklı çalışmıyor.
- WebSocket açıkken gelen job payload'larında parse hatası görülebiliyor.
- Rich (HTML) yazdırma başarısız olduğunda iş tamamen fail'e düşebiliyor.
- Sunucu tarafında farklı endpoint'lerden farklı payload şekilleri üretilebiliyor.
- Loglarda altyapı kaynaklı kesintiler var:
  - Redis bağlantı kopması
  - DB bağlantı hatası (`SQLSTATE[HY000] [2002]`)

## 2) Bu Turda Yapılan İyileştirmeler

### Agent (C#)

- Payload parse daha toleranslı hale getirildi.
- WebSocket job işleme UI thread'e güvenli taşındı.
- WebSocket hata yönetimi iyileştirildi:
  - `error` mesaj tipi işleniyor.
  - Hata sayısı eşik değeri aştığında (varsayılan 3), WS oturum için geçici kapanıyor.
  - Polling ile otomatik devam ediliyor.
- Ayarlara kullanıcı kontrollü seçenek eklendi:
  - `AutoDisableWebSocketOnErrors`
- Heartbeat akışı gerçek başarı/fail durumuna göre düzeltildi.
- Rich yazdırma başarısızsa fast fallback ile job kurtarılıyor.

### Sunucu (PHP + WS gateway)

- Payload normalizasyonu merkezileştirildi (`normalizePrintPayloadArray`).
- Queue ve print-jobs tarafında payload yapısı tutarlılaştırıldı.
- `queue-print-job.php` içinde payload erişim ve `order_ref` çözümleme hataları düzeltildi.
- WS gateway JSON limiti artırıldı ve token doğrulaması güçlendirildi.

### Dağıtım

- Inno Setup script'i ile kurulumlu paket (Program Ekle/Kaldır görünürlüğü) eklendi.

## 3) Neden Bu Yaklaşım

- WS'yi tamamen kapatmak yerine "önce dene, sorunlu makinede polling'e düş" modeli daha güvenli.
- Rich baskıyı tamamen kaldırmak yerine fallback ile başarı oranı artırılıyor.
- Payload'u tek normalize katmanına almak, platformlar arası uyumsuzlukları azaltıyor.

## 4) Kalan Riskler

- Windows ortamında tam derleme/test henüz yapılmadı (Linux ortamında `WindowsDesktop` hedefleri yok).
- DB/Redis kesintilerinde print queue davranışı için daha net retry ve alarm politikası gerekli.
- WS sunucusunda per-message idempotency/ack takibi şu an temel seviyede.

## 5) Önerilen Sonraki Sprint

1. **Windows E2E test matrisi**
   - Windows 10/11 + farklı yazıcı sürücüleri
   - WS açık/kapalı senaryoları
   - Rich fail -> fast fallback senaryosu

2. **Payload sözleşmesini kilitleme**
   - JSON Schema (v2) oluştur
   - API katmanında schema validate + anlaşılır hata mesajı

3. **Gözlemlenebilirlik**
   - Job bazlı correlation id
   - Agent log + server log eşleştirme
   - Kritik hata sayacı (WS parse fail, rich fail, heartbeat fail)

4. **Kurulum/operasyon**
   - Sürüm yükseltmede ayar migrasyonu testi
   - Sessiz kurulum parametreleri dokümantasyonu

5. **CI/CD release hattı**
   - Windows runner üzerinde build + smoke test
   - Setup paket üretimi
   - Otomatik release artifact

## 6) Başarı Kriteri

- Aynı job için yazdırma başarı oranı >= %99
- WS sorunlu makinelerde manuel müdahalesiz polling'e düşüş
- Rich yazdırma fail olduğunda fast fallback ile job kaybı olmaması
- Kurulumlu paketin tüm hedef makinelerde Program Ekle/Kaldır'da görünmesi
