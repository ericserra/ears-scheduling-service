namespace EARS.Integration.Application.Dtos;

/// <summary>Paciente como retornado/enviado à Amplimed.</summary>
public sealed record PacienteAmplimed(
    string Id,
    string Nome,
    string? Cpf,
    string? Telefone,
    DateOnly? DataNascimento);

public sealed record CriarPacienteRequest(
    string Nome,
    string Cpf,
    string Telefone,
    DateOnly? DataNascimento,
    string? Observacoes);

/// <summary>Slot livre na agenda do profissional.</summary>
public sealed record HorarioDisponivel(
    string ProfissionalId,
    string ProfissionalNome,
    DateTimeOffset Inicio,
    DateTimeOffset Fim);

public sealed record CriarAgendamentoRequest(
    string PacienteId,
    string ProfissionalId,
    DateTimeOffset Inicio,
    string? Observacoes);

public sealed record AgendamentoAmplimed(
    string Id,
    string PacienteId,
    string PacienteNome,
    string? PacienteTelefone,
    string ProfissionalId,
    string ProfissionalNome,
    DateTimeOffset Inicio,
    string Status);

public sealed record ProfissionalAmplimed(string Id, string Nome, string? Especialidade);
