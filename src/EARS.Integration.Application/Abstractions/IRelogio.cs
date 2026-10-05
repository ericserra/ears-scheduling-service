namespace EARS.Integration.Application.Abstractions;

/// <summary>Abstração de tempo — deixa os jobs e a janela de horário testáveis.</summary>
public interface IRelogio
{
    DateTimeOffset AgoraUtc { get; }
    DateTimeOffset AgoraLocal { get; }
    DateOnly HojeLocal { get; }

    /// <summary>
    /// Instante em que o dia local começa, no fuso da clínica.
    /// </summary>
    /// <remarks>
    /// Necessário porque "os agendamentos do dia 11" é um intervalo no fuso da
    /// clínica, não em UTC. Tratar a borda como meia-noite UTC deixaria de fora
    /// as consultas depois das 21h e incluiria as da noite anterior.
    /// </remarks>
    DateTimeOffset InicioDoDiaLocal(DateOnly dia);
}
