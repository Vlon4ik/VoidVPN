using System;

namespace VoidVPN.Core.Models
{
    public enum LogLevel { Debug, Info, Warn, Error }

    public sealed class LogEntry
    {
        public string   Time    { get; }
        public LogLevel Level   { get; }
        public string   Message { get; }

        public LogEntry(LogLevel level, string message)
        {
            Time    = DateTime.Now.ToString("HH:mm:ss");
            Level   = level;
            Message = message;
        }

        public string Tag  => Level switch { LogLevel.Debug=>"DBG", LogLevel.Warn=>"WRN", LogLevel.Error=>"ERR", _=>"INF" };
        public string Full => $"{Time}  {Tag}  {Message}";
    }
}
