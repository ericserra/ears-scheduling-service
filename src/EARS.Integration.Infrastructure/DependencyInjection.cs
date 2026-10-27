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
        services.Configure<RegrasClinicaOptions>(configuration.GetSection(RegrasClinicaOptions.SectionName));
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
        services.AddSingleton<IProntidaoIntegracao, ProntidaoIntegracao>();

        services.AddScoped<ProcessarMensagemRecebidaUseCase>();
        services.AddScoped<IDialogoAtendimento>(sp => sp.GetRequiredService<ProcessarMensagemRecebidaUseCase>());
        services.AddScoped<IniciarAtendimentoUseCase>();
        services.AddScoped<ExpirarConversasUseCase>();
        services.AddScoped<ProcessarRespostaConfirmacaoUseCase>();
        services.AddScoped<EnviarConfirmacaoD1UseCase>();
        services.AddScoped<EnviarLembreteUseCase>();
        services.AddScoped<SincronizarAgendaUseCase>();

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
                c.DefaultRequestHeaders.Add("x-Authorization", amplimed.Pacientes.Cabecalho);
            })
            .AddPolicyHandler((sp, _) => PoliticasHttp.Retry(Logger(sp), "AmplimedPacientes"))
            .AddPolicyHandler((sp, _) => PoliticasHttp.CircuitBreaker(Logger(sp), "AmplimedPacientes"))
            .AddPolicyHandler(PoliticasHttp.Timeout());

        // O identificador da API de Agendamento vai no caminho da rota, montado
        // pelo client. Só o token é cabeçalho.
        services.AddHttpClient<IAmplimedAgendamentoClient, AmplimedAgendamentoClient>(c =>
            {
                c.BaseAddress = new Uri(amplimed.Agendamento.BaseUrl);
                c.DefaultRequestHeaders.Add("x-authorization", amplimed.Agendamento.Token);
            })
            .AddPolicyHandler((sp, _) => PoliticasHttp.Retry(Logger(sp), "AmplimedAgendamento"))
            .AddPolicyHandler((sp, _) => PoliticasHttp.CircuitBreaker(Logger(sp), "AmplimedAgendamento"))
            .AddPolicyHandler(PoliticasHttp.Timeout());

        // A Platform API autentica por X-Sleekflow-Api-Key, não por Bearer.
        services.AddHttpClient<ISleekFlowClient, SleekFlowClient>(c =>
            {
                c.BaseAddress = new Uri(sleekFlow.BaseUrl);
                c.DefaultRequestHeaders.Add("X-Sleekflow-Api-Key", sleekFlow.ApiKey);
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
            // Módulo 2 só entra quando a clínica confirmar que quer lembrete
            // automático. Enquanto não confirmar, os jobs nem são registrados:
            // o risco é o paciente receber a confirmação em duplicidade com o
            // que a clínica já manda por outro meio.
            if (automacao.ConfirmacaoAtiva)
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
            }

            // Expiração de conversa roda sempre: é ela que devolve o menu a
            // quem desistiu no meio do agendamento.
            var expiracao = new JobKey(nameof(ExpiracaoConversaJob));
            q.AddJob<ExpiracaoConversaJob>(o => o.WithIdentity(expiracao));
            q.AddTrigger(t => t
                .ForJob(expiracao)
                .WithIdentity($"{nameof(ExpiracaoConversaJob)}-trigger")
                .WithCronSchedule(automacao.CronExpiracaoConversa, x => x.InTimeZone(fuso)));

            // Polling da agenda: a Amplimed não oferece webhook nesta integração.
            var sincronizacao = new JobKey(nameof(SincronizacaoAgendaJob));
            q.AddJob<SincronizacaoAgendaJob>(o => o.WithIdentity(sincronizacao));
            q.AddTrigger(t => t
                .ForJob(sincronizacao)
                .WithIdentity($"{nameof(SincronizacaoAgendaJob)}-trigger")
                .WithCronSchedule(automacao.CronSincronizacaoAgenda, x => x.InTimeZone(fuso)));

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
