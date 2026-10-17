#!/usr/bin/env python3
"""Inventory only the project's lock files and installed package license metadata.

No network calls, dependency changes, user configuration, or application data reads.
Upstream notices are copied verbatim. This script grants no license to project code.
"""
from __future__ import annotations

import argparse
import ctypes
import datetime as dt
import hashlib
import json
from pathlib import Path
import re
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
DOCS = ROOT / "docs"
NOTICE_NAME = re.compile(r"license|licence|notice|copying", re.I)


def sha(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def label(path: Path) -> str:
    try:
        return path.relative_to(ROOT).as_posix()
    except ValueError:
        return path.as_posix()


def license_files(directory: Path) -> list[Path]:
    if not directory.is_dir():
        return []
    files = [item for item in directory.iterdir() if item.is_file() and NOTICE_NAME.search(item.name)]
    for child in (directory / "licenses", directory / "Licenses"):
        if child.is_dir():
            files.extend(item for item in child.rglob("*") if item.is_file())
    # Never follow a package's license symlink into an unrelated directory.
    return sorted({item for item in files if item.resolve().is_relative_to(directory.resolve())})


def file_record(path: Path) -> dict:
    return {"path": label(path), "sha256": sha(path)}


def read_json(path: Path) -> dict:
    return json.loads(path.read_text(encoding="utf-8-sig"))


def npm_inventory(scope: str, fallback: Path | None, notices: list[tuple[str, Path]]) -> list[dict]:
    directory = ROOT / scope
    lock_path = directory / "package-lock.json"
    lock = read_json(lock_path)
    root = lock["packages"][""]
    direct_runtime = root.get("dependencies", {})
    direct_dev = root.get("devDependencies", {})
    records = []
    for location, package in sorted(lock["packages"].items()):
        if not location:
            continue
        name = location.rsplit("node_modules/", 1)[-1]
        package_dir = directory / location
        installed_from = "project-node_modules"
        if not (package_dir / "package.json").is_file() and name == "playwright-core" and fallback:
            package_dir = fallback
            installed_from = "external-test-tool-package; no REA code copied"
        installed = (package_dir / "package.json").is_file()
        metadata = read_json(package_dir / "package.json") if installed else {}
        lock_license = package.get("license")
        metadata_license = metadata.get("license")
        files = license_files(package_dir) if installed else []
        for path in files:
            notices.append((f"npm {name}@{package['version']} ({scope})", path))
        status = "installed-metadata" if metadata_license else "lock-metadata-only" if lock_license else "unverified"
        if installed and metadata.get("version") != package["version"]:
            status = "version-mismatch"
        if lock_license and metadata_license and lock_license != metadata_license:
            status = "license-mismatch"
        records.append({
            "ecosystem": "npm", "scope": scope, "name": name, "version": package["version"],
            "direct": name in direct_runtime or name in direct_dev,
            "directUse": "runtime" if name in direct_runtime else "development/test" if name in direct_dev else None,
            "development": bool(package.get("dev", False)), "optional": bool(package.get("optional", False)),
            "lockfile": label(lock_path), "location": location, "integrity": package.get("integrity"),
            "resolved": package.get("resolved"), "installed": installed,
            "installedFrom": label(package_dir) if installed else None,
            "installationKind": installed_from if installed else "not-installed-on-this-platform",
            "installedVersion": metadata.get("version"),
            "declaredLicense": metadata_license or lock_license,
            "lockDeclaredLicense": lock_license, "installedDeclaredLicense": metadata_license,
            "licenseStatus": status,
            "repository": metadata.get("repository"), "homepage": metadata.get("homepage"),
            "licenseFiles": [file_record(path) for path in files],
        })
    return records


def nuget_inventory(cache: Path, notices: list[tuple[str, Path]]) -> list[dict]:
    graph: dict[tuple[str, str], dict] = {}
    for project in ("Hengchuan.Api", "Hengchuan.Tests"):
        lock_path = ROOT / "backend" / project / "packages.lock.json"
        for framework, dependencies in read_json(lock_path)["dependencies"].items():
            for name, package in dependencies.items():
                if package.get("type") == "Project":
                    continue  # Original project code is not a third-party dependency.
                key = (name.lower(), package["resolved"])
                record = graph.setdefault(key, {
                    "ecosystem": "NuGet", "name": name, "version": package["resolved"],
                    "contentHash": package.get("contentHash"), "projects": [],
                })
                record["projects"].append({"lockfile": label(lock_path), "framework": framework, "type": package["type"]})
    for (lower_name, version), record in graph.items():
        directory = cache / lower_name / version
        nuspecs = list(directory.glob("*.nuspec"))
        files = license_files(directory)
        if not nuspecs:
            record.update({"installed": False, "licenseStatus": "unverified-missing-nuspec", "licenseFiles": []})
            continue
        nuspec = nuspecs[0]
        metadata = next(child for child in ET.parse(nuspec).getroot() if child.tag.endswith("metadata"))
        values = {child.tag.rsplit("}", 1)[-1]: (child.text or "").strip() for child in metadata}
        license_node = next((child for child in metadata if child.tag.rsplit("}", 1)[-1] == "license"), None)
        repository = next((child.attrib for child in metadata if child.tag.rsplit("}", 1)[-1] == "repository"), None)
        declaration = None if license_node is None else {"type": license_node.get("type"), "value": (license_node.text or "").strip()}
        if declaration and declaration["type"] == "file":
            candidate = directory / declaration["value"]
            if candidate.is_file() and candidate.resolve().is_relative_to(directory.resolve()):
                files = sorted(set([*files, candidate]))
        for path in files:
            notices.append((f"NuGet {record['name']}@{version}", path))
        record.update({
            "installed": True, "nuspec": file_record(nuspec), "installedVersion": values.get("version"),
            "licenseDeclaration": declaration, "legacyLicenseUrl": values.get("licenseUrl"),
            "declaredLicense": declaration["value"] if declaration and declaration["type"] == "expression" else None,
            "licenseStatus": "nuspec-expression" if declaration and declaration["type"] == "expression" else "nuspec-file" if declaration else "unverified-legacy-license-url",
            "authors": values.get("authors"), "copyright": values.get("copyright"),
            "repository": repository, "projectUrl": values.get("projectUrl"),
            "licenseFiles": [file_record(path) for path in files],
        })
    return sorted(graph.values(), key=lambda row: (row["name"].lower(), row["version"]))


def native_sqlite(cache: Path, nuget: list[dict]) -> dict:
    wrapper = next(row for row in nuget if row["name"].lower() == "sqlitepclraw.lib.e_sqlite3")
    path = cache / "sqlitepclraw.lib.e_sqlite3" / wrapper["version"] / "runtimes/linux-x64/native/libe_sqlite3.so"
    result = {
        "component": "SQLite native engine (distinct from SQLitePCLRaw wrapper/package)",
        "package": f"SQLitePCLRaw.lib.e_sqlite3 {wrapper['version']}",
        "license": "LicenseRef-SQLite-Public-Domain", "licenseSource": "https://www.sqlite.org/copyright.html",
        "wrapperPackageLicense": "Apache-2.0",
        "verification": "Only sqlite3_libversion() on the installed Linux x64 library; no database opened, no application data read",
        "otherPlatforms": "Package contains other native builds; their binary versions were not executed/verified here",
    }
    if path.is_file():
        library = ctypes.CDLL(str(path))
        library.sqlite3_libversion.restype = ctypes.c_char_p
        result.update({"version": library.sqlite3_libversion().decode("ascii"), "binary": file_record(path), "status": "verified-local-version; upstream-public-domain-declaration"})
    else:
        result.update({"version": None, "status": "unverified-native-binary-not-installed"})
    return result


def copy_notices(notices: list[tuple[str, Path]], nuget: list[dict]) -> dict:
    groups: dict[str, dict] = {}
    for component, path in notices:
        digest = sha(path)
        group = groups.setdefault(digest, {"components": [], "sources": [], "path": path})
        if component not in group["components"]:
            group["components"].append(component)
        group["sources"].append(label(path))
    lines = [
        "HENGCHUAN OBSERVATION — THIRD-PARTY PACKAGE LICENSES AND NOTICES",
        "Generated from installed packages. Upstream content below is preserved verbatim.",
        "Identical files are deduplicated by SHA-256 with all component attributions retained.",
        "This is NOT a license grant for original Hengchuan application code.",
        "The full package inventory, lockfile versions, and unverified declarations are in dependency-licenses.json.",
        "\nNuGet package declarations (not replacement license texts):",
    ]
    for row in nuget:
        declaration = row.get("licenseDeclaration")
        lines.append(f"{row['name']} {row['version']}: {declaration or 'UNVERIFIED legacy license URL: ' + str(row.get('legacyLicenseUrl'))}; copyright: {row.get('copyright')}; authors: {row.get('authors')}")
    for digest, group in sorted(groups.items()):
        lines.extend(["\n" + "=" * 78, "COMPONENTS: " + "; ".join(group["components"]), "SOURCES: " + "; ".join(group["sources"]), "SHA256: " + digest, "=" * 78, group["path"].read_text(encoding="utf-8-sig")])
    target = DOCS / "THIRD-PARTY-NOTICES.txt"
    target.write_text("\n".join(lines) + "\n", encoding="utf-8")
    return {"path": label(target), "sha256": sha(target), "uniqueFiles": len(groups), "attributedFiles": len(notices)}


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--nuget-root", type=Path, default=Path("/workspace/.tools/nuget"))
    parser.add_argument("--playwright-root", type=Path, default=Path("/workspace/rea-tool/node_modules/playwright-core"))
    parser.add_argument("--audit-record", type=Path, help="Optional JSON record of separately executed dotnet vulnerability checks")
    args = parser.parse_args()
    DOCS.mkdir(exist_ok=True)
    notices: list[tuple[str, Path]] = []
    npm = npm_inventory("frontend", None, notices) + npm_inventory("tests", args.playwright_root, notices)
    nuget = nuget_inventory(args.nuget_root, notices)
    previous = DOCS / "dependency-licenses.json"
    audit = read_json(args.audit_record) if args.audit_record else read_json(previous).get("vulnerabilityAudit") if previous.is_file() else None
    unknown = [f"{row['ecosystem']} {row['name']}@{row['version']}: {row['licenseStatus']}" for row in [*npm, *nuget] if row["licenseStatus"].startswith("unverified") or "mismatch" in row["licenseStatus"]]
    inventory = {
        "schemaVersion": 1, "generatedAt": dt.datetime.now(dt.timezone.utc).isoformat(),
        "method": "Locked versions plus installed package.json/license files and NuGet nuspec declarations; no legal opinion or project license grant",
        "projectLicense": "Not assigned; original application code is not automatically relicensed by this inventory",
        "summary": {"frontendLocked": sum(row["scope"] == "frontend" for row in npm), "frontendInstalled": sum(row["scope"] == "frontend" and row["installed"] for row in npm), "acceptanceLocked": sum(row["scope"] == "tests" for row in npm), "uniqueNugetPackages": len(nuget), "unverifiedOrMismatched": unknown},
        "inputs": [file_record(ROOT / relative) for relative in ("frontend/package-lock.json", "tests/package-lock.json", "backend/Hengchuan.Api/packages.lock.json", "backend/Hengchuan.Tests/packages.lock.json")],
        "npm": npm, "nuget": nuget, "nativeSqlite": native_sqlite(args.nuget_root, nuget),
        "notices": copy_notices(notices, nuget), "vulnerabilityAudit": audit,
        "exclusions": ["Original business code, native SVG UI/charts and synthetic fixtures", "Flashcat source code, trademarks and assets: not copied", "REA tool: installed outside the project, not a product/runtime dependency, no REA implementation copied", "LogMind, HolmesGPT and Aurora: not incorporated", "OS Chromium, SDK and container/OS dependency inventories are outside the npm/NuGet lock inventory"],
    }
    (DOCS / "dependency-licenses.json").write_text(json.dumps(inventory, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(inventory["summary"], ensure_ascii=False, indent=2))
    print(f"Notices: {inventory['notices']['uniqueFiles']} unique files; {inventory['notices']['attributedFiles']} attributions")


if __name__ == "__main__":
    main()
