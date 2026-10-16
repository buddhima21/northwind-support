using Microsoft.EntityFrameworkCore;
using SupportDesk.Domain.Repositories;
using SupportDesk.Domain.Aggregates.Tickets;
using SupportDesk.Application.Exceptions;

namespace SupportDesk.Infrastructure.Data;

/// <summary>
/// EF Core context for the support desk, and the unit of work that commits each request's
/// changes. Mapping lives in the per-aggregate configuration classes in
/// <c>Data/Configurations</c>.
/// </summary>
public class SupportDbContext(DbContextOptions<SupportDbContext> options) : DbContext(options), IUnitOfWork
{
    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        RejectHistoryChanges();
        try
        {
            return await base.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException("The ticket changed while this request was being processed. Reload it and try again.");
        }
    }

    public override int SaveChanges()
    {
        RejectHistoryChanges();
        try { return base.SaveChanges(); }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException("The ticket changed while this request was being processed. Reload it and try again.");
        }
    }

    private void RejectHistoryChanges()
    {
        if (ChangeTracker.Entries<TicketEscalation>().Any(entry => entry.State is EntityState.Modified or EntityState.Deleted))
            throw new InvalidOperationException("Escalation history is immutable.");
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(SupportDbContext).Assembly);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Every timestamp in this database is UTC; say so, so that it survives the round trip.
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
        configurationBuilder.Properties<DateTime?>().HaveConversion<NullableUtcDateTimeConverter>();
    }
}
