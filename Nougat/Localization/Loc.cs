using System.Globalization;

namespace Nougat.Localization;

/// <summary>
/// Kurzform für Übersetzungen aus C#-Code (ViewModels, Services). Im XAML
/// wird stattdessen <c>{loc:Tr Key}</c> benutzt, weil dort ein Live-Binding
/// gebraucht wird — ein aus C# geholter String ist eine Momentaufnahme und
/// wechselt bei einem Sprachwechsel nicht mit.
///
/// Für Statustexte ist das gewollt: Eine Log-Zeile bleibt in der Sprache
/// stehen, in der sie entstanden ist.
/// </summary>
public static class Loc
{
    /// <summary>Übersetzt einen Key.</summary>
    public static string T(string key) => LocalizationService.Instance[key];

    /// <summary>Übersetzt einen Key und füllt die Platzhalter {0}, {1}, ...</summary>
    public static string T(string key, params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, LocalizationService.Instance[key], args);
}
