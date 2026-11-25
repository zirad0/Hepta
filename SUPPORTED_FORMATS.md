# Desteklenen biçimler

Her yönlü kaynak–hedef çifti bir kez sayılır. JPG/JPEG, TIFF/TIF, Markdown/MD ve YAML/YML eşanlamlıdır. Aynı GIF’ten tek PNG ya da bir PNG kare dizisi almak aynı biçim çiftidir. Arşivleme, birleştirme, bölme ve pano işlemleri toplamdan ayrıdır.

| Kaynak | Hedef | Çift sayısı |
|---|---|---:|
| PNG, JPG, BMP, TIFF | Diğer üç raster biçimi, WebP, ICO, SVG, PDF | 28 |
| GIF | PNG, JPG, BMP, TIFF, ICO, SVG, PDF, WebP, MP4 | 9 |
| ICO | PNG | 1 |
| WebP | PNG, JPG, BMP, TIFF, ICO, SVG, PDF | 7 |
| AVIF | PNG, JPG, BMP, TIFF, ICO, SVG, PDF, WebP | 8 |
| SVG | PNG, JPG, PDF | 3 |
| PDF | PNG, JPG, TXT | 3 |
| TXT, Markdown | HTML, PDF | 4 |
| CSV | TSV, XLSX, JSON | 3 |
| TSV | CSV, XLSX, JSON | 3 |
| XLSX, XLS, ODS | CSV, TSV | 6 |
| JSON, YAML | Birbirine | 2 |
| SRT, VTT | Birbirine | 2 |
| MP3, WAV, M4A, FLAC, WMA | Gruptaki diğer dört biçim | 20 |
| MP4, WMV, AVI | Gruptaki diğer iki video biçimi ve beş ses biçimi | 21 |
| **Toplam** | | **120** |

## Davranış ve sınırlar

- Tek kareli görüntüler raster hedeflere dönüştürülür. SVG tracing komşu renk alanlarının dış ve iç sınırlarını yol olarak yazar. Fotoğraflar nicemlenir. GIF kareleri ayrı PNG olarak çıkarılabilir veya MP4’e dönüştürülebilir. ICO oluştururken yedi boyut yazılır. ICO → PNG en büyük kareyi seçer. JPEG/PDF görüntülerinde beyaz zemin kullanılır.
- WebP çıktısı yerel VP8L kodlayıcıyla kayıpsız üretilir. WebP ve AVIF okuma, Windows görüntü bileşeninin gömülü örnek dosyayı gerçekten açabilmesine bağlıdır. Bileşen yoksa ilgili giriş işlemleri devre dışı kalır.
- Yerel SVG okuyucu yolları, temel şekilleri, düz metni, grupları, dönüşümleri ve desteklenen gradyanları çizer. Filtre, dış kaynak, script, DTD ve desteklenmeyen öğeler reddedilir. Tarayıcı düzeyinde SVG ve CSS kapsamı yoktur.
- PDF → PNG/JPG tüm sayfaları numaralandırır. PDF → TXT gerçek metin katmanı gerektirir. PDFium bölme ve birleştirmede PDF sayfalarını doğrudan içe aktarır. Seçilebilir metnin korunduğu fixture ile doğrulandı. Görsel girdiler için JPEG tabanlı yeni sayfa üretilir. Dijital imzalar, belge yer imleri ve etkileşimli form davranışı ayrıca kontrol edilmelidir.
- Sade, tek sayfalı çalışma kitapları desteklenir. XLS için BIFF8 Workbook akışı gerekir. Formüller, birleşik hücreler, desteklenmeyen sayısal biçimler ve bazı karmaşık BIFF kayıtları reddedilir. CSV/TSV → XLSX hücreleri metin yazar. JSON çıktısı başlık satırı dahil dizi dizisidir. Hesap tablosu formülüne benzeyen girdiler CSV/TSV dışa aktarımında reddedilir.
- YAML desteği iç içe eşleme, liste ve temel skalerlerle sınırlıdır. Etiketler, nesne kurucuları, çapalar ve çok satırlı blok sözdizimi desteklenmez. Markdown HTML çıktısı başlık, liste, kalın yazı ve kodu destekler. Markdown PDF’de başlıklar büyütülür. Metin PDF’inde mevcut yazı tipinin desteklemediği karakter reddedilir.
- Medya desteği Windows kodlayıcı ve çözücülerine bağlıdır. Testler belirli ve yeniden üretilebilir akışları kapsar. Aynı uzantıdaki her codec, profil, altyazı, çoklu ses izi ve metadata korunmaz. Videodan ses çıkarmak bir ses izi gerektirir.
- Dosya ve klasörlerden ZIP/TAR/7Z oluşturulur. ZIP/TAR/7Z çıkarılır. RAR okuması, sıkıştırılmamış RAR4 örneğiyle doğrulandı. Parolalı arşiv veya RAR oluşturma desteği yoktur. GZ tek dosyayı sıkıştırır/açar. TAR/7Z/RAR, uygun Windows arşiv bileşenine bağlıdır.
- Ses, video, PDF ve TXT birleştirme, PDF bölme, ad ve yol kopyalama ayrıca desteklenir. ZIP/GZ/TAR/7Z/RAR işlemleri ve birleştirme türleri yukarıdaki 120 çift sayısına dahil değildir.

**Etkinleştirilmeyen hedefler** arasında AVIF yazma, HEIC, MKV, MOV, WebM, FLV, MPEG, ham AAC, OGG ve Opus bulunur. Başka bir kapsayıcıya aynı uzantıyı takarak dönüşüm yapılmaz. OCR, PDF → Office ve e-kitap işlemleri bu sürümde sunulmaz.
