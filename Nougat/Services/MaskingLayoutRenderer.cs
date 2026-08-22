using System;
using System.Collections.Concurrent;
using System.Text;
using NLog;
using NLog.Config;
using NLog.LayoutRenderers;
using NLog.LayoutRenderers.Wrappers;
using NLog.Layouts;

namespace Nougat.Services;

/// <summary>
/// NLog-Wrapper, der bekannte Secret-Werte in Log-Zeilen durch "***MASKED***" ersetzt.
/// Registriert wird der Renderer beim Programmstart, Werte werden über
/// <see cref="Register"/> hinzugefügt bzw. per <see cref="Unregister"/> entfernt.
/// </summary>
[LayoutRenderer("masked")]
[ThreadAgnostic]
public sealed class MaskingLayoutRenderer : WrapperLayoutRendererBase
{
    private const string Placeholder = "***MASKED***";
    private static readonly ConcurrentDictionary<string, byte> _secrets = new(StringComparer.Ordinal);

    public static void Register(string? secret)
    {
        if (!string.IsNullOrWhiteSpace(secret) && secret.Length >= 4)
            _secrets.TryAdd(secret, 0);
    }

    public static void Unregister(string? secret)
    {
        if (!string.IsNullOrWhiteSpace(secret))
            _secrets.TryRemove(secret, out _);
    }

    public static void RegisterRenderer()
    {
        LogManager.Setup().SetupExtensions(e => e.RegisterLayoutRenderer<MaskingLayoutRenderer>("masked"));
    }

    protected override string Transform(string text)
    {
        if (string.IsNullOrEmpty(text) || _secrets.IsEmpty)
            return text;

        var sb = new StringBuilder(text);
        foreach (var secret in _secrets.Keys)
            sb.Replace(secret, Placeholder);
        return sb.ToString();
    }
}
