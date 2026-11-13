#!/usr/bin/env python3
"""Acrescenta uma versão ao manifest.json do repositório de plugins do Jellyfin.

Uso: manifest.py <manifest.json> <versão X.Y.Z> <targetAbi> <url do zip> <md5> <changelog>
O Jellyfin lê esse arquivo (Painel → Plugins → Repositórios) e instala/atualiza o plugin.
"""
import json
import sys
from datetime import datetime, timezone
from pathlib import Path

GUID = "6f1b7c1e-8a4e-4c2b-9b7a-2f3d5e9a1b40"

path, version, abi, url, md5, changelog = sys.argv[1:7]
file = Path(path)
manifest = json.loads(file.read_text()) if file.exists() else []

plugin = next((p for p in manifest if p["guid"] == GUID), None)
if plugin is None:
    plugin = {
        "guid": GUID,
        "name": "Bergafin Auth",
        "description": "Login no Jellyfin só pelo Authentik (OIDC), com Quick Connect aprovado pelo Authentik.",
        "overview": "Login só pelo Authentik",
        "owner": "Agelinena",
        "category": "Authentication",
        "versions": [],
    }
    manifest.append(plugin)

full = version + ".0"
plugin["versions"] = [v for v in plugin["versions"] if v["version"] != full]
plugin["versions"].insert(0, {
    "version": full,
    "changelog": changelog,
    "targetAbi": abi,
    "sourceUrl": url,
    "checksum": md5,
    "timestamp": datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"),
})
# Mantém as 20 versões mais recentes
plugin["versions"] = plugin["versions"][:20]
file.write_text(json.dumps(manifest, indent=2, ensure_ascii=False) + "\n")
