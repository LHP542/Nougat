# Nougat

> Projekt der **Arbeitsgruppe 5424 IT-Basis-Dienste** der Landeshauptstadt Potsdam.
> Alle dienstlichen Repos liegen in der Organisation [LHP542](https://git.flexfamily.de/LHP542)
> auf git.flexfamily.de.
>
> **Dienstrechner laden nichts aus dem Internet.** Updates holen die Anwendungen
> dort aus dem Samba-Share, nicht von einem Server draußen.

[![Release](https://git.flexfamily.de/LHP542/Nougat/badges/release.svg)](https://git.flexfamily.de/LHP542/Nougat/releases)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

Offline-NuGet-Bundle-Builder für die Repos der Organisation LHP542 — Desktop-App für Windows und Linux (C# / .NET 10 / Avalonia 12).

Nougat listet die Repos der Organisation `LHP542` auf git.flexfamily.de per Checkbox auf, sammelt aus den markierten Repos alle `PackageReference`-Einträge (inklusive Central Package Management), führt `dotnet restore` aus und schreibt einen flachen `NuGet-Local`-Ordner samt `nuget.config.windows` und `README.txt` — bereit für die Offline-Installation auf dem Arbeitslaptop.

Nachbau/Ersatz des `nuget-offline-bundle.sh`-Skripts, aber datengetrieben: keine hart-codierte Paketliste mehr.

## Features

- Repo-Liste der Organisation `LHP542` via API (mit Cache + optionalem Token für den Zugriff auf private Repos).
- Ein gemeinsames Bundle aus mehreren Repos. Legen zwei Repos dasselbe Paket in **unterschiedlichen Versionen** fest, landen **beide** im Bundle — jedes Repo wird einzeln restored und bekommt genau die Version, die es selbst festlegt. (Nur die höchste zu nehmen wäre gefährlich: NuGet löst offline still auf die nächsthöhere auf, und der Fehler zeigt sich erst zur Laufzeit.)
- CPM-aware: `PackageReference` ohne Version wird per `Directory.Packages.props` desselben Repos aufgelöst.
- **Transitive Pins inklusive:** Nutzt ein Repo `CentralPackageTransitivePinningEnabled`, stehen in seiner `Directory.Packages.props` Pakete, die kein Projekt direkt referenziert. Nougat sammelt sie mit und pinnt sie im Anker genauso — sonst landen im Bundle die niedrigsten statt der tatsächlich aufgelösten Versionen.
- **Baut gegen alle TargetFrameworks der ausgewählten Repos**, damit auch die Windows-Ref- und Runtime-Packs eines `net10.0-windows`-Projekts im Bundle liegen.
- Live-Log des `dotnet restore`-Fortschritts im UI, mit Cancel-Support.
- .NET-10-SDK-Bootstrap: falls kein passendes SDK gefunden wird, kann Nougat es per `dotnet-install`-Skript nach `~/.dotnet-nougat` installieren (kein sudo, kein System-Umbau).
- Zielordner wird vor jedem Lauf komplett geleert — analog zum Referenz-Skript.
- PAT verschlüsselt (AES-GCM mit lokalem Master-Key), nie im Klartext gespeichert, im Log automatisch maskiert.
- Oberfläche auf **Deutsch und Englisch**, live umschaltbar in den Einstellungen — ohne Neustart.
- **Update-Check und Selbst-Update** über GitHub Releases: Das Über-Fenster lädt die neue Version, tauscht sie aus und startet Nougat neu.

## Installation

Fertige Pakete gibt es auf der [Releases-Seite](https://git.flexfamily.de/LHP542/Nougat/releases):

**Windows:** `Nougat-X.Y.Z-win-x64.zip` herunterladen, entpacken, `Nougat.exe` starten. Keine Installation nötig (self-contained, .NET ist enthalten).

**Linux (AppImage, empfohlen):**

```bash
chmod +x Nougat-*-x86_64.AppImage
./Nougat-*-x86_64.AppImage
```

**Linux (tar.gz):** `Nougat-X.Y.Z-linux-x64.tar.gz` entpacken und `./Nougat` starten.

## Bedienung

1. **Repo-Liste laden:** Beim ersten Start (oder über „Aktualisieren") holt Nougat die Repo-Liste von GitHub. Ergebnis wird lokal gecached (TTL in den Einstellungen).
2. **Repos auswählen:** Checkbox pro Repo — die Auswahl bleibt zwischen den Sessions erhalten.
3. **Zielordner prüfen:** Der Zielordner steht im Footer (Default: `~/nuget-offline-work/NuGet-Local`).
4. **„Bundle bauen":** Nougat analysiert die csproj-/`Directory.Packages.props`-Dateien via GitHub Contents-API, erzeugt pro Repo ein Anker-Projekt und lässt `dotnet restore` durchlaufen — alle in denselben Paket-Cache. Anschließend werden alle `.nupkg` flach in den Zielordner kopiert, dazu `nuget.config.windows` und `README.txt`.
5. **Auf den Zielrechner übertragen:** Ordnerinhalt nach `C:\NuGet-Local` kopieren, `nuget.config.windows` in `nuget.config` umbenennen — Details stehen in der generierten `README.txt`.

## Einstellungen

Erreichbar über den Menü-Eintrag „Einstellungen". Persistiert unter `$XDG_CONFIG_HOME/Nougat/settings.json` (Linux/macOS) bzw. `%APPDATA%\Nougat\settings.json` (Windows):

- **GitHub PAT (optional):** Erhöht das API-Rate-Limit von 60/h auf 5000/h. Wird AES-GCM-verschlüsselt mit einem lokal generierten Master-Key gespeichert.
- **Zielordner / Arbeitsverzeichnis:** Frei wählbar.
- **Target-RIDs:** `win-x64` (Default), `linux-x64`, `osx-x64` — je aktiver RID wird zusätzlich `dotnet restore --runtime <rid>` ausgeführt.
- **Repo-Cache-TTL (Stunden):** Nach Ablauf wird die Repo-Liste beim nächsten Start neu geladen.
- **Anzeigesprache:** Deutsch oder Englisch. Die Umschaltung wirkt sofort in allen offenen Fenstern.

## .NET-SDK-Handling

Über den Menü-Eintrag „.NET SDK installieren" kann Nougat ein SDK 10 nach `~/.dotnet-nougat` bootstrappen (via `dotnet-install.sh` / `.ps1`). Das globale `~/.dotnet` bleibt unangetastet. Erkennt Nougat ein System-SDK ≥ 10 auf `PATH`, wird das verwendet.

## Updates

Beim Start prüft Nougat im Hintergrund, ob ein neueres Release vorliegt, und schreibt das Ergebnis als Zeile ins Log-Panel. Ist der Rechner offline oder blockt ein Proxy, passiert nichts weiter — der Check stört den Betrieb nie.

Installiert wird nur auf Klick: **Menü › Über › „⬇ Update installieren"**. Nougat lädt das zur Plattform passende Paket, startet ein Austausch-Skript und beendet sich; danach läuft die neue Version. Das Protokoll des Austauschs liegt unter `$XDG_STATE_HOME/Nougat/update.log` (Linux) bzw. im Temp-Ordner des Update-Laufs (Windows).

Der Update-Check braucht keinen PAT, solange das Repo öffentlich ist. Ein gesetzter PAT wird trotzdem mitgeschickt — damit funktioniert der Weg unverändert, falls die Organisation ihre Repos später auf privat stellt.

## Logs & Fehlersuche

Logdateien liegen im Unterordner `logs/` neben der Anwendung (Tagesarchiv, 14 Tage). Bei einem Problem bitte ein Issue mit der aktuellen Logdatei eröffnen — PATs werden automatisch maskiert.

## Entwicklung

```bash
dotnet build       # bauen
dotnet test        # 50 Unit-Tests
dotnet run --project Nougat
```

Release: VS-Code-Task „release (tag + push)" — prüft den Git-Zustand, setzt den Tag `vX.Y.Z` und stößt die GitHub-Action an, die alle Pakete baut.

## Lizenz

MIT — siehe [LICENSE](LICENSE).

---

☕ Gefällt dir das Tool? [Buy me a coffee](https://buymeacoffee.com/kroste)
