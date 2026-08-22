
namespace Nougat.Models;

public enum LogSeverity { Trace, Debug, Info, Warn, Error }

public sealed record LogEntry(DateTime Timestamp, LogSeverity Severity, string Message)
{
    public static LogEntry Info(string msg) => new(DateTime.Now, LogSeverity.Info, msg);
    public static LogEntry Warn(string msg) => new(DateTime.Now, LogSeverity.Warn, msg);
    public static LogEntry Error(string msg) => new(DateTime.Now, LogSeverity.Error, msg);
    public static LogEntry Debug(string msg) => new(DateTime.Now, LogSeverity.Debug, msg);
}
