# NetMetric.Memory Modülü Dokümantasyonu

## 🤖 Amaç

`NetMetric.Memory` modülü, çalışan .NET uygulamasının bellek kullanımını ölçmek için kullanılır. GC istatistikleri, işlem belleği ve sistem genelindeki bellek bilgilerini ölçerek zaman serisi metriklerine dönüştürür.

## ✅ Özellikler

* GC (Çöp Toplayıcı) metrikleri
* Çalışan işlemin kullandığı bellek
* Sistem bellek istatistikleri (platforma özgü)
* Kubernetes/Linux cgroup v2 desteği
* Hatalı durumları ayrıca etiketler

---

## ✏️ Yapı ve Parçalar

### 1. `MemoryModule`

Bellek toplayıcılarını seçilen ayarlara göre bağlar.

#### Parametreler:

* `IMetricFactory factory`: Metrik nesneleri oluşturmak için kullanılır
* `MemoryModuleOptions`: Hangi toplayıcıların aktif olacağını belirler

#### Metotlar:

* `GetCollectors()`: Etkin kolektörleri verir
* `OnInit`, `OnBeforeCollect`, `OnAfterCollect`, `OnDispose`: Yaşam döngüsü desteği

---

### 2. `MemoryModuleOptions`

Bellek modülü için konfigürasyon sınıfıdır.

#### Anahtar Seçenekler:

* `EnableProcess`: İşlem belleği toplansın mı
* `EnableSystem`: Sistem RAM bilgisi toplansın mı
* `EnableGc`: GC metrikleri toplansın mı
* `EnableCgroup`: cgroup v2 (K8s) desteği

#### Hazır Önayarlar:

* `Light`, `Default`, `Verbose`

---

### 3. `GcInfoCollector`

GC nesil bazlı istatistikleri toplar.

#### Metrikler:

* `collections` per GC generation
* `collections_per_sec`
* `pause.percent`, `heap.size.bytes`, `heap.fragmented.bytes`

#### Etiketler:

* `kind`, `gen`, `status`

---

### 4. `ProcessMemoryCollector`

Aktif işlemin bellek kullanımını raporlar.

#### Metrikler:

* Working Set
* Private Memory
* Virtual Memory
* Managed Heap (GC.GetTotalMemory)

---

### 5. `SystemMemoryCollector`

Platforma özgü sistem bellek bilgilerini toplar.

#### Desteklenen OS'ler:

* **Windows**: Total, Available, Used
* **Linux**: /proc/meminfo verileri
* **macOS**: vm\_stat üzerinden total, free, wired, active, inactive
* **Linux Cgroup v2**: Limit, Usage, Swap verileri

#### Etiketler:

* `kind`: total, used, cached, vb.
* `os`: Windows, Linux, macOS
* `status`: ok, unsupported, error

---

### 6. `IMetricCollector` Arayüzü

Tüm toplayıcılar şu imzayı uygular:

```csharp
Task<IMetric?> CollectAsync(CancellationToken ct);
```

---

## 📊 Örnek Kullanım

```csharp
var module = new MemoryModule(factory, MemoryModuleOptions.FromPreset(MemoryModulePreset.Verbose));
foreach (var collector in module.GetCollectors())
{
    var metric = await collector.CollectAsync();
    exporter.Export(metric);
}
```

---

## 💡 Notlar

* Collector'lar "multi gauge" yapısıyla birden fazla alt-metrik döner.
* Tüm toplayıcılar iptal (cancellation) ve hata durumlarını `status` etiketiyle bildirir.
* Platform özellikleri koşullu olarak uygulanır.

---

## 📑 Lisans

Apache-2.0 özgür lisansı altındadır. Kaynak kod ve mimari NetMetric tarafından sürdürülmektedir.
