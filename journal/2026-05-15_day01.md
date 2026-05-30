## Day 1 — 15 May 2026 (Cuma)

### Bugün yapılanlar
- Prompt Engineer agent kuruldu (sistem prompt + ilk task: taxonomy design)
- Apartman triage classification taxonomy 3 turda lock'landı (v1 → v2 → v3)
- 6 mimari karar locked: neighbor_dispute ayrı kategori, multi_ticket strategy, 
  hybrid emergency architecture (Layer 1 soft + Layer 2 authority), 
  utility_outage subtype, cause_effect handling (single ticket + severity 
  upgrade), same_category split rule (location_based)
- emergency_phrases v2 lock'landı, dual-confidence schema yerleşti
- causal_relation 3-değer enum (independent / effect_of_primary / cause_of_primary)
- location_hint progressive enhancement plan (Day 14+ enum migration kararı)

### Beklemediğim problem / sürpriz
- PE iki kez yetki sınırını ihlal etti: önce manifest'te "v2.status: locked" + 
  "pm_review_completed" yazdı; sonra external_outage'da "PM dashboard toggle 
  cevabı verdi" diye kurmaca PM atıfı yaptı.
- İkisi de iyi niyetli scope-creativity ama foundry hierarchy ihlali.
- Aynı PE'nin domain bilinci de bu turda çok yüksek çıktı: causal_relation 
  taxonomy + mismatch_log mechanism + dual-confidence split — decision package'da 
  olmayan ama ortaya çıkan içeriksel zenginlik.

### Aldığım karar + sebep
- **Karar:** Agent sistem prompt'larına "Decision Authority" bölümü eklenecek (Day 2 
  Architect kurulduğunda ADR olarak işlenir).
- **Neden:** Coach + PE sistem prompt'larında "Eskalasyon Kuralları" var ama 
  "Versioning & Lock Authority" yok. Bu boşluk PE'nin yetki uydurmasına yol açtı.
- **Alternatif:** PE'i sıkıca disipline et (manuel correction her turda). Reddedildi — 
  sistemik gap sistem prompt fix'iyle kapanır, davranış disiplini her seferinde 
  manuel olmaz.

- **Karar:** taxonomy ve emergency_phrases lock'lu, v4 yapmak için yeni decision 
  package gerekir.
- **Neden:** Repository convention. Lock = döküman authoritative reference, free 
  edit yok.

### Keşke önceden bilseymişim
- Decision package'da "Agent ne karar verebilir, ne karar veremez" maddesini explicit 
  yazsam, PE iki yetki ihlalinden birini yapmazdı. Lesson learned.
- 3 round review (v1 → v2 → v3) Day 1'in tamamını aldı — taxonomy design tek başına 
  bir günlük iş. Roadmap §5 Day 1: "Project skeleton + paperwork" diyor; gerçek Day 1: 
  "Prompt Engineer kurulumu + taxonomy lock". Skeleton Day 2'ye kaydı. Bu iyi bir 
  şey — taxonomy lock'suz skeleton yanlış field'lar üretirdi.

### Parking Lot (Day 21 sonrası Founder — Strategic için)
- ASEE konsepti: autonomous evolution engineer rol tanımı. Bu proje için over-scoped 
  ama bazı pattern'leri (confidence escalation, hybrid emergency layer, eval-driven 
  prompt versioning) küçük ölçekte buraya sızdı. Scale-up kariyer yönü 
  değerlendirilmeli, ama Day 21 sonrası.

### Yarın (Day 2, 16 May Cumartesi)
- 09:00 — Build — Skeleton & Domain Models conversation aç
- 09:00-12:00 — dotnet solution + Postgres docker + EF migrations
- 12:00-18:00 — Domain models tasarımı: Ticket/Resident/Message records 
  (taxonomy v3 lock'lu field'ları ile uyumlu)
- Akşam — IMessageChannel + MockChannel iskeleti
- Day 2 sonu mini-checkpoint: skeleton kaç saatte tamamlandı, domain models 
  nereye kadar geldi, Day 3 channel abstraction'a normal devam mı?