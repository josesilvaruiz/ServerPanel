using System.Text.RegularExpressions;

namespace ServerPanel.Services;

// El crash conocido (mapas con física/colisión rota) siempre deja la misma firma:
// "Created physics for <mapa>" justo antes de que el watchdog del propio motor
// mate el proceso. Aislado en su propia clase para poder probarlo contra logs
// reales sin tener que arrancar todo el BackgroundService.
public static class CrashLogParser
{
    private static readonly Regex WatchdogTimeoutPattern = new(
        "Watchdog timeout exceeded", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex CreatedPhysicsPattern = new(
        @"Created physics for (\S+)", RegexOptions.Compiled);

    public static bool TryExtractCrashedMap(string logs, out string mapName)
    {
        mapName = "";

        if (!WatchdogTimeoutPattern.IsMatch(logs))
            return false;

        var matches = CreatedPhysicsPattern.Matches(logs);
        if (matches.Count == 0)
            return false;

        // El último "Created physics for X" antes del timeout es el mapa que colgó el motor.
        mapName = matches[^1].Groups[1].Value;
        return true;
    }
}
