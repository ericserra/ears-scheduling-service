using EARS.Integration.Application.Dtos;

namespace EARS.Integration.Application.Abstractions;

/// <summary>
/// API de Cadastro de Pacientes. Autenticação: cabeçalho x-Authorization com "Basic {token}".
/// </summary>
public interface IAmplimedPacientesClient
{
    /// <summary>
    /// Busca por CPF e telefone em conjunto. Telefone isolado não desambigua
    /// pacientes que compartilham o mesmo número — lição do serviço GMC.
    /// </summary>
    Task<PacienteAmplimed?> BuscarPorCpfETelefoneAsync(string cpf, string telefone, CancellationToken ct = default);

    Task<PacienteAmplimed?> BuscarPorCpfAsync(string cpf, CancellationToken ct = default);

    Task<PacienteAmplimed> CriarAsync(CriarPacienteRequest request, CancellationToken ct = default);
}

/// <summary>
/// API de Agendamento. Autenticação: par Identificador + Token no cabeçalho X-authorization.
/// </summary>
public interface IAmplimedAgendamentoClient
{
    Task<IReadOnlyList<HorarioDisponivel>> ListarHorariosDisponiveisAsync(
        string profissionalId, DateOnly de, DateOnly ate, CancellationToken ct = default);

    Task<AgendamentoAmplimed> CriarAsync(CriarAgendamentoRequest request, CancellationToken ct = default);

    Task CancelarAsync(string agendamentoId, string? motivo, CancellationToken ct = default);

    /// <summary>
    /// Cláusula 3ª do contrato: endpoint não descrito na documentação pública.
    /// Retorna null quando indisponível — o chamador cai no espelho local (cenário B).
    /// </summary>
    Task<IReadOnlyList<AgendamentoAmplimed>?> ListarPorPeriodoAsync(
        DateOnly de, DateOnly ate, CancellationToken ct = default);

    /// <summary>
    /// Cláusula 3ª do contrato: idem. Retorna false quando o endpoint não existe,
    /// permitindo registrar o status apenas no espelho local.
    /// </summary>
    Task<bool> AtualizarStatusAsync(string agendamentoId, string status, CancellationToken ct = default);
}

/// <summary>
/// API de Dados. Autenticação: client_id/client_secret trocados por token Bearer temporário.
/// </summary>
public interface IAmplimedDadosClient
{
    Task<IReadOnlyList<ProfissionalAmplimed>> ListarProfissionaisAsync(CancellationToken ct = default);

    Task<IReadOnlyList<PacienteAmplimed>> ListarPacientesAsync(
        int pagina, int tamanhoPagina = 100, CancellationToken ct = default);
}
