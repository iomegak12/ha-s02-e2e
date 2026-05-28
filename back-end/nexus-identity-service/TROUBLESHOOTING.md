# Troubleshooting — nexus-identity-service

> Common failures and how to recover. Expanded as later phases ship.

## Phase 0

### `dotnet run` fails with "port 8000 already in use"

Another process holds the port. Either stop it or override:

```powershell
$env:ASPNETCORE_URLS="http://+:8001"
dotnet run --project src/Nexus.Identity.Api
```

### `dotnet build` fails with "net10.0 not found"

Install the .NET 10 SDK. Verify with `dotnet --list-sdks`.

## Future phases (placeholders)

- DB unreachable on startup → see Phase 2 notes once added.
- JWKS empty → see Phase 3 notes once added.
- Refresh token rejected → see Phase 3 notes once added.
- Rate limit triggered → see Phase 1 notes once added.
- OTLP exporter errors → see Phase 1 notes once added.
- Container fails to start as non-root → see Phase 8 notes once added.
