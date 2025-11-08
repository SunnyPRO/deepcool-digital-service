using System;
using System.IO;
using System.Text;
using System.Threading;

namespace DeepcoolService.Utils
{
    public static class Logger
    {
        private static readonly object _lock = new object();
        private static string _logFilePath;
        private static long _maxBytes = 2 * 1024 * 1024; // 2MB simple rotation
        private static int _packetSampleModulo = 1; // change to >1 to reduce packet spam
        private static int _packetCounter = 0;
        private enum Level { Off=0, Error=1, Warn=2, Info=3 }
        private static Level _level = Level.Info;

        static Logger()
        {
            try
            {
                var baseDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "DeepcoolService");
                Directory.CreateDirectory(baseDir);
                _logFilePath = Path.Combine(baseDir, "deepcool.log");
                WriteRaw("--- Logger initialized at " + DateTime.UtcNow.ToString("o") + " ---");
            }
            catch
            {
                _logFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "deepcool_fallback.log");
            }
            // Apply environment overrides
            string lvl = Environment.GetEnvironmentVariable("DEEPCOOL_LOG_LEVEL")?.ToUpperInvariant();
            switch (lvl)
            {
                case "OFF": _level = Level.Off; break;
                case "ERROR": _level = Level.Error; break;
                case "WARN": _level = Level.Warn; break;
                case "INFO": _level = Level.Info; break;
            }
            if (int.TryParse(Environment.GetEnvironmentVariable("DEEPCOOL_PACKET_SAMPLE"), out int sample) && sample > 1)
            {
                _packetSampleModulo = sample;
            }
            if (int.TryParse(Environment.GetEnvironmentVariable("DEEPCOOL_MAX_LOG_MB"), out int mb) && mb > 0 && mb < 512)
            {
                _maxBytes = mb * 1024L * 1024L;
            }
            if (_level != Level.Info) WriteRaw("--- Log level set to " + _level + " ---");
            if (_packetSampleModulo > 1) WriteRaw("--- Packet sampling modulo=" + _packetSampleModulo + " ---");
            WriteRaw("--- Max log size bytes=" + _maxBytes + " ---");
        }

        private static void RotateIfNeeded()
        {
            try
            {
                if (File.Exists(_logFilePath) && new FileInfo(_logFilePath).Length > _maxBytes)
                {
                    string archived = _logFilePath + ".1";
                    if (File.Exists(archived)) File.Delete(archived);
                    File.Move(_logFilePath, archived);
                }
            }
            catch { /* ignore rotation failures */ }
        }

        private static void WriteRaw(string line)
        {
            lock (_lock)
            {
                RotateIfNeeded();
                File.AppendAllText(_logFilePath, line + Environment.NewLine, Encoding.UTF8);
            }
        }

        public static void ApplyRuntimeConfig()
        {
            lock (_lock)
            {
                string lvl = Environment.GetEnvironmentVariable("DEEPCOOL_LOG_LEVEL")?.ToUpperInvariant();
                Level newLevel = _level;
                switch (lvl)
                {
                    case "OFF": newLevel = Level.Off; break;
                    case "ERROR": newLevel = Level.Error; break;
                    case "WARN": newLevel = Level.Warn; break;
                    case "INFO": newLevel = Level.Info; break;
                }
                if (newLevel != _level)
                {
                    _level = newLevel;
                    WriteRaw("--- Runtime log level changed to " + _level + " ---");
                }
                if (int.TryParse(Environment.GetEnvironmentVariable("DEEPCOOL_PACKET_SAMPLE"), out int sample) && sample > 1)
                {
                    _packetSampleModulo = sample;
                    WriteRaw("--- Runtime packet sample modulo=" + _packetSampleModulo + " ---");
                }
                if (int.TryParse(Environment.GetEnvironmentVariable("DEEPCOOL_MAX_LOG_MB"), out int mb) && mb > 0 && mb < 512)
                {
                    _maxBytes = mb * 1024L * 1024L;
                    WriteRaw("--- Runtime max log size bytes=" + _maxBytes + " ---");
                }
            }
        }

        public static void Info(string message)
        {
            if (_level >= Level.Info)
                WriteRaw($"[{DateTime.UtcNow:O}] INFO  {message}");
        }

        public static void Warn(string message)
        {
            if (_level >= Level.Warn)
                WriteRaw($"[{DateTime.UtcNow:O}] WARN  {message}");
        }

        public static void Error(string message, Exception ex = null)
        {
            if (_level >= Level.Error)
                WriteRaw($"[{DateTime.UtcNow:O}] ERROR {message} {(ex != null ? ex.ToString() : string.Empty)}");
        }

        public static void Packet(byte[] packet)
        {
            if (packet == null) return;
            if (_level < Level.Info) return; // treat packets as info verbosity
            int c = Interlocked.Increment(ref _packetCounter);
            if (c % _packetSampleModulo != 0) return; // sampling
            var sb = new StringBuilder();
            foreach (var b in packet)
                sb.Append(b.ToString("X2")).Append(' ');
            WriteRaw($"[{DateTime.UtcNow:O}] PACKET {sb.ToString().Trim()}");
        }
    }
}
