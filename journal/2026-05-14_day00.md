## Day 0 — 14 May 2026 (Perşembe)

### Bugün yapılanlar
- Project Files'a primer v1 ve roadmap v1 yüklendi.
- the outreach target'a holding email (Comm Coach üzerinden) gönderildi.
- Agent foundry pattern'i kuruldu: PM → sistem prompt'u yazar, yeni conversation Coach olur.
- `Agent — Comm Coach` kuruldu ve aktif.
- `Ops — Meta Business Verification` thread'i hazırlandı, Day 1 paperwork için.
- Conversation naming convention belirlendi: PM, Agent, Build, Ops prefix sistemi.
- Org chart v2 oluşturuldu (4 katman + conditional layer).
- Primer ve roadmap v2'ye revize edildi (tarih hataları, Ops layer, naming convention).

### Beklemediğim problem / sürpriz
- Roadmap v1'de gün isimleri 1 gün kayıktı (14 May Çarşamba değil Perşembe).
- Day 20 ve Day 21 aynı tarihte (3 Jun) gözüküyordu — fiziksel olarak imkânsız.
- PM ben olduğum halde tarih doğrulaması yapmamıştım.

### Aldığım karar + sebep
- **Karar:** 7 agent foundry, faz faz kurulacak; hepsini Day 0'da kurmayacağız.
  - **Neden:** Day 0'da 9 agent kurmak Day 3'te orchestration debug demek. Marjinal değeri olmayan disiplinli erteleme.
  - **Alternatif:** Hepsini bugün kursak, ekstra fonksiyon kazanır mıydık? Hayır — kullanılmayan agent ölü maliyettir.
- **Karar:** Founder — Strategic ayrı bir conversation OLARAK ŞİMDİ AÇILMAYACAK, koşula bağlı.
  - **Neden:** Solo build'de aşırı meta-organization, asıl işi yapmaktan kaçma şekli olabilir.
- **Karar:** Org chart Project Files'a değil, repo'ya (`docs/agents/org-chart.md`) gidecek.
  - **Neden:** Project Files context'i şişirir, repo decision history'nin doğal yeri. Day 21'de Loom Q5 için kullanılabilir kaynak.

### Keşke önceden bilseymişim
- Day 0 risk register'ında "PM her phase boundary'sinde takvim doğrulaması yapar" satırı olmalıydı. v1'de yoktu; v2'ye eklendi.
- Agent foundry'nin "bootstrap chain" yapısı baştan netleştirilseydi, "agent'ları kim kuruyor" sorusu gelmezdi.

### Yarın (Day 1, 15 May Cuma)
- 08:00 — `Ops — Meta BM` thread'inde paperwork başlat (Meta Business Manager hesabı + verification submit).
- 09:30 — PM thread'inde Prompt Engineer sistem prompt'unu al, `Agent — Prompt Engineer` aç.
- 10:00 — Build başlangıç: dotnet solution skeleton + Postgres docker compose + ilk migration.