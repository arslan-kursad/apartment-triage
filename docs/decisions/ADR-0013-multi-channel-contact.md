# ADR-0013 — Multi-Channel Contact Model

**Status:** Proposed
**Date:** 2026-05-29
**Decider:** Architect
**Context:** Day 19, Sakinler CRUD genişletme (T6)

---

## Bağlam

`Resident` entity'si bir sakini iki tür veriyle tanımlıyordu:

- **Kanal kimliği (channel identity):** `WhatsAppNumber` (E.164), `TelegramId` (long). Bunlar gelen mesajın hangi sakine ait olduğunu çözen (resolution) anahtarlardır; benzersizlik kısıtı (unique index) bunların üzerinde tanımlıdır.
- **Görünen ad:** `DisplayName` (kanal profilinden).

Eksik olan: sakine **ulaşmak için** kullanılan iletişim bilgisi, kanal kimliğinden ayrı tutulmuyordu. Pratik sorunlar:

1. Yöneticinin sakini telefonla araması gereken durumlar var (acil arıza). Sakinin WhatsApp numarası ile **arama numarası** aynı olmak zorunda değil (örn. WhatsApp Business hattı, eşinin hattı, sabit hat).
2. Telegram'da kimlik `TelegramId`'dir (sayısal, kullanıcıya gösterilmez). İnsan-okunur `@username` ise hem değişebilir hem de kimlik olarak güvenilmez — ama yöneticinin sakini bulması/iletişim kurması için faydalıdır.

Bu iki ihtiyaç "kanal kimliği" alanlarına sıkıştırılırsa anlam karışır: arama numarasını `WhatsAppNumber`'a yazmak unique index'i ve kanal resolution'ı bozar.

---

## Karar

`Resident` entity'sine **kanal kimliğinden ayrı** iki nullable iletişim alanı eklendi:

| Alan | Tip | Anlam | Kimlik mi? |
|------|-----|-------|-----------|
| `ContactPhone` | `string?` (max 20) | Sakine ulaşmak için telefon. Serbest format. | **Hayır** — resolution'da kullanılmaz, unique değil. |
| `TelegramUsername` | `string?` (max 50) | `@username`, görüntü/iletişim amaçlı. Kaydederken baştaki `@` kırpılır. | **Hayır** — `TelegramId` kanal kimliği olarak kalır. |

### Değişiklik kapsamı

- **Domain:** `Resident.ContactPhone`, `Resident.TelegramUsername` (private set). `UpdateContactInfo(...)` iki yeni opsiyonel parametreyle genişletildi; `TelegramUsername` set edilirken `.TrimStart('@')` uygulanır.
- **Persistence:** `ResidentConfiguration` — `contact_phone` (20), `telegram_username` (50) maxlength. Migration: `202605292159_AddContactInfoToResidents` — iki nullable kolon ekler, reversible, veri kaybı yok.
- **API:** `ResidentUpsertRequest` iki alan kazandı; `POST /api/residents` ve `PUT /api/residents/{id}` bunları `UpdateContactInfo` üzerinden yazar; `GET /api/residents` çıktısında döner.
- **UI:** Sakin formu (modal) "İletişim Telefonu" ve "Telegram Kullanıcı Adı" alanlarıyla genişletildi; edit prefill ham (maskelenmemiş) değerleri yükler.

### Tasarım ilkesi: kimlik ≠ iletişim

Kanal kimliği alanları (resolution + unique index) ile iletişim alanları (yöneticiye kolaylık) **ayrı tutulur**. Bu ayrım, ileride bir sakinin birden çok kanaldan (WhatsApp + Telegram) tanınması durumunda kimlik çözümünü bozmadan iletişim bilgisini zenginleştirmeye izin verir.

---

## Kapsam Dışı (bilinçli ertelenen)

**Çoklu kanal resolution (cross-channel identity linking)** bu ADR'ın kapsamı dışındadır. Yani "aynı kişi hem WhatsApp hem Telegram'dan yazıyor" durumunda iki `Resident` kaydını tek kişiye bağlama mekanizması **yapılmadı**. T6 yalnızca iletişim verisi modelini ekler; kimlik birleştirme ayrı bir karar (gerekirse ayrı ADR).

Gerekçe: resolution birleştirme, merge çakışmaları (hangi `DisplayName`/`ApartmentNumber` kazanır), KVKK anonymization etkileşimi ve unique index revizyonu gibi alt kararlar içerir — Loom demo scope'unu aşar.

---

## Reddedilen Alternatifler

- **Ayrı `Contact` entity'si (1-N):** Bir sakine birden çok iletişim kanalı bağlayan normalize tablo. Demo için overengineering; tek `ContactPhone` + `TelegramUsername` ihtiyacı karşılıyor. Çoklu iletişim gerçek ihtiyaç olursa yeniden değerlendirilir.
- **`WhatsAppNumber`'ı arama numarası olarak çift kullanmak:** Unique index ve kanal resolution'ı bozar; anlam karışır. Reddedildi.
- **`TelegramUsername`'ı kimlik yapmak:** Username değişebilir ve benzersiz garanti edilmez; `TelegramId` kimlik olarak kalmalı.
