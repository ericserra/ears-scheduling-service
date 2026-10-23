using EARS.Integration.Application.Abstractions;
using EARS.Integration.Application.Dtos;
using EARS.Integration.Application.Options;
using EARS.Integration.Domain.Entities;
using EARS.Integration.Domain.ValueObjects;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EARS.Integration.Application.UseCases;

public sealed record ResultadoSincronizacao(
    int Lidos,
    int Criados,
    int Conciliados,
    int VinculadosAgenda,
    int IgnoradosSemCpf,
    int IgnoradosSemTelefone)
{
    public static ResultadoSincronizacao Vazio => new(0, 0, 0, 0, 0, 0);

    public ResultadoSincronizacao Somar(ResultadoSincronizacao outro) => new(
        Lidos + outro.Lidos,
        Criados + outro.Criados,
        Conciliados + outro.Conciliados,
        VinculadosAgenda + outro.VinculadosAgenda,
        IgnoradosSemCpf + outro.IgnoradosSemCpf,
        IgnoradosSemTelefone + outro.IgnoradosSemTelefone);
}

/// <summary>
/// Polling da agenda da clínica — o cenário A da Cláusula 3ª.
/// </summary>
/// <remarks>
/// A Amplimed não oferece webhook nesta integração, então a única forma de saber
/// de um agendamento marcado no sistema da clínica é consultar
/// <c>listar-agendamentos</c> periodicamente. O espelho local continua sendo a
/// base de leitura do disparo D-1 e do lembrete; este caso de uso é o que o
/// mantém fiel à agenda.
///
/// Duas consequências do formato da API moldam o que está aqui:
///
/// 1. A listagem da agenda <em>não devolve telefone</em>. Para poder enviar
///    WhatsApp é preciso buscar o celular na API de Pacientes pelo CPF. Sem CPF
///    ou sem celular, o agendamento é ignorado com log: não há como avisar o
///    paciente, e copiar dado pessoal sem finalidade seria pior do que não ter.
///
/// 2. Agendamento criado por esta integração nasce só com o UUID e ganha o par
///    (usuclin, idAgendamento) aqui, no primeiro polling que o encontrar. É esse
///    par que o <c>alterar-status-agendamento</c> exige.
/// </remarks>
public sealed class SincronizarAgendaUseCase(
    IAmplimedAgendamentoClient agendamentoClient,
    IAmplimedPacientesClient pacientesClient,
    IProntidaoIntegracao prontidao,
    IPacienteRepository pacientes,
    IAgendamentoRepository agendamentos,
    IUnitOfWork uow,
    IRelogio relogio,
    IOptions<AutomacaoOptions> options,
    ILogger<SincronizarAgendaUseCase> logger)
{
    private readonly AutomacaoOptions _opcoes = options.Value;

    /// <summary>Sincroniza a janela configurada a partir de hoje.</summary>
    public async Task<ResultadoSincronizacao> ExecutarAsync(CancellationToken ct = default)
    {
        // Sem credencial da Amplimed não há o que sincronizar, e insistir a
        // cada quinze minutos só enche o log de erro enquanto o ambiente
        // espera as chaves chegarem.
        if (!prontidao.AmplimedConfigurada)
        {
            logger.LogDebug("Polling da agenda ocioso: credenciais da Amplimed ainda não configuradas.");
            return ResultadoSincronizacao.Vazio;
        }

        if (!_opcoes.LeituraAgendaViaApi)
        {
            logger.LogInformation(
                "Cenário B em vigor (LeituraAgendaViaApi=false); polling da agenda não executado.");
            return ResultadoSincronizacao.Vazio;
        }

        var total = ResultadoSincronizacao.Vazio;
        var dias = Math.Max(1, _opcoes.DiasSincronizacaoAgenda);

        for (var i = 0; i < dias; i++)
        {
            ct.ThrowIfCancellationRequested();
            total = total.Somar(await ExecutarParaDiaAsync(relogio.HojeLocal.AddDays(i), ct));
        }

        logger.LogInformation(
            "Agenda sincronizada em {Dias} dia(s): {Lidos} lidos, {Criados} criados, " +
            "{Conciliados} conciliados, {Vinculados} vinculados ao par da agenda, " +
            "{SemCpf} sem CPF, {SemTelefone} sem telefone.",
            dias, total.Lidos, total.Criados, total.Conciliados,
            total.VinculadosAgenda, total.IgnoradosSemCpf, total.IgnoradosSemTelefone);

        return total;
    }

    public async Task<ResultadoSincronizacao> ExecutarParaDiaAsync(DateOnly dia, CancellationToken ct = default)
    {
        var daAgenda = await agendamentoClient.ListarAgendaCompletaDoDiaAsync(dia, ct);

        int criados = 0, conciliados = 0, vinculados = 0, semCpf = 0, semTelefone = 0;

        foreach (var item in daAgenda)
        {
            ct.ThrowIfCancellationRequested();

            var statusLocal = MapaStatus.ParaLocal(item.Status ?? StatusAmplimed.Agendado);

            // Caminho curto: já conhecemos o par (usuclin, idAgendamento).
            var existente = await agendamentos.ObterPorIdAmplimedAsync(
                item.Usuclin, item.IdAgendamento, ct);

            if (existente is not null)
            {
                existente.ConciliarComAgenda(statusLocal, item.Inicio, item.DuracaoMinutos);
                conciliados++;
                continue;
            }

            var paciente = await ResolverPacienteAsync(item.Beneficiario, ct);

            if (paciente is null)
            {
                if (string.IsNullOrWhiteSpace(item.Beneficiario.Cpf)) semCpf++;
                else semTelefone++;
                continue;
            }

            // Late binding: o que nasceu no WhatsApp ainda não tem o par inteiro.
            var semVinculo = await agendamentos.ObterSemVinculoDaAgendaAsync(paciente.Id, item.Inicio, ct);

            if (semVinculo is not null)
            {
                semVinculo.VincularAgenda(item.Usuclin, item.IdAgendamento);
                semVinculo.ConciliarComAgenda(statusLocal, item.Inicio, item.DuracaoMinutos);
                vinculados++;
                continue;
            }

            await agendamentos.AdicionarAsync(Agendamento.DaAgenda(
                item.Usuclin,
                item.IdAgendamento,
                paciente.Id,
                item.Profissional.Nome,
                item.Inicio,
                item.DuracaoMinutos,
                statusLocal), ct);

            criados++;
        }

        await uow.SalvarAsync(ct);

        return new ResultadoSincronizacao(
            daAgenda.Count, criados, conciliados, vinculados, semCpf, semTelefone);
    }

    /// <summary>
    /// Resolve o paciente da agenda no espelho local, buscando o celular na API
    /// de Pacientes quando ele ainda não é conhecido. Devolve <c>null</c> quando
    /// não há como enviar mensagem — sem CPF para buscar, ou sem celular válido.
    /// </summary>
    private async Task<Paciente?> ResolverPacienteAsync(
        BeneficiarioDaAgenda beneficiario, CancellationToken ct)
    {
        // codRef cunhado por nós identifica o paciente sem nenhuma chamada extra.
        if (!string.IsNullOrWhiteSpace(beneficiario.CodRef)
            && beneficiario.CodRef.StartsWith(Paciente.PrefixoCodReferencia, StringComparison.Ordinal))
        {
            var porCodRef = await pacientes.ObterPorCodReferenciaAsync(beneficiario.CodRef, ct);
            if (porCodRef is not null) return porCodRef;
        }

        if (!Cpf.TryCreate(beneficiario.Cpf, out var cpf) || cpf is null)
        {
            logger.LogInformation(
                "Agendamento da agenda sem CPF utilizável (codp {Codp}); ignorado no envio automático.",
                beneficiario.Codp);
            return null;
        }

        var local = await pacientes.ObterPorCpfAsync(cpf.Value, ct);
        if (local is not null) return local;

        // A listagem da agenda não traz telefone: é preciso ir à API de Pacientes.
        var remoto = await pacientesClient.BuscarPorCpfAsync(cpf.Value, ct);

        if (remoto is null)
        {
            logger.LogWarning(
                "CPF da agenda sem cadastro correspondente na API de Pacientes; agendamento ignorado.");
            return null;
        }

        if (!Telefone.TryCreate(remoto.Celular, out var telefone) || telefone is null)
        {
            logger.LogInformation(
                "Paciente {CodReferencia} sem celular válido na Amplimed; agendamento fica para a recepção.",
                remoto.CodReferencia ?? "(sem codReferencia)");
            return null;
        }

        var paciente = new Paciente(
            remoto.Nome,
            telefone,
            remoto.DataNascimento,
            cpf);

        await pacientes.AdicionarAsync(paciente, ct);

        // Paciente antigo da clínica sem codReferencia: amarra o nosso agora, para
        // que um agendamento futuro pelo WhatsApp caia no upsert por idUnico em
        // vez de arriscar um cadastro duplicado.
        if (string.IsNullOrWhiteSpace(remoto.CodReferencia))
        {
            if (await pacientesClient.VincularCodReferenciaAsync(cpf.Value, paciente.CodReferencia, ct))
                paciente.MarcarCodReferenciaVinculado();
        }

        return paciente;
    }
}
