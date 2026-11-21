# Upload manager integration tests

Run from the client repository root:

```bash
dotnet run --project tests/StellarSync.UploadManager.Tests
```

This dependency-free suite links the production upload manager, scheduler,
transfer models, and progress stream. Lightweight environment stubs replace
Dalamud services, file compression, logging, and HTTP transport, allowing
controlled HTTP responses and cancellation races without a game or server.

It verifies rejected uploads remain unverified, fallback uploads restore their
buffer and verify only on success, responses are disposed, old/cancelled sessions
cannot clear newer progress or refill verification after disconnect, manual
uploads survive outfit cancellation, and compression is serialized across batches.
Controlled requests also verify configured limits of 1/2/4/10 and safe clamping
of zero, negative, and excessive stored upload settings to the 1–10 range.
