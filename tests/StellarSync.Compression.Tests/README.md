# Upload compression compatibility tests

This console harness links the production `UploadFileCompressor` and uses the same
cached K4os legacy package as the client. It requires no Dalamud installation or
HTTP server.

Run from the client repository:

```bash
dotnet restore tests/StellarSync.Compression.Tests --source /tmp
dotnet run --project tests/StellarSync.Compression.Tests --no-restore
```

The offline restore command assumes the client's K4os packages are already cached.
Use your usual NuGet source if those packages have not been restored previously.

Coverage includes exact bytes compared with `LZ4Wrapper.WrapHC`, legacy decoding
and server SHA1 validation, empty/tiny files, incompressible and repetitive data,
mixed content, compression/pool/allocation size boundaries, retained outputs after
pool reuse, concurrent calls, pre-cancellation and recovery, and missing files.
The stream overload additionally verifies short reads, exact declared-length reads,
truncated input rejection, cancellation during a partially completed read, and
successful recovery after read failures or cancellation.
