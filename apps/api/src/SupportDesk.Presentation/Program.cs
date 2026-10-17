using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using SupportDesk.Infrastructure.Authentication;
using SupportDesk.Application;
using SupportDesk.Domain.Aggregates.Tickets;
using SupportDesk.Infrastructure;
using SupportDesk.Presentation.Extensions;
using SupportDesk.Presentation.Filters;
using SupportDesk.Presentation.Middleware;

var builder = WebApplication.CreateBuilder(args);

const string FrontendCorsPolicy = "frontend";

builder.Services
    .AddControllers(options => options.Filters.Add<ValidationFilter>())
    .AddJsonOptions(options =>
    {
        // Enums travel as their names, both ways, so the API reads well from the browser.
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });

builder.Services.AddCors(options => options.AddPolicy(
    FrontendCorsPolicy,
    policy => policy
        .WithOrigins("http://localhost:5173", "http://127.0.0.1:5173")
        .AllowAnyHeader()
        .AllowAnyMethod()));

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddSingleton(services =>
{
    var settings = services.GetRequiredService<IConfiguration>().GetRequiredSection("Jwt").Get<JwtSettings>()
        ?? throw new InvalidOperationException("The Jwt configuration section is missing.");
    settings.Validate();
    return settings;
});
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<JwtSettings>((options, settings) =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = settings.CreateValidationParameters();
    });
builder.Services.AddAuthorization();

// Bind and validate the shared business configuration at startup. The policy itself
// has no dependency on ASP.NET, configuration binding, or the database.
var slaSettings = builder.Configuration.GetRequiredSection("Sla").Get<SlaSettings>()
    ?? throw new InvalidOperationException("The Sla configuration section is missing.");
builder.Services.AddSingleton(new SlaPolicy(slaSettings));

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

var app = builder.Build();
// Validate the final configuration before serving requests, including host overrides.
_ = app.Services.GetRequiredService<JwtSettings>();

app.UseMiddleware<ExceptionHandlingMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();

    // Keeps a developer machine one command away from a working database.
    await app.MigrateAndSeedAsync();
}

app.UseCors(FrontendCorsPolicy);
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();

// Exposes the entry point to the HTTP integration test host.
public partial class Program;
