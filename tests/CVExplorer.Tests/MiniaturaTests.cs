using CVExplorer.Host.Features.Thumbnails;

namespace CVExplorer.Tests;

public sealed class MiniaturaTests
{
    private static string ArquivoTemp(string extensao, byte[] conteudo)
    {
        var caminho = Path.Combine(Path.GetTempPath(), $"cvx-{Guid.NewGuid():N}{extensao}");
        File.WriteAllBytes(caminho, conteudo);
        return caminho;
    }

    [Theory]
    [InlineData("a.obj")]
    [InlineData("a.fbx")]
    [InlineData("a.glb")]
    [InlineData("a.GLTF")]
    [InlineData("a.stl")]
    [InlineData("a.usdz")]
    public void ModelosSaoRenderizadosPeloApp(string nome)
    {
        Assert.True(ThumbnailService.EhModelo3D(nome));
    }

    [Theory]
    [InlineData("a.psd")]
    [InlineData("a.exr")]
    [InlineData("a.blend")]
    [InlineData("a.mxf")]
    public void OutrosFormatosNaoPassamPeloRenderizador3d(string nome)
    {
        Assert.False(ThumbnailService.EhModelo3D(nome));
    }

    [Fact]
    public void AiComCabecalhoPdfVaiParaOPdfJs()
    {
        var caminho = ArquivoTemp(".ai", "%PDF-1.6\n..."u8.ToArray());
        try
        {
            Assert.True(ThumbnailService.EhDocumentoPdf(caminho));
        }
        finally
        {
            File.Delete(caminho);
        }
    }

    [Fact]
    public void AiSemCabecalhoPdfNaoEhTratadoComoPdf()
    {
        // .ai salvo sem "Create PDF Compatible File": PostScript puro.
        var caminho = ArquivoTemp(".ai", "%!PS-Adobe-3.0\n..."u8.ToArray());
        try
        {
            Assert.False(ThumbnailService.EhDocumentoPdf(caminho));
        }
        finally
        {
            File.Delete(caminho);
        }
    }

    [Fact]
    public void PdfTambemUsaOPdfJs()
    {
        var caminho = ArquivoTemp(".pdf", "%PDF-1.7\n..."u8.ToArray());
        try
        {
            Assert.True(ThumbnailService.EhDocumentoPdf(caminho));
        }
        finally
        {
            File.Delete(caminho);
        }
    }

    [Fact]
    public void ChaveDoCacheMudaComTamanhoDataETamanhoPedido()
    {
        var data = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);

        var base1 = ThumbnailService.Chave(@"C:\a\b.obj", 10, data, 256);
        var outroTamanhoArquivo = ThumbnailService.Chave(@"C:\a\b.obj", 11, data, 256);
        var outraData = ThumbnailService.Chave(@"C:\a\b.obj", 10, data.AddDays(1), 256);
        var outroPedido = ThumbnailService.Chave(@"C:\a\b.obj", 10, data, 512);

        Assert.Equal(20, base1.Length);
        Assert.NotEqual(base1, outroTamanhoArquivo);
        Assert.NotEqual(base1, outraData);
        Assert.NotEqual(base1, outroPedido);
        Assert.Equal(base1, ThumbnailService.Chave(@"c:\A\B.obj", 10, data, 256));
    }
}
