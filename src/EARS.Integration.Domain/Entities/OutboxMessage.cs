using EARS.Integration.Domain.Enums;

namespace EARS.Integration.Domain.Entities;

/// <summary>
/// Outbox: falha persistente em chamada externa não se perde — o evento fica
/// aqui e é reprocessado pelo worker com backoff.
/// </summary>
public class OutboxMessage : EntidadeBase
{
    private OutboxMessage() { }

    public OutboxMessage(string tipo, string payloadJson)
    {
        Tipo = tipo;
        PayloadJson = payloadJson;
        Status = StatusOutbox.Pendente;
        ProximaTentativaEm = DateTimeOffset.UtcNow;
    }

    public string Tipo { get; private set; } = null!;
    public string PayloadJson { get; private set; } = null!;
    public StatusOutbox Status { get; private set; }
    public int Tentativas { get; private set; }
    public DateTimeOffset ProximaTentativaEm { get; private set; }
    public string? UltimoErro { get; private set; }

    public void MarcarProcessando()
    {
        Status = StatusOutbox.Processando;
        Tocar();
    }

    public void MarcarConcluido()
    {
        Status = StatusOutbox.Concluido;
        UltimoErro = null;
        Tocar();
    }

    /// <summary>Backoff exponencial limitado; após o teto de tentativas, marca como falha definitiva.</summary>
    public void RegistrarFalha(string erro, int maxTentativas)
    {
        Tentativas++;
        UltimoErro = erro;

        if (Tentativas >= maxTentativas)
        {
            Status = StatusOutbox.Falhou;
        }
        else
        {
            Status = StatusOutbox.Pendente;
            var minutos = Math.Min(Math.Pow(2, Tentativas), 60);
            ProximaTentativaEm = DateTimeOffset.UtcNow.AddMinutes(minutos);
        }

        Tocar();
    }
}
