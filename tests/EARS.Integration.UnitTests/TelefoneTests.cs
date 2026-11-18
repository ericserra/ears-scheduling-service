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
    [InlineData("555189234034", "5551989234034")]
    [InlineData("(51) 8923-4034", "5551989234034")]
    [InlineData("5182291007", "5551982291007")]
    [InlineData("+55 16 9 9759-8594", "5516997598594")]
    public void CelularSemONonoDigitoGanhaOnono(string entrada, string esperado)
    {
        Telefone.TryCreate(entrada, out var tel).Should().BeTrue();
        tel!.Value.Should().Be(esperado);
    }

    [Theory]
    [InlineData("555132095929")]
    [InlineData("(51) 3209-5929", "555132095929")]
    public void TelefoneFixoNaoRecebeNono(string entrada, string esperado = "")
    {
        Telefone.TryCreate(entrada, out var tel).Should().BeTrue();
        tel!.Value.Should().Be(esperado == "" ? entrada : esperado);
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
