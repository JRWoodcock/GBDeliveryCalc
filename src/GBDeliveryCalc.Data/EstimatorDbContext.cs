using GBDeliveryCalc.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace GBDeliveryCalc.Data;

/// <summary>EF Core model for the estimator database. Schema changes go through migrations.</summary>
public sealed class EstimatorDbContext(DbContextOptions<EstimatorDbContext> options) : DbContext(options)
{
    public DbSet<TeamEntity> Teams => Set<TeamEntity>();
    public DbSet<VehicleEntity> Vehicles => Set<VehicleEntity>();
    public DbSet<CompanySettingsEntity> CompanySettings => Set<CompanySettingsEntity>();
    public DbSet<EstimateEntity> Estimates => Set<EstimateEntity>();
    public DbSet<EstimateActualsEntity> EstimateActuals => Set<EstimateActualsEntity>();
    public DbSet<EstimateRevisionEntity> EstimateRevisions => Set<EstimateRevisionEntity>();
    public DbSet<FuelPriceCacheEntity> FuelPriceCache => Set<FuelPriceCacheEntity>();
    public DbSet<FuelFetchStateEntity> FuelFetchStates => Set<FuelFetchStateEntity>();

    public static DbContextOptions<EstimatorDbContext> OptionsFor(string databasePath) =>
        new DbContextOptionsBuilder<EstimatorDbContext>()
            .UseSqlite(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder { DataSource = databasePath, ForeignKeys = true }.ToString())
            .Options;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TeamEntity>(e =>
        {
            e.ToTable("Teams");
            e.Property(x => x.Name).HasMaxLength(100).IsRequired();
            e.Property(x => x.Description).HasMaxLength(500);
        });
        modelBuilder.Entity<VehicleEntity>(e =>
        {
            e.ToTable("Vehicles");
            e.Property(x => x.Name).HasMaxLength(100).IsRequired();
        });
        modelBuilder.Entity<CompanySettingsEntity>(e =>
        {
            e.ToTable("CompanySettings");
            e.Property(x => x.Id).ValueGeneratedNever();
            e.HasData(new CompanySettingsEntity
            {
                Id = 1,
                OriginAddress = "",
                DefaultInstallMinutes = Core.Model.CompanySettings.FactoryDefaultInstallMinutes,
                RoundLaborHoursUp = false,
            });
        });
        modelBuilder.Entity<EstimateEntity>(e =>
        {
            e.ToTable("Estimates");
            e.HasIndex(x => x.QuoteNumber).IsUnique();
            e.Property(x => x.QuoteNumber).HasMaxLength(32).IsRequired();
            e.HasOne(x => x.Actuals).WithOne().HasForeignKey<EstimateActualsEntity>(a => a.EstimateId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<EstimateRevisionEntity>(e =>
        {
            e.ToTable("EstimateRevisions");
            e.HasIndex(x => new { x.EstimateId, x.Revision }).IsUnique();
            e.HasOne<EstimateEntity>().WithMany().HasForeignKey(x => x.EstimateId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<EstimateActualsEntity>(e =>
        {
            e.ToTable("EstimateActuals");
            e.HasKey(x => x.EstimateId);
        });
        modelBuilder.Entity<FuelPriceCacheEntity>(e =>
        {
            e.ToTable("FuelPriceCache");
            e.Property(x => x.State).HasMaxLength(2).IsRequired().HasDefaultValue(Core.Model.UsStates.Default);
            e.HasIndex(x => new { x.FuelType, x.State });
        });
        modelBuilder.Entity<FuelFetchStateEntity>(e =>
        {
            e.ToTable("FuelFetchStates");
            e.Property(x => x.State).HasMaxLength(2).IsRequired().HasDefaultValue(Core.Model.UsStates.Default);
            e.HasKey(x => new { x.FuelType, x.State });
            e.Property(x => x.FuelType).ValueGeneratedNever();
        });
    }
}

/// <summary>Used only by `dotnet ef` at design time.</summary>
public sealed class DesignTimeFactory : IDesignTimeDbContextFactory<EstimatorDbContext>
{
    public EstimatorDbContext CreateDbContext(string[] args) =>
        new(EstimatorDbContext.OptionsFor(Path.Combine(Path.GetTempPath(), "grover-estimator-design.db")));
}
