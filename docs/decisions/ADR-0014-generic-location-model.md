# ADR-0014 — Generic Location Model

**Status:** Proposed
**Date:** 2026-05-29
**Decider:** Architect
**Context:** Day 19, konum verisi modelleme tartışması

> **Not:** Bu ADR ileriye dönük bir mimari öneridir; **henüz implemente edilmedi**. Architect onayı ve faz planı bekliyor. Mevcut `LocationHint` free-text alanı bu karar uygulanana kadar geçerlidir.

---

## Bağlam

Ticket'ın konum bilgisi bugün tek bir serbest-metin alanında tutuluyor:

```csharp
/// <summary>Free-text location extracted by Classifier. Max 100 chars.</summary>
public string? LocationHint { get; private set; }
```

Classifier bunu mesajdan çıkarır (örn. "A blok 3. kat asansör", "bodrum kazan dairesi", "5 numaralı dairenin banyosu"). Serbest metin demo için yeterli ama şu yetenekleri **engelliyor**:

1. **Gruplama/filtreleme:** "B blok'taki tüm açık talepler" veya "ortak alan arızaları" sorgulanamıyor — metin eşleşmesi kırılgan (`"A blok"` vs `"A-blok"` vs `"a bloğu"`).
2. **Yönlendirme (routing):** Router, sorunun ortak alan mı yoksa daire içi mi olduğunu (yönetim sorumluluğu vs sakin sorumluluğu) yapısal olarak bilemiyor.
3. **Tekrar/benzerlik (Enricher):** Aynı fiziksel konumdaki tekrarlayan arızalar (aynı asansör) konum yapısı olmadan güvenilir eşleşmiyor.
4. **Çok-bloklu site:** Daire numarası serbest-metin (`Resident.ApartmentNumber`) olduğu için blok/kat/daire ayrıştırması yok.

---

## Karar (Öneri)

Serbest metni **koruyarak** yapısal bir konum value object'i eklenir. Geriye dönük uyumlu, kademeli geçiş.

### Önerilen model

```csharp
public enum LocationType
{
    Unknown = 0,     // çıkarılamadı
    Unit,            // belirli daire (sakin sorumluluğu ağırlıklı)
    CommonArea,      // merdiven, asansör, lobi, otopark (yönetim sorumluluğu)
    Building,        // bina geneli / teknik hacim (kazan dairesi, su deposu)
    Exterior         // dış cephe, bahçe, çevre
}

public sealed record TicketLocation(
    LocationType Type,
    string?      Block,      // "A", "B" — normalize edilmiş
    string?      Floor,      // "3", "bodrum", "zemin"
    string?      UnitRef,    // daire referansı (Type=Unit ise)
    string       RawHint);   // Classifier'ın ürettiği orijinal metin (her zaman korunur)
```

### İlkeler

- **RawHint her zaman korunur** — yapısal alanlar çıkarılamasa bile orijinal metin kaybolmaz (mevcut `LocationHint` davranışının üst kümesi).
- **Geriye dönük uyumlu geçiş:** İlk fazda `LocationHint` (string) korunur; `TicketLocation` paralel eklenir. Classifier prompt'u yapısal alanları doldurmaya başlar. Eski kayıtlar `Type=Unknown, RawHint=eski LocationHint` ile doldurulur (data migration).
- **Generic (apartmana özel değil):** Model bir siteye/binaya özgü kodlanmaz; `Block`/`Floor` serbest ama normalize edilmiş string'lerdir. Bu, farklı apartman yapılarına (tek blok, çok blok, villa sitesi) uyum sağlar.
- **Sorumluluk sinyali:** `LocationType` (Unit vs CommonArea vs Building), Router'ın yönetim/sakin sorumluluk ayrımına yapısal girdi sağlar — ama routing kuralının kendisi bu ADR'ın kapsamı dışında.

---

## Açık Sorular (Architect kararı bekliyor)

1. **`Resident.ApartmentNumber` ile ilişki:** Daire numarası da serbest-metin. `UnitRef` bununla normalize edilmeli mi, yoksa bağımsız mı kalmalı? (Çapraz-tutarlılık riski.)
2. **Persistence:** `TicketLocation` owned entity (EF `OwnsOne`, aynı tabloya kolonlar) mı, yoksa JSON kolon mu? Owned entity sorgulanabilirlik için daha iyi; JSON daha esnek.
3. **Faz planı:** Bu, Loom demo (Day 21) öncesi scope'a girer mi, yoksa post-demo iyileştirme mi? Classifier prompt revizyonu + migration + eval güncellemesi gerektirir.

---

## Reddedilen Alternatifler

- **Tam adres normalizasyonu (geocoding / standart adres şeması):** Apartman içi konum için aşırı; il/ilçe/mahalle gibi alanlar alakasız.
- **Siteye özel konfigürasyon tabloları (blok/kat enum'ları DB'de tanımlı):** Tek-apartman demo için erken optimizasyon; generic string-tabanlı model yeterli ve taşınabilir.
- **Mevcut serbest-metni koru, hiçbir şey ekleme:** Gruplama/yönlendirme/benzerlik yeteneklerini kalıcı olarak engelliyor; ADR'ın çözmek istediği sorun bu.
