using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using NLog;
using Nougat.Views;

namespace Nougat.Infrastructure;

/// <summary>
/// Fängt unbehandelte Exceptions aus allen drei Quellen (AppDomain, TaskScheduler,
/// Dispatcher), schreibt sie als Fatal ins Log und zeigt dem Nutzer einen Dialog,
/// statt still abzustürzen.
/// </summary>
public static class GlobalExceptionHandler
{
    private static readonly Logger _logger = LogManager.GetCurrentClassLogger();

    /// <summary>Verhindert eine Dialog-Lawine, wenn eine Exception im Sekundentakt wiederkehrt.</summary>
    private static int _dialogOpen;

    public static void Install()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            var ex = e.ExceptionObject as Exception;
            _logger.Fatal(ex, "Unbehandelte AppDomain-Exception");
            // Kein Dialog: Bei IsTerminating ist die Runtime bereits im Abbau,
            // ein neues Fenster kommt nicht mehr zuverlässig hoch.
            if (!e.IsTerminating)
                ShowDialog(ex);
        };

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            _logger.Fatal(e.Exception, "Unbeobachtete Task-Exception");
            e.SetObserved();
            ShowDialog(e.Exception);
        };

        // Der UI-Thread ist die häufigste Quelle: Ein Fehler in einem Command-Handler
        // oder Binding landet hier. Ohne Handled=true reißt er den Prozess mit.
        Dispatcher.UIThread.UnhandledException += (_, e) =>
        {
            _logger.Fatal(e.Exception, "Unbehandelte Dispatcher-Exception");
            e.Handled = true;
            ShowDialog(e.Exception);
        };

        _logger.Debug("GlobalExceptionHandler installiert (AppDomain, TaskScheduler, Dispatcher).");
    }

    private static void ShowDialog(Exception? ex)
    {
        if (ex is null)
            return;

        if (Interlocked.Exchange(ref _dialogOpen, 1) == 1)
            return;

        void Show()
        {
            try
            {
                var owner = (Avalonia.Application.Current?.ApplicationLifetime
                    as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
                var window = new ErrorWindow(ex);
                window.Closed += (_, _) => Volatile.Write(ref _dialogOpen, 0);

                if (owner is { IsVisible: true })
                    window.ShowDialog(owner);
                else
                    window.Show();
            }
            catch (Exception dialogEx)
            {
                // Der Fehlerdialog darf niemals selbst zum Absturzgrund werden.
                Volatile.Write(ref _dialogOpen, 0);
                _logger.Error(dialogEx, "Fehlerdialog konnte nicht angezeigt werden.");
            }
        }

        if (Dispatcher.UIThread.CheckAccess())
            Show();
        else
            Dispatcher.UIThread.Post(Show);
    }
}
