# Nougat

## Grundlagen

- **Was:** Avalonia-Desktop-App, die aus mehreren Kroste-GitHub-Repos ein Offline-NuGet-Bundle erzeugt. Datengetriebener Ersatz für `nuget-offline-bundle.sh`.
- **Stack:** C# / .NET 10 / Avalonia 12, CommunityToolkit.Mvvm, Microsoft.Extensions.DependencyInjection, NLog (mit Secret-Masking), xunit.v3 + FluentAssertions 7.x
- **Struktur:** Flach (kein `src/`), `.slnx`, Central Package Management (`Directory.Packages.props`), `Directory.Build.props`, MinVer (Tags `v*`)
- **Konventionen:** GlobalExceptionHandler, InfoWindow mit Version + BMC-Button, `TreatWarningsAsErrors`
- **Kommunikation:** Deutsch, „du". Lars entwirft, Claude implementiert.

## Aktueller Stand

- Projekt initial angelegt: alle Kroste-Standard-Templates übernommen, App-Icon (Bonbon in Gold) generiert, DI-Bootstrapping steht, System-Tray, ChromeWindow, InfoWindow mit BMC-Button.
- Kern-Services fertig: `GithubRepoService` (mit Paginierung + PAT-Header), `RepoCacheService`, `CsprojAnalyzer` (CPM-aware), `PackageDeduplicator` (SemVer-basiert), `AnchorProjectGenerator`, `ProcessRunner` (Live-Streaming), `DotnetSdkService` (Bootstrap nach `~/.dotnet-nougat`), `RestoreRunner`, `BundleAssembler`, `NugetConfigWriter`, `BundleOrchestrator`.
- UI: MainWindow (Repos links, Log rechts, Progress im Footer), SettingsWindow, InfoWindow, SdkInstallWindow.
- 34 Unit-Tests, alle grün (< 100 ms).
- Basis: `dotnet build` und `dotnet test` grün; End-to-End-Test mit echter GitHub-API + `dotnet restore` steht noch aus (Netzwerk-abhängig).

## Roadmap

1. **git init + ersten Commit + Tag v0.1.0** damit MinVer eine echte Version berechnet.
2. **End-to-End-Test** mit 2 Repos (DTM + NetScanner), Zielordner prüfen (`find *.nupkg | wc -l` ≥ 60, `nuget.config.windows` vorhanden).
3. **Windows-Cross-Check** auf dem Arbeitslaptop: erzeugtes Bundle nach `C:\NuGet-Local` kopieren, Kroste-Projekt offline restoren.
4. **UpdateService** noch nicht implementiert — der Skill sieht das vor, ist aber optional.
5. **DPAPI-Backend** für Windows falls jemand vom AES-GCM-Store weg will (aktuell reicht der auch unter Windows).

## Referenz

- Vorbild-Skript: `/home/OsteL/Entwicklung/Nougat/nuget-offline-bundle.sh` (bewusst im Repo-Root belassen).
- App-Struktur folgt Allpaca (Kroste-Referenzprojekt für Konventionen).
- CPM-Merge-Regel im `CsprojAnalyzer`: PackageReference ohne Version wird über `Directory.Packages.props` desselben Repos aufgelöst; nicht auffindbar → Warning, überspringen (nie eine Version raten).
- Deduplizierung: `Semver.SortOrderComparer` bevorzugt, dann `System.Version`, dann `StringComparer.OrdinalIgnoreCase` als Notnagel; Range-Strings wie `[7.2.2,8.0.0)` werden auf die untere Grenze normalisiert.
- SDK-Bootstrap installiert bewusst nach `~/.dotnet-nougat` (nicht `~/.dotnet`), damit das globale Distrobox/Bazzite-SDK unangetastet bleibt.
- Secrets (GitHub-PAT) inline im `settings.json` als `"pat": "ENC1:<base64>"` (AES-GCM, Master-Key `~/.config/Nougat/protect.key`, chmod 0600). Bewusst kein libsecret — vermeidet DBus-Dependency.
