namespace ServerPanel.Services;

/// <summary>Una conexión de jugador (evento Connect del log) con lo justo para las estadísticas globales.</summary>
public sealed record Cs2ConnectRecord(DateTime TimestampUtc, string PlayerName, string? SteamId64, string ServerKey);

/// <summary>Retención a N días: de los jugadores cuya primera visita fue hace al menos N días,
/// cuántos volvieron otro día dentro de esos N días.</summary>
public sealed record Cs2RetentionStat(int Days, int Eligible, int Returned)
{
    public double? Pct => Eligible == 0 ? null : Returned * 100.0 / Eligible;
}

/// <summary>Cohorte semanal: jugadores cuya primera visita cayó en esa semana y qué % siguió
/// jugando 1..4 semanas después (null = esa semana todavía no ha llegado).</summary>
public sealed record Cs2WeeklyCohort(DateOnly WeekStart, int Size, double?[] ReturnPct);

public sealed record Cs2PlayerSummary(
    string   SteamId64,
    string   Name,
    int      ActiveDays,
    int      Connects,
    DateTime FirstSeenUtc,
    DateTime LastSeenUtc,
    IReadOnlyList<string> Servers);

public sealed record Cs2ServerUnique(string ServerKey, int UniquePlayers);

public sealed record Cs2GlobalPlayerStats(
    int      TotalUnique,
    DateTime? FirstEventUtc,
    int      Unique7d,
    int      Unique30d,
    int      New7d,
    int      New30d,
    int      Returning,
    double   AvgActiveDays,
    IReadOnlyList<Cs2RetentionStat> Retention,
    IReadOnlyList<Cs2WeeklyCohort>  Cohorts,
    IReadOnlyList<Cs2PlayerSummary> TopPlayers,
    IReadOnlyList<Cs2ServerUnique>  PerServer,
    int      SharedAcrossServers,
    string[] DailyLabels,
    double[] DailyUnique,
    double[] DailyNew)
{
    public double ReturningPct => TotalUnique == 0 ? 0 : Returning * 100.0 / TotalUnique;
}

/// <summary>
/// Cálculo en memoria (sin BD) de jugadores únicos y retención a partir de los eventos Connect.
/// Los días se cuentan en hora local (Madrid) para que "volvió otro día" signifique lo que un
/// humano espera, no un corte a medianoche UTC.
/// </summary>
public static class Cs2PlayerStatsCalculator
{
    public static readonly int[] RetentionDays = [1, 7, 30];
    public const int CohortWeeks = 8;
    public const int CohortFollowWeeks = 4;
    public const int DailyWindowDays = 30;

    // SteamID64 de cuentas individuales: 76561197960265728 + accountId. El accountId 0 es el que
    // sale de un SteamID3 vacío ([U:1:0]) — típico de bots — y no es un jugador real.
    const ulong SteamId64Base = 76561197960265728UL;

    public static bool IsValidSteamId64(string? id) =>
        id is { Length: 17 } && id.All(char.IsAsciiDigit)
        && ulong.TryParse(id, out var v) && v > SteamId64Base && v < SteamId64Base + uint.MaxValue;

    public static Cs2GlobalPlayerStats Compute(
        IEnumerable<Cs2ConnectRecord> connects,
        DateTime nowUtc,
        TimeZoneInfo tz,
        int topN = 25)
    {
        // Identidad estricta por SteamID64: lo que no traiga uno válido (bots, líneas de log
        // incompletas) no cuenta. Así un cambio de nombre no duplica jugadores y dos cuentas con
        // el mismo nombre no se mezclan.
        var list = connects
            .Where(c => IsValidSteamId64(c.SteamId64) && !string.IsNullOrWhiteSpace(c.PlayerName))
            .ToList();

        static string KeyOf(Cs2ConnectRecord c) => c.SteamId64!;

        DateOnly DayOf(DateTime utc) =>
            DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), tz));

        var today = DayOf(nowUtc);

        var players = list
            .GroupBy(KeyOf)
            .Select(g =>
            {
                var ordered = g.OrderBy(c => c.TimestampUtc).ToList();
                return new
                {
                    Key     = g.Key,
                    Name    = ordered[^1].PlayerName, // nombre más reciente
                    Days    = new SortedSet<DateOnly>(ordered.Select(c => DayOf(c.TimestampUtc))),
                    Connects = ordered.Count,
                    First   = ordered[0].TimestampUtc,
                    Last    = ordered[^1].TimestampUtc,
                    Servers = ordered.Select(c => c.ServerKey).Distinct().OrderBy(s => s).ToList(),
                };
            })
            .ToList();

        bool ActiveSince(SortedSet<DateOnly> days, DateOnly from) => days.Max >= from;

        var from7  = today.AddDays(-6);
        var from30 = today.AddDays(-29);

        var retention = RetentionDays.Select(n =>
        {
            var eligible = players.Where(p => p.Days.Min.AddDays(n) <= today).ToList();
            var returned = eligible.Count(p =>
                p.Days.GetViewBetween(p.Days.Min.AddDays(1), p.Days.Min.AddDays(n)).Count > 0);
            return new Cs2RetentionStat(n, eligible.Count, returned);
        }).ToList();

        // Semanas de lunes a domingo.
        DateOnly WeekOf(DateOnly d) => d.AddDays(-(((int)d.DayOfWeek + 6) % 7));
        var thisWeek = WeekOf(today);
        var cohorts = new List<Cs2WeeklyCohort>();
        for (int w = CohortWeeks - 1; w >= 0; w--)
        {
            var start = thisWeek.AddDays(-7 * w);
            var members = players.Where(p => WeekOf(p.Days.Min) == start).ToList();
            var pct = new double?[CohortFollowWeeks];
            for (int k = 1; k <= CohortFollowWeeks; k++)
            {
                var ks = start.AddDays(7 * k);
                if (ks > today || members.Count == 0) { pct[k - 1] = null; continue; }
                var back = members.Count(p => p.Days.GetViewBetween(ks, ks.AddDays(6)).Count > 0);
                pct[k - 1] = back * 100.0 / members.Count;
            }
            cohorts.Add(new Cs2WeeklyCohort(start, members.Count, pct));
        }

        var dailyLabels = new string[DailyWindowDays];
        var dailyUnique = new double[DailyWindowDays];
        var dailyNew    = new double[DailyWindowDays];
        for (int i = 0; i < DailyWindowDays; i++)
        {
            var d = from30.AddDays(i);
            dailyLabels[i] = d.ToString("dd/MM");
            dailyUnique[i] = players.Count(p => p.Days.Contains(d));
            dailyNew[i]    = players.Count(p => p.Days.Min == d);
        }

        var perServer = list
            .GroupBy(c => c.ServerKey)
            .Select(g => new Cs2ServerUnique(g.Key, g.Select(KeyOf).Distinct().Count()))
            .OrderByDescending(s => s.UniquePlayers)
            .ToList();

        return new Cs2GlobalPlayerStats(
            TotalUnique:   players.Count,
            FirstEventUtc: list.Count == 0 ? null : list.Min(c => c.TimestampUtc),
            Unique7d:      players.Count(p => ActiveSince(p.Days, from7)),
            Unique30d:     players.Count(p => ActiveSince(p.Days, from30)),
            New7d:         players.Count(p => p.Days.Min >= from7),
            New30d:        players.Count(p => p.Days.Min >= from30),
            Returning:     players.Count(p => p.Days.Count >= 2),
            AvgActiveDays: players.Count == 0 ? 0 : players.Average(p => p.Days.Count),
            Retention:     retention,
            Cohorts:       cohorts,
            TopPlayers:    players
                .OrderByDescending(p => p.Days.Count)
                .ThenByDescending(p => p.Connects)
                .ThenByDescending(p => p.Last)
                .Take(topN)
                .Select(p => new Cs2PlayerSummary(p.Key, p.Name, p.Days.Count, p.Connects, p.First, p.Last, p.Servers))
                .ToList(),
            PerServer:     perServer,
            SharedAcrossServers: players.Count(p => p.Servers.Count > 1),
            DailyLabels:   dailyLabels,
            DailyUnique:   dailyUnique,
            DailyNew:      dailyNew);
    }
}
