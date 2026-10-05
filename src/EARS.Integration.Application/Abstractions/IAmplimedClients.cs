using EARS.Integration.Application.Dtos;

namespace EARS.Integration.Application.Abstractions;

/// <summary>
/// API de Cadastro de Pacientes.
/// Base: <c>{api_gateway}/pacientes/integration</c>.
/// Autenticação: cabeçalho <c>x-Authorization: Basic {token}</c>.
/// </summary>
public interface IAmplimedPacientesClient
{
    /// <summary>
    /// Busca por CPF em <c>GET /pattient</c>. Aceita CPF com ou sem máscara.
    /// Devolve <c>null</c> quando a Amplimed não tem cadastro para o CPF.
    /// </summary>
    Task<PacienteAmplimed?> BuscarPorCpfAsync(string cpf, CancellationToken ct = default);

    /// <summary>Busca pelo codReferencia que este serviço cunhou.</summary>
    Task<PacienteAmplimed?> BuscarPorCodReferenciaAsync(string codReferencia, CancellationToken ct = default);

    /// <summary>Uma página de pacientes alterados na janela informada.</summary>
    Task<IReadOnlyList<PacienteAmplimed>> ListarAsync(FiltroPacientes filtro, CancellationToken ct = default);

    /// <summary>
    /// <c>POST /pattient</c>. Atenção: a resposta é apenas
    /// <c>{ success, message }</c> — não devolve o paciente nem um id, então o
    /// identificador utilizável é o codReferencia que enviamos.
    /// </summary>
    Task<bool> CadastrarAsync(CadastrarPacienteRequest request, CancellationToken ct = default);

    /// <summary>
    /// <c>PUT /codreferencia</c>. Vincula o nosso codReferencia a um cadastro que
    /// já existia na Amplimed, achado pelo CPF. É o que evita cadastro duplicado
    /// para paciente antigo da clínica.
    /// </summary>
    Task<bool> VincularCodReferenciaAsync(string cpf, string codReferencia, CancellationToken ct = default);
}

/// <summary>
/// API de Agendamento.
/// Base: <c>{api_gateway}/integracoes/referencia/{identificador}</c> — o
/// identificador vai no caminho, não em cabeçalho.
/// Autenticação: cabeçalho <c>x-authorization: {token}</c>.
/// </summary>
/// <remarks>
/// A Amplimed não oferece webhook nesta integração: acompanhar novos
/// agendamentos e mudanças de status exige polling de
/// <c>listar-agendamentos</c>. Ver ADR 0002.
/// </remarks>
public interface IAmplimedAgendamentoClient
{
    /// <summary>Profissionais com integração configurada (<c>listar-profissionais</c>).</summary>
    Task<IReadOnlyList<ProfissionalAmplimed>> ListarProfissionaisAsync(
        FiltroProfissionais? filtro = null, CancellationToken ct = default);

    /// <summary>Slots livres na escala do profissional (<c>listar-horarios</c>).</summary>
    Task<IReadOnlyList<HorarioDisponivel>> ListarHorariosDisponiveisAsync(
        string codigoProfissional, DateOnly de, DateOnly ate, CancellationToken ct = default);

    /// <summary>
    /// Uma página da agenda da clínica em um dia (<c>listar-agendamentos</c>).
    /// Cobre agendamentos de qualquer canal, não só os desta integração.
    /// </summary>
    Task<PaginaDaAgenda> ListarAgendamentosDoDiaAsync(
        DateOnly dia, int pagina = 1, string? codigoProfissional = null, string? cpf = null,
        CancellationToken ct = default);

    /// <summary>Percorre todas as páginas do dia e devolve a agenda inteira.</summary>
    Task<IReadOnlyList<AgendamentoDaAgenda>> ListarAgendaCompletaDoDiaAsync(
        DateOnly dia, CancellationToken ct = default);

    /// <summary>Agendamentos do paciente feitos via integração (<c>historico-agendamentos</c>).</summary>
    Task<IReadOnlyList<AgendamentoDoHistorico>> ListarHistoricoDoPacienteAsync(
        string idUnico, CancellationToken ct = default);

    /// <summary>
    /// <c>cadastrar-agendamento</c>. Marca pelo <c>idHorario</c> e faz upsert do
    /// paciente. Devolve o UUID da integração.
    /// </summary>
    Task<AgendamentoCriado> CadastrarAsync(CadastrarAgendamentoRequest request, CancellationToken ct = default);

    /// <summary>
    /// <c>alterar-status-agendamento</c>. Exige o par (usuclin, idAgendamento) da
    /// listagem da agenda — não aceita o UUID da integração. É por aqui que o
    /// "Confirmado" da resposta do paciente chega à agenda da clínica.
    /// </summary>
    Task<bool> AlterarStatusAsync(
        string usuclin, long idAgendamento, StatusAmplimed status, CancellationToken ct = default);

    /// <summary>
    /// <c>cancelar-agendamento</c>. Só vale para agendamento criado via
    /// integração, e aceita apenas o UUID. Para o que veio da agenda da clínica,
    /// o caminho é <see cref="AlterarStatusAsync"/> com
    /// <see cref="StatusAmplimed.Cancelado"/>.
    /// </summary>
    Task<bool> CancelarAsync(string idIntegracao, string? motivo, CancellationToken ct = default);

    /// <summary>
    /// <c>excluir-agendamento</c>. Remove em vez de cancelar — não é usado no
    /// fluxo do paciente, fica disponível para operação.
    /// </summary>
    Task<bool> ExcluirAsync(string idIntegracao, CancellationToken ct = default);

    /// <summary>Link de teleatendimento de um agendamento, quando houver.</summary>
    Task<string?> BuscarLinkTeleatendimentoAsync(string idAgendamento, CancellationToken ct = default);
}
