using LastTrain.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace LastTrain.Api.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Station> Stations => Set<Station>();
    public DbSet<Line> Lines => Set<Line>();
    public DbSet<StationLine> StationLines => Set<StationLine>();
    public DbSet<LastTrainService> LastTrainServices => Set<LastTrainService>();
    public DbSet<PublicHoliday> PublicHolidays => Set<PublicHoliday>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<StationLine>()
            .HasIndex(sl => new { sl.LineId, sl.SequenceIndex })
            .IsUnique();

        b.Entity<StationLine>()
            .HasOne(sl => sl.Station)
            .WithMany(s => s.StationLines)
            .HasForeignKey(sl => sl.StationId);

        b.Entity<StationLine>()
            .HasOne(sl => sl.Line)
            .WithMany(l => l.StationLines)
            .HasForeignKey(sl => sl.LineId);

        b.Entity<LastTrainService>()
            .HasOne(x => x.Station)
            .WithMany()
            .HasForeignKey(x => x.StationId)
            .OnDelete(DeleteBehavior.Restrict);

        b.Entity<LastTrainService>()
            .HasOne(x => x.TowardsStation)
            .WithMany()
            .HasForeignKey(x => x.TowardsStationId)
            .OnDelete(DeleteBehavior.Restrict);

        b.Entity<LastTrainService>()
            .HasIndex(x => new { x.StationId, x.LineId, x.TowardsStationId })
            .IsUnique();
    }
}
