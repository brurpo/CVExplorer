using CVExplorer.Host.Features.Media;

namespace CVExplorer.Tests;

/// <summary>
/// Regressao do bug em que o percentual ficava travado em 0: o parser lia
/// "out_time_us=" a partir do indice 11 e pegava o "=", nunca o valor.
/// </summary>
public sealed class ProgressoFfmpegTests
{
    [Theory]
    [InlineData("out_time_us", "1500000", 1.5)]
    [InlineData("out_time_ms", "1500000", 1.5)] // o ffmpeg emite microssegundos nos dois
    [InlineData("out_time_us", "0", 0.0)]
    [InlineData("out_time_us", "45045000", 45.045)]
    public void LeMicrossegundos(string chave, string valor, double esperado)
    {
        Assert.True(TranscodeService.TrySegundos(chave, valor, out var segundos));
        Assert.Equal(esperado, segundos, 3);
    }

    [Fact]
    public void LeOutTimeFormatado()
    {
        Assert.True(TranscodeService.TrySegundos("out_time", "00:00:12.500000", out var segundos));
        Assert.Equal(12.5, segundos, 3);
    }

    [Fact]
    public void IgnoraValoresInvalidos()
    {
        Assert.False(TranscodeService.TrySegundos("out_time_us", "N/A", out _));
        Assert.False(TranscodeService.TrySegundos("out_time", "lixo", out _));
    }

    [Fact]
    public void PercentualAcompanhaODecorrerDoArquivo()
    {
        var duracao = 45.045;

        var inicio = Percentual("out_time_us", "0", duracao);
        var meio = Percentual("out_time_us", "22522500", duracao);
        var fim = Percentual("out_time_us", "45045000", duracao);

        Assert.Equal(0, inicio, 1);
        Assert.InRange(meio, 49, 51);
        Assert.InRange(fim, 99, 100);
        Assert.True(inicio < meio && meio < fim, "o percentual precisa crescer durante a conversao");
    }

    private static double Percentual(string chave, string valor, double duracao)
    {
        Assert.True(TranscodeService.TrySegundos(chave, valor, out var segundos));
        return Math.Clamp(segundos / duracao * 100, 0, 100);
    }
}
