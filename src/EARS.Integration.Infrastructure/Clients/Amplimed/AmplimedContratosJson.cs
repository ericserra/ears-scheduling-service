using System.Text.Json.Serialization;

namespace EARS.Integration.Infrastructure.Clients.Amplimed;

// ---------------------------------------------------------------------------
// Espelho literal dos payloads documentados. Mantido interno de propósito:
// o resto do sistema conversa pelos DTOs normalizados da camada de aplicação,
// e as esquisitices da API (snake_case misturado com camelCase, datas sem fuso,
// status em texto, "pattient" com dois t) ficam presas neste arquivo.
// ---------------------------------------------------------------------------

internal sealed record ProcedimentoJson(
    [property: JsonPropertyName("id")] string? Id,
    [property: JsonPropertyName("nome")] string? Nome,
    [property: JsonPropertyName("active")] bool? Active,
    [property: JsonPropertyName("external_id")] int? ExternalId);

internal sealed record ConvenioJson(
    [property: JsonPropertyName("id")] string? Id,
    [property: JsonPropertyName("nome")] string? Nome,
    [property: JsonPropertyName("active")] bool? Active,
    [property: JsonPropertyName("external_id")] int? ExternalId);

internal sealed record ProfissionalJson(
    [property: JsonPropertyName("nomeProfissional")] string? NomeProfissional,
    [property: JsonPropertyName("codigoProfissional")] string? CodigoProfissional,
    [property: JsonPropertyName("especialidades")] string? Especialidades,
    [property: JsonPropertyName("telemedicina")] bool? Telemedicina,
    [property: JsonPropertyName("nomeConsultorio")] string? NomeConsultorio,
    [property: JsonPropertyName("datasDisponiveis")] List<string>? DatasDisponiveis,
    [property: JsonPropertyName("procedimentos")] List<ProcedimentoJson>? Procedimentos,
    [property: JsonPropertyName("convenios")] List<ConvenioJson>? Convenios);

internal sealed record HorarioJson(
    [property: JsonPropertyName("idHorario")] string? IdHorario,
    [property: JsonPropertyName("dataHora")] string? DataHora,
    [property: JsonPropertyName("codigoProfissional")] string? CodigoProfissional,
    [property: JsonPropertyName("teleconsulta")] bool? Teleconsulta,
    [property: JsonPropertyName("procedimento")] int? Procedimento,
    [property: JsonPropertyName("procedimentoDesc")] string? ProcedimentoDesc,
    [property: JsonPropertyName("convenio")] int? Convenio,
    [property: JsonPropertyName("convenioDesc")] string? ConvenioDesc,
    [property: JsonPropertyName("valor")] decimal? Valor);

internal sealed record BeneficiarioJson(
    [property: JsonPropertyName("codp")] long? Codp,
    [property: JsonPropertyName("codRef")] string? CodRef,
    [property: JsonPropertyName("cpf")] string? Cpf,
    [property: JsonPropertyName("nr_carteira")] string? NrCarteira,
    [property: JsonPropertyName("nome")] string? Nome);

internal sealed record AgendamentoDaAgendaJson(
    [property: JsonPropertyName("usuclin")] string? Usuclin,
    [property: JsonPropertyName("idAgendamento")] long? IdAgendamento,
    [property: JsonPropertyName("data_hora")] string? DataHora,
    [property: JsonPropertyName("duracao")] int? Duracao,
    [property: JsonPropertyName("status")] string? Status,
    [property: JsonPropertyName("procedimento")] string? Procedimento,
    [property: JsonPropertyName("convenio")] string? Convenio,
    [property: JsonPropertyName("unidade")] string? Unidade,
    [property: JsonPropertyName("enderecoUnidade")] string? EnderecoUnidade,
    [property: JsonPropertyName("beneficiario")] BeneficiarioJson? Beneficiario);

internal sealed record ProfissionalDaAgendaJson(
    [property: JsonPropertyName("nome")] string? Nome,
    [property: JsonPropertyName("especialidade")] string? Especialidade,
    [property: JsonPropertyName("conselho")] string? Conselho,
    [property: JsonPropertyName("codConselho")] string? CodConselho,
    [property: JsonPropertyName("ufConselho")] string? UfConselho,
    [property: JsonPropertyName("agendamentos")] List<AgendamentoDaAgendaJson>? Agendamentos);

internal sealed record ListarAgendamentosJson(
    [property: JsonPropertyName("profissionais")] List<ProfissionalDaAgendaJson>? Profissionais,
    [property: JsonPropertyName("currentPage")] int? CurrentPage,
    [property: JsonPropertyName("totalPages")] int? TotalPages);

internal sealed record HistoricoJson(
    [property: JsonPropertyName("id")] string? Id,
    [property: JsonPropertyName("profissional")] string? Profissional,
    [property: JsonPropertyName("nomeProfissional")] string? NomeProfissional,
    [property: JsonPropertyName("horaAtendimento")] string? HoraAtendimento,
    [property: JsonPropertyName("status")] string? Status,
    [property: JsonPropertyName("descricaoProcedimento")] string? DescricaoProcedimento);

internal sealed record CadastrarAgendamentoJson(
    [property: JsonPropertyName("idUnico")] string IdUnico,
    [property: JsonPropertyName("idHorario")] string IdHorario,
    [property: JsonPropertyName("cpf")] string? Cpf,
    [property: JsonPropertyName("nome")] string Nome,
    [property: JsonPropertyName("dataNascimento")] string? DataNascimento,
    [property: JsonPropertyName("celular")] string Celular,
    [property: JsonPropertyName("email")] string? Email,
    [property: JsonPropertyName("sexoBiologico")] string? SexoBiologico,
    [property: JsonPropertyName("nrCarteirinha")] string? NrCarteirinha,
    [property: JsonPropertyName("validadeCarteirinha")] string? ValidadeCarteirinha,
    [property: JsonPropertyName("cep")] string? Cep,
    [property: JsonPropertyName("endereco")] string? Endereco,
    [property: JsonPropertyName("numero")] string? Numero,
    [property: JsonPropertyName("complemento")] string? Complemento,
    [property: JsonPropertyName("bairro")] string? Bairro,
    [property: JsonPropertyName("cidade")] string? Cidade,
    [property: JsonPropertyName("uf")] string? Uf,
    [property: JsonPropertyName("observacoesAgendamento")] string? ObservacoesAgendamento);

internal sealed record AgendamentoCriadoJson(
    [property: JsonPropertyName("message")] string? Message,
    [property: JsonPropertyName("id")] string? Id);

/// <summary>
/// O usuclin é numérico aqui (o exemplo da documentação manda <c>19</c>, sem
/// aspas), embora a listagem da agenda o devolva como string <c>"1"</c>.
/// </summary>
internal sealed record AlterarStatusJson(
    [property: JsonPropertyName("usuclin")] long Usuclin,
    [property: JsonPropertyName("idAgendamento")] long IdAgendamento,
    [property: JsonPropertyName("statusCode")] int StatusCode);

internal sealed record CancelarAgendamentoJson(
    [property: JsonPropertyName("idAgendamento")] string IdAgendamento,
    [property: JsonPropertyName("motivoCancelamento")] string? MotivoCancelamento);

internal sealed record ExcluirAgendamentoJson(
    [property: JsonPropertyName("idAgendamento")] string IdAgendamento);

internal sealed record MensagemJson(
    [property: JsonPropertyName("message")] string? Message);

// --- API de Cadastro de Pacientes -----------------------------------------

internal sealed record PacienteJson(
    [property: JsonPropertyName("codReferencia")] string? CodReferencia,
    [property: JsonPropertyName("nomeSocial")] string? NomeSocial,
    [property: JsonPropertyName("nomeRegistro")] string? NomeRegistro,
    [property: JsonPropertyName("dataNascimento")] string? DataNascimento,
    [property: JsonPropertyName("genero")] string? Genero,
    [property: JsonPropertyName("cpf")] string? Cpf,
    [property: JsonPropertyName("celular")] string? Celular,
    [property: JsonPropertyName("email")] string? Email);

/// <summary>
/// A listagem de pacientes aparece nos exemplos de duas formas: array puro e
/// envelopado em <c>{ "data": [...] }</c>. O client aceita as duas.
/// </summary>
internal sealed record PacientesEnvelopeJson(
    [property: JsonPropertyName("data")] List<PacienteJson>? Data);

internal sealed record CadastrarPacienteJson(
    [property: JsonPropertyName("codReferencia")] string CodReferencia,
    [property: JsonPropertyName("nomeRegistro")] string NomeRegistro,
    [property: JsonPropertyName("nomeSocial")] string? NomeSocial,
    [property: JsonPropertyName("cpf")] string? Cpf,
    [property: JsonPropertyName("dataNascimento")] string? DataNascimento,
    [property: JsonPropertyName("genero")] string? Genero,
    [property: JsonPropertyName("celular")] string? Celular,
    [property: JsonPropertyName("email")] string? Email,
    [property: JsonPropertyName("obs")] Dictionary<string, string>? Obs);

internal sealed record VincularCodReferenciaJson(
    [property: JsonPropertyName("cpf")] string Cpf,
    [property: JsonPropertyName("codReferencia")] string CodReferencia);

internal sealed record SucessoJson(
    [property: JsonPropertyName("success")] bool? Success,
    [property: JsonPropertyName("message")] string? Message,
    [property: JsonPropertyName("codReferencia")] string? CodReferencia);
