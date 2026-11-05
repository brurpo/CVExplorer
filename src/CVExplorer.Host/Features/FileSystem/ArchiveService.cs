using System.Collections.Concurrent;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;

namespace CVExplorer.Host.Features.FileSystem;

/// <summary>
/// Arquivo compactado tratado como pasta. O caminho virtual é o do próprio arquivo mais o
/// trecho interno: <c>C:\a\b.zip\imagens\foto.jpg</c> não existe no disco, mas o host
/// reconhece o ancestral que é arquivo compactado e responde a listagem a partir dele.
///
/// O arquivo é aberto sob demanda e reaberto a cada operação: manter um 7z aberto travaria
/// o arquivo contra exclusão e não sobrevive a ele ser substituído por fora.
/// </summary>
public sealed class ArchiveService
{
    private static readonly HashSet<string> Extensoes = new(StringComparer.OrdinalIgnoreCase)
    {
        ".zip", ".7z", ".rar", ".tar", ".gz", ".tgz", ".bz2", ".tbz2", ".xz", ".txz", ".zst", ".cab", ".iso",
    };

    private static readonly ConcurrentDictionary<string, DateTime> Avisados = new(StringComparer.OrdinalIgnoreCase);

    public static bool EhCompactado(string caminho) =>
        Extensoes.Contains(Path.GetExtension(caminho.TrimEnd('\\')));

    /// <summary>
    /// Separa um caminho virtual em (arquivo compactado, trecho interno). Devolve null quando
    /// o caminho é uma pasta de verdade. Pasta com nome de .zip continua sendo pasta.
    /// </summary>
    public static (string Arquivo, string Interno)? Separar(string caminho)
    {
        if (string.IsNullOrWhiteSpace(caminho) || Directory.Exists(caminho))
        {
            return null;
        }

        var atual = caminho.TrimEnd('\\', '/');
        var interno = new Stack<string>();

        while (true)
        {
            if (Directory.Exists(atual))
            {
                return null;
            }

            if (File.Exists(atual) && EhCompactado(atual))
            {
                var partes = interno.ToArray();
                return (atual, string.Join('/', partes));
            }

            var pai = Path.GetDirectoryName(atual);
            if (string.IsNullOrEmpty(pai) || pai == atual)
            {
                return null;
            }

            interno.Push(Path.GetFileName(atual));
            atual = pai;
        }
    }

    /// <summary>Lista uma pasta de dentro do arquivo, com as subpastas que só existem no nome das entradas.</summary>
    public List<FileEntry> Listar(string arquivo, string interno)
    {
        var prefixo = Normalizar(interno);
        var pastas = new Dictionary<string, FileEntry>(StringComparer.OrdinalIgnoreCase);
        var arquivos = new List<FileEntry>();

        using var aberto = ArchiveFactory.OpenArchive(arquivo);
        foreach (var entrada in aberto.Entries)
        {
            if (entrada.IsDirectory || string.IsNullOrEmpty(entrada.Key))
            {
                continue;
            }

            var chave = Normalizar(entrada.Key);
            if (prefixo.Length > 0)
            {
                if (!chave.StartsWith(prefixo + "/", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                chave = chave[(prefixo.Length + 1)..];
            }

            var barra = chave.IndexOf('/');
            if (barra >= 0)
            {
                var nomePasta = chave[..barra];
                if (!pastas.ContainsKey(nomePasta))
                {
                    pastas[nomePasta] = new FileEntry
                    {
                        Nome = nomePasta,
                        Caminho = Juntar(arquivo, interno, nomePasta),
                        Pasta = true,
                        Ext = string.Empty,
                        Modificado = entrada.LastModifiedTime ?? DateTimeOffset.MinValue,
                        Criacao = entrada.LastModifiedTime ?? DateTimeOffset.MinValue,
                    };
                }
                else
                {
                    pastas[nomePasta].Tamanho++;
                }

                continue;
            }

            arquivos.Add(new FileEntry
            {
                Nome = chave,
                Caminho = Juntar(arquivo, interno, chave),
                Pasta = false,
                Ext = Path.GetExtension(chave).ToLowerInvariant(),
                Tamanho = entrada.Size,
                Modificado = entrada.LastModifiedTime ?? DateTimeOffset.MinValue,
                Criacao = entrada.LastModifiedTime ?? DateTimeOffset.MinValue,
            });
        }

        var lista = pastas.Values
            .OrderByDescending(e => e.Pasta)
            .ThenBy(e => e.Nome, NaturalComparer.Instance)
            .ToList();

        lista.AddRange(arquivos.OrderBy(e => e.Nome, NaturalComparer.Instance));
        return lista;
    }

    /// <summary>Extrai uma ou mais entradas para uma pasta do disco, achatando os nomes.</summary>
    public List<string> Extrair(string arquivo, IReadOnlyList<string> internos, string destino)
    {
        Directory.CreateDirectory(destino);
        var extraidos = new List<string>();

        using var aberto = ArchiveFactory.OpenArchive(arquivo);
        foreach (var entrada in aberto.Entries)
        {
            if (entrada.IsDirectory || string.IsNullOrEmpty(entrada.Key))
            {
                continue;
            }

            var chave = Normalizar(entrada.Key);
            if (!Corresponde(chave, internos))
            {
                continue;
            }

            // Só o nome: "pasta/sub/arq.txt" vira "arq.txt" dentro do destino.
            var nome = Path.GetFileName(chave);
            var saida = CaminhoLivre(Path.Combine(destino, nome));

            using (var origem = entrada.OpenEntryStream())
            using (var alvo = File.Create(saida))
            {
                origem.CopyTo(alvo);
            }

            extraidos.Add(saida);
        }

        return extraidos;
    }

    /// <summary>
    /// Extrai para uma pasta temporária e devolve os caminhos reais. É assim que "copiar"
    /// de dentro de um compactado funciona: o restante do app continua lidando com arquivos.
    /// </summary>
    public List<string> ExtrairParaTemporario(string arquivo, IReadOnlyList<string> internos)
    {
        var raiz = Path.Combine(Path.GetTempPath(), "CVExplorer", "compactados", Guid.NewGuid().ToString("N")[..8]);
        var nomeBase = Path.GetFileNameWithoutExtension(arquivo);
        var destino = Path.Combine(raiz, nomeBase);

        var extraidos = Extrair(arquivo, internos, destino);
        if (extraidos.Count == 0)
        {
            return extraidos;
        }

        // Uma entrada só: o temporário é o arquivo; várias: a pasta que as contém.
        return extraidos;
    }

    /// <summary>Nome da pasta que "Extrair para pasta nova" deve criar (sem extensão).</summary>
    public static string PastaPadrao(string arquivo) => Path.GetFileNameWithoutExtension(arquivo.TrimEnd('\\'));

    public static string Juntar(string arquivo, string interno, string nome) =>
        string.IsNullOrEmpty(interno)
            ? Path.Combine(arquivo, nome)
            : Path.Combine(arquivo, interno.Replace('/', '\\'), nome);

    private static string Normalizar(string interno) =>
        (interno ?? string.Empty).Replace('\\', '/').Trim('/');

    private static bool Corresponde(string chave, IReadOnlyList<string> internos)
    {
        foreach (var interno in internos ?? Array.Empty<string>())
        {
            var alvo = Normalizar(interno);

            // Entrada avulsa ou tudo o que está dentro da pasta pedida.
            if (chave.Equals(alvo, StringComparison.OrdinalIgnoreCase)
                || chave.StartsWith(alvo + "/", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Nunca sobrescreve: "nome (2).ext" quando já existe.</summary>
    private static string CaminhoLivre(string caminho)
    {
        if (!File.Exists(caminho) && !Directory.Exists(caminho))
        {
            return caminho;
        }

        var pasta = Path.GetDirectoryName(caminho) ?? string.Empty;
        var nome = Path.GetFileNameWithoutExtension(caminho);
        var ext = Path.GetExtension(caminho);

        for (var i = 2; i < 1000; i++)
        {
            var tentativa = Path.Combine(pasta, $"{nome} ({i}){ext}");
            if (!File.Exists(tentativa) && !Directory.Exists(tentativa))
            {
                return tentativa;
            }
        }

        return caminho;
    }

    /// <summary>Nomes de dentro do arquivo (para saber se o caminho existe).</summary>
    public bool ExisteEntrada(string arquivo, string interno)
    {
        var alvo = Normalizar(interno);
        if (alvo.Length == 0)
        {
            return true;
        }

        using var aberto = ArchiveFactory.OpenArchive(arquivo);
        foreach (var entrada in aberto.Entries)
        {
            if (entrada.IsDirectory || string.IsNullOrEmpty(entrada.Key))
            {
                continue;
            }

            var chave = Normalizar(entrada.Key);
            if (chave.Equals(alvo, StringComparison.OrdinalIgnoreCase)
                || chave.StartsWith(alvo + "/", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}


