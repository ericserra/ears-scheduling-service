using EARS.Integration.Domain.ValueObjects;
using FluentAssertions;
using Xunit;

namespace EARS.Integration.UnitTests;

public class TelefoneTests
{
    [Theory]
    [InlineData("(51) 99999-8888", "5551999998888")]
    [InlineData("51999998888", "5551999998888")]
    [InlineData("5551999998888", "5551999998888")]
    [InlineData("+55 51 99999-8888", "5551999998888")]
    public void DeveNormalizarParaE164(string entrada, string esperado)
    {
        Telefone.TryCreate(entrada, out var tel).Should().BeTrue();
        tel!.Value.Should().Be(esperado);
    }

    [Theory]
    [InlineData("123")]
    [InlineData("")]
    [InlineData(null)]
    public void DeveRejeitarEntradaInvalida(string? entrada)
    {
        Telefone.TryCreate(entrada, out _).Should().BeFalse();
    }
}
