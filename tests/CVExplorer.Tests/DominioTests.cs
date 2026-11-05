using System.Text;
using CVExplorer.Host.Features.FileSystem;
using CVExplorer.Host.Features.Projects;
using CVExplorer.Host.Features.Search;

namespace CVExplorer.Tests;

public sealed class PastaTemporaria : IDisposable
{
    public string Caminho { get; }

    public PastaTemporaria()
    {
        Caminho = Path.Combine(Path.GetTempPath(), "cvexplorer-testes", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Caminho);
    }

    public string CriarPasta(string relativa)
    {
        var caminho = Path.GetFullPath(Path.Combine(Caminho, relativa));
        Directory.CreateDirectory(caminho);
        return caminho;
    }

    public string CriarArquivo(string relativa, string conteudo = "x")
    {
        var caminho = Path.GetFullPath(Path.Combine(Caminho, relativa));
        Directory.CreateDirectory(Path.GetDirectoryName(caminho)!);
        File.WriteAllText(caminho, conteudo);
        return caminho;
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Caminho))
            {
                Directory.Delete(Caminho, recursive: true);
            }
        }
        catch
        {
            // pasta temporaria: se ficar, o Windows limpa depois
        }
    }
}

public sealed class TemplateCatalogTests
{
    private static readonly List<TemplateNode> Modelo = TemplateCatalog.Default();

    [Fact]
    public void NaRaizDoProjetoOfereceAsPastasDePrimeiroNivel()
    {
        var filhos = TemplateCatalog.FilhosPara(Modelo, "PROJETO DEMO", "1_CV_PROJETOS", raizDeProjeto: true);

        Assert.Equal(new[] { "PACOTE_GRAFICO", "CHAMADAS", "ABERTURA", "EDITORIA", "SAIDA", "REFERENCIAS" },
            filhos.Select(f => f.Nome));
    }

    [Fact]
    public void NaRaizOGanhaSobreONomeDaPasta()
    {
        // Projeto chamado ELEMENTOS: o "Novo" deve listar o primeiro nivel, nao os filhos de ELEMENTOS.
        var filhos = TemplateCatalog.FilhosPara(Modelo, "ELEMENTOS", "1_CV_PROJETOS", raizDeProjeto: true);

        Assert.Contains(filhos, f => f.Nome == "PACOTE_GRAFICO");
        Assert.DoesNotContain(filhos, f => f.Nome == "AUDIO");
    }

    [Fact]
    public void PastaComNomeDoModeloDevolveOsFilhos()
    {
        var filhos = TemplateCatalog.FilhosPara(Modelo, "elementos", "ORIGEM", raizDeProjeto: false);

        Assert.Equal(new[] { "AUDIO", "MOV", "SEQUENCIAS", "IMAGENS", "FONTS" }, filhos.Select(f => f.Nome));
    }

    [Fact]
    public void PastaDentroDeUmQuadroCasaPeloToken()
    {
        var filhos = TemplateCatalog.FilhosPara(Modelo, "CENA_01", "EDITORIA", raizDeProjeto: false);

        Assert.Equal(new[] { "ELEMENTOS", "PROJETOS", "REFERENCIAS" }, filhos.Select(f => f.Nome));
    }

    [Fact]
    public void PastaForaDoModeloNaoOfereceNada()
    {
        var filhos = TemplateCatalog.FilhosPara(Modelo, "qualquer", "lugar", raizDeProjeto: false);

        Assert.Empty(filhos);
    }

    [Fact]
    public void EncontraONoDeQuadroDoModelo()
    {
        var quadro = TemplateCatalog.QuadroNode(Modelo);

        Assert.NotNull(quadro);
        Assert.Contains(quadro!.Filhos, f => f.Nome == "ELEMENTOS");
    }

    [Fact]
    public void QuadroSegueOModeloDoArquivoSemNomesFixos()
    {
        using var temp = new PastaTemporaria();
        var arquivo = temp.CriarArquivo("modelo-projeto.json", """
        [
          { "Nome": "EDITORIA", "Filhos": [
            { "Nome": "{QUADRO}", "Filhos": [ { "Nome": "CUSTOM_A" }, { "Nome": "CUSTOM_B" } ] }
          ] }
        ]
        """);

        var modelo = TemplateCatalog.LoadOrSeed(arquivo);
        var filhos = TemplateCatalog.FilhosPara(modelo, "CENA_01", "EDITORIA", raizDeProjeto: false);

        Assert.Equal(new[] { "CUSTOM_A", "CUSTOM_B" }, filhos.Select(f => f.Nome));
    }

    [Fact]
    public void QuadroCriaSoAsPastasEscolhidas()
    {
        using var temp = new PastaTemporaria();
        var qualquer = temp.CriarPasta("PACOTE_GRAFICO");

        var resultado = TemplateMaterializer.CreateQuadro(
            qualquer, "CENA_01", new[] { "PROJETOS", "ELEMENTOS" });

        Assert.Empty(resultado.Erros);
        Assert.True(Directory.Exists(Path.Combine(qualquer, "CENA_01", "PROJETOS")));
        Assert.True(Directory.Exists(Path.Combine(qualquer, "CENA_01", "ELEMENTOS")));
        // o que não foi marcado não nasce, nem o conteúdo das marcadas
        Assert.False(Directory.Exists(Path.Combine(qualquer, "CENA_01", "REFERENCIAS")));
        Assert.False(Directory.Exists(Path.Combine(qualquer, "CENA_01", "PROJETOS", "AE")));
        Assert.False(Directory.Exists(Path.Combine(qualquer, "ELEMENTOS")));
        Assert.Equal(3, resultado.Criadas.Count);
    }

    [Fact]
    public void QuadroComSubpastaEscolhidaCriaOsAncestrais()
    {
        using var temp = new PastaTemporaria();

        var resultado = TemplateMaterializer.CreateQuadro(
            temp.Caminho, "CENA_01", new[] { "PROJETOS/AE", "ELEMENTOS/AUDIO" });

        Assert.True(Directory.Exists(Path.Combine(temp.Caminho, "CENA_01", "PROJETOS", "AE")));
        Assert.True(Directory.Exists(Path.Combine(temp.Caminho, "CENA_01", "ELEMENTOS", "AUDIO")));
        Assert.False(Directory.Exists(Path.Combine(temp.Caminho, "CENA_01", "PROJETOS", "PSD")));
        Assert.Equal(5, resultado.Criadas.Count);
    }

    [Fact]
    public void QuadroSemSelecaoCriaSoAPasta()
    {
        using var temp = new PastaTemporaria();

        var resultado = TemplateMaterializer.CreateQuadro(temp.Caminho, "CENA_01", Array.Empty<string>());

        Assert.Single(resultado.Criadas);
        Assert.True(Directory.Exists(Path.Combine(temp.Caminho, "CENA_01")));
        Assert.Empty(Directory.EnumerateDirectories(Path.Combine(temp.Caminho, "CENA_01")));
    }

    [Fact]
    public void QuadroRepetidoNaoDuplica()
    {
        using var temp = new PastaTemporaria();
        var selecionadas = new[] { "PROJETOS", "ELEMENTOS" };

        var primeiro = TemplateMaterializer.CreateQuadro(temp.Caminho, "CENA_01", selecionadas);
        var segundo = TemplateMaterializer.CreateQuadro(temp.Caminho, "CENA_01", selecionadas);

        Assert.NotEmpty(primeiro.Criadas);
        Assert.Empty(segundo.Criadas);
        Assert.Empty(segundo.Erros);
        Assert.Contains("CENA_01", segundo.JaExistiam);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("...")]
    public void QuadroSemNomeValidoNaoCriaNada(string nome)
    {
        using var temp = new PastaTemporaria();

        var resultado = TemplateMaterializer.CreateQuadro(temp.Caminho, nome, new[] { "PROJETOS" });

        Assert.NotEmpty(resultado.Erros);
        Assert.Empty(Directory.EnumerateDirectories(temp.Caminho));
    }

    [Fact]
    public void QuadroUsaONomeLimpoComoPasta()
    {
        using var temp = new PastaTemporaria();

        TemplateMaterializer.CreateQuadro(temp.Caminho, "CENA:01 ", new[] { "PROJETOS" });

        Assert.True(Directory.Exists(Path.Combine(temp.Caminho, "CENA_01")));
    }
}

public sealed class TemplateMaterializerTests
{
    [Fact]
    public void CriaSomenteOPathSelecionadoComAncestrais()
    {
        using var temp = new PastaTemporaria();

        var resultado = TemplateMaterializer.Materialize(
            temp.Caminho,
            new[] { "PACOTE_GRAFICO/ELEMENTOS/MOV" },
            Array.Empty<string>());

        Assert.True(Directory.Exists(Path.Combine(temp.Caminho, "PACOTE_GRAFICO")));
        Assert.True(Directory.Exists(Path.Combine(temp.Caminho, "PACOTE_GRAFICO", "ELEMENTOS")));
        Assert.True(Directory.Exists(Path.Combine(temp.Caminho, "PACOTE_GRAFICO", "ELEMENTOS", "MOV")));

        // nada alem do selecionado
        Assert.False(Directory.Exists(Path.Combine(temp.Caminho, "PACOTE_GRAFICO", "PROJETOS")));
        Assert.False(Directory.Exists(Path.Combine(temp.Caminho, "EDITORIA")));
        Assert.Equal(3, resultado.Criadas.Count);
    }

    [Fact]
    public void SegundaExecucaoNaoCriaNadaEnaoDuplica()
    {
        using var temp = new PastaTemporaria();
        var selecionadas = new[] { "CHAMADAS/ELEMENTOS/MOV", "CHAMADAS/PREVIEW" };

        var primeira = TemplateMaterializer.Materialize(temp.Caminho, selecionadas, Array.Empty<string>());
        var segunda = TemplateMaterializer.Materialize(temp.Caminho, selecionadas, Array.Empty<string>());

        Assert.Equal(4, primeira.Criadas.Count);
        Assert.Empty(segunda.Criadas);
        Assert.Equal(4, segunda.JaExistiam.Count);
        Assert.False(Directory.Exists(Path.Combine(temp.Caminho, "CHAMADAS", "ELEMENTOS", "MOV", "MOV")));
    }

    [Fact]
    public void ExpandeUmQuadroPorNome()
    {
        using var temp = new PastaTemporaria();

        var resultado = TemplateMaterializer.Materialize(
            temp.Caminho,
            new[] { "EDITORIA/{QUADRO}/ELEMENTOS/MOV" },
            new[] { "CHAMADA 01", "CHAMADA 02" });

        Assert.True(Directory.Exists(Path.Combine(temp.Caminho, "EDITORIA", "CHAMADA 01", "ELEMENTOS", "MOV")));
        Assert.True(Directory.Exists(Path.Combine(temp.Caminho, "EDITORIA", "CHAMADA 02", "ELEMENTOS", "MOV")));
        Assert.DoesNotContain(resultado.Criadas, c => c.Contains("{QUADRO}"));
    }

    [Fact]
    public void SemNomeDeQuadroIgnoraOPathComToken()
    {
        using var temp = new PastaTemporaria();

        var resultado = TemplateMaterializer.Materialize(
            temp.Caminho,
            new[] { "EDITORIA/{QUADRO}/ELEMENTOS" },
            Array.Empty<string>());

        Assert.Empty(resultado.Criadas);
        Assert.Single(resultado.Ignoradas);
        Assert.False(Directory.Exists(Path.Combine(temp.Caminho, "EDITORIA")));
    }

    [Theory]
    [InlineData("../fora")]
    [InlineData("PASTA/../../fora")]
    public void NaoEscapaDaRaizDoProjeto(string caminhoPerigoso)
    {
        using var temp = new PastaTemporaria();

        var resultado = TemplateMaterializer.Materialize(
            temp.Caminho,
            new[] { caminhoPerigoso },
            Array.Empty<string>());

        Assert.Empty(resultado.Criadas);
        Assert.False(Directory.Exists(Path.Combine(Path.GetDirectoryName(temp.Caminho)!, "fora")));
    }

    [Fact]
    public void SanitizaNomeDeQuadroInvalido()
    {
        using var temp = new PastaTemporaria();

        TemplateMaterializer.Materialize(
            temp.Caminho,
            new[] { "EDITORIA/{QUADRO}" },
            new[] { "CHAMADA:01?" });

        Assert.True(Directory.Exists(Path.Combine(temp.Caminho, "EDITORIA", "CHAMADA_01_")));
    }
}

public sealed class ProjectServiceTests
{
    [Fact]
    public void RegisterCriaLerDeVoltaEMantemMarcadorOculto()
    {
        using var temp = new PastaTemporaria();
        var servico = new ProjectService();

        var criado = servico.Register(temp.Caminho, "PROGRAMA X", "pacote_grafico", "Cliente Y");

        Assert.True(File.Exists(Path.Combine(temp.Caminho, ".cvproj")));
        Assert.Equal("PROGRAMA X", criado.Nome);
        Assert.Equal("PACOTE_GRAFICO", criado.Tipo);

        var lido = servico.TryRead(temp.Caminho);
        Assert.NotNull(lido);
        Assert.Equal(criado.Id, lido!.Id);
        Assert.Equal("Cliente Y", lido.Cliente);

        var atributos = File.GetAttributes(Path.Combine(temp.Caminho, ".cvproj"));
        Assert.True(atributos.HasFlag(FileAttributes.Hidden));
    }

    [Fact]
    public void RegisterNaoSobrescreveMarcadorExistente()
    {
        using var temp = new PastaTemporaria();
        var servico = new ProjectService();

        var primeiro = servico.Register(temp.Caminho, "Original");
        var segundo = servico.Register(temp.Caminho, "Outro nome");

        Assert.Equal(primeiro.Id, segundo.Id);
        Assert.Equal("Original", segundo.Nome);
    }

    [Fact]
    public void DetectaPorMarcadorEPorEstrutura()
    {
        using var temp = new PastaTemporaria();
        var servico = new ProjectService();

        var comMarcador = temp.CriarPasta("PROJETO_A");
        servico.Register(comMarcador, "PROJETO A");

        var porEstrutura = temp.CriarPasta("PROJETO_B");
        Directory.CreateDirectory(Path.Combine(porEstrutura, "PACOTE_GRAFICO", "ELEMENTOS"));
        Directory.CreateDirectory(Path.Combine(porEstrutura, "CHAMADAS"));

        var apenasPasta = temp.CriarPasta("PROJETO_C");
        Directory.CreateDirectory(Path.Combine(apenasPasta, "QUALQUER_COISA"));

        var encontrados = servico.Detect(new[] { temp.Caminho }, maxDepth: 2);

        Assert.Contains(encontrados, p => p.Caminho == comMarcador && p.Registrado);
        Assert.Contains(encontrados, p => p.Caminho == porEstrutura && p.Origem == "estrutura");
        Assert.DoesNotContain(encontrados, p => p.Caminho == apenasPasta);
    }

    [Fact]
    public void ResolveEncontraOProjetoSubindoPelosPais()
    {
        using var temp = new PastaTemporaria();
        var servico = new ProjectService();

        var raizProjeto = temp.CriarPasta("LUGAR ALEATORIO/PROJETO X");
        servico.Register(raizProjeto, "PROJETO X");

        var fundo = temp.CriarPasta("LUGAR ALEATORIO/PROJETO X/PACOTE_GRAFICO/ELEMENTOS/MOV");

        var encontrado = servico.Resolve(fundo);

        Assert.NotNull(encontrado);
        Assert.Equal(raizProjeto, encontrado!.Caminho);
        Assert.Equal("PROJETO X", encontrado.Nome);
    }

    [Fact]
    public void ResolveDevolveNuloForaDeProjeto()
    {
        using var temp = new PastaTemporaria();
        var servico = new ProjectService();
        var solta = temp.CriarPasta("PASTA SEM MARCADOR/SUB");

        Assert.Null(servico.Resolve(solta));
    }

    [Fact]
    public void AtalhosListamSomenteAsPastasQueExistem()
    {
        using var temp = new PastaTemporaria();
        var servico = new ProjectService();

        var projeto = temp.CriarPasta("PROJETO");
        servico.Register(projeto, "PROJETO");
        Directory.CreateDirectory(Path.Combine(projeto, "PACOTE_GRAFICO", "PROJETOS", "AE"));
        Directory.CreateDirectory(Path.Combine(projeto, "PACOTE_GRAFICO", "PROJETOS", "MAYA"));
        Directory.CreateDirectory(Path.Combine(projeto, "PACOTE_GRAFICO", "ELEMENTOS", "MOV"));
        Directory.CreateDirectory(Path.Combine(projeto, "PREVIEW"));

        var atalhos = servico.Shortcuts(projeto);

        Assert.Contains(atalhos, a => a.Caminho.EndsWith(Path.Combine("PROJETOS", "MAYA")) && a.Grupo == "PROJETOS");
        Assert.Contains(atalhos, a => a.Caminho.EndsWith(Path.Combine("PROJETOS", "AE")));
        Assert.Contains(atalhos, a => a.Nome == "PREVIEW");
        // Nada de pastas inexistentes inventadas pela lista fixa.
        Assert.DoesNotContain(atalhos, a => a.Nome == "BLENDER");
        Assert.DoesNotContain(atalhos, a => a.Nome == "CHAMADAS");
    }
}

public sealed class FileNameTests
{
    [Fact]
    public void UniquePathGeraSufixoNumerico()
    {
        using var temp = new PastaTemporaria();
        var existente = temp.CriarArquivo("video.mov");

        var novo = FileOperations.UniquePath(existente, ".mov", false);

        Assert.Equal(Path.Combine(temp.Caminho, "video (2).mov"), novo);
    }

    [Fact]
    public void UniquePathIncrementaAteAcharLivre()
    {
        using var temp = new PastaTemporaria();
        var existente = temp.CriarArquivo("video.mov");
        temp.CriarArquivo("video (2).mov");

        var novo = FileOperations.UniquePath(existente, ".mov", false);

        Assert.Equal(Path.Combine(temp.Caminho, "video (3).mov"), novo);
    }

    [Fact]
    public void RenameMantemArquivoExistenteIntacto()
    {
        using var temp = new PastaTemporaria();
        var alvo = temp.CriarArquivo("a.txt");
        temp.CriarArquivo("b.txt", "conteudo original do b");

        var novo = FileOperations.Rename(alvo, "b.txt");

        Assert.Equal(Path.Combine(temp.Caminho, "b (2).txt"), novo);
        Assert.Equal("conteudo original do b", File.ReadAllText(Path.Combine(temp.Caminho, "b.txt")));
    }

    [Fact]
    public void SanitizeRemoveCaracteresInvalidos()
    {
        Assert.Equal("teste_", FileOperations.Sanitize("teste:"));
        Assert.Equal("nome", FileOperations.Sanitize("nome..."));
        Assert.Equal(string.Empty, FileOperations.Sanitize("   "));
    }

    [Theory]
    [InlineData("Shot 2", "Shot 10", -1)]
    [InlineData("Shot 10", "Shot 2", 1)]
    [InlineData("a", "a", 0)]
    [InlineData("Chamada 01", "chamada 1", 0)]
    public void OrdenacaoNaturalRespeitaNumeros(string a, string b, int esperado)
    {
        var comparacao = Math.Sign(NaturalComparer.Instance.Compare(a, b));
        Assert.Equal(esperado, comparacao);
    }
}

public sealed class SearchQueryTests
{
    [Fact]
    public void TextoLivreCasaNoNomeIgnorandoAcentoECaixa()
    {
        var query = SearchQuery.Parse("chamada");

        Assert.True(query.Matches(@"C:\p\CHAMADA_ABERTURA.aep", "CHAMADA_ABERTURA.aep"));
        Assert.False(query.Matches(@"C:\p\outra.aep", "outra.aep"));
    }

    [Fact]
    public void FiltraPorExtensao()
    {
        var query = SearchQuery.Parse("ext:psd,ai logo");

        Assert.True(query.Matches(@"C:\p\logo.psd", "logo.psd"));
        Assert.True(query.Matches(@"C:\p\logo.ai", "logo.ai"));
        Assert.False(query.Matches(@"C:\p\logo.png", "logo.png"));
    }

    [Fact]
    public void FiltraPorTipo()
    {
        var query = SearchQuery.Parse("tipo:video");

        Assert.True(query.Matches(@"C:\p\a.mov", "a.mov"));
        Assert.True(query.Matches(@"C:\p\a.mp4", "a.mp4"));
        Assert.False(query.Matches(@"C:\p\a.psd", "a.psd"));
    }

    [Fact]
    public void FiltraPorPasta()
    {
        var query = SearchQuery.Parse("pasta:\"Pacote Gráfico\"");

        Assert.True(query.Matches(@"C:\p\Pacote Grafico\ELEMENTOS\MOV\a.mov", "a.mov"));
        Assert.False(query.Matches(@"C:\p\EDITORIA\a.mov", "a.mov"));
    }

    [Fact]
    public void TextoVazioNaoEhBusca()
    {
        Assert.True(SearchQuery.Parse("   ").Vazia);
    }
}
