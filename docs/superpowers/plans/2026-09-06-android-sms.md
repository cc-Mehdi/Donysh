# Android SMS companion implementation plan

**Goal:** Opt-in Android bank SMS ingestion as reviewable Donysh expenses, without API floods.

**Architecture:** Shared deterministic parser (linked source in web, Android and tests). HTTPS device credentials scoped to one user and selected workspace/category; revocation and membership rechecked server-side. Durable phone outbox, bounded batches, pacing and backoff; database receipts enforce idempotency even after expense deletion.

**Tech stack:** Standalone .NET 9 MAUI Windows/Android companion, native Android SMS receiver/JobScheduler, bounded private atomic-file outbox, existing .NET 10 ASP.NET Core/EF Core PostgreSQL web application.

## Constraints / approved design

- Direct APK distribution; no Play Store. RECEIVE_SMS only, no historical inbox import.
- Explicit sender allowlist per bank. Mellat and Blu withdrawals only; reject ambiguous/OTP/deposit messages. Input rial, output toman. Preserve original message only for matching bank messages.
- Batch maximum 5, minimum 30 seconds between sends; exponential backoff up to 30 minutes with jitter and Retry-After. Offline queue capped at 1,000, visible overflow warning. No unbounded network work in receiver.
- Global API rate/concurrency limits plus persistent per-device interval. Maximum body 32 KiB. No plaintext device secrets in database/logs. Credentials in Android SecureStorage.
- One destination workspace/category per paired device; changing destination requires a new pairing (pending queue remains bound to original credential; block switching until empty). Account and bank retained on imported expense.
- Never commit or deploy automatically. Existing web deployment builds only the web project; Android stays independent.

## Task 1 — parser and policy tests

Files: `HesabYar.Web/Sms/Shared/BankSmsParser.cs`, `SmsContracts.cs`, `Donysh.Sms.Tests/`.
- [x] Write executable tests against the user samples, localized digits, invalid amounts/dates, balance/OTP/deposit rejection, deterministic receipt keys, capped backoff.
- [x] Run failing parser/outbox tests, implement and rerun. Additional boundary regressions caught negative Blu suffix matching and escaped JSON byte-size overflow.

## Task 2 — secure ingestion and review

Files: `HesabYar.Web/Sms/`, `Data/ApplicationDbContext.cs`, `Data/DatabaseInitializer.cs`, `Domain/FinanceModels.cs`, `Pages/Mobile/`, `Pages/Expenses/`, `Program.cs`, layout.
- [x] Pairing page chooses accessible workspace and non-archived category. Generate one-time 10-minute code; phone exchanges for 256-bit device token. Limit attempts and active devices.
- [x] Ingestion validates token, workspace membership, category and payload. Serialize each device through a database row update, pace requests and atomically save expense + receipt. Receipt outlives expense deletion.
- [x] Add source text/date/time/bank + needs-review state and filtered expense list. Existing expenses remain unchanged.
- [ ] Build web and verify database schema and endpoint behavior where local infrastructure permits.

## Task 3 — Android app

Files: `Donysh.Companion/` standalone MAUI Windows/Android project with its own .NET 9 `global.json` and solution.
- [x] Pairing/consent screen, explicit sender allowlists, queue/status and error messages, pause/resume and manual retry.
- [x] SMS receiver verifies sender, joins multipart SMS, parses locally and enqueues synchronously in private atomic-file storage. The bounded outbox avoids an extra Android database dependency and is tested directly on .NET.
- [x] JobScheduler drains only one bounded batch; persistent next-attempt time, network constraint, backoff and reboot scheduling. Redaction: unrelated SMS never persisted or transmitted.
- [ ] Build APK; document installation, signing, pairing, device-only acceptance tests and background delivery limitations.

## Verification

From `Donysh.Companion`, `dotnet run --project ../Donysh.Sms.Tests` exercises shared production behavior on .NET 9. `dotnet build Donysh.Companion.csproj -f net9.0-windows10.0.19041.0` checks the F5-compatible Windows target and `dotnet build Donysh.Companion.csproj -f net9.0-android` builds the Android target when SDK 35/JDK 17 are installed. `dotnet build HesabYar.Web/HesabYar.Web.csproj` and the API tests remain on the root .NET 10 SDK. Real phone checks include offline restart, duplicate broadcast, 100+ pending messages, permission denial, revoked credential and membership removal. Do not claim real-device delivery without testing on a phone.

## Verified on 2026-09-06

- 19 shared parser/outbox/desktop-preview checks pass, including persistence, capacity overflow and corrupt-file preservation.
- 10 local HTTP checks pass, including HTTPS, malformed/oversized requests, flood rejection and ordinary-web isolation.
- Web Release build and Tailwind CSS build pass; one pre-existing KnownNetworks deprecation warning remains.
- The standalone solution selects SDK 9.0.317 and targets .NET 9 for both Windows and Android. A clean Windows solution build completes with zero warnings/errors and the unpackaged app launches with the Windows-only preview; automated preview checks cover both supported bank samples without sending them.
- PostgreSQL integration checks are implemented in Donysh.Api.Tests/PostgresChecks.cs but skipped locally: no running PostgreSQL/Docker daemon.
- A clean Android Debug APK build against Android platform 35, build-tools 35.0.0 and JDK 17 completes with zero warnings/errors. The signed test APK SHA-256 is `3D76C2F0BCB4CDBA7B0E1B6E99243C385FA212DAC0B8185C7D68DF385E3DFA23`. Installation, permission behavior and background delivery on a real phone remain unverified. The manual GitHub Actions workflow targets the same platform and is not run automatically.
- No commit, push, deployment, production database access or production signing key generation performed.
