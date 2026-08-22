using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace Nougat.Controls;

/// <summary>
/// Kroste-Standard-Titelleiste für Fenster mit WindowDecorations.BorderOnly:
/// Drag zum Verschieben, Doppelklick zum Maximieren, eigene Min/Max/Close-Buttons.
/// </summary>
public partial class TitleBar : UserControl
{
    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<TitleBar, string?>(nameof(Title));

    public string? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public TitleBar()
    {
        InitializeComponent();
        MinButton.Click += (_, _) => { if (Host is { } w) w.WindowState = WindowState.Minimized; };
        MaxButton.Click += (_, _) => ToggleMaximize();
        CloseButton.Click += (_, _) => Host?.Close();
        Bar.PointerPressed += OnBarPointerPressed;
        Bar.DoubleTapped += OnBarDoubleTapped;
    }

    // ACHTUNG (Avalonia 12): VisualRoot ist NICHT mehr das Window.
    private Window? Host => TopLevel.GetTopLevel(this) as Window;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TitleProperty)
            TitleText.Text = Title;
    }

    private void OnBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (LandedOnInteractiveChild(e.Source))
            return;

        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            Host?.BeginMoveDrag(e);
    }

    private void OnBarDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (LandedOnInteractiveChild(e.Source))
            return;

        ToggleMaximize();
    }

    /// <summary>
    /// Läuft vom Ereignis-Ursprung den Visual-Tree hoch bis zur Titelleisten-Border
    /// und meldet true, wenn unterwegs ein interaktives Control liegt.
    ///
    /// WARUM (Bug real im Checkmk Cockpit v1.7.5): PointerPressed bubbelt. Ein
    /// Button fängt den Press selbst ab und captured den Pointer — eine ComboBox
    /// tut das NICHT. Ohne diesen Guard startet BeginMoveDrag einen Fenster-Drag,
    /// der Pointer wandert ans OS, und die ComboBox sieht nie ein PointerReleased:
    /// Das Dropdown lässt sich gar nicht mehr öffnen, nur der ToolTip erscheint.
    /// Die ElementRole-Rollen helfen hier NICHT — die regeln den OS-Hit-Test-Pfad,
    /// dieser Handler ist der managed Fallback und läuft davon unabhängig.
    /// </summary>
    private bool LandedOnInteractiveChild(object? source)
    {
        for (var v = source as Visual; v is not null; v = v.GetVisualParent())
        {
            // Die Titelleiste selbst (und alles darüber) ist Drag-Fläche.
            if (ReferenceEquals(v, Bar))
                return false;

            // Button deckt ToggleButton/CheckBox/RadioButton/RepeatButton mit ab.
            if (v is Button or ComboBox or TextBox or Slider or ListBox or MenuItem)
                return true;

            // Auffangnetz: alles Fokussierbare will den Klick selbst verarbeiten.
            if (v is InputElement { Focusable: true })
                return true;
        }

        // Ursprung liegt außerhalb der Titelleiste (z. B. in einem Popup-Root).
        return true;
    }

    private void ToggleMaximize()
    {
        if (Host is { } w)
            w.WindowState = w.WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
    }
}
