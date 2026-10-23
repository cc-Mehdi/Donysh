# Donysh development guide

## Product map

- `Donysh.Landing` serves `donysh.ir` and links users to the product services.
- `Donysh.Auth` serves `auth.donysh.ir` and is the single OpenID Connect authority.
- `HesabYar.Web` serves `finance.donysh.ir` and keeps its own local Finance session and data.
- `Donysh.Life` serves `life.donysh.ir` and keeps its own local Life session and data.
- `docker-compose.platform.yml` and `Caddyfile.platform` define the production topology.

## Authentication invariants

- Auth is the only service that accepts account credentials. Finance and Life must use authorization-code flow with PKCE.
- Product session cookies stay host-scoped and distinct; never share an application cookie across subdomains.
- OIDC redirect and post-logout URIs must be exact HTTPS addresses registered by `AuthClientSeeder`.
- Set granted scopes on the OpenIddict principal before resolving claim destinations. Finance requires `sub`, `email`, and `email_verified` in the signed ID token.
- A failed callback must render a stable error page and must never automatically challenge again. Failure query markers accept `true` and legacy `1` values.
- Keep correlation and nonce cookies secure, HTTP-only, and compatible with the selected response mode.
- Do not weaken CSP, PKCE, issuer, nonce, state, email-verification, or redirect validation to work around an authentication failure.
- Cross-product logout proofs are short-lived, audience-scoped, and signed with `CrossAppLogout:Secret`.
- Preserve the logout chain, all three CSP `form-action` origins, and both desktop and mobile Life sign-out controls described in `docs/platform/AUTH-LOGOUT.md`.

## Development workflow

1. Read this file and inspect `git status` before editing. Preserve unrelated user changes.
2. Trace cross-service changes through Auth, Finance, Life, deployment configuration, and their regression-test projects.
3. Never commit `.env`, certificates, passwords, client secrets, database data, or generated local artifacts.
4. Add or update a regression test for every bug fix. Prefer behavior assertions over source-text assertions.
5. Run the focused test projects first, then build `HesabYar.sln` in Release mode.
6. If frontend styles change, run the relevant `npm run build:css` command and commit the generated `wwwroot/css/app.css` output.
7. If deployment files change, validate Compose and Caddy configuration before release.

## Useful verification commands

```powershell
dotnet build HesabYar.sln --configuration Release
dotnet run --project Donysh.Api.Tests --configuration Release
dotnet run --project Donysh.Auth.Tests --configuration Release
dotnet run --project Donysh.Landing.Tests --configuration Release
dotnet run --project Donysh.Life.Tests --configuration Release
dotnet run --project Donysh.Sms.Tests --configuration Release
```

Production authentication smoke tests must cover:

- anonymous Finance and Life requests redirect once to Auth;
- an existing Auth session returns to each product without credential re-entry;
- the Finance callback creates a valid local Identity session;
- callback failure produces one visible error response, not a redirect loop;
- logout clears the current product, peer product, and central Auth sessions.

## Database and deployment safety

- Use EF Core migrations for schema changes and keep migration checks passing against PostgreSQL.
- Never run `docker compose down -v` in normal development or deployment; production volumes contain user data and persisted Data Protection keys.
- Keep production secrets in the server `.env`; `.env.example` contains placeholders only.
- Deployment changes must preserve the rollback path in `scripts/server-deploy.sh`.

## Commit messages

After finishing a task, commit only its changes and push the commit to the remote branch. Preserve unrelated local changes.

After completing each logical section, provide the user with a concise English commit message prefixed by the relevant gitmoji. Example:

```text
🐛 Fix Finance OIDC claims and stop authentication redirect loops
```
