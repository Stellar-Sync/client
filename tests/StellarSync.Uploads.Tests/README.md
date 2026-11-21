# Upload scheduling tests

Run from the repository root:

```bash
dotnet run --project tests/StellarSync.Uploads.Tests
```

This dependency-free console suite compiles the production `UploadBatchRunner`
directly. It does not load Dalamud, compress real files, contact a server, or
require third-party test packages.

Controlled upload gates verify the 1/2/4/10 limits,
serial preparation and bounded lookahead, completion ordering, cancellation,
failure propagation, and draining of outstanding uploads before returning.
The shared limiter tests additionally exercise concurrent batches, limit changes,
cancelled slot acquisition, and idempotent slot release.
The production upload stream is also linked to check exact bytes/content length,
progress after completed writes, and cancellation during a blocked network write.
