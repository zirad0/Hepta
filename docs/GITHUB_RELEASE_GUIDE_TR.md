# Hepta sürümünü GitHub’da yayımlama

1. GitHub hesabınla giriş yap. Kaynağı yayımlamadan önce Hepta’nın lisansını seç ve Hakkında sayfasındaki Discord ile Buy Me a Coffee adreslerinin sana ait olduğunu doğrula.
2. GitHub Desktop’ta **File → New repository** ile bir depo oluştur. Hazırlanan kaynak ZIP’indeki dosyaları bu deponun köküne çıkar. README ile src klasörü aynı düzeyde olmalı. Alternatif olarak GitHub web sitesinde yeni depoyu oluşturup **Add file → Upload files** ile kaynakları yükleyebilirsin.
3. İlk değişiklik listesinde src, tests, assets, installer, scripts, docs ve .github klasörlerini kontrol et. Ayarlar, geçmiş, build ve release klasörleri ya da kişisel dosyalar Git değişikliklerinde yer almamalı.
4. GitHub Desktop’ta değişiklik özeti yaz, **Commit** ve ardından **Publish repository** seç. Görünürlüğü sen belirle. Web arayüzünde **Commit changes** kullan.
5. **Actions** sekmesindeki Windows derlemesini ve test sonuçlarını kontrol et. Temiz bir Windows 11 x64 ortamında çevrimdışı kurulum, kısayollar, gerçek dosya seçimi, yükseltme ve kaldırma kontrollerini bitir. Sınırlamalar için TEST_REPORT.md dosyasını incele.
6. **Releases → Draft a new release** seç. Doğruladığın commit üzerinde `v1.0.0` etiketi oluştur. Başlığı Hepta 1.0.0 yap, RELEASE_NOTES.md içeriğini ekle. Doğrulama eksikleri sürüyorsa taslağı sakla veya ön sürüm olarak işaretle.
7. **Attach binaries** alanına **Hepta-Setup.exe** ve **Hepta-Setup.sha256** ekle. Dosya boyutunu ve sağlama toplamını kontrol et. İmzalı veya SmartScreen tarafından doğrulanmış şeklinde tanıtma. Hazır olduğunda **Publish release** seç.
8. Sonraki sürümde yeni commit, yeni sürüm numarası, yeni etiket ve o derlemeye ait yeni sağlama toplamını kullan. Önceki sürümün kurucusunu başka bir dosyayla değiştirme.

Kaynak depo geliştirme içindir. Son kullanıcı Releases sayfasındaki kurucuyu indirir ve çalıştırır.
