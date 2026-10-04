# Yeni EF çeviri kuralı veya sağlayıcı profili ekleme

Önce [genel kural rehberini](adding-a-rule.md) ve [UO0001 kapsamını](rules/UO0001.md) okuyun.
Yeni bir unsupported overload aynı SQL uyumluluğu sorumluluğuna aitse UO0001 kataloğunu genişletir;
her overload için yeni diagnostic veya Code Fix gerekmez.

1. **Tam sembolü belirleyin.** Bildiren tip, instance/static biçimi, generic arity, dönüş tipi,
   parametre tipleri ve `RefKind` bilgilerini doğrulayın. `MethodSignature` ile compilation'dan gerçek
   `IMethodSymbol` çözümleyin. Yalnız invocation metni veya metot adı üzerinden eşleştirmeyin.
2. **Sağlayıcı ve sürümü doğrulayın.** Resmi dokümantasyonun yanında sabit release tag'indeki translator,
   SQL visitor ve translator kayıtlarını okuyun. Ortak relational çeviricilerini de kontrol edin.
   Dokümantasyonda görünmemek tek başına unsupported kanıtı değildir. Kanıt yoksa kataloğa eklemeyin.
   Yeni sürüm için ayrı profil tanımlayın; eski profilin doğrulama kapsamını sessizce genişletmeyin.
3. **Kataloğa ekleyin.** `TranslationRules.Create` içinde uygun imza tanımını ve doğrulanmış profil
   maskesini ekleyin. `UnsupportedMethodRule.Evaluate` ortak sözleşmesi seçili profiller içinden
   unsupported olanları döndürür. Casing ve comparison aileleri bunun iki gerçek örneğidir.
   Yeni veri girdisi analyzer callback'lerini, sorgu kaynağı takibini veya satır bağımlılığını değiştirmez.
   İlk gerçekten bağlama bağlı kural gerektiğinde yalnız bu değişkenlik noktasında dar bir değerlendirme
   sözleşmesi çıkarın; mevcut imza kurallarını aynı sözleşmeye uyarlayın. Her helper'a interface eklemeyin.
4. **Davranış testleri ekleyin.** Her profil için pozitif tam overload; komşu desteklenen/bilinmeyen overload;
   kullanıcı tipi/extension benzeri; captured ve bellek değerleri; nullable/conversion; tam diagnostic span
   ve arguments testlerini ekleyin. Profil birleşiminde yalnız sorunlu profillerin adlandırıldığını doğrulayın.
   Sorgu bağlamı genişletiliyorsa bağımsız expression/delegate ve materialization regresyonları da gerekir.
5. **Belgeleri ve release tracking'i güncelleyin.** UO0001 matrisine sabit kaynak bağlantılarını, sürümü
   ve sınırları ekleyin. Yeni profilin `.editorconfig` adını belgeleyin; gerekirse README örneklerini değiştirin.
   Descriptor/severity değişirse `AnalyzerReleases` biçimine uygun kaydedin; mevcut ID'yi yeniden kullanmayın.
6. **Doğrulayın.** README'deki restore, Release build/test/pack, paket layout ve consumer kontrollerini çalıştırın.
   Roslyn testleri SQL çeviri kabul testi değildir. Gerçek provider kabulünü ayrıca doğrulamadan doğrulanmış
   runtime davranışı olarak sunmayın. Analyzer assembly'sine EF runtime veya Workspaces bağımlılığı eklemeyin.
