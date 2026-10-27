using EARS.Integration.Application.Abstractions;
using EARS.Integration.Infrastructure.Clients;
using Microsoft.Extensions.Options;

namespace EARS.Integration.Infrastructure;

/// <inheritdoc />
public sealed class ProntidaoIntegracao : IProntidaoIntegracao
{
    public ProntidaoIntegracao(
        IOptions<AmplimedOptions> amplimed,
        IOptions<SleekFlowOptions> sleekFlow)
    {
        var a = amplimed.Value;
        var s = sleekFlow.Value;

        var pendencias = new List<string>();

        if (string.IsNullOrWhiteSpace(a.Pacientes.Token))
            pendencias.Add("Amplimed__Pacientes__Token");

        if (string.IsNullOrWhiteSpace(a.Agendamento.Identificador))
            pendencias.Add("Amplimed__Agendamento__Identificador");

        if (string.IsNullOrWhiteSpace(a.Agendamento.Token))
            pendencias.Add("Amplimed__Agendamento__Token");

        if (string.IsNullOrWhiteSpace(s.ApiKey))
            pendencias.Add("SleekFlow__ApiKey");

        if (string.IsNullOrWhiteSpace(s.RemetenteTelefone))
            pendencias.Add("SleekFlow__RemetenteTelefone");

        // Sem ele o webhook rejeita tudo, então o SleekFlow não consegue
        // entregar mensagem nenhuma: é impedimento, não degradação.
        if (string.IsNullOrWhiteSpace(s.WebhookSecret))
            pendencias.Add("SleekFlow__WebhookSecret");

        AmplimedConfigurada = !pendencias.Any(p => p.StartsWith("Amplimed", StringComparison.Ordinal));
        SleekFlowConfigurado = !pendencias.Any(p => p.StartsWith("SleekFlow", StringComparison.Ordinal));

        // O usuclin não impede atender: sem ele o serviço cai no cenário B da
        // Cláusula 3ª, que é degradação prevista, não impedimento.
        if (string.IsNullOrWhiteSpace(a.Agendamento.Usuclin))
            pendencias.Add("Amplimed__Agendamento__Usuclin (opcional: sem ele, cenário B)");

        Pendencias = pendencias;
    }

    public bool Pronta => AmplimedConfigurada && SleekFlowConfigurado;

    public bool AmplimedConfigurada { get; }

    public bool SleekFlowConfigurado { get; }

    public IReadOnlyList<string> Pendencias { get; }
}
