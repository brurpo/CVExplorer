using CVExplorer.Host.Features.FileSystem;

namespace CVExplorer.Tests;

/// <summary>
/// O arraste so pode mover dentro da mesma unidade. Mover entre volumes e, por baixo, uma
/// copia seguida de apagar o original: um erro no meio (disco cheio, rede caindo) levaria
/// o arquivo junto. Estes testes travam a regra usada pelo arraste entre janelas e pelo
/// arraste vindo do Explorer.
/// </summary>
public sealed class VolumesTests
{
    [Fact]
    public void MesmaLetraDeDiscoMove()
    {
        Assert.True(Volumes.MesmaUnidade(@"E:\projetos\a.mov", @"E:\saida"));
        Assert.True(Volumes.MesmaUnidade(@"e:\PROJETOS\a.mov", @"E:\saida"));
        Assert.True(Volumes.MesmaUnidade(@"E:\a.mov", @"E:\"));
    }

    [Fact]
    public void LetrasDiferentesCopiam()
    {
        Assert.False(Volumes.MesmaUnidade(@"E:\projetos\a.mov", @"C:\Temp\saida"));
        Assert.False(Volumes.MesmaUnidade(@"D:\capturas\a.mov", @"E:\saida"));
        Assert.False(Volumes.MesmaUnidade(@"C:\a.mov", @"D:\b"));
    }

    [Fact]
    public void CaminhoDeRedeComparaServidorECompartilhamento()
    {
        Assert.True(Volumes.MesmaUnidade(
            @"\\SERVIDOR\Midia\chamadas\a.mov",
            @"\\servidor\midia\saida"));

        // Compartilhamentos diferentes do mesmo servidor sao unidades diferentes.
        Assert.False(Volumes.MesmaUnidade(
            @"\\servidor\midia\a.mov",
            @"\\servidor\backup\saida"));

        // Disco local para rede tambem e outra unidade.
        Assert.False(Volumes.MesmaUnidade(@"E:\a.mov", @"\\servidor\midia\saida"));
    }

    [Fact]
    public void CaminhoSemRaizNuncaMove()
    {
        // Sem raiz reconhecivel o palpite seguro e copiar.
        Assert.False(Volumes.MesmaUnidade("", @"E:\saida"));
        Assert.False(Volumes.MesmaUnidade(@"E:\a.mov", ""));
    }

    [Fact]
    public void CaminhoRelativoEResolvidoContraODiretorioAtual()
    {
        var atual = Directory.GetCurrentDirectory();
        var raiz = Path.GetPathRoot(atual)!;

        Assert.True(Volumes.MesmaUnidade("arquivo.mov", atual));

        // Qualquer pasta no mesmo volume do diretorio atual continua sendo a mesma unidade.
        Assert.True(Volumes.MesmaUnidade("arquivo.mov", raiz));
    }
}
