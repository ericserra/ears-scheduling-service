using EARS.Integration.Domain.ValueObjects;
using FluentAssertions;
using Xunit;

namespace EARS.Integration.UnitTests;

public class CpfTests
{
    [Theory]
    [InlineData("529.982.247-25")]
    [InlineData("52998224725")]
    public void DeveAceitarCpfValido(string entrada)
    {
        Cpf.TryCreate(entrada, out var cpf).Should().BeTrue();
        cpf!.Value.Should().Be("52998224725");
    }

    [Theory]
    [InlineData("111.111.111-11")]   // dígitos repetidos
    [InlineData("529.982.247-26")]   // dígito verificador errado
    [InlineData("1234567890")]       // comprimento inválido
    [InlineData("")]
    [InlineData(null)]
    public void DeveRejeitarCpfInvalido(string? entrada)
    {
        Cpf.TryCreate(entrada, out var cpf).Should().BeFalse();
        cpf.Should().BeNull();
    }

    [Fact]
    public void DeveFormatarParaExibicao()
    {
        Cpf.Create("52998224725").Formatado().Should().Be("529.982.247-25");
    }
}
