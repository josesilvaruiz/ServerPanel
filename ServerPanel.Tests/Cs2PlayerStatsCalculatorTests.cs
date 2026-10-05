using ServerPanel.Services;

namespace ServerPanel.Tests;

public class Cs2PlayerStatsCalculatorTests
{
    private static readonly TimeZoneInfo Utc = TimeZoneInfo.Utc;
    private static readonly DateTime Now = new(2026, 10, 5, 12, 0, 0, DateTimeKind.Utc); // lunes

    // "1", "2"... -> SteamID64 válido (base + accountId). Cualquier otra cosa se pasa tal cual.
    private static string? Sid(string? id) =>
        id is not null && uint.TryParse(id, out var n) && id.Length < 17 ? (76561197960265728UL + n).ToString() : id;

    private static Cs2ConnectRecord C(int daysAgo, string name, string? steam, string server = "A", int hour = 10) =>
        new(Now.Date.AddDays(-daysAgo).AddHours(hour), name, Sid(steam), server);

    [Fact]
    public void Counts_unique_players_by_steamid_even_if_name_changes()
    {
        var stats = Cs2PlayerStatsCalculator.Compute(
        [
            C(3, "Pepe", "111"),
            C(1, "PepeNuevo", "111"),
            C(1, "Ana", "222"),
        ], Now, Utc);

        Assert.Equal(2, stats.TotalUnique);
        Assert.Contains(stats.TopPlayers, p => p.Name == "PepeNuevo" && p.ActiveDays == 2);
    }

    [Fact]
    public void Connects_without_valid_steamid_are_ignored()
    {
        var stats = Cs2PlayerStatsCalculator.Compute(
        [
            C(2, "Pepe", "111"),
            C(0, "Pepe", null),                      // sin SteamID
            C(0, "BOT Kyle", "76561197960265728"),   // accountId 0 (bot)
            C(0, "Raro", "abc"),                     // basura
        ], Now, Utc);

        Assert.Equal(1, stats.TotalUnique);
        Assert.Equal(0, stats.Returning);
        Assert.Equal(1, stats.TopPlayers[0].Connects);
    }

    [Fact]
    public void Same_name_different_steamids_are_different_players()
    {
        var stats = Cs2PlayerStatsCalculator.Compute(
        [
            C(1, "Player", "1"),
            C(1, "Player", "2"),
        ], Now, Utc);

        Assert.Equal(2, stats.TotalUnique);
    }

    [Theory]
    [InlineData("76561198000000000", true)]
    [InlineData("76561197960265728", false)]
    [InlineData("7656119800000000", false)]
    [InlineData("7656119800000000x", false)]
    [InlineData(null, false)]
    public void Validates_steamid64(string? id, bool expected) =>
        Assert.Equal(expected, Cs2PlayerStatsCalculator.IsValidSteamId64(id));

    [Fact]
    public void Same_day_reconnects_do_not_count_as_returning()
    {
        var stats = Cs2PlayerStatsCalculator.Compute(
        [
            C(5, "Pepe", "111", hour: 10),
            C(5, "Pepe", "111", hour: 18),
        ], Now, Utc);

        Assert.Equal(0, stats.Returning);
        Assert.Equal(2, stats.TopPlayers[0].Connects);
        Assert.Equal(1, stats.TopPlayers[0].ActiveDays);
    }

    [Fact]
    public void Retention_only_counts_eligible_players_and_returns_inside_window()
    {
        var stats = Cs2PlayerStatsCalculator.Compute(
        [
            // vuelve al día siguiente
            C(20, "A", "1"), C(19, "A", "1"),
            // vuelve a los 5 días (cuenta en D7, no en D1)
            C(20, "B", "2"), C(15, "B", "2"),
            // nunca vuelve
            C(20, "C", "3"),
            // primera visita hoy: no es elegible para ninguna retención
            C(0, "D", "4"),
        ], Now, Utc);

        var d1 = stats.Retention.Single(r => r.Days == 1);
        var d7 = stats.Retention.Single(r => r.Days == 7);
        var d30 = stats.Retention.Single(r => r.Days == 30);

        Assert.Equal((3, 1), (d1.Eligible, d1.Returned));
        Assert.Equal((3, 2), (d7.Eligible, d7.Returned));
        Assert.Equal(0, d30.Eligible);
        Assert.Null(d30.Pct);
    }

    [Fact]
    public void New_and_unique_windows()
    {
        var stats = Cs2PlayerStatsCalculator.Compute(
        [
            C(40, "Old", "1"), C(2, "Old", "1"),
            C(10, "Mid", "2"),
            C(1, "Fresh", "3"),
        ], Now, Utc);

        Assert.Equal(2, stats.Unique7d);   // Old, Fresh
        Assert.Equal(3, stats.Unique30d);  // + Mid
        Assert.Equal(1, stats.New7d);      // Fresh
        Assert.Equal(2, stats.New30d);     // Mid, Fresh
        Assert.Equal(Cs2PlayerStatsCalculator.DailyWindowDays, stats.DailyUnique.Length);
        Assert.Equal(1, stats.DailyNew[^2]); // Fresh, ayer
    }

    [Fact]
    public void Per_server_and_shared_players()
    {
        var stats = Cs2PlayerStatsCalculator.Compute(
        [
            C(1, "A", "1", "Prod"), C(1, "A", "1", "Test"),
            C(1, "B", "2", "Prod"),
        ], Now, Utc);

        Assert.Equal(2, stats.TotalUnique);
        Assert.Equal(1, stats.SharedAcrossServers);
        Assert.Equal(2, stats.PerServer.Single(s => s.ServerKey == "Prod").UniquePlayers);
        Assert.Equal(1, stats.PerServer.Single(s => s.ServerKey == "Test").UniquePlayers);
    }

    [Fact]
    public void Weekly_cohort_tracks_following_weeks_and_leaves_future_null()
    {
        // Now = lunes 05/10. Cohorte de la semana del 21/09: dos jugadores, uno vuelve la semana siguiente.
        var stats = Cs2PlayerStatsCalculator.Compute(
        [
            C(14, "A", "1"), C(7, "A", "1"),
            C(14, "B", "2"),
        ], Now, Utc);

        var cohort = stats.Cohorts.Single(c => c.WeekStart == new DateOnly(2026, 9, 21));
        Assert.Equal(2, cohort.Size);
        Assert.Equal(50, cohort.ReturnPct[0]);
        Assert.Equal(0, cohort.ReturnPct[1]);   // semana actual, todavía nadie
        Assert.Null(cohort.ReturnPct[2]);       // futura
    }

    [Fact]
    public void Empty_input_is_safe()
    {
        var stats = Cs2PlayerStatsCalculator.Compute([], Now, Utc);
        Assert.Equal(0, stats.TotalUnique);
        Assert.Null(stats.FirstEventUtc);
        Assert.Equal(0, stats.ReturningPct);
    }
}
