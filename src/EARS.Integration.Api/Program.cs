using EARS.Integration.Api.Endpoints;
using EARS.Integration.Application.Abstractions;
using EARS.Integration.Infrastructure;
using EARS.Integration.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((ctx, cfg) => cfg
    .ReadFrom.Configuration(ctx.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console());

builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.AddHealthChecks()
    .AddNpgSql(builder.Configuration.GetConnectionString("Postgres")!, name: "postgres");

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddProblemDetails();

var app = builder.Build();

app.UseSerilogRequestLogging();
app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapHealthChecks("/health");
app.MapWebhookEndpoints();
app.MapAdminEndpoints();

// Migrações aplicadas na subida: ambiente único, sem pipeline de release separado.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
}

// Estado da configuração, uma vez, na subida. O serviço sobe mesmo incompleto,
// porque o ambiente precisa existir antes das credenciais chegarem; o que ele
// não faz é aceitar paciente nesse estado.
{
    var prontidao = app.Services.GetRequiredService<IProntidaoIntegracao>();
    var log = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Prontidao");

    if (prontidao.Pronta)
    {
        log.LogInformation("Configuração completa: o motor aceita atendimento.");
    }
    else
    {
        log.LogWarning(
            "SERVIÇO NO AR, MAS SEM ATENDER. Falta configurar: {Pendencias}. " +
            "Enquanto isso, /webhooks/sleekflow/atendimento responde 503 e o Flow Builder " +
            "deve mandar o paciente para a Recepção. Veja /admin/prontidao.",
            string.Join(", ", prontidao.Pendencias));
    }
}

await app.RunAsync();

public partial class Program;
