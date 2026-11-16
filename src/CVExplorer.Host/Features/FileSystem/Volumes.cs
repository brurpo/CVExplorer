namespace CVExplorer.Host.Features.FileSystem;

/// <summary>
/// Regras de unidade de disco compartilhadas pelo arraste. Mover entre unidades e, por
/// baixo, uma copia seguida de apagar o original: se algo falhar no meio (disco cheio,
/// rede caindo) o arquivo pode sumir. Por isso o arraste so move dentro da mesma unidade
/// — entre unidades ele copia, que e o mesmo que o Explorer faz.
/// </summary>
internal static class Volumes
{
    public static bool MesmaUnidade(string origem, string destino)
    {
        try
        {
            var raizOrigem = Raiz(origem);
            var raizDestino = Raiz(destino);

            return raizOrigem.Length > 0
                && string.Equals(raizOrigem, raizDestino, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// "E:\..." → "E:"; "\\servidor\compartilhado\..." → "\\servidor\compartilhado".
    /// Caminho sem raiz reconhecivel devolve vazio, e quem chama trata como outra unidade.
    /// </summary>
    private static string Raiz(string caminho)
    {
        if (string.IsNullOrWhiteSpace(caminho))
        {
            return string.Empty;
        }

        var completo = Path.GetFullPath(caminho);
        if (completo.StartsWith(@"\\", StringComparison.Ordinal))
        {
            var partes = completo[2..].Split('\\', StringSplitOptions.RemoveEmptyEntries);
            return partes.Length >= 2 ? $@"\\{partes[0]}\{partes[1]}" : string.Empty;
        }

        return Path.GetPathRoot(completo) ?? string.Empty;
    }
}
