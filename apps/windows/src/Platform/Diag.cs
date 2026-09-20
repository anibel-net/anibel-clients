using System.IO;
using System.Threading;

namespace Anibel.App.Services;

public static class Diag
{
    private static readonly object Lock = new();
    private static readonly string Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "anibel-debug.log");

    public static void Log(string line)
    {
        var stamp = $"{DateTime.Now:HH:mm:ss.fff} {line}{Environment.NewLine}";
        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                lock (Lock)
                {
                    File.AppendAllText(Path, stamp);
                }
            }
            catch
            {
            }
        });
    }
}
