# Nougat

> Projekt der **Arbeitsgruppe 5424 IT-Basis-Dienste** der Landeshauptstadt Potsdam.
> Alle dienstlichen Repos liegen in der GitHub-Organisation [LHP542](https://github.com/LHP542).

[![CI](https://github.com/LHP542/Nougat/actions/workflows/ci.yml/badge.svg)](https://github.com/LHP542/Nougat/actions/workflows/ci.yml)
[![Release](https://img.shields.io/github/v/release/LHP542/Nougat)](https://github.com/LHP542/Nougat/releases)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

Offline-NuGet-Bundle-Builder fuer Kroste-Repos — Desktop-App fuer Windows und Linux (C# / .NET 10 / Avalonia 12).

Nougat listet die GitHub-Repos unter `Kroste` per Checkbox auf, sammelt aus den markierten Repos alle `PackageReference`-Eintraege (inklusive Central Package Management), fuehrt `dotnet restore` aus und schreibt einen flachen `NuGet-Local`-Ordner samt `nuget.config.windows` und `README.txt` — bereit fuer die Offline-Installation auf dem Arbeitslaptop.

Nachbau/Ersatz des `nuget-offline-bundle.sh`-Skripts, aber datengetrieben: keine hart-codierte Paketliste mehr.

## Features

- Repo-Liste vom GitHub-User `Kroste` via API (mit Cache + optionalem PAT fuer 5000 Requests/h).
- Ein gemeinsames Bundle aus mehreren Repos, mit automatischer Deduplizierung bei Version-Konflikten (hoechste Version gewinnt).
- CPM-aware: `PackageReference` ohne Version wird per `Directory.Packages.props` desselben Repos aufgeloest.
- Live-Log des `dotnet restore`-Fortschritts im UI, mit Cancel-Support.
- .NET-10-SDK-Bootstrap: falls kein passendes SDK gefunden wird, kann Nougat es per `dotnet-install`-Skript nach `~/.dotnet-nougat` installieren (kein sudo, kein System-Umbau).
- Zielordner wird vor jedem Lauf komplett geleert — analog zum Referenz-Skript.
- PAT verschluesselt (AES-GCM mit lokalem Master-Key), nie im Klartext gespeichert, im Log automatisch maskiert.

## Installation

Fertige Pakete gibt es auf der [Releases-Seite](https://github.com/LHP542/Nougat/releases):

**Windows:** `Nougat-X.Y.Z-win-x64.zip` herunterladen, entpacken, `Nougat.exe` starten. Keine Installation noetig (self-contained, .NET ist enthalten).

**Linux (AppImage, empfohlen):**

```bash
chmod +x Nougat-*-x86_64.AppImage
./Nougat-*-x86_64.AppImage
```

**Linux (tar.gz):** `Nougat-X.Y.Z-linux-x64.tar.gz` entpacken und `./Nougat` starten.

## Bedienung

1. **Repo-Liste laden:** Beim ersten Start (oder ueber „Aktualisieren") holt Nougat die Repo-Liste von GitHub. Ergebnis wird lokal gecached (TTL in den Einstellungen).
2. **Repos auswaehlen:** Checkbox pro Repo — die Auswahl bleibt zwischen den Sessions erhalten.
3. **Zielordner pruefen:** Der Zielordner steht im Footer (Default: `~/nuget-offline-work/NuGet-Local`).
4. **„Bundle bauen":** Nougat analysiert die csproj-/`Directory.Packages.props`-Dateien via GitHub Contents-API, dedupliziert die Pakete (bei Konflikten hoechste Version), erzeugt ein Anker-Projekt und laesst `dotnet restore` durchlaufen. Anschliessend werden alle `.nupkg` flach in den Zielordner kopiert, dazu `nuget.config.windows` und `README.txt`.
5. **Auf den Zielrechner uebertragen:** Ordnerinhalt nach `C:\NuGet-Local` kopieren, `nuget.config.windows` in `nuget.config` umbenennen — Details stehen in der generierten `README.txt`.

## Einstellungen

Erreichbar ueber den Menue-Eintrag „Einstellungen". Persistiert unter `$XDG_CONFIG_HOME/Nougat/settings.json` (Linux/macOS) bzw. `%APPDATA%\Nougat\settings.json` (Windows):

- **GitHub PAT (optional):** Erhoeht das API-Rate-Limit von 60/h auf 5000/h. Wird AES-GCM-verschluesselt mit einem lokal generierten Master-Key gespeichert.
- **Zielordner / Arbeitsverzeichnis:** Frei waehlbar.
- **Target-RIDs:** `win-x64` (Default), `linux-x64`, `osx-x64` — je aktiver RID wird zusaetzlich `dotnet restore --runtime <rid>` ausgefuehrt.
- **Repo-Cache-TTL (Stunden):** Nach Ablauf wird die Repo-Liste beim naechsten Start neu geladen.

## .NET-SDK-Handling

Ueber den Menue-Eintrag „.NET SDK installieren" kann Nougat ein SDK 10 nach `~/.dotnet-nougat` bootstrappen (via `dotnet-install.sh` / `.ps1`). Das globale `~/.dotnet` bleibt unangetastet. Erkennt Nougat ein System-SDK ≥ 10 auf `PATH`, wird das verwendet.

## Logs & Fehlersuche

Logdateien liegen im Unterordner `logs/` neben der Anwendung (Tagesarchiv, 14 Tage). Bei einem Problem bitte ein Issue mit der aktuellen Logdatei eroeffnen — PATs werden automatisch maskiert.

## Entwicklung

```bash
dotnet build       # bauen
dotnet test        # 34 Unit-Tests
dotnet run --project Nougat
```

Release: VS-Code-Task „release (tag + push)" — prueft den Git-Zustand, setzt den Tag `vX.Y.Z` und stoesst die GitHub-Action an, die alle Pakete baut.

## Lizenz

MIT — siehe [LICENSE](LICENSE).

---

☕ Gefaellt dir das Tool? [Buy me a coffee](https://buymeacoffee.com/kroste)
