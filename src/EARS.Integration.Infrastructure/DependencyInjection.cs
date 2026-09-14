using System.Net.Http.Headers;
using EARS.Integration.Application.Abstractions;
using EARS.Integration.Application.Options;
using EARS.Integration.Application.UseCases;
using EARS.Integration.Infrastructure.Clients;
using EARS.Integration.Infrastructure.Clients.Amplimed;
using EARS.Integration.Infrastructure.Clients.SleekFlow;
using EARS.Integration.Infrastructure.Jobs;
using EARS.Integration.Infrastructure.Persistence;
using EARS.Integration.Infrastructure.Resilience;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Quartz;

namespace EARS.Integration.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<AutomacaoOptions>(configuration.GetSection(AutomacaoOptions.SectionName));
        services.Configure<AmplimedOptions>(configuration.GetSection(AmplimedOptions.SectionName));
        services.Configure<SleekFlowOptions>(configuration.GetSection(SleekFlowOptions.SectionName));

        services.AddDbContext<AppDbContext>(opt =>
            opt.UseNpgsql(configuration.GetConnectionString("Postgres")));

        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<AppDbContext>());
        services.AddScoped<IPacienteRepository, PacienteRepository>();
        services.AddScoped<IAgendamentoRepository, AgendamentoRepository>();
        services.AddScoped<IConversaRepository, ConversaRepository>();
        services.AddScoped<IMensagemLogRepository, MensagemLogRepository>();
        services.AddScoped<IOptOutRepository, OptOutRepository>();
        services.AddScoped<IOutboxRepository, OutboxRepository>();
        services.AddSingleton<IRelogio, Relogio>();

        services.AddScoped<ProcessarMensagemRecebidaUseCase>();
        services.AddScoped<ProcessarRespostaConfirmacaoUseCase>();
        services.AddScoped<EnviarConfirmacaoD1UseCase>();
        services.AddScoped<EnviarLembreteUseCase>();

        AddHttpClients(services, configuration);
        AddQuartz(services, configuration);

        return services;
    }

    private static void AddHttpClients(IServiceCollection services, IConfiguration configuration)
    {
        var amplimed = configuration.GetSection(AmplimedOptions.SectionName).Get<AmplimedOptions>()
                       ?? new AmplimedOptions();
        var sleekFlow = configuration.GetSection(SleekFlowOptions.SectionName).Get<SleekFlowOptions>()
                        ?? new SleekFlowOptions();

        services.AddHttpClient<IAmplimedPacientesClient, AmplimedPacientesClient>(c =>
            {
                c.BaseAddress = new Uri(amplimed.Pacientes.BaseUrl);
                c.DefaultRequestHeaders.Add("x-Authorization", $"Basic {amplimed.Pacientes.Token}");
            })
            .AddPolicyHandler((sp, _) => PoliticasHttp.Retry(Logger(sp), "AmplimedPacientes"))
            .AddPolicyHandler((sp, _) => PoliticasHttp.CircuitBreaker(Logger(sp), "AmplimedPacientes"))
            .AddPolicyHandler(PoliticasHttp.Timeout());

        services.AddHttpClient<IAmplimedAgendamentoClient, AmplimedAgendamentoClient>(c =>
            {
                c.BaseAddress = new Uri(amplimed.Agendamento.BaseUrl);
                c.DefaultRequestHeaders.Add("X-authorization", amplimed.Agendamento.Token);
                c.DefaultRequestHeaders.Add("X-identificador", amplimed.Agendamento.Identificador);
            })
            .AddPolicyHandler((sp, _) => PoliticasHttp.Retry(Logger(sp), "AmplimedAgendamento"))
            .AddPolicyHandler((sp, _) => PoliticasHttp.CircuitBreaker(Logger(sp), "AmplimedAgendamento"))
            .AddPolicyHandler(PoliticasHttp.Timeout());

        services.AddHttpClient<IAmplimedDadosClient, AmplimedDadosClient>(c =>
            {
                c.BaseAddress = new Uri(amplimed.Dados.BaseUrl);
            })
            .AddPolicyHandler((sp, _) => PoliticasHttp.Retry(Logger(sp), "AmplimedDados"))
            .AddPolicyHandler((sp, _) => PoliticasHttp.CircuitBreaker(Logger(sp), "AmplimedDados"))
            .AddPolicyHandler(PoliticasHttp.Timeout());

        services.AddHttpClient<ISleekFlowClient, SleekFlowClient>(c =>
            {
                c.BaseAddress = new Uri(sleekFlow.BaseUrl);
                c.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", sleekFlow.ApiKey);
            })
            .AddPolicyHandler((sp, _) => PoliticasHttp.Retry(Logger(sp), "SleekFlow"))
            .AddPolicyHandler((sp, _) => PoliticasHttp.CircuitBreaker(Logger(sp), "SleekFlow"))
            .AddPolicyHandler(PoliticasHttp.Timeout());
    }

    private static void AddQuartz(IServiceCollection services, IConfiguration configuration)
    {
        var automacao = configuration.GetSection(AutomacaoOptions.SectionName).Get<AutomacaoOptions>()
                        ?? new AutomacaoOptions();

        var fuso = TimeZoneConverter.TZConvert.GetTimeZoneInfo(automacao.FusoHorario);

        services.AddQuartz(q =>
        {
            var confirmacao = new JobKey(nameof(ConfirmacaoD1Job));
            q.AddJob<ConfirmacaoD1Job>(o => o.WithIdentity(confirmacao));
            q.AddTrigger(t => t
                .ForJob(confirmacao)
                .WithIdentity($"{nameof(ConfirmacaoD1Job)}-trigger")
                .WithCronSchedule(automacao.CronConfirmacao, x => x.InTimeZone(fuso)));

            var lembrete = new JobKey(nameof(LembreteJob));
            q.AddJob<LembreteJob>(o => o.WithIdentity(lembrete));
            q.AddTrigger(t => t
                .ForJob(lembrete)
                .WithIdentity($"{nameof(LembreteJob)}-trigger")
                .WithCronSchedule(automacao.CronLembrete, x => x.InTimeZone(fuso)));

            var outbox = new JobKey(nameof(OutboxDispatcherJob));
            q.AddJob<OutboxDispatcherJob>(o => o.WithIdentity(outbox));
            q.AddTrigger(t => t
                .ForJob(outbox)
                .WithIdentity($"{nameof(OutboxDispatcherJob)}-trigger")
                .WithCronSchedule(automacao.CronOutbox, x => x.InTimeZone(fuso)));
        });

        services.AddQuartzHostedService(o => o.WaitForJobsToComplete = true);
    }

    private static ILogger Logger(IServiceProvider sp) =>
        sp.GetRequiredService<ILoggerFactory>().CreateLogger("HttpResilience");
}
