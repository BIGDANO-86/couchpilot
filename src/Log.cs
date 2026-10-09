using System;
using System.IO;
using System.Text;

namespace CouchPilot
{
    /// <summary>
    /// Small rolling log. Diagnostics proved essential while developing this
    /// behaviour, because almost everything it does happens while nobody is
    /// looking at the screen.
    /// </summary>
    internal static class Log
    {
        private static readonly object Gate = new object();
        private const long MaxBytes = 512 * 1024;

        public static string Dir =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CouchPilot");

        public static string Path_ => System.IO.Path.Combine(Dir, "couchpilot.log");

        public static void Write(string message)
        {
            try
            {
                lock (Gate)
                {
                    Directory.CreateDirectory(Dir);

                    var fi = new FileInfo(Path_);
                    if (fi.Exists && fi.Length > MaxBytes)
                    {
                        var keep = File.ReadAllLines(Path_);
                        var from = Math.Max(0, keep.Length / 2);
                        File.WriteAllLines(Path_, keep[from..]);
                    }

                    File.AppendAllText(Path_,
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + message + Environment.NewLine,
                        Encoding.UTF8);
                }
            }
            catch
            {
                // Logging must never take the app down.
            }
        }
    }
}
