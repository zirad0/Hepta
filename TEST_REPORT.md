# Hepta 1.0.0 — doğrulama raporu

**25 Kasım 2026 · Windows 11 x64 26H2, yapı 26300.** Release derlemesinde **158 otomatik senaryodan 157’si geçti, 0’ı başarısız oldu, 1’i erişim kısıtı nedeniyle atlandı.** İki paket kontrolü bu sayıdan ayrıdır. Windows Server tabanlı GitHub Actions koşusu yereldeki Windows 11 koşusundan farklıdır ve GitHub üzerinde çalıştırılmadı.

| Alan | Yapılan doğrulama |
|---|---|
| Raster, SVG ve ICO | Format imzası, yeniden açılabilirlik, boyut, renk, alfa, yedi ICO boyutu, renk alanlarının dış/iç sınırları ve onaylı menü SVG’sinin arka planda çizimi. WebP’de tam renk alfabesi ve yarı saydamlık sınandı. |
| PDF ve metin | Türkçe vektör metni taşıyan PDF üretildi, bağımsız Windows okuyucusunda açıldı. PDFium ile birleştirildi ve bölündü. Her parçadan İstanbul kelimesi tekrar çıkarıldı. Sayfa sayısı, aralık sıralama, geçersiz noktalar ve görüntü dışa aktarma sınandı. 12 × 8 punto sayfa 192 DPI’da 32 × 22 piksel çıktı verdi. |
| Tablo ve yapılandırılmış veri | XLSX/ODS/BIFF8 fixture, ayraçlar, Unicode, kaçışlar, satır içi yeni satır, boş kayıt, baştaki sıfırlar, güvenli formül reddi, iç içe JSON/YAML, SRT/VTT zaman damgaları. |
| Ses ve video | WAV sinyali ve ses izli MP4 klibi üretildi. Etkin kaynak–hedef matrisinde yeniden kodlama, dosya başlığı, yeniden açma, süreler, ses ve video ekleme sınandı. FLAC çıktısı WAV’a geri çözülerek ölçüldü. AVI, WMV, MP4 ile WAV, MP3, FLAC, WMA ve M4A test edildi. |
| Arşiv ve dosya güvenliği | ZIP/TAR/7Z/GZ, RAR4 okuma, Unicode, alt klasör, zararlı göreli yol reddi, çıktı hedefi girdi klasörü içindeyken kendini arşive eklememe, bozuk ve kilitli dosya, uzun Windows yolu, kaynak değişirken yayımı durdurma, eşzamanlı ad çakışmaları, iptal ve geçici çıktı temizliği. |
| Kısmi çıktı | Birleştirilebilir dosya yanında geçersiz dosya üretildi. Başarılı/başarısız listeleri ve bekleme kaydı incelendi. JSON’a yazılıp tekrar açılan kararda Sakla ve ayrı koşuda Sil sınandı. |
| Arayüz | 12 ana sayfa WPF ile oluşturuldu ve düzenlendi. Genel sayfa çalışan kontrollerden PNG’ye çizildi. V3 geometrisi ve yerelleştirme anahtarı eşitliği kontrol edildi. Ayar kaydı, eski şema geçişi, bozuk ayar reddi, kısayolları takas etme ve çakışmada geri alma sınandı. |
| Çoklu monitör | Gerçek, görünmez test penceresi üç bağlı monitörün çalışma alanlarının iki karşı köşesine yerleştirildi. Windows HWND koordinatlarıyla sınır kontrolü geçti. Sol ekranın X koordinatı −1080, sağ ekranın X koordinatı 3840 idi. |
| Kurucu | Setup EXE içindeki MSI açıldı, iç CAB çıkarıldı. Hepta.exe ve pdfium.dll SHA256 değerleri derlemedeki dosyalarla eşleşti. Çıkan Hepta.exe kendi geçici verileriyle 12 sayfayı açtı, metin dönüştürdü ve iki sayfalı PDF üretti. Kurucu SHA256 dosyası doğrulandı. |

## Ölçülen performans

Ölçüm ayrı bir x64 süreçte, ana pencere gizli ve tepsi simgesi etkin durumdayken alındı. İki saniye ısınmanın ardından **5,015 saniyede ölçülen CPU süresi 0 ms** ve anlık çalışma kümesi **86.732.800 bayt (82,7 MiB)** idi. CPU değeri işletim sisteminin sayaç çözünürlüğüyle sınırlıdır.

Altı eylemli radyal WPF bileşeni iki ısınma turu sonrasında 40 kez oluşturuldu, ölçüldü ve yerleştirildi. **Medyan 2,468 ms, 95. yüzdelik 3,010 ms**. Bu süreye Gezgin seçimi, global tuş teslimi, fiziksel ekran sunumu ve açılış animasyonu dahil değildir.

## Doğrulanamayanlar ve kalan işler

- Gerçek global kısayol kaydı bu yönetilen oturumda Win32 hata 5 döndürdü. Otomatik çakışma testi bu nedenle **atlandı**. Ayrı, deterministik kayıt/geri alma testi geçti. Shell COM’a dışarıdan erişim de engellendi. Masaüstü, çok sekmeli Gezgin, uygulamalar arası odak, ikinci örnek ve gerçek kısayol zinciri normal oturumda doğrulanmalıdır.
- Windows Installer hizmetine yönetilen oturumdan bağlantı reddedildi. MSI yönetimsel çıkarma denemesi **1601** döndürdü. Gerçek kurulum, onarım, sürüm yükseltme, otomatik başlangıç ve kaldırma için temiz Windows 11 VM testi bekliyor. Paket içinden çalıştırma bu testi karşılamaz.
- Gerçek monitör yerleştirmesi kontrol edildi. Fiziksel monitörü çalışırken sökme, her tema/dil için görsel karşılaştırma, birleşik pencere etkileşimleri, düşük disk alanı, tüm parola/codec varyantları ve her bildirim zamanlaması uçtan uca doğrulanmadı.
- Türkçe/İngilizce arayüz metinleri mevcut. Bazı teknik motor ve sistem hata ayrıntıları kendi dillerinde görünür. Desteklenmeyen biçimler, belge alt kümeleri ve Windows codec bağımlılıkları SUPPORTED_FORMATS.md içinde listelidir.
- Kısmi kararlar ön kontrolde dışlanan girdiler için çalışır. Yayım aşamasında beklenmeyen hata oluşursa iş başarısız olur ve geçici çıktı temizlenir. Çalışan işlerin sistem kapanışından sonra kaldığı yerden sürdürülmesi desteklenmez.

## Yeniden çalıştırma

```powershell
./scripts/build.ps1 -Test -Measure -Installer
./scripts/check-package.ps1
```

Temiz sistem testleri, lisans seçimi ve destek adresi kontrolü bitene kadar bu sürüm yayın adayıdır.

Kaynak teslim ZIP’i ayrı, boş bir klasöre çıkarıldı. Aynı sabitlenmiş PDFium DLL’iyle çevrimdışı derleme, 157 başarılı test ve 1 erişim engelli test sonucu yeniden elde edildi. Yeniden üretilen kurucunun içindeki dosyalar karşılaştırıldı ve paketlenmiş EXE testi geçti.


## Paket güncellemesi

Şeffaf ikon ve kaynak paket düzeni değiştirildi. Yukarıdaki test sonuçları önceki derlemeye aittir; bu değişikliklerin ardından Windows derlemesi, testler ve kurulum doğrulaması yeniden yapılmalıdır.
