using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SupportDesk.Application.Abstractions;
using SupportDesk.Domain.Repositories;
using SupportDesk.Domain.Aggregates.Agents;
using SupportDesk.Infrastructure.Data;
using SupportDesk.Infrastructure.Queries;
using SupportDesk.Infrastructure.Repositories;
using SupportDesk.Infrastructure.Services;
using SupportDesk.Infrastructure.Authentication;
using SupportDesk.Infrastructure.Health;

namespace SupportDesk.Infrastructure;

/// <summary>
/// Wires the domain's repositories and the application's abstractions to their SQL Server /
/// system implementations.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddDbContext<SupportDbContext>(options =>
        {
            var connectionString = configuration.GetConnectionString("Default")
                ?? throw new InvalidOperationException(
                    "Connection string 'Default' is missing. See README.md for local setup.");
            options.UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure());
        });

        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<SupportDbContext>());

        services.AddHealthChecks()
            .AddCheck<DatabaseHealthCheck>("database", timeout: TimeSpan.FromSeconds(5));

        services.AddSingleton<IClock, SystemClock>();

        services.AddScoped<ITicketRepository, TicketRepository>();
        services.AddScoped<IAgentRepository, AgentRepository>();
        services.AddScoped<ICustomerRepository, CustomerRepository>();
        services.AddScoped<ICategoryRepository, CategoryRepository>();

        services.AddScoped<ITicketQueries, TicketQueries>();
        services.AddScoped<ITicketEscalationQueries, TicketEscalationQueries>();
        services.AddScoped<ICustomerQueries, CustomerQueries>();
        services.AddScoped<IAgentQueries, AgentQueries>();
        services.AddScoped<IAgentAssignmentQueries, AgentAssignmentQueries>();
        services.AddScoped<ICategoryQueries, CategoryQueries>();

        services.AddScoped<SupportDbSeeder>();
        services.AddScoped<IPasswordHasher<Agent>, PasswordHasher<Agent>>();
        services.AddScoped<IAgentAuthenticationService, AgentAuthenticationService>();

        return services;
    }
}
