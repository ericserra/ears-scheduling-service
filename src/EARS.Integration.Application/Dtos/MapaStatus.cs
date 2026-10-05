using System.Globalization;
using System.Text;
using EARS.Integration.Domain.Enums;

namespace EARS.Integration.Application.Dtos;

/// <summary>
/// Tradução entre o status local e a relação de status da Amplimed.
/// </summary>
/// <remarks>
/// A API fala de status de duas formas: <c>listar-agendamentos</c> devolve texto
/// em português ("Agendado", "Cancelado") e <c>alterar-status-agendamento</c>
/// recebe o <c>statusCode</c> numérico. Os dois passam por aqui.
/// </remarks>
public static class MapaStatus
{
    /// <summary>
    /// Status local correspondente, ou <c>null</c> quando o estado é só nosso.
    /// <see cref="StatusAgendamento.NaoConfirmado"/> não tem equivalente: a
    /// Amplimed segue mostrando "Agendado", e propagá-lo mentiria para a recepção.
    /// </summary>
    public static StatusAmplimed? ParaAmplimed(StatusAgendamento status) => status switch
    {
        StatusAgendamento.Confirmado => StatusAmplimed.Confirmado,
        StatusAgendamento.Agendado => StatusAmplimed.Agendado,
        StatusAgendamento.Esperando => StatusAmplimed.Esperando,
        StatusAgendamento.Cancelado => StatusAmplimed.Cancelado,
        StatusAgendamento.NaoCompareceu => StatusAmplimed.NaoCompareceu,
        StatusAgendamento.EmAtendimento => StatusAmplimed.EmAtendimento,
        StatusAgendamento.Finalizado => StatusAmplimed.Finalizado,
        StatusAgendamento.Remarcado => StatusAmplimed.Reagendado,
        _ => null
    };

    public static StatusAgendamento ParaLocal(StatusAmplimed status) => status switch
    {
        StatusAmplimed.Confirmado => StatusAgendamento.Confirmado,
        StatusAmplimed.Agendado => StatusAgendamento.Agendado,
        StatusAmplimed.Esperando => StatusAgendamento.Esperando,
        StatusAmplimed.Cancelado => StatusAgendamento.Cancelado,
        StatusAmplimed.NaoCompareceu => StatusAgendamento.NaoCompareceu,
        StatusAmplimed.EmAtendimento => StatusAgendamento.EmAtendimento,
        StatusAmplimed.Finalizado => StatusAgendamento.Finalizado,
        StatusAmplimed.Reagendado => StatusAgendamento.Remarcado,
        _ => StatusAgendamento.Agendado
    };

    /// <summary>
    /// Interpreta o texto de status da listagem. Tolera acento, caixa e espaço,
    /// porque o rótulo é de exibição e não contrato. Texto desconhecido devolve
    /// <c>null</c> — e o chamador registra em log em vez de inventar um status.
    /// </summary>
    public static StatusAmplimed? DeTexto(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return null;

        return Normalizar(texto) switch
        {
            "confirmado" => StatusAmplimed.Confirmado,
            "agendado" => StatusAmplimed.Agendado,
            "esperando" => StatusAmplimed.Esperando,
            "cancelado" => StatusAmplimed.Cancelado,
            "ncompareceu" or "naocompareceu" => StatusAmplimed.NaoCompareceu,
            "ematendimento" => StatusAmplimed.EmAtendimento,
            "finalizado" => StatusAmplimed.Finalizado,
            "reagendado" or "remarcado" => StatusAmplimed.Reagendado,
            _ => null
        };
    }

    private static string Normalizar(string entrada)
    {
        var decomposto = entrada.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposto.Length);

        foreach (var c in decomposto)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            if (char.IsWhiteSpace(c) || c is '-' or '_') continue;
            sb.Append(c);
        }

        return sb.ToString().Normalize(NormalizationForm.FormC);
    }
}
