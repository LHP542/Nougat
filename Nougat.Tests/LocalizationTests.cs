using System.Globalization;
using System.Resources;
using System.Xml.Linq;
using FluentAssertions;
using Nougat.Localization;
using Xunit;

namespace Nougat.Tests;

public class LocalizationTests
{
    private static readonly ResourceManager Rm = new(
        "Nougat.Localization.Strings", typeof(LocalizationService).Assembly);

    [Fact]
    public void Kroste_Standard_mindestens_Englisch_und_Deutsch()
    {
        var isos = LocalizationService.SupportedCultures.Select(c => c.Iso).ToList();
        isos.Should().Contain("en").And.Contain("de");

        // Englisch ist die Neutral-Kultur und muss deshalb an erster Stelle stehen.
        isos[0].Should().Be("en");
    }

    [Fact]
    public void Jeder_Key_hat_eine_deutsche_Uebersetzung()
    {
        // Direkt gegen die Resx-Dateien im Quellbaum, nicht gegen die Satellite-
        // Assembly: So faellt ein vergessener Eintrag schon im Test auf und nicht
        // erst als "!Key!" in der laufenden UI.
        var en = Keys("Strings.resx");
        var de = Keys("Strings.de.resx");

        en.Should().NotBeEmpty();
        de.Except(en).Should().BeEmpty("die deutsche Datei darf keine unbekannten Keys enthalten");
        en.Except(de).Should().BeEmpty("jeder englische Key braucht eine deutsche Uebersetzung");
    }

    [Theory]
    [InlineData("en", "Ready.")]
    [InlineData("de", "Bereit.")]
    public void Liefert_die_Sprache_der_aktiven_Kultur(string iso, string expected) =>
        Rm.GetString("Status_Ready", CultureInfo.GetCultureInfo(iso)).Should().Be(expected);

    [Fact]
    public void Unbekannter_Key_faellt_sichtbar_auf() =>
        LocalizationService.Instance["GibtEsNicht"].Should().Be("!GibtEsNicht!");

    [Fact]
    public void Platzhalter_werden_in_beiden_Sprachen_gefuellt()
    {
        LocalizationService.Instance.SetCulture("de");
        Loc.T("Main_SelectedCount", 3).Should().Be("3 ausgewählt");

        LocalizationService.Instance.SetCulture("en");
        Loc.T("Main_SelectedCount", 3).Should().Be("3 selected");
    }

    /// <summary>
    /// Sucht die Resx im Quellbaum, statt einen relativen Pfad ab bin/ zu raten —
    /// der bricht, sobald jemand den Output-Pfad umstellt.
    /// </summary>
    private static string[] Keys(string fileName)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Directory.Build.props")))
            dir = dir.Parent;

        dir.Should().NotBeNull("die Tests laufen aus dem Repo-Baum heraus");
        var path = Path.Combine(dir!.FullName, "Nougat", "Localization", fileName);
        File.Exists(path).Should().BeTrue($"{fileName} muss im Quellbaum liegen");

        return XDocument.Load(path).Root!
            .Elements("data")
            .Select(d => d.Attribute("name")!.Value)
            .ToArray();
    }
}
