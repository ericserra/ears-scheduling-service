namespace EARS.Integration.Application.Abstractions;

/// <summary>Abstração de tempo — deixa os jobs e a janela de horário testáveis.</summary>
public interface IRelogio
{
    DateTimeOffset AgoraUtc { get; }
    DateTimeOffset AgoraLocal { get; }
    DateOnly HojeLocal { get; }
}
