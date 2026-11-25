# Hepta — Arayüz tasarım kılavuzu

Marka ve menü kaynakları `assets/Hepta.png`, `assets/Hepta.ico`, `assets/Hepta.svg` ve `assets/Hepta_Menu_V3.svg` dosyalarındadır.

## Kesin geometri

- SVG'deki `center-heptagon` yolunu ve `petal-1`–`petal-7` yollarını esas al. Tam 7 kenarlı merkez; üstte tek köşe, altta yatay düz kenar. Yumuşak köşeler. 
- Eşit açılar: 360/7 derece. Alt orta yaprak Diğer; sağda üç, solda üç işlem yaprağı. Üst ortada ek bir yaprak bulunmaz. Yapraklar birbirine yakın ve dış kenarları yuvarlak.
- Diğer ve 1/3 gibi sayfa göstergesi aynı alt yaprağın içinde. Sayı gerçek sayfa durumuna bağlıdır.
- SVG görsel bir şablondur; WPF Path/Geometry, dönüştürmeler ve bağımsız hit-test bölgeleri ile gerçek etkileşimli bileşene dönüştür. Menüye tek bir PNG olarak tıklanıyormuş gibi davranma. Etiketleri yatay tut; dil, ölçek ve metin boyutu değişikliklerini destekle.
- Antrasit ve sıcak altın renkleri koru; abartılı parlama, dekoratif dalgalar, opak dikdörtgen arka plan ekleme. Ana pencere bir PDF editörü veya medya oynatıcı değildir.
- SVG'nin örnek metinleri/formatları yalnızca örnektir, uyumluluk kaydı belirleyicidir.

## Boyutlar ve etkileşim

- Ana uygulama referansları 1600×1000 piksel; bu pencereyi zorunlu sabit piksel boyutunda çalıştırma. DIP, DPI, minimum kullanılabilir boyut ve gerektiğinde kaydırma ile duyarlı düzen kur.
- Menü için 320 DIP civarında ayarlanabilir başlangıç boyutu uygundur; gerçek çizim ve gölgeler dahil monitör çalışma alanına sığdır. Varsayılan animasyon 250 ms.
- PDF bölme ~320×234 DIP; birleştirme ~340×294 DIP (içerik sayısına göre yükseklik); JPG ~320×230 DIP; MP4 ~340×318 DIP. Bildirimler ~340 DIP genişlik. İlk açılış ~430×445 DIP, kurulum ~560×410 DIP. Bunlar statik referans ölçüleridir; okunabilirlik ve içerik gerektirdiğinde uyarlanabilir.
- Dosya adı: Sabit maksimum karakter sayısı kullanılmaz. Mevcut genişlik ve gerçek font ölçülerine göre mümkün olan en uzun başlangıcı göster. Sığıyorsa tam adı; sığmıyorsa başlangıç + üç nokta/ellipsis + tam uzantı. Tek satır. 500 ms hover sonrası tam ad. Pencere boyutu ve font değişiminde yeniden hesapla. Örnek kısaltmaları sabit kodlama. Aynı davranışı bölme, birleştirme, bildirim, kuyruk, geçmiş ve işleme özel ayarlarda uygula. Çok uzun uzantı/erişilebilirlik durumlarında tam adı tooltip ile mutlaka erişilebilir tut.
- Birleştirme satırında yalnızca tutamaç sürükleme başlatır; ada tıklamak dosyayı açar ve listeyi kapatmaz. Sayı ve uzantı görünür.
- Başarı bildirimi 5 saniye, hata 10 saniye, ilerleme gerçek iş bitene kadar. Kısmi karar otomatik kaybolmaz. X ile gizlense bile karar kuyrukta kalır. Başarılı/başarısız dosyalar ayrıntıda gösterilir.
- Kurulumun görünümü seçilen ücretsiz kurulum motorunun sınırlarına göre uyarlanabilir. Gerçek kurulumu ve okunabilirliği feda etme.

Logo referansı assets/Hepta.png dosyasıdır. Uygulama simgesi assets/Hepta.ico, vektör logo kaynağı assets/Hepta.svg altında bulunur.
