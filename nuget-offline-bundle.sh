#!/usr/bin/env bash
# =============================================================================
#  nuget-offline-bundle.sh
# -----------------------------------------------------------------------------
#  Erzeugt ein Offline-NuGet-Bundle für das DTM-Projekt
#  (Avalonia 11.2.3 + .NET 10, Build-Ziel Windows / win-x64).
#
#  Laufzielsystem:  Bazzite / Fedora Atomic (immutable)
#  Vorgehen:        - .NET 10 SDK in ~/.dotnet installieren (kein sudo, kein reboot)
#                   - Anker-csproj mit allen PackageReferences erzeugen
#                   - dotnet restore (managed + runtime win-x64)
#                   - alle .nupkg flach nach ~/nuget-offline-work/NuGet-Local kopieren
#                   - passende nuget.config für die Windows-Seite mitliefern
#
#  Bedienung:
#      chmod +x nuget-offline-bundle.sh
#      ./nuget-offline-bundle.sh
#
#  Ergebnis liegt anschließend in:
#      ~/nuget-offline-work/NuGet-Local/
#  Dort liegen alle .nupkg + nuget.config.windows + README.txt.
#  Diesen Ordnerinhalt 1:1 auf den Windows-Rechner nach C:\NuGet-Local kopieren.
# =============================================================================

set -euo pipefail

# --- Konfiguration -----------------------------------------------------------
WORK_DIR="${HOME}/nuget-offline-work"
OUT_HIER="${WORK_DIR}/packages-cache"          # hierarchischer NuGet-Cache
OUT_FLAT="${WORK_DIR}/NuGet-Local"             # flacher .nupkg-Ordner (Output)
ANCHOR="${WORK_DIR}/Restore.csproj"
DOTNET_DIR="${HOME}/.dotnet"
DOTNET_CHANNEL="10.0"
TARGET_RIDS=("win-x64")                        # bei Bedarf: ("win-x64" "linux-x64")

# --- Konsolen-Helfer ---------------------------------------------------------
C_G='\033[1;32m'; C_B='\033[1;34m'; C_Y='\033[1;33m'; C_R='\033[1;31m'; C_0='\033[0m'
info() { echo -e "${C_B}[i]${C_0} $*"; }
ok()   { echo -e "${C_G}[+]${C_0} $*"; }
warn() { echo -e "${C_Y}[!]${C_0} $*"; }
err()  { echo -e "${C_R}[x]${C_0} $*" >&2; }
step() { echo; echo -e "${C_B}==== $* ====${C_0}"; }

# --- 1) .NET 10 SDK sicherstellen --------------------------------------------
step "1/5  .NET 10 SDK prüfen / installieren"

ensure_dotnet() {
    # bereits im PATH und passende Version?
    if command -v dotnet >/dev/null 2>&1 \
       && dotnet --list-sdks 2>/dev/null | grep -q '^10\.'; then
        DOTNET_BIN="$(command -v dotnet)"
        ok "dotnet 10 SDK vorhanden: ${DOTNET_BIN} ($(${DOTNET_BIN} --version))"
        return
    fi

    # in ~/.dotnet schon installiert?
    if [[ -x "${DOTNET_DIR}/dotnet" ]] \
       && "${DOTNET_DIR}/dotnet" --list-sdks 2>/dev/null | grep -q '^10\.'; then
        DOTNET_BIN="${DOTNET_DIR}/dotnet"
        export PATH="${DOTNET_DIR}:${PATH}"
        export DOTNET_ROOT="${DOTNET_DIR}"
        ok "dotnet 10 SDK in ${DOTNET_DIR} ($(${DOTNET_BIN} --version))"
        return
    fi

    warn "Kein .NET 10 SDK gefunden -> installiere via dotnet-install.sh"
    mkdir -p "${DOTNET_DIR}"
    local installer="/tmp/dotnet-install.sh"
    curl -fsSL https://dot.net/v1/dotnet-install.sh -o "${installer}"
    chmod +x "${installer}"
    "${installer}" --channel "${DOTNET_CHANNEL}" --install-dir "${DOTNET_DIR}"
    rm -f "${installer}"

    DOTNET_BIN="${DOTNET_DIR}/dotnet"
    export PATH="${DOTNET_DIR}:${PATH}"
    export DOTNET_ROOT="${DOTNET_DIR}"
    ok "Installiert: $(${DOTNET_BIN} --version)"

    info "Tipp: dauerhaft verfügbar machen mit:"
    echo "      echo 'export PATH=\"\$HOME/.dotnet:\$PATH\"' >> ~/.bashrc"
    echo "      echo 'export DOTNET_ROOT=\"\$HOME/.dotnet\"'   >> ~/.bashrc"
}

# Telemetrie aus, EULA bestätigen (läuft sonst interaktiv)
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1

ensure_dotnet

# --- 2) Anker-Projekt anlegen ------------------------------------------------
step "2/5  Anker-Projekt mit allen PackageReferences erzeugen"

mkdir -p "${WORK_DIR}"

cat > "${ANCHOR}" <<'CSPROJ'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <!-- net10.0-windows weggelassen, da Anker nur restored, nicht buildet,
         und die Windows-spezifischen Pakete via Runtime-Schalter win-x64 mitkommen. -->
    <TargetFramework>net10.0</TargetFramework>
    <OutputType>Exe</OutputType>
    <Nullable>enable</Nullable>

    <NuGetAudit>false</NuGetAudit>
    <UseAppHost>false</UseAppHost>
    <EnableDefaultItems>false</EnableDefaultItems>

    <!-- Wir wollen NUR runterladen, nichts bauen -->
    <NoBuild>true</NoBuild>
  </PropertyGroup>

  <ItemGroup>
    <!-- Direkte Pakete aus DTM.csproj / Test-Projekt -->
    <PackageReference Include="NLog" Version="6.1.3" />
    <PackageReference Include="System.Data.Odbc" Version="10.0.7" />
    <PackageReference Include="CommunityToolkit.Mvvm" Version="8.3.2" />
    <PackageReference Include="Avalonia" Version="11.2.3" />
    <PackageReference Include="Avalonia.Desktop" Version="11.2.3" />
    <PackageReference Include="Avalonia.Themes.Fluent" Version="11.2.3" />
    <PackageReference Include="Avalonia.Fonts.Inter" Version="11.2.3" />
    <PackageReference Include="Avalonia.Controls.DataGrid" Version="11.2.3" />
    <PackageReference Include="Avalonia.Diagnostics" Version="11.2.3" Condition="'$(Configuration)' == 'Debug'" />
    <!-- Tmds.DBus.Protocol bewusst auf 0.21.2; höhere Versionen brechen Avalonia 11.2.3.
         NU1903 (CVE-Hinweis) ist für uns unkritisch, da wir die Client-Seite nutzen. -->
    <PackageReference Include="Tmds.DBus.Protocol" Version="0.21.2" />
    <!-- PowerShell-Backend: in-process Runspace statt Process.Start mit Pipes. -->
    <PackageReference Include="Microsoft.PowerShell.SDK" Version="7.6.1" />

    <!-- Test-Stack -->
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.0" />
    <PackageReference Include="xunit" Version="2.9.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
    <PackageReference Include="FluentAssertions" Version="7.0.0" />
  </ItemGroup>
</Project>
CSPROJ

# nuget.config NUR für den Anker-Restore (nuget.org erlaubt, kein <clear/> der den Online-Zugriff blockt)
cat > "${WORK_DIR}/nuget.config" <<'NUGET'
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" protocolVersion="3" />
  </packageSources>
</configuration>
NUGET

ok "Anker erzeugt: ${ANCHOR}"

# --- 3) Restore (managed + runtime-spezifisch) -------------------------------
step "3/5  NuGet-Restore (transitive Dependencies + Windows native runtime)"

rm -rf "${OUT_HIER}"
mkdir -p "${OUT_HIER}"

pushd "${WORK_DIR}" >/dev/null

info "Phase A: portable managed restore"
"${DOTNET_BIN}" restore "${ANCHOR}" \
    --packages "${OUT_HIER}" \
    --verbosity minimal

for rid in "${TARGET_RIDS[@]}"; do
    info "Phase B: RID-spezifischer restore (${rid})"
    "${DOTNET_BIN}" restore "${ANCHOR}" \
        --packages "${OUT_HIER}" \
        --runtime "${rid}" \
        --verbosity minimal
done

popd >/dev/null
ok "Hierarchischer Cache fertig unter: ${OUT_HIER}"

# --- 4) Flachen NuGet-Feed für den Windows-Rechner zusammenstellen ----------
step "4/5  Flachen Feed (.nupkg-Sammlung) erzeugen"

rm -rf "${OUT_FLAT}"
mkdir -p "${OUT_FLAT}"

# Alle .nupkg-Dateien aus dem hierarchischen Cache flach kopieren.
# (keine .nupkg.sha512 / .nupkg.metadata - explizit -name '*.nupkg')
while IFS= read -r -d '' f; do
    name="$(basename "$f")"
    if [[ ! -e "${OUT_FLAT}/${name}" ]]; then
        cp -- "$f" "${OUT_FLAT}/${name}"
    fi
done < <(find "${OUT_HIER}" -type f -name '*.nupkg' -print0)

NUPKG_COUNT=$(find "${OUT_FLAT}" -maxdepth 1 -name '*.nupkg' | wc -l)
TOTAL_SIZE=$(du -sh "${OUT_FLAT}" | awk '{print $1}')
ok "${NUPKG_COUNT} Pakete (${TOTAL_SIZE}) in ${OUT_FLAT}"

# nuget.config für die Windows-Seite mit dazu legen
cat > "${OUT_FLAT}/nuget.config.windows" <<'NUGET'
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="local" value="C:\NuGet-Local" />
  </packageSources>

  <config>
    <add key="globalPackagesFolder" value="C:\NuGet-GlobalCache" />
  </config>

  <packageSourceMapping>
    <packageSource key="local">
      <package pattern="*" />
    </packageSource>
  </packageSourceMapping>
</configuration>
NUGET

# Kleine README
cat > "${OUT_FLAT}/README.txt" <<README
Offline-NuGet-Bundle für DTM (Avalonia 11.2.3 / .NET 10 / Windows)
=====================================================================
Generiert am: $(date)
Pakete:       ${NUPKG_COUNT}
Größe:      ${TOTAL_SIZE}

Schritt 1 - Übertragen
-----------------------
Den KOMPLETTEN Inhalt dieses Ordners (alle *.nupkg + nuget.config.windows)
auf den Windows-Rechner nach C:\NuGet-Local kopieren.
Bestehende gleichnamige Dateien können überschrieben werden.

Schritt 2 - nuget.config aktivieren
-----------------------------------
Die Datei "nuget.config.windows" auf dem Windows-Rechner umbenennen zu
"nuget.config" und entweder
  a) neben die DTM.sln legen (gilt nur für dieses Projekt), oder
  b) nach %APPDATA%\NuGet\NuGet.Config kopieren (gilt benutzerweit).

Schritt 3 - Audit deaktivieren (.NET 10)
----------------------------------------
In DTM.csproj oder einer Directory.Build.props zusätzlich setzen:
  <PropertyGroup>
    <NuGetAudit>false</NuGetAudit>
  </PropertyGroup>

Hintergrund: .NET 10 prüft transitive Pakete per Default gegen einen
Online-Vulnerability-Feed. Offline führt das zu NU1900/NU1901-Fehlern.

Schritt 4 - Cache leeren + restore
----------------------------------
  dotnet nuget locals all --clear
  dotnet restore --force
  dotnet list package --include-transitive   (zur Kontrolle)
README

# --- 5) Zusammenfassung ------------------------------------------------------
step "5/5  Fertig"
ok "Quelle (hierarchischer Cache):  ${OUT_HIER}"
ok "ZIEL  (flach -> C:\\NuGet-Local): ${OUT_FLAT}"
ok "Pakete: ${NUPKG_COUNT}   Gesamtgröße: ${TOTAL_SIZE}"
echo
info "Nächste Schritte:"
echo "  1) Inhalt von ${OUT_FLAT} auf USB / Share kopieren"
echo "  2) Auf dem Windows-Rechner nach C:\\NuGet-Local entpacken"
echo "  3) nuget.config.windows -> nuget.config umbenennen"
echo "  4) auf der Windows-Maschine:  dotnet nuget locals all --clear && dotnet restore --force"
echo
info "Erste Sichtkontrolle der erzeugten Pakete:"
ls -1 "${OUT_FLAT}" | head -n 20
echo "  ... ($(( NUPKG_COUNT > 20 ? NUPKG_COUNT - 20 : 0 )) weitere)"
