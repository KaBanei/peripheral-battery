using System;
using System.IO;
using System.Text;

namespace Pb.Core
{
    public static class Log
    {
        private static readonly object Sync = new object();
        private static string _path;

        public static string FilePath
        {
            get
            {
                if (_path == null) _path = Path.Combine(AppConfig.DataDir, "app.log");
                return _path;
            }
        }

        public static void Info(string message) { Write("INFO", message); }
        public static void Warn(string message) { Write("WARN", message); }
        public static void Error(string message) { Write("ERROR", message); }

        public static void Error(string message, Exception ex)
        {
            Write("ERROR", message + Environment.NewLine + ex);
        }

        private static void Write(string level, string message)
        {
            try
            {
                lock (Sync)
                {
                    RotateIfNeeded();
                    File.AppendAllText(FilePath,
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " [" + level + "] " + message + Environment.NewLine,
                        new UTF8Encoding(false));
                }
            }
            catch { }
        }

        private static void RotateIfNeeded()
        {
            try
            {
                var fi = new FileInfo(FilePath);
                if (fi.Exists && fi.Length > 2 * 1024 * 1024)
                {
                    string bak = FilePath + ".1";
                    if (File.Exists(bak)) File.Delete(bak);
                    File.Move(FilePath, bak);
                }
            }
            catch { }
        }
    }
}
