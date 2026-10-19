#!/usr/bin/env python3
from __future__ import annotations

import json
import re
import sys
import xml.etree.ElementTree as ET

from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
REQUIRED = [
    "HesabYar.sln",
    "docker-compose.yml",
    "HesabYar.Web/Dockerfile",
    "HesabYar.Web/HesabYar.Web.csproj",
    "HesabYar.Web/Program.cs",
    "HesabYar.Web/Pages/Account/Login.cshtml",
    "HesabYar.Web/Pages/Expenses/Index.cshtml",
    "HesabYar.Web/Pages/Budgets/Index.cshtml",
    "HesabYar.Web/Pages/Savings/Index.cshtml",
    "HesabYar.Web/Pages/Reports/Index.cshtml",
    "HesabYar.Web/Pages/Workspaces/Index.cshtml",
    "HesabYar.Web/ModelBinding/FlexibleDecimalModelBinder.cs",
    "HesabYar.Web/ModelBinding/PersianDateOnlyModelBinder.cs",
    "Donysh.Auth/Donysh.Auth.csproj",
    "Donysh.Auth/Program.cs",
    "Donysh.Auth/Data/Migrations/AuthDbContextModelSnapshot.cs",
    "Donysh.Auth/Pages/Account/Login.cshtml",
    "Donysh.Auth/wwwroot/css/app.css",
    "Donysh.Auth.Tests/Donysh.Auth.Tests.csproj",
    "Donysh.Landing/Donysh.Landing.csproj",
    "Donysh.Landing/Program.cs",
    "Donysh.Landing/Pages/Index.cshtml",
    "Donysh.Landing/Styles/app.css",
    "Donysh.Landing/wwwroot/css/app.css",
    "Donysh.Landing/package-lock.json",
    "Donysh.Landing/Dockerfile",
    "Donysh.Landing.Tests/Donysh.Landing.Tests.csproj",
    "Donysh.Life/Donysh.Life.csproj",
    "Donysh.Life/Program.cs",
    "Donysh.Life/Data/LifeDbContext.cs",
    "Donysh.Life/Data/Migrations/LifeDbContextModelSnapshot.cs",
    "Donysh.Life/Pages/Index.cshtml",
    "Donysh.Life/Styles/app.css",
    "Donysh.Life/wwwroot/css/app.css",
    "Donysh.Life/package-lock.json",
    "Donysh.Life/Dockerfile",
    "Donysh.Life.Tests/Donysh.Life.Tests.csproj",
    "docker-compose.platform.yml",
    "Caddyfile.platform",
    "docs/platform/BASELINE.md",
    "docs/platform/ARCHITECTURE.md",
    "docs/platform/ROADMAP.md",
    "docs/platform/AUTH.md",
    "docs/platform/DEPLOYMENT.md",
    "docs/platform/MIGRATION.md",
    ".github/workflows/deploy.yml",
    "scripts/server-deploy.sh",
    "CI-CD.md",
]


def balanced(text: str, opening: str, closing: str) -> bool:
    depth = 0
    in_string = False
    escaped = False
    for char in text:
        if in_string:
            if escaped:
                escaped = False
            elif char == "\\":
                escaped = True
            elif char == '"':
                in_string = False
            continue
        if char == '"':
            in_string = True
        elif char == opening:
            depth += 1
        elif char == closing:
            depth -= 1
            if depth < 0:
                return False
    return depth == 0


def main() -> int:
    errors: list[str] = []
    for item in REQUIRED:
        if not (ROOT / item).is_file():
            errors.append(f"missing: {item}")

    try:
        json.loads((ROOT / "HesabYar.Web/package.json").read_text(encoding="utf-8"))
        json.loads((ROOT / "HesabYar.Web/appsettings.json").read_text(encoding="utf-8"))
        json.loads((ROOT / "global.json").read_text(encoding="utf-8"))
        json.loads((ROOT / "Donysh.Auth/package.json").read_text(encoding="utf-8"))
        json.loads((ROOT / "Donysh.Auth/appsettings.json").read_text(encoding="utf-8"))
        json.loads((ROOT / "Donysh.Landing/package.json").read_text(encoding="utf-8"))
        json.loads((ROOT / "Donysh.Landing/appsettings.json").read_text(encoding="utf-8"))
        json.loads((ROOT / "Donysh.Life/package.json").read_text(encoding="utf-8"))
        json.loads((ROOT / "Donysh.Life/appsettings.json").read_text(encoding="utf-8"))
    except Exception as exc:
        errors.append(f"invalid json: {exc}")

    frontend_projects = ["HesabYar.Web", "Donysh.Auth", "Donysh.Landing", "Donysh.Life"]
    for project in frontend_projects:
        lockfile = ROOT / project / "package-lock.json"
        try:
            lock = json.loads(lockfile.read_text(encoding="utf-8"))
        except Exception as exc:
            errors.append(f"invalid npm lockfile {project}/package-lock.json: {exc}")
            continue

        for package_name, package in lock.get("packages", {}).items():
            resolved = package.get("resolved")
            if resolved and not resolved.startswith("https://registry.npmjs.org/"):
                errors.append(
                    f"untrusted npm registry in {project}/package-lock.json "
                    f"for {package_name or '<root>'}: {resolved}"
                )

    try:
        ET.parse(ROOT / "HesabYar.Web/HesabYar.Web.csproj")
        ET.parse(ROOT / "Donysh.Auth/Donysh.Auth.csproj")
        ET.parse(ROOT / "Donysh.Auth.Tests/Donysh.Auth.Tests.csproj")
        ET.parse(ROOT / "Donysh.Landing/Donysh.Landing.csproj")
        ET.parse(ROOT / "Donysh.Landing.Tests/Donysh.Landing.Tests.csproj")
        ET.parse(ROOT / "Donysh.Life/Donysh.Life.csproj")
        ET.parse(ROOT / "Donysh.Life.Tests/Donysh.Life.Tests.csproj")
    except Exception as exc:
        errors.append(f"invalid csproj xml: {exc}")

    source_roots = [
        ROOT / "HesabYar.Web",
        ROOT / "Donysh.Auth",
        ROOT / "Donysh.Auth.Tests",
        ROOT / "Donysh.Landing",
        ROOT / "Donysh.Landing.Tests",
        ROOT / "Donysh.Life",
        ROOT / "Donysh.Life.Tests",
    ]
    for path in (path for source_root in source_roots for path in source_root.rglob("*.cs")):
        text = path.read_text(encoding="utf-8")
        # The simple delimiter check below is not a C# parser. Remove non-interpolated
        # raw string bodies so SQL/JSON braces inside triple-quoted literals do not
        # produce false positives; the real syntax check is the dotnet build.
        text = re.sub(r'(?<!\$)"""[\s\S]*?"""', '""', text)
        text = re.sub(r"'(?:\\.|[^'\\])'", "''", text)
        if not balanced(text, "{", "}"):
            errors.append(f"unbalanced braces: {path.relative_to(ROOT)}")
        if not balanced(text, "(", ")"):
            errors.append(f"unbalanced parentheses: {path.relative_to(ROOT)}")
        if not balanced(text, "[", "]"):
            errors.append(f"unbalanced brackets: {path.relative_to(ROOT)}")
        if "TODO" in text or "NotImplementedException" in text:
            errors.append(f"unfinished marker: {path.relative_to(ROOT)}")

    for compose_name in ["docker-compose.yml", "docker-compose.domain.yml", "docker-compose.platform.yml"]:
        compose = (ROOT / compose_name).read_text(encoding="utf-8")
        if not compose.startswith("name:") or "\nservices:\n" not in compose:
            errors.append(f"{compose_name} has no top-level services mapping")

    compose = (ROOT / "docker-compose.yml").read_text(encoding="utf-8")
    for token in ["postgres:17-alpine", "DataProtection__KeysPath", "${APP_PORT:-8080}:8080"]:
        if token not in compose:
            errors.append(f"compose token missing: {token}")

    platform_compose = (ROOT / "docker-compose.platform.yml").read_text(encoding="utf-8")
    for token in [
        "auth-db:",
        "auth:",
        "AUTH_SIGNING_CERT_PASSWORD",
        "FINANCE_DOMAIN",
        "OidcClients__0__RedirectUris__1",
        "OidcClients__0__PostLogoutRedirectUris__1",
        "Navigation__DefaultReturnUrl",
        "landing:",
        "Products__FinanceUrl",
        "life-db:",
        "life:",
        "LIFE_OIDC_CLIENT_SECRET",
        "ConnectionStrings__LifeConnection",
    ]:
        if token not in platform_compose:
            errors.append(f"platform compose token missing: {token}")

    workflow = (ROOT / ".github/workflows/deploy.yml").read_text(encoding="utf-8")
    for token in [
        "name: CI and Deploy",
        "\njobs:\n",
        "uses: actions/checkout@v4",
        "Verify committed frontend assets are current",
    ]:
        if token not in workflow:
            errors.append(f"workflow token missing: {token}")

    for dockerfile_name in [
        "HesabYar.Web/Dockerfile",
        "Donysh.Auth/Dockerfile",
        "Donysh.Landing/Dockerfile",
        "Donysh.Life/Dockerfile",
    ]:
        dockerfile = (ROOT / dockerfile_name).read_text(encoding="utf-8")
        if "npm ci" in dockerfile or "FROM node:" in dockerfile:
            errors.append(f"{dockerfile_name} must consume CI-verified compiled assets")
        if "--mount=type=cache,id=donysh-nuget" not in dockerfile:
            errors.append(f"{dockerfile_name} must cache restored NuGet packages")

    pages_text = "\n".join(
        path.read_text(encoding="utf-8")
        for path in (ROOT / "HesabYar.Web/Pages").rglob("*.cshtml")
    )
    for token in ["data-money", "data-persian-date"]:
        if token not in pages_text:
            errors.append(f"UI token missing: {token}")

    caddy = (ROOT / "Caddyfile.platform").read_text(encoding="utf-8")
    if "{$DOMAIN} {" not in caddy or "reverse_proxy landing:8080" not in caddy:
        errors.append("platform Caddyfile must serve Landing on the primary domain")
    if "{$FINANCE_DOMAIN} {" not in caddy or "reverse_proxy web:8080" not in caddy:
        errors.append("platform Caddyfile must serve Finance only on its product domain")
    if "{$LIFE_DOMAIN} {" not in caddy or "reverse_proxy life:8080" not in caddy:
        errors.append("platform Caddyfile must serve Life on its product domain")

    if errors:
        print("Validation failed:")
        for error in errors:
            print(f"- {error}")
        return 1

    print("Static project validation passed.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
