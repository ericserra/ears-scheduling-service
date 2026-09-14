using EARS.Integration.Domain.Entities;
using EARS.Integration.Domain.Enums;
using FluentAssertions;
using Xunit;

namespace EARS.Integration.UnitTests;

public class OutboxMessageTests
{
    [Fact]
    public void FalhaAgendaNovaTentativaComBackoff()
    {
        var msg = new OutboxMessage("teste", "{}");
        msg.RegistrarFalha("erro", maxTentativas: 5);

        msg.Status.Should().Be(StatusOutbox.Pendente);
        msg.Tentativas.Should().Be(1);
        msg.ProximaTentativaEm.Should().BeAfter(DateTimeOffset.UtcNow);
    }

    [Fact]
    public void FalhaDefinitivaAposTetoDeTentativas()
    {
        var msg = new OutboxMessage("teste", "{}");
        for (var i = 0; i < 3; i++)
            msg.RegistrarFalha("erro", maxTentativas: 3);

        msg.Status.Should().Be(StatusOutbox.Falhou);
    }
}
