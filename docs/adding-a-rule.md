# Yeni analyzer, Code Fix veya refactoring ekleme

UO0001 kapsamındaki yeni metot ve sağlayıcı desteği için
[EF çeviri kataloğunu genişletme rehberini](adding-ef-translation-support.md) izleyin.

## Önce özelliğin sahibi

- Şirket kuralı: `UO0001`, `UO0002`, … biçiminde yeni diagnostic ayırın.
- Mevcut Microsoft diagnostic'ine düzeltme: mevcut `CAxxxx`/`CSxxxx` ID'sini `FixableDiagnosticIds`
  listesine koyun. Aynı diagnostic için yeniden analyzer veya bir `UO` karşılığı üretmeyin.
- Kullanıcı seçiminden başlayan bağımsız refactoring: `CodeRefactoringProvider` kullanın;
  diagnostic ayırmanız gerekmez. `src/UO.Analyzers.CodeFixes/Refactorings/<Feature>/` altında tutun.

## ID ve descriptor

1. `src/UO.Analyzers/Diagnostics/DiagnosticIds.cs` içerisinde henüz kullanılmamış sıradaki ID'yi
   anlamlı bir sabit olarak ekleyin: `public const string MeaningfulRule = "UO0001";`.
2. ID'yi silindikten sonra dahi başka bir kurala vermeyin. Kuralın anlamını değiştiren değişiklikte
   yeni ID değerlendirin. CA1848 bu numaralandırmanın parçası değildir.
3. `DiagnosticCategories` içinden uygun kategori seçin: `UO.Performance`, `UO.Reliability`,
   `UO.Design`, `UO.Logging`, `UO.Usage`.
4. Descriptor'ı ilgili analyzer içinde `internal static readonly DiagnosticDescriptor` olarak
   doğrudan oluşturun. Henüz descriptor factory/registry abstraction'ı gerekmez.
5. Başlık, açıklama, message arguments, default severity ve enabled-by-default kararını açıkça verin.
   Kullanıcının sorunu anlamasını ve ne yapacağını söyleyen metin kullanın. Lokalizasyon gerektiğinde
   `.resx` ve `LocalizableResourceString` kullanın. Gerçek dokümantasyon URL'si varsa `helpLinkUri` ekleyin.
6. `AnalyzerReleases.Unshipped.md` dosyasına kuralı ekleyin:

```markdown
### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
UO0001 | UO.Usage | Warning | MeaningfulRuleAnalyzer
```

Sürüm yayımlarken tabloyu `AnalyzerReleases.Shipped.md` dosyasındaki `## Release x.y.z`
bölümüne taşıyın. Değişen/çıkarılan kuralları Roslyn release tracking biçimiyle kaydedin.
CA1848 için bu dosyalara yeni diagnostic eklenmez.

## Analyzer uygulaması

Dosya: `src/UO.Analyzers/Rules/UO0001MeaningfulRule/MeaningfulRuleAnalyzer.cs`.

```csharp
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class MeaningfulRuleAnalyzer : DiagnosticAnalyzer
{
    internal static readonly DiagnosticDescriptor Rule = new(
        DiagnosticIds.MeaningfulRule,
        "Actionable title",
        "Actionable explanation for '{0}'",
        DiagnosticCategories.Usage,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        // Register the narrowest syntax/operation callback required by this rule.
    }
}
```

Bu örnek bir şablondur; gerçek callback, semantik kontrol ve testler olmadan kural tamamlanmış sayılmaz.

- Gerekli framework tiplerini `RegisterCompilationStartAction` içinde bir kez çözümleyin.
- Method/type isimleriyle tek başına karar vermeyin; sembolleri `SymbolEqualityComparer.Default` ile karşılaştırın.
- Operasyon ağacının conversion/overload bilgisini kullanın; expression tipini syntax'tan tahmin etmeyin.
- Analyzer instance'ında compilation, SemanticModel veya mutable global state tutmayın.
- Generated code politikasını açıkça belirleyin. Varsayılan yeni kural politikası analiz etmemektir.
- Callback'teki CancellationToken'ı sorgulara iletin, uzun döngülerde cancellation kontrol edin.
- Diagnostic'i IDE'nin kullanıcıya göstereceği dar ve anlamlı konumda raporlayın.
- Compiler assembly'sine Workspaces/MEF bağımlılığı eklemeyin. Ortak helper yalnız gerçek tekrar varsa çıkarılır.

## Code Fix

Dosya: `src/UO.Analyzers.CodeFixes/<Feature>/MeaningfulRuleCodeFixProvider.cs`.

- `[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(...)), Shared]` ile export edin.
- `FixableDiagnosticIds` doğru sahibin ID'sini döndürsün. UO sabitlerini paylaşmak gerekirse
  CodeFixes projesinden Analyzers projesine reference ekleyin; sabitleri public yapma kararını sınırlı tutun.
- Diagnostic span'i token, member name veya invocation'ın tamamı olabilir. Syntax ancestor'ını
  bulduktan sonra semantic eligibility'yi yeniden doğrulayın.
- Geçersiz/eksik syntax, unresolved sembol veya güvenli desteklenmeyen overload'da action kaydetmeyin.
- Eligibility ve transformation'ı ayırın; CA1848 için `LoggingInvocationAnalyzer` uygunluğu doğrular,
  `LoggingInvocation` doğrulanan çağrı verisini taşır. `LoggerMessageNaming` isimlendirmeyi,
  `LoggerMessageSyntaxFactory` syntax üretimini, `LoggerMessageDocumentRewriter` belge düzenlemesini
  üstlenir. Provider yalnız Code Action kaydını yönetir.
- Metot adı yapılan işlemi, hedefini ve varsa yan etkisini anlatsın. Örneğin
  `ReplaceExtensionCallWithSourceGeneratedLoggingAsync` dönüşümün amacını,
  `ReserveUniqueIdentifier` ise dönen ismin aynı zamanda ayrıldığını belirtir.
  Bir metotta farklı sorumluluklar birikiyorsa yalnız adını uzatmak yerine bu sorumlulukları ayırın.
- Sabit, lokalize edilebilir kullanıcı başlığı ve kararlı equivalence key kullanın.
- `DocumentEditor`, `SyntaxGenerator`, `SyntaxFactory` ile node üretin. Kod üretmek için string birleştirme
  veya `ParseStatement`/`ParseMemberDeclaration` hack'leri kullanmayın.
- Fully-qualified sembol node'ları üretin; `ImportAdder`, `Simplifier` ve annotation tabanlı `Formatter`
  ile gerekli bölgeleri düzenleyin. Çakışmalarda qualification kalsın.
- Invocation evaluation sırası, implicit conversion, yan etkiler, trivia ve surrounding scope'u test edin.
- Aynı fix tekrar uygulandığında ve diğer partial dosyalarda çakışma olmadığını kontrol edin.
- BatchFixer yalnız bağımsız ve birleşebilir edit'lerde kullanılır. Ortak member insertion/naming
  yapan CA1848 fix'i bilinçli olarak `GetFixAllProvider() => null` döndürür.

## Testler

`tests/UO.Analyzers.Tests/Analyzers/` ve `CodeFixes/` altında gerçek davranışa odaklanan testler ekleyin.
`Infrastructure.Verifiers` xUnit ile kullanılabilen Roslyn `AnalyzerTest<T>` ve `CodeFixTest<TAnalyzer,TCodeFix>`
yardımcılarını sağlar. `DefaultVerifier` kullanılır; eski XUnit verifier adapter paketi gerekmez.

En az şunları denetleyin:

1. Beklenen diagnostic ID, severity, location ve arguments.
2. Geçerli kodda diagnostic/action olmaması; benzer isimli ilgisiz semboller.
3. Tam beklenen fixed source ve derlenebilirlik; gerekiyorsa gerçek source generator.
4. Nullable, generic, namespace, nested type ve farklı partial dosyalar.
5. Tekrarlı fix ve identifier/using/member çakışmaları.
6. Unsupported syntax/overload/type, cancellation ve generated code politikası.
7. Dönüşümün yan etkileri varsa derlenmiş kodla davranış testi.

Microsoft diagnostic'ine bağlanırken en az bir testi gerçek Microsoft analyzer'ıyla çalıştırın.
`MicrosoftLoggingCodeFixTest` tam source karşılaştırması yapar. `LoggingTestHost` eligibility
matrisini ve generator derlemesini çalıştırır. Sentetik diagnostic yalnız gerçek analyzer'ın
raporlamadığı unsupported konumlarda provider'ın savunmalarını sınamak için kullanılır.

## Paket ve kabul

```bash
dotnet restore
dotnet build
dotnet test
dotnet pack -c Release
python3 scripts/verify-package.py artifacts/packages/UO.Analyzers.1.0.0.nupkg
```

[Geliştirme rehberindeki](development.md#local-validation) consumer restore/build kontrolünü de çalıştırın. Paket layout'unda yalnız iki ürün DLL'i
`analyzers/dotnet/cs/` altında bulunur; `lib/` ve `runtimes/` asset'i, runtime dependency ve Roslyn DLL'i
bulunmaz. Host bağımlılığı eklenmesi IDE loading kabul testini gerektirir.

Sürüm `Directory.Build.props`, paket bilgisi packaging projesi, SDK sürümü `global.json`, NuGet
sürümleri `Directory.Packages.props` üzerinden yönetilir. Roslyn taban sürümünü yükseltmek minimum IDE
host sürümünü de yükseltir; consumer hedef framework'üyle karıştırmayın.

Son olarak desteklenen IDE'de kurulum, diagnostic görünürlüğü, Quick Fix preview/application ve
yeniden derlemeyi manuel doğrulayın. CI derlemesi IDE discovery kabulünün yerine geçmez.
