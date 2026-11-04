using System;
using System.IO;
using System.Text;

namespace CruciblePilot;

/// <summary>
/// Append-only run log so a session can be reviewed after the fact without grepping dalamud.log.
/// One file per day in %APPDATA%\XIVLauncher\pluginConfigs\CruciblePilot\logs\.
/// </summary>
public sealed class CpLog : IDisposable
{
    private readonly object gate = new();
    private readonly string directory;
    private StreamWriter? writer;
    private DateOnly currentDay;

    public CpLog(string configDirectory)
    {
        this.directory = Path.Combine(configDirectory, "logs");
        Directory.CreateDirectory(this.directory);
    }

    public string CurrentFile { get; private set; } = string.Empty;

    public void Info(string message) => this.Write("INF", message);

    public void Warn(string message) => this.Write("WRN", message);

    public void Error(Exception exception, string message) =>
        this.Write("ERR", message + Environment.NewLine + exception);

    public void Error(string message) => this.Write("ERR", message);

    private void Write(string level, string message)
    {
        lock (this.gate)
        {
            try
            {
                var today = DateOnly.FromDateTime(DateTime.Now);
                if (this.writer == null || today != this.currentDay)
                {
                    this.writer?.Dispose();
                    this.currentDay = today;
                    this.CurrentFile = Path.Combine(this.directory, $"cruciblepilot-{today:yyyyMMdd}.log");
                    this.writer = new StreamWriter(this.CurrentFile, append: true, new UTF8Encoding(false))
                    {
                        AutoFlush = true,
                    };
                    this.writer.WriteLine($"===== CruciblePilot session {DateTime.Now:yyyy-MM-dd HH:mm:ss} =====");
                }

                this.writer.WriteLine($"{DateTime.Now:HH:mm:ss.fff} [{level}] {message}");
            }
            catch
            {
                // never let logging break the run
            }
        }
    }

    public void Dispose()
    {
        lock (this.gate)
        {
            this.writer?.Dispose();
            this.writer = null;
        }
    }
}
