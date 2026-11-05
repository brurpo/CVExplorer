using System.IO.Compression;
using CVExplorer.Host.Features.FileSystem;

namespace CVExplorer.Tests;

/// <summary>
/// Compactado aberto como pasta: o caminho virtual é o do arquivo mais o trecho interno,
/// e o resto do app (listar, copiar, extrair) trabalha em cima disso.
/// </summary>
public sealed class ArchiveTests : IDisposable
{
    private readonly string _raiz = Path.Combine(Path.GetTempPath(), $"cvx-zip-{Guid.NewGuid():N}");

    public ArchiveTests() => Directory.CreateDirectory(_raiz);

    private string MontarPacote()
    {
        var zip = Path.Combine(_raiz, "PACOTE.zip");
        using var arquivo = new FileStream(zip, FileMode.Create);
        using var zipAberto = new ZipArchive(arquivo, ZipArchiveMode.Create);

        void Escrever(string nome, string conteudo)
        {
            var entrada = zipAberto.CreateEntry(nome);
            using var stream = entrada.Open();
            using var escritor = new StreamWriter(stream);
            escritor.Write(conteudo);
        }

        Escrever("leia-me.txt", "texto");
        Escrever("IMAGENS/quadro 01.png", "a");
        Escrever("IMAGENS/quadro 02.png", "b");
        return zip;
    }

    [Fact]
    public void CaminhoDentroDoZipApontaParaOArquivo()
    {
        var zip = MontarPacote();
        var dentro = Path.Combine(zip, "IMAGENS", "quadro 01.png");

        var separado = ArchiveService.Separar(dentro);

        Assert.NotNull(separado);
        Assert.Equal(zip, separado!.Value.Arquivo);
        Assert.Equal("IMAGENS/quadro 01.png", separado.Value.Interno);
    }

    [Fact]
    public void PastaDeVerdadeNaoEhConfundidaComArquivoTemporario()
    {
        var pasta = Path.Combine(_raiz, "sem-sufixo");
        Directory.CreateDirectory(pasta);

        Assert.Null(ArchiveService.Separar(pasta));
        Assert.Null(ArchiveService.Separar(Path.Combine(_raiz, "inexistente.txt")));
    }

    [Fact]
    public void ListaAsEntradasComoPastasEArquivos()
    {
        var zip = MontarPacote();
        var servico = new ArchiveService();

        var raiz = servico.Listar(zip, string.Empty);
        Assert.Equal(2, raiz.Count);
        Assert.True(raiz[0].Pasta);
        Assert.Equal("IMAGENS", raiz[0].Nome);
        Assert.Equal(Path.Combine(zip, "IMAGENS"), raiz[0].Caminho);
        Assert.False(raiz[1].Pasta);
        Assert.Equal(5, raiz[1].Tamanho);

        var interna = servico.Listar(zip, "IMAGENS");
        Assert.Equal(2, interna.Count);
        Assert.All(interna, e => Assert.False(e.Pasta));
        Assert.Equal("quadro 01.png", interna[0].Nome);
        Assert.Equal(Path.Combine(zip, "IMAGENS", "quadro 01.png"), interna[0].Caminho);
    }

    [Fact]
    public void ExtraiSomenteOQueFoiPedidoSemSobrescrever()
    {
        var zip = MontarPacote();
        var servico = new ArchiveService();
        var destino = Path.Combine(_raiz, "saida");
        Directory.CreateDirectory(destino);
        File.WriteAllText(Path.Combine(destino, "leia-me.txt"), "ja existia");

        var extraidos = servico.Extrair(zip, new[] { "leia-me.txt", "IMAGENS/quadro 02.png" }, destino);

        Assert.Equal(2, extraidos.Count);
        Assert.Contains(extraidos, p => Path.GetFileName(p) == "leia-me (2).txt");
        Assert.Equal("ja existia", File.ReadAllText(Path.Combine(destino, "leia-me.txt")));
        Assert.Contains(extraidos, p => Path.GetFileName(p) == "quadro 02.png");
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_raiz, recursive: true);
        }
        catch
        {
            // temporário de teste
        }
    }
}
