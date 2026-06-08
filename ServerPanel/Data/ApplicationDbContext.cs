using Microsoft.EntityFrameworkCore;
using ServerPanel.Models;

namespace ServerPanel.Data;

public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options) { }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.ConfigureWarnings(w =>
            w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning));
    }

    public DbSet<VpsMetricSnapshot> VpsMetricSnapshots => Set<VpsMetricSnapshot>();
    public DbSet<Cs2ServerSnapshot> Cs2ServerSnapshots => Set<Cs2ServerSnapshot>();
    public DbSet<Cs2PlayerSession> Cs2PlayerSessions => Set<Cs2PlayerSession>();
    public DbSet<PageVisit> PageVisits => Set<PageVisit>();
    public DbSet<SavedCmdGroup> SavedCmdGroups => Set<SavedCmdGroup>();
    public DbSet<SavedCmd> SavedCmds => Set<SavedCmd>();
}
