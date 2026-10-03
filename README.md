# UO.Analyzers

C# Roslyn analyzer, diagnostic, Code Fix ve refactoring'leri için ortak repository.
İlk özellik, **Microsoft CA1848** diagnostic'ine **Convert to source-generated LoggerMessage**
Code Action'ını ekler. CA1848 analyzer'ı yeniden yazılmaz; diagnostic Microsoft'un .NET analyzer'ından gelir.
Bu sürüm henüz `UO000X` kuralı yayımlamaz.

## Kurulum

Paketi NuGet.org'a yayımladıktan sonra consumer projeye ekleyin:

```xml
<PackageReference Include="UO.Analyzers"
                  Version="1.0.0"
                  PrivateAssets="all" />
```

Sürümü yayımlanan `x.y.z` ile değiştirin. Lokal deneme için `artifacts/packages` klasörünü
NuGet source olarak kullanabilirsiniz. Repository oluşturulması paketi herhangi bir feed'e yayımlamaz.

### NuGet.org'a otomatik yayımlama

Yayımlama **Trusted Publishing (GitHub OIDC)** kullanır; GitHub'a kalıcı NuGet API key
veya `NUGET_API_KEY` repository secret'ı eklemek gerekmez.

NuGet.org'daki Trusted Publishing kaydı şu değerlerle eşleşmelidir:

| Alan | Değer |
| --- | --- |
| Package owner / NuGet kullanıcı adı | `uozturk` |
| Publisher | `GitHubActions` |
| Repository Owner | `ugurozturk` |
| Repository | `UO.Analyzer` |
| Workflow | `publish-nuget.yml` (yalnız dosya adı) |
| Environment | Boş; workflow GitHub Environment kullanmıyor |
| Scopes | `Push new packages and package versions` |
| Glob pattern | `UO.*` (`UO.Analyzers` paketini kapsar) |

1. NuGet.org hesabınızda bu Trusted Publishing kaydını oluşturun veya mevcut kaydı kullanın.
2. Repository'nin varsayılan branch'i `main` olmalı; iki workflow dosyası da bu branch'te bulunmalı.
3. `Directory.Build.props` içindeki `Version` değerini yayımlamak istediğiniz sürüme ayarlayıp
   `main` branch'ine push edin.

`build-test-pack` build, test, paket yapısı ve consumer kontrollerini çalıştırır.
Başarılı tamamlanınca ayrı `publish-nuget` workflow'u, **aynı CI çalışmasının artifact'ındaki**
paketi NuGet.org'a gönderir; paketi yeniden derlemez. Pull request, başka branch veya başarısız
CI çalışmaları yayımlama yapmaz. `id-token: write` izniyle `NuGet/login@v1`, `uozturk`
kullanıcısı adına GitHub OIDC token'ını NuGet'te geçici API key ile değiştirir. Bu anahtar
yalnız yayımlama adımına aktarılır; kalıcı secret okunmaz.

Sürüm otomatik artırılmaz. Aynı sürüm zaten NuGet.org'da varsa `--skip-duplicate` ile atlanır;
yeni kodu dağıtmak için `Version` değerini artırın. Trusted Publishing kaydının alanları
eşleşmezse, kayıt pasifse veya paket sahipliği uygun değilse yayımlama başarısız olur.
Kaydı düzelttikten sonra `publish-nuget` çalışmasını yeniden çalıştırabilirsiniz;
ilgili CI artifact'ı hâlâ mevcut olmalıdır.

NuGet'te `Use within 7 day(s) to keep it permanently active` görünüyorsa kayıt geçici
olarak aktiftir. Bu süre içinde ilk başarılı yayımlama kaydı kalıcı olarak etkinleştirir.
Süre dolarsa NuGet'teki `Activate for 7 days` ile yeniden etkinleştirip yayımlamayı tekrarlayın.

Kaynaklar: [NuGet Trusted Publishing](https://learn.microsoft.com/en-us/nuget/nuget-org/trusted-publishing),
[NuGet/login action](https://github.com/NuGet/login).

Consumer, `Microsoft.Extensions.Logging.Abstractions` ve onun logging source generator'ını
içermelidir; framework/package üzerinden zaten geliyorsa tekrar eklemeye gerek yoktur.
CA1848'in görünür olması için consumer `.editorconfig` dosyasında:

```ini
[*.cs]
dotnet_diagnostic.CA1848.severity = warning
```

.NET SDK analyzer'larının etkin olduğundan emin olun (`EnableNETAnalyzers=true`).
UO.Analyzers bu ayarı consumer adına değiştirmez ve Microsoft analyzer'ını paketine kopyalamaz.
Paket runtime dependency değildir: DLL'ler yalnızca `analyzers/dotnet/cs/` altında bulunur;
consumer'ın runtime çıktısına kopyalanmaz.

Analyzer ve Code Fix assembly'leri **netstandard2.0 / Roslyn 4.14** hedefler.
IDE host'unun Roslyn 4.14 veya daha yeni bir sürüm kullanması gerekir; bu taban sürüm daha
geniş IDE uyumluluğu için bilinçli seçilmiştir. Consumer için C# 9+ gerekir. Otomatik testlerin
referans ortamı .NET 10 ve Logging.Abstractions 10.0.5'tir; diğer IDE/generator sürümleri
ayrıca kabul testinden geçirilmelidir. IDE'nin NuGet CodeFixProvider keşfini desteklemesi gerekir.

## CA1848 dönüşümü

Before:

```csharp
public class OrderService
{
    private readonly ILogger<OrderService> _logger;

    public OrderService(ILogger<OrderService> logger) => _logger = logger;

    public void Process(int orderId)
    {
        _logger.LogInformation(
            "Processing order {OrderId}",
            orderId);
    }
}
```

After:

```csharp
public partial class OrderService
{
    private readonly ILogger<OrderService> _logger;

    public OrderService(ILogger<OrderService> logger) => _logger = logger;

    public void Process(int orderId)
    {
        LogProcessingOrder(_logger, orderId);
    }

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Processing order {OrderId}",
        SkipEnabledCheck = true)]
    private static partial void LogProcessingOrder(ILogger logger, int orderId);
}
```

Mevcut `LoggerExtensions` çağrısı `ILogger.Log` metodunu seviyenin açık olup olmadığına
bakmadan çağırır. `SkipEnabledCheck = true` bu davranışı korur; uygulamanızda isteniyorsa
seviye kontrolü ayrıca tasarlanabilir. Receiver, exception ve payload expression'ları
özgün sırayla, birer kez değerlendirilir. Bu nedenle exception parametresi payload'lardan
**önce** kalır:

```csharp
LogProcessingFailed(logger, exception, orderId);

[LoggerMessage(Level = LogLevel.Error, Message = "Processing {OrderId} failed", SkipEnabledCheck = true)]
private static partial void LogProcessingFailed(ILogger logger, Exception? exception, int orderId);
```

**Event metadata:** standart source-generated logging'e geçişle generator, method isminden
EventId/EventName üretir. Önceki çağrıdaki varsayılan `0`/isimsiz event korunmaz.
Tüm method'lara `EventId = 0` yazmak tekrarlı ID uyarılarına yol açtığından böyle bir değişiklik
yapılmaz. Event metadata'ya göre filtreleme yapan uygulamalar dönüşümü buna göre değerlendirmelidir.
Mesajın içeriği, placeholder casing/format/alignment bilgisi ve exception korunur.

### Desteklenenler

- `LogTrace`, `LogDebug`, `LogInformation`, `LogWarning`, `LogError`, `LogCritical`.
- `ILogger`, `ILogger<T>`; field, property, parameter ve yan etkili receiver expression'ları.
- Sabit mesajlar, sıfır veya çok sayıda structured argüman, exception overload'u.
- Semantik tipler: nullable, array, tuple, containing class'a ait generic tip parametreleri.
- `OrderId → orderId`, `URL → url`, keyword için `@class`; format ve alignment korunur.
- En yakın class'a method ekleme; nested class ve dış class'lara gerektiğinde `partial` ekleme.
- File-scoped/block namespace, mevcut partial class, farklı partial dosyalardaki üye çakışmaları.
- Method/overload, local symbol, generator backing member ve explicit EventName çakışmalarından kaçınma.
- Güvenli import ekleme, Simplifier ve Formatter; aynı isimli tiplerde qualification korunur.

### İlk sürüm sınırları

Güvenli eşleme yapılamıyorsa Code Action sunulmaz:

- Runtime/interpolated mesajlar (sabit değer olarak çözülemeyenler), dynamic çağrılar.
- EventId overload'ları, static `LoggerExtensions.LogInformation(...)` biçimi, named argümanlar.
- Explicit `params` array/null, mesaj/payload sayısının uyuşmaması, bozuk template.
- Tekrarlı placeholder'lar (case-insensitive dahil): kaynak logging her occurrence için ayrı değer
  alır, generator ise isme göre eşler. Değerleri birleştirmek veya yeniden adlandırmak log semantiğini değiştirir.
- C# identifier'ına eşlenemeyen, `_` ile başlayan, numeric veya destructuring placeholder'ları.
- Structured payload olarak Exception/ILogger/LogLevel; generator bu tipleri özel yorumlar.
- Anonymous, dynamic, ref-like, pointer veya generic method'a ait tipler.
- Conditional access, expression-bodied çağrılar, expression tree, top-level, struct/record/interface/file-local type.
- Containing type içerisinde preprocessor directive bulunan durumlar.
- Fix All: ortak type ve method isimlerini değiştiren edit'ler için BatchFixer kullanılmaz.

Sabit yerel mesaj değişkenleri attribute'a literal olarak taşınır; artık kullanılmayan local const
varsa consumer'ın normal unused-variable diagnostic'i görünebilir. Kaynakta mevcut hatalı kod
ve generator'ın consumer tarafından devre dışı bırakılması ayrıca düzeltilmelidir.

## Yapı

```text
src/UO.Analyzers/                  # Diagnostics, Rules, paylaşılan compiler yardımcıları
src/UO.Analyzers.CodeFixes/        # Logging ve IDE tarafı yardımcılar; ileride Refactorings/
tests/UO.Analyzers.Tests/          # Roslyn verifier, gerçek analyzer/generator ve runtime testleri
tests/PackageConsumer/            # Paket üretildikten sonra çalışan ayrı consumer smoke projesi
packaging/UO.Analyzers.Package/    # Tek dağıtım paketi
docs/adding-a-rule.md             # UO000X analyzer + Code Fix geliştirme rehberi
scripts/verify-package.py         # NuGet layout ve consumer runtime asset kontrolü
UO.Analyzers.slnx
```

`UO.Analyzers` yalnız compiler API'lerine bağımlıdır. Code Fix tarafı Workspaces/MEF kullanır.
Roslyn, System.Composition ve test dependency'leri NuGet paketine taşınmaz; IDE kendi host
assembly'lerini sağlar. Gelecekte Code Fix'ler ortak diagnostic sabitlerine ihtiyaç duyarsa
CodeFixes → Analyzers project reference eklenebilir; ters bağımlılık kurulmaz.

## Geliştirme ve doğrulama

`global.json` ile sabitlenen .NET 10 SDK ve paket kontrolü için Python 3 gerekir:

```bash
dotnet restore
dotnet build
dotnet test
dotnet pack -c Release
python3 scripts/verify-package.py artifacts/packages/UO.Analyzers.1.0.0.nupkg
dotnet restore tests/PackageConsumer/PackageConsumer.csproj --source ./artifacts/packages --source https://api.nuget.org/v3/index.json --packages ./artifacts/consumer-packages
dotnet build tests/PackageConsumer/PackageConsumer.csproj -c Release --no-restore
python3 scripts/verify-package.py artifacts/packages/UO.Analyzers.1.0.0.nupkg --consumer tests/PackageConsumer
```

Consumer smoke projesi, restore öncesinde pakete ihtiyaç duyduğu için solution'a dahil değildir.
CI bu sırayı uygular. Testler yalnız sentetik diagnostic'e dayanmaz: Microsoft CA1848 analyzer'ını
çalıştırır, Microsoft logging generator'ıyla dönüşüm sonucunu derler, Roslyn CodeFix.Testing
ile tam kaynak çıktısını karşılaştırır ve üretilmiş assembly'leri çalıştırarak argüman sırasını,
exception'ı, formatted message ve structured state'i denetler. IDE içinde manuel Quick Fix
keşif testi otomatik testlerden ayrı bir kabul adımıdır.

Yeni kural için `UO0001` ile başlayan kullanılmamış bir ID ayırın, `Rules/` altında analyzer'ı,
CodeFixes altında provider'ı ve tests altında pozitif/negatif testleri ekleyin. Descriptor'lar
doğrudan oluşturulur; ortak ID/category sabitleri kullanılır. Ayrıntılar:
[yeni kural ekleme rehberi](docs/adding-a-rule.md).

Microsoft referansları: [source-generated logging](https://learn.microsoft.com/en-us/dotnet/core/extensions/logging/source-generation),
[CA1848](https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/quality-rules/ca1848),
[duplicate EventId diagnostic](https://learn.microsoft.com/en-us/dotnet/fundamentals/syslib-diagnostics/syslib1006).
