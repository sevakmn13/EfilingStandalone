# EFiling Library

A standalone, extensible C# library for electronic court filing via JTI (Journal Technologies) ECF 4.0 endpoints.

## Project Structure

```
EFiling.Core/              Core interfaces, models, caching, encryption
EFiling.Providers.JTI/     JTI-specific SOAP + REST client implementation
EFiling.Tests/             Unit + integration tests
```

## Quick Start

### 1. Prerequisites

- .NET 9.0 SDK
- Court staging credentials (contact your court's e-filing administrator)

### 2. Set Up Credentials

Credentials are stored **encrypted** in a gitignored config file, with the encryption key in an environment variable.

#### Step 1 — Choose a passphrase

Pick a strong passphrase. This is the master key for encrypting/decrypting court credentials. **Never commit it to any file.**

```powershell
# Set it as an environment variable (PowerShell)
$env:EFILING_PASSPHRASE = "your-strong-passphrase-here"

# Or set it permanently (persists across sessions)
[System.Environment]::SetEnvironmentVariable("EFILING_PASSPHRASE", "your-strong-passphrase-here", "User")
```

#### Step 2 — Encrypt your credentials

```powershell
$env:EFILING_ENCRYPT_USERNAME = "your-court-username"
$env:EFILING_ENCRYPT_PASSWORD = "your-court-password"

dotnet test EFiling.Tests --filter "EncryptCredentials_GenerateEncryptedValues" --verbosity normal
```

This prints `ENC:...` values to the console. Copy them.

Then **clear the plaintext env vars** immediately:

```powershell
Remove-Item Env:EFILING_ENCRYPT_USERNAME
Remove-Item Env:EFILING_ENCRYPT_PASSWORD
```

#### Step 3 — Create your config file

```powershell
Copy-Item EFiling.Tests\testsettings.template.json EFiling.Tests\testsettings.json
```

Edit `testsettings.json` and paste the `ENC:...` values into the `Username` and `Password` fields for each environment.

### 3. Run Tests

```powershell
# Make sure passphrase is set
$env:EFILING_PASSPHRASE = "your-strong-passphrase-here"

# Run all tests (unit + integration)
dotnet test EFiling.Tests

# Run only unit tests (no network required)
dotnet test EFiling.Tests --filter "Category!=Integration"

# Run only integration tests (requires network + credentials)
dotnet test EFiling.Tests --filter "Category=Integration"
```

## Configuration

### Config File Structure (`testsettings.json`)

```json
{
  "Courts": {
    "Madera": {
      "Environments": {
        "Staging": {
          "CourtId": "madera",
          "DisplayName": "Madera Superior Court",
          "ProviderType": "JTI",
          "Environment": "Staging",
          "SoapEndpoint": "https://...",
          "RestBaseUrl": "https://...",
          "Username": "ENC:base64-encrypted-value",
          "Password": "ENC:base64-encrypted-value",
          "IsActive": true
        },
        "Production": {
          "...same structure..."
        }
      }
    }
  }
}
```

### Adding a New Court

1. Add a new entry under `Courts` in `testsettings.json` (see template)
2. Encrypt the credentials using the steps above
3. Access it in tests: `TestConfiguration.GetCourt("CourtName.Staging")`

### Changing the Passphrase

If you need to rotate the passphrase:

1. Set the new passphrase: `$env:EFILING_PASSPHRASE = "new-passphrase"`
2. Re-encrypt all credentials using the encrypt helper test
3. Replace all `ENC:...` values in `testsettings.json` with the new ones
4. Update the env var on all developer machines / CI servers

## Security Model

| Layer | What | Where |
|---|---|---|
| **Encryption** | AES-256-CBC, PBKDF2 key derivation (100K iterations), random salt + IV per value | `EFiling.Core/Security/EFilingEncryption.cs` |
| **Passphrase** | `EFILING_PASSPHRASE` environment variable | Machine environment only, never in files |
| **Config file** | `testsettings.json` with `ENC:` encrypted credentials | Gitignored, local machine only |
| **Template** | `testsettings.template.json` with placeholder values | Committed, no secrets |
| **Production** | database via `ISettingService` (future) | DB + env var for passphrase |

### What's committed vs. what's local

| File | In Git? | Contains secrets? |
|---|---|---|
| `testsettings.template.json` | Yes | No — placeholder values only |
| `testsettings.json` | **No** (gitignored) | Encrypted credentials |
| `EFILING_PASSPHRASE` env var | **No** | The encryption key |
| `EFilingEncryption.cs` | Yes | No — just the algorithm |

## Architecture

### Implemented (Phase 1-3)

- **SOAP Client** — `JtiSoapClient` + `SoapEnvelopeBuilder` for ECF 4.0 operations
- **REST Client** — `JtiRestClient` for code list / lookup endpoints
- **GetPolicy** — Fetches court configuration (code list URLs, endpoints)
- **Code Lists** — CASE_TYPE, PARTY_TYPE, CASE_CATEGORY, etc. with relationships
- **Document List** — Document types with metadata requirements per case type
- **Court Locations** — Lookup by zip code
- **Attorney Search** — By bar number or name
- **Caching** — In-memory cache with configurable TTL (24h policy, 12h code lists)