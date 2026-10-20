namespace EARS.Integration.Application.Dtos;

// ---------------------------------------------------------------------------
// API de Agendamento — /integracoes/referencia/{identificador}/...
// Os tipos abaixo já estão normalizados: datas convertidas para o fuso da
// clínica e status traduzido. O formato bruto da Amplimed (datas sem fuso,
// status em português, resposta aninhada por profissional) fica contido nos
// clients da camada de infraestrutura.
// ---------------------------------------------------------------------------

/// <summary>Relação de status da API de Agendamento. O valor é o próprio statusCode.</summary>
public enum StatusAmplimed
{
    Confirmado = 0,
    Agendado = 1,
    Esperando = 2,
    Cancelado = 3,
    NaoCompareceu = 4,
    EmAtendimento = 5,
    Finalizado = 6,
    Reagendado = 7
}

public sealed record ProcedimentoAmplimed(string Id, string Nome, int? ExternalId, bool Ativo);

public sealed record ConvenioAmplimed(string Id, string Nome, int? ExternalId, bool Ativo);

/// <summary>
/// Profissional com integração configurada. Vem de <c>listar-profissionais</c>
/// na própria API de Agendamento — não há dependência da API de Dados.
/// </summary>
public sealed record ProfissionalAmplimed(
    string CodigoProfissional,
    string Nome,
    string? Especialidades,
    bool Telemedicina,
    string? NomeConsultorio,
    IReadOnlyList<DateOnly> DatasDisponiveis,
    IReadOnlyList<ProcedimentoAmplimed> Procedimentos,
    IReadOnlyList<ConvenioAmplimed> Convenios);

public sealed record FiltroProfissionais(
    DateOnly? DataInicial = null,
    DateOnly? DataFinal = null,
    string? NomeProfissional = null,
    string? Especialidade = null,
    int? CodigoIbge = null);

/// <summary>
/// Slot livre na escala do profissional.
/// </summary>
/// <remarks>
/// <see cref="IdHorario"/> é um token opaco composto pela Amplimed
/// (profissional_data_inicio_fim_tele_convenio_procedimento) e é o <em>único</em>
/// jeito de marcar: o <c>cadastrar-agendamento</c> não aceita data e hora soltas.
/// Guardar o token é o que permite revalidar a disponibilidade sem comparar
/// horários, evitando a corrida entre a oferta e a escolha do paciente.
/// </remarks>
public sealed record HorarioDisponivel(
    string IdHorario,
    string CodigoProfissional,
    DateTimeOffset Inicio,
    bool Teleconsulta,
    int? Procedimento,
    string? ProcedimentoDescricao,
    int? Convenio,
    string? ConvenioDescricao,
    decimal? Valor);

public sealed record ProfissionalDaAgenda(
    string Nome,
    string? Especialidade,
    string? Conselho,
    string? CodigoConselho,
    string? UfConselho);

/// <summary>
/// Paciente como aparece na listagem da agenda. Note a ausência de telefone:
/// para enviar WhatsApp é preciso buscar o celular na API de Pacientes por CPF.
/// </summary>
public sealed record BeneficiarioDaAgenda(
    long? Codp,
    string? CodRef,
    string? Cpf,
    string? Nome,
    string? NumeroCarteira);

/// <summary>Agendamento da clínica, achatado a partir da resposta aninhada por profissional.</summary>
public sealed record AgendamentoDaAgenda(
    string Usuclin,
    long IdAgendamento,
    DateTimeOffset Inicio,
    int? DuracaoMinutos,
    StatusAmplimed? Status,
    string? StatusTexto,
    string? Procedimento,
    string? Convenio,
    string? Unidade,
    ProfissionalDaAgenda Profissional,
    BeneficiarioDaAgenda Beneficiario);

/// <summary>Uma página de <c>listar-agendamentos</c> (50 por página sem filtro).</summary>
public sealed record PaginaDaAgenda(
    IReadOnlyList<AgendamentoDaAgenda> Agendamentos,
    int PaginaAtual,
    int TotalPaginas);

/// <summary>
/// Corpo do <c>cadastrar-agendamento</c>. O endpoint faz upsert de paciente:
/// procura por <see cref="IdUnico"/>, depois por CPF + data de nascimento, e
/// só então cria. Por isso <see cref="DataNascimento"/> importa tanto.
/// </summary>
public sealed record CadastrarAgendamentoRequest(
    string IdUnico,
    string IdHorario,
    string? Cpf,
    string Nome,
    DateOnly? DataNascimento,
    string Celular,
    string? Email = null,
    string? SexoBiologico = null,
    string? NumeroCarteirinha = null,
    DateOnly? ValidadeCarteirinha = null,
    string? Cep = null,
    string? Endereco = null,
    string? Numero = null,
    string? Complemento = null,
    string? Bairro = null,
    string? Cidade = null,
    string? Uf = null,
    string? Observacoes = null);

/// <summary>Resposta do <c>cadastrar-agendamento</c>: UUID da integração.</summary>
public sealed record AgendamentoCriado(string IdIntegracao, string? Mensagem);

/// <summary>Item de <c>historico-agendamentos/{idUnico}</c> — só o criado via integração.</summary>
public sealed record AgendamentoDoHistorico(
    string IdIntegracao,
    string CodigoProfissional,
    string ProfissionalNome,
    DateTimeOffset Inicio,
    StatusAmplimed? Status,
    string? StatusTexto,
    string? ProcedimentoDescricao);

// ---------------------------------------------------------------------------
// API de Cadastro de Pacientes — /pacientes/integration/...
// ---------------------------------------------------------------------------

/// <summary>
/// Paciente na API de Pacientes. Não há id interno exposto: o identificador é
/// o <c>codReferencia</c>, cunhado pelo integrador.
/// </summary>
public sealed record PacienteAmplimed(
    string? CodReferencia,
    string Nome,
    string? Cpf,
    string? Celular,
    DateOnly? DataNascimento,
    string? Email,
    string? Genero);

public sealed record CadastrarPacienteRequest(
    string CodReferencia,
    string Nome,
    string? Cpf,
    string? Celular,
    DateOnly? DataNascimento,
    string? Genero = null,
    string? Email = null,
    IReadOnlyDictionary<string, string>? Observacoes = null);

/// <summary>
/// Filtros de <c>GET /pattient</c>. Não existe busca por telefone — daí a regra
/// de identificação por CPF na conversa, com o telefone servindo apenas para
/// casar com o espelho local.
/// </summary>
public sealed record FiltroPacientes(
    string? Cpf = null,
    string? Nome = null,
    string? CodReferencia = null,
    DateOnly? AtualizadoDe = null,
    DateOnly? AtualizadoAte = null,
    int? Pagina = null);
