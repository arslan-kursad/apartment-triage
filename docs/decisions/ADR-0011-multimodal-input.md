# ADR-0011: Multimodal Input Support

## Status

Accepted — 2026-05-27

## Context

Residents report maintenance issues via Telegram. Text-only input misses visual
evidence (wall cracks, exposed wiring, water damage). Richer input improves
classification accuracy and reduces clarification round-trips.

## Decision

Add image support to the Telegram channel. Voice and video were evaluated and
rejected for this iteration.

### Image

- Accepted formats: JPEG, PNG, WebP, GIF
- Max size: 10 MB (Telegram enforces this)
- One image per message (Telegram limit)
- Storage: persisted to DB (`messages.image_data` bytea, `messages.image_mime_type` text)
- Pipeline: base64 content block sent to ClassifierAgent via Anthropic vision API
- Dashboard: ticket detail page renders image inline (data-URI)

### Voice (rejected)

Whisper.net 1.9.0 attempted. Root cause: native runtime (`.so`) triggered
`SIGABRT` via CLR static initializer on Linux — crashed the entire DI chain and
took down the Telegram polling pipeline. The crash bypasses all `try/catch`
because the CLR fast-fails on native `.so` load errors at assembly init time,
not at instantiation time. `NoopTranscriptionService` registration did not help;
only removing `Whisper.net.Runtime` from the csproj resolved the crash.

Decision: dropped for demo scope.

Lesson: native NuGet packages that ship a runtime binary (`.so`/`.dll`) require
explicit validation that the binary loads cleanly on the target OS/arch before
merging. The `Whisper.net.Runtime` package was not tested on Linux/Fly.io prior
to integration.

### Video (rejected)

Anthropic API does not support video input. Frame extraction via FFmpeg would
add unacceptable complexity and binary bloat on the Fly.io free-tier image.

## Consequences

**Positive**
- Visual evidence improves triage quality for electrical, structural, and plumbing issues
- Reduced `CategoryAmbiguous` rate for problems that require a visual (wiring, water damage, cracks)
- Residents can attach a single photo per message without any extra steps

**Negative / Trade-offs**
- DB storage grows ~200–500 KB per image ticket; KVKK anonymization (`Anonymize()`) already nulls `ImageData`
- Voice gap: residents cannot report via audio message; unsupported messages receive an explanatory rejection reply
- Video gap: multi-frame evidence not supported in this iteration
