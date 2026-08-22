# Nougat

## Grundlagen

- **Was:** Avalonia-Desktop-App, die aus mehreren Repos der GitHub-Organisation **LHP542** ein Offline-NuGet-Bundle erzeugt. Datengetriebener Ersatz für `nuget-offline-bundle.sh`.
- **Stack:** C# / .NET 10 / Avalonia 12, CommunityToolkit.Mvvm, Microsoft.Extensions.DependencyInjection, NLog (mit Secret-Masking), xunit.v3 + FluentAssertions 7.x
- **Struktur:** Flach (kein `src/`), `.slnx`, Central Package Management (`Directory.Packages.props`), `Directory.Build.props`, MinVer (Tags `v*`)
- **Konventionen:** GlobalExceptionHandler, InfoWindow mit Version + BMC-Button, `TreatWarningsAsErrors`, `EnforceCodeStyleInBuild`
- **Kommunikation:** Deutsch, „du". Lars entwirft, Claude implementiert.
- **Schreibweise:** Echte Umlaute (ä ö ü ß) in allen deutschen Texten — UI, Logs, Kommentare, Doku. Die Ersatzschreibweise `ae/oe/ue/ss` ist verboten. Ausnahme: PowerShell-Skripte brauchen dafür ein UTF-8-BOM (Windows PowerShell 5.1 liest sonst ANSI).

## Aktueller Stand

- **Kern-Services fertig:** `GithubRepoService` (Paginierung, PAT, Orgs- und User-Endpunkt), `RepoCacheService`, `CsprojAnalyzer` (CPM-aware), `PackageDeduplicator` (SemVer), `AnchorProjectGenerator`, `ProcessRunner` (Live-Streaming), `DotnetSdkService` (Bootstrap nach `~/.dotnet-nougat`), `RestoreRunner`, `BundleAssembler`, `NugetConfigWriter`, `BundleOrchestrator`.
- **UI:** MainWindow (Repos links, Log rechts, Fortschritt im Footer), SettingsWindow, InfoWindow, SdkInstallWindow, ErrorWindow. Alles im Card-Look mit der vollen Style-Bibliothek aus dem kroste-avalonia-Skill.
- **Lokalisierung:** Deutsch + Englisch, 73 Keys, Live-Wechsel über die Einstellungen ohne Neustart.
- **Self-Update:** `UpdateService` prüft `/releases/latest` und installiert auf Klick im Über-Fenster (Windows-ZIP, Linux-tar.gz, AppImage).
- **50 Unit-Tests, alle grün** (< 100 ms). `dotnet build` und `dotnet test` grün, CI auf GitHub grün.
- **Live gegen die echte GitHub-API geprüft:** App startet, Tray installiert sich, Update-Check läuft (200, Version korrekt verglichen), Logfile sauber.
- **Offen:** End-to-End-Bundle-Bau mit echtem `dotnet restore` ist noch nicht gelaufen.

## Roadmap

1. **End-to-End-Test** mit 2 Repos, Zielordner prüfen (`find *.nupkg | wc -l` ≥ 60, `nuget.config.windows` vorhanden).
2. **Windows-Cross-Check** auf dem Arbeitslaptop: erzeugtes Bundle nach `C:\NuGet-Local` kopieren, ein Projekt offline restoren. Dabei `scripts/build_icon.ps1` einmal laufen lassen — der PowerShell-Port ist bisher ungetestet (auf Bazzite ist kein `pwsh` installiert).
3. **Self-Update real durchspielen:** Tag setzen, Release abwarten, Update aus einer älteren Version heraus installieren. Der Pfad ist nach Muster gebaut, aber noch nie durchlaufen.
4. **README auf Englisch?** Der Skill verlangt das für multilinguale Apps. Für ein internes Werkzeug einer deutschsprachigen Arbeitsgruppe ist das fraglich — bewusst offen, Lars entscheidet.
5. **DPAPI-Backend** für Windows, falls jemand vom AES-GCM-Store weg will (aktuell reicht der auch unter Windows).

## Referenz

- Vorbild-Skript: `nuget-offline-bundle.sh` (bewusst im Repo-Root belassen).
- App-Struktur folgt Allpaca (Kroste-Referenzprojekt für Konventionen).
- **CPM-Merge-Regel** im `CsprojAnalyzer`: PackageReference ohne Version wird über `Directory.Packages.props` desselben Repos aufgelöst; nicht auffindbar → Warning, überspringen (nie eine Version raten).
- **Deduplizierung:** `Semver.SortOrderComparer` bevorzugt, dann `System.Version`, dann `StringComparer.OrdinalIgnoreCase` als Notnagel; Range-Strings wie `[7.2.2,8.0.0)` werden auf die untere Grenze normalisiert. `PackageVersionSelector.IsNewer` nutzt dieselbe Ordnung für den Update-Check.
- **SDK-Bootstrap** installiert bewusst nach `~/.dotnet-nougat` (nicht `~/.dotnet`), damit das globale Distrobox/Bazzite-SDK unangetastet bleibt.
- **Secrets:** GitHub-PAT inline im `settings.json` als `"pat": "ENC1:<base64>"` (AES-GCM, Master-Key `~/.config/Nougat/protect.key`, chmod 0600). Bewusst kein libsecret — vermeidet DBus-Dependency.
- **Versionsquelle ist `AppVersion.Current`** (InformationalVersion ohne `+sha`), nirgends `AssemblyName.Version`: MinVer rundet die vierstellige AssemblyVersion bei Prereleases auf X.Y.Z.0, die App würde zwischen zwei Tags behaupten, sie sei das Release.
- **Update-Download über die Asset-API-URL**, nicht über `browser_download_url`: Mit `Accept: application/octet-stream` funktioniert derselbe Aufruf für öffentliche *und* private Repos. `LHP542/Nougat` ist aktuell **öffentlich** (anonymer Check liefert 200, kein PAT nötig) — der `UpdateService` nutzt den named HttpClient `"github"` trotzdem, damit ein vorhandener PAT automatisch mitgeht und der Weg beim Umschalten auf privat unverändert trägt. Ein 404 beim Check ist dann der erste Hinweis auf einen fehlenden PAT.
- **Log-Filter:** `System.Net.Http.*` wird bis `maxlevel="Info"` verworfen — `Microsoft.Extensions.Http` schreibt seine vier Zeilen pro Request auf Info, ein Filter bis `Debug` fängt davon nichts.
- **Bootstrap-Persistenz:** `MainWindowViewModel._persistSettings` verhindert, dass das Setzen von `OutputDirectory` im Konstruktor die `settings.json` schreibt. Ohne das Flag speicherte jeder App-Start ungefragt.
- **Style-Klassen und Resource-Keys scheitern in Avalonia still.** Nach jedem Umbau abgleichen, welche `Classes=`/`DynamicResource` referenziert und welche definiert sind — `Classes="menu"` stand hier zwei Commits lang ohne zugehörigen Style im XAML.
