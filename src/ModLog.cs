using System.Reflection;
using Godot;

namespace DgLabSocketSpire2;

internal static class ModLog
{
    private static readonly object Gate = new();
    private static readonly string RootDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? AppContext.BaseDirectory;
    private static readonly string LogPath = Path.Combine(RootDir, "dglab_socket_spire2.log");

    public static void Info(string message)
    {
        Write("INFO", message);
    }

    public static void Warn(string message)
    {
        Write("WARN", message);
    }

    public static void Error(string message, Exception? ex = null)
    {
        Write("ERROR", ex == null ? message : $"{message}{System.Environment.NewLine}{ex}");
    }

    private static void Write(string level, string message)
    {
        var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{level}] {message}";
        lock (Gate)
        {
            try
            {
                Directory.CreateDirectory(RootDir);
                File.AppendAllText(LogPath, line + System.Environment.NewLine);
            }
            catch
            {
            }
        }

        if (level == "ERROR")
        {
            GD.PrintErr("[DG-LAB Socket] ", line);
        }
        else
        {
            GD.Print("[DG-LAB Socket] ", line);
        }
    }
}
