using System.Text.Json;
using System.Text.RegularExpressions;
using CVExplorer.Host.Ipc;

namespace CVExplorer.Tests;

/// <summary>
/// Garante que nenhuma chave usada pela interface ficou sem traducao em pt-BR.
/// Isso impede que um texto novo entre no codigo e apareca como "chave.crua" na tela.
/// </summary>
public sealed class IdiomasTests
{
    private static readonly string[] PerfisDinamicos =
    {
        "video.perfis.auto", "video.perfis.prores422hq", "video.perfis.prores422",
        "video.perfis.prores4444", "video.perfis.dnxhr_hqx", "video.perfis.proxy",
    };

    [Fact]
    public void TodasAsChavesUsadasExistemNoDicionario()
    {
        var raiz = RaizRepositorio();
        var jsonPath = Path.Combine(raiz, "ui", "i18n", "pt-BR.json");
        var dicionario = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(jsonPath))!;

        var usadas = new HashSet<string>(StringComparer.Ordinal);
        foreach (var arquivo in Directory.EnumerateFiles(Path.Combine(raiz, "ui", "js"), "*.js", SearchOption.AllDirectories))
        {
            var conteudo = File.ReadAllText(arquivo);
            foreach (Match m in Regex.Matches(conteudo, @"\bt\(\s*'([^']+)'"))
            {
                usadas.Add(m.Groups[1].Value);
            }

            foreach (Match m in Regex.Matches(conteudo, "\\bt\\(\\s*\"([^\"]+)\""))
            {
                usadas.Add(m.Groups[1].Value);
            }
        }

        var html = File.ReadAllText(Path.Combine(raiz, "ui", "index.html"));
        foreach (Match m in Regex.Matches(html, @"data-i18n(?:-placeholder|-title)?=""([^""]+)"""))
        {
            usadas.Add(m.Groups[1].Value);
        }

        foreach (var perfil in PerfisDinamicos)
        {
            usadas.Add(perfil);
        }

        var faltando = usadas.Where(c => !dicionario.ContainsKey(c)).OrderBy(c => c).ToList();
        Assert.True(faltando.Count == 0, "Chaves sem tradução em pt-BR: " + string.Join(", ", faltando));
    }

    [Fact]
    public void NenhumTextoVazioNoDicionario()
    {
        var raiz = RaizRepositorio();
        var dicionario = JsonSerializer.Deserialize<Dictionary<string, string>>(
            File.ReadAllText(Path.Combine(raiz, "ui", "i18n", "pt-BR.json")))!;

        Assert.All(dicionario, par => Assert.False(string.IsNullOrWhiteSpace(par.Value), $"chave vazia: {par.Key}"));
    }

    private static string RaizRepositorio()
    {
        var pasta = new DirectoryInfo(AppContext.BaseDirectory);
        while (pasta is not null)
        {
            if (Directory.Exists(Path.Combine(pasta.FullName, "ui"))
                && Directory.Exists(Path.Combine(pasta.FullName, "src")))
            {
                return pasta.FullName;
            }

            pasta = pasta.Parent;
        }

        throw new InvalidOperationException("Raiz do repositorio nao encontrada.");
    }
}

public sealed class InterfacesGlobaisTests
{
    [Fact]
    public void TodoComandoChamadoPelaInterfaceEstaRegistradoNoHost()
    {
        var raiz = RaizRepositorio();
        var comandosHost = File.ReadAllText(Path.Combine(raiz, "src", "CVExplorer.Host", "Commands.cs"));

        var chamados = new HashSet<string>(StringComparer.Ordinal);
        foreach (var arquivo in Directory.EnumerateFiles(Path.Combine(raiz, "ui", "js"), "*.js", SearchOption.AllDirectories))
        {
            var conteudo = File.ReadAllText(arquivo);
            foreach (Match m in Regex.Matches(conteudo, @"\bcall\(\s*'([a-zA-Z.]+)'"))
            {
                chamados.Add(m.Groups[1].Value);
            }
        }

        var faltando = chamados
            .Where(c => !comandosHost.Contains($"\"{c}\"", StringComparison.Ordinal))
            .OrderBy(c => c)
            .ToList();

        Assert.True(faltando.Count == 0, "Comandos sem rota no host: " + string.Join(", ", faltando));
    }

    private static string RaizRepositorio()
    {
        var pasta = new DirectoryInfo(AppContext.BaseDirectory);
        while (pasta is not null)
        {
            if (Directory.Exists(Path.Combine(pasta.FullName, "ui"))
                && Directory.Exists(Path.Combine(pasta.FullName, "src")))
            {
                return pasta.FullName;
            }

            pasta = pasta.Parent;
        }

        throw new InvalidOperationException("Raiz do repositorio nao encontrada.");
    }
}

public sealed class CancelamentoPorOperacaoTests
{
    [Fact]
    public void ReaproveitarOMesmoOpIdDepoisDeDescartadoNaoEstoura()
    {
        var registro = new CancellationRegistry();

        using (var primeiro = registro.Create("conv-1"))
        {
            Assert.False(primeiro.IsCancellationRequested);
        }

        // Segundo arquivo da mesma conversão usa o mesmo opId.
        using var segundo = registro.Create("conv-1");
        Assert.False(segundo.IsCancellationRequested);
    }

    [Fact]
    public void CancelarAtingeAOperacaoEmCurso()
    {
        var registro = new CancellationRegistry();
        using var cts = registro.Create("conv-2");

        registro.Cancel("conv-2");

        Assert.True(cts.IsCancellationRequested);
    }
}
