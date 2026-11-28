using System;
using System.Collections.Generic;
using BepInEx.Logging;
using UnityEngine;

namespace Road96Toolkit
{
    // Keeps the last lines of the BepInEx log (every mod plus Unity's own messages) for the console
    // panel of the dev view.
    internal sealed class LogConsole : ILogListener
    {
        public struct Line { public string Text; public LogLevel Level; public string Time; }

        const int Max = 300;
        static readonly List<Line> _lines = new List<Line>();
        static readonly object _lock = new object();
        static LogConsole _instance;
        public static int Version { get; private set; }

        public LogLevel LogLevelFilter => LogLevel.All;

        public static void Start()
        {
            if (_instance != null) return;
            _instance = new LogConsole();
            BepInEx.Logging.Logger.Listeners.Add(_instance);
        }

        public void LogEvent(object sender, LogEventArgs e)
        {
            var text = e.Data?.ToString() ?? "";
            var src = e.Source?.SourceName;
            if (!string.IsNullOrEmpty(src) && src != "Unity Log") text = "[" + src + "] " + text;
            lock (_lock)
            {
                _lines.Add(new Line { Text = text, Level = e.Level, Time = DateTime.Now.ToString("HH:mm:ss") });
                if (_lines.Count > Max) _lines.RemoveRange(0, _lines.Count - Max);
                Version++;
            }
        }

        public void Dispose() { }

        public static List<Line> Snapshot()
        {
            lock (_lock) return new List<Line>(_lines);
        }

        public static void Clear()
        {
            lock (_lock) { _lines.Clear(); Version++; }
        }

        public static Color ColorOf(LogLevel l) =>
            (l & (LogLevel.Error | LogLevel.Fatal)) != 0 ? new Color(1f, 0.45f, 0.4f)
            : (l & LogLevel.Warning) != 0 ? new Color(1f, 0.85f, 0.35f)
            : (l & LogLevel.Debug) != 0 ? new Color(0.6f, 0.6f, 0.6f)
            : new Color(0.85f, 0.85f, 0.85f);
    }
}
