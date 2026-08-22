using System;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Nougat.Chrome;

/// <summary>
/// Custom-Chrome nach Avalonia-12-Konvention (Kroste-Standard):
/// BorderOnly (NICHT None — sonst fehlen die nativen Resize-Griffe) und Client-Area
/// bis in die Dekoration ausgedehnt. Ohne ExtendClientArea liegt die OS-Caption-
/// Hit-Test-Zone über der eigenen Titelleiste und schluckt Klicks und Drag.
/// </summary>
public class ChromeWindow : Window
{
    private static readonly Uri IconUri = new("avares://Nougat/Assets/nougat.png");

    protected ChromeWindow()
    {
        WindowDecorations = WindowDecorations.BorderOnly;
        ExtendClientAreaToDecorationsHint = true;
        ExtendClientAreaTitleBarHeightHint = -1;
        CanResize = true;
        ApplyAppIcon();
    }

    /// <summary>
    /// Setzt das App-Icon als Fenster-Icon. Bewusst mit try/catch: Ein fehlendes
    /// oder kaputtes Asset darf kein Fenster am Öffnen hindern — dann bleibt es
    /// beim generischen Icon des Fenstermanagers.
    /// </summary>
    private void ApplyAppIcon()
    {
        try
        {
            if (!AssetLoader.Exists(IconUri))
                return;

            using var stream = AssetLoader.Open(IconUri);
            Icon = new WindowIcon(new Bitmap(stream));
        }
        catch (Exception)
        {
            // Ohne Icon ist die App voll funktionsfähig.
        }
    }
}
