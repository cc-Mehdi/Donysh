#!/usr/bin/env python3
from __future__ import annotations

import json
import sys
import xml.etree.ElementTree as ET

import yaml
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
    "HesabYar.Web/Pages/Shared/_OnboardingGuide.cshtml",
    "HesabYar.Web/ModelBinding/FlexibleDecimalModelBinder.cs",
    "HesabYar.Web/ModelBinding/PersianDateOnlyModelBinder.cs",
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
    except Exception as exc:
        errors.append(f"invalid json: {exc}")

    try:
        ET.parse(ROOT / "HesabYar.Web/HesabYar.Web.csproj")
    except Exception as exc:
        errors.append(f"invalid csproj xml: {exc}")

    for path in (ROOT / "HesabYar.Web").rglob("*.cs"):
        text = path.read_text(encoding="utf-8")
        if not balanced(text, "{", "}"):
            errors.append(f"unbalanced braces: {path.relative_to(ROOT)}")
        if not balanced(text, "(", ")"):
            errors.append(f"unbalanced parentheses: {path.relative_to(ROOT)}")
        if not balanced(text, "[", "]"):
            errors.append(f"unbalanced brackets: {path.relative_to(ROOT)}")
        if "TODO" in text or "NotImplementedException" in text:
            errors.append(f"unfinished marker: {path.relative_to(ROOT)}")

    for compose_name in ["docker-compose.yml", "docker-compose.domain.yml"]:
        compose = (ROOT / compose_name).read_text(encoding="utf-8")
        try:
            parsed_compose = yaml.safe_load(compose)
            if not isinstance(parsed_compose, dict) or "services" not in parsed_compose:
                errors.append(f"{compose_name} has no services mapping")
        except Exception as exc:
            errors.append(f"invalid {compose_name} yaml: {exc}")

    compose = (ROOT / "docker-compose.yml").read_text(encoding="utf-8")
    for token in ["postgres:17-alpine", "DataProtection__KeysPath", "${APP_PORT:-8080}:8080"]:
        if token not in compose:
            errors.append(f"compose token missing: {token}")

    workflow = (ROOT / ".github/workflows/deploy.yml").read_text(encoding="utf-8")
    try:
        yaml.safe_load(workflow)
    except Exception as exc:
        errors.append(f"invalid GitHub Actions yaml: {exc}")

    pages_text = "\n".join(
        path.read_text(encoding="utf-8")
        for path in (ROOT / "HesabYar.Web/Pages").rglob("*.cshtml")
    )
    for token in ["data-money", "data-persian-date", "data-guide-open"]:
        if token not in pages_text:
            errors.append(f"UI token missing: {token}")

    if errors:
        print("Validation failed:")
        for error in errors:
            print(f"- {error}")
        return 1

    print("Static project validation passed.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
