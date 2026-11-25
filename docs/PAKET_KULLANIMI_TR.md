# Düzeltilmiş paketi kullanma

1. ZIP dosyasını yeni bir boş klasöre çıkarın. Önceki çalışan Hepta klasörünü silmeyin.
2. Yeni `Hepta` klasöründe adres çubuğuna `cmd` yazıp Enter'a basın.
3. Kendi güvenilir proje dosyalarınız için, gerekiyorsa, aşağıdaki komutla yalnızca bu projedeki scriptlerin internetten indirme işaretini kaldırın:

```cmd
pwsh -NoProfile -Command "Get-ChildItem -Path '.\scripts','.\installer' -Filter '*.ps1' -Recurse -File | Unblock-File"
```

4. Önceki başarılı testte kullanılan aynı yerel PDFium dosyasını kullanmak istiyorsanız (dosya bu konumda bulunuyorsa) şu komutu girin:

```cmd
set "HEPTA_PDFIUM_DLL=%USERPROFILE%\Desktop\Hepta\build\pdfium.dll"
```

5. `Build.cmd` çalıştırın. Yerel DLL yalnızca eski SHA256 kontrolünü geçerse kabul edilir; aksi halde resmi sabitlenmiş wheel doğrulanarak indirilir. İndirme geliştirici derlemesine aittir.
6. Yeni paket doğrulaması için `pwsh -NoProfile -File ".\scripts\check-package.ps1"` çalıştırın.
7. `release\Hepta-Setup.exe` dosyası oluşunca çalışan Hepta'yı kapatıp yeni kurucuyu çalıştırın. Masaüstü simgesi eski kalırsa önce yeni `build\Hepta.exe` simgesini farklı isimli bir kopyada kontrol edin.
8. GitHub ana depoya kaynakları; Releases bölümüne `Hepta-Setup.exe` ve `Hepta-Setup.sha256` dosyalarını ekleyin. `build` ve `release` klasörleri ana depoya dahil edilmemelidir.

Bu kaynak paketi Windows dışında düzenlendi; yeni Windows EXE veya kurulum paketi burada oluşturulmadı. Yayın öncesi yeni derleme ve gerçek kurulum testi gereklidir.
