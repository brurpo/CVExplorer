using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace CVExplorer.Host.Features.Media;

/// <summary>
/// Sequencia de imagens (render de 3D, EXR de composicao, DPX de scanner...).
/// Reconhecida por prefixo + numeracao + extensao, no mesmo diretorio.
/// </summary>
public sealed partial class ImageSequence
{
    /// <summary>Minimo de quadros para valer a pena converter como video.</summary>
    public const int MinimoQuadros = 3;

    private static readonly HashSet<string> Extensoes = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".tif", ".tiff", ".exr", ".dpx", ".tga", ".bmp", ".hdr",
    };

    public string Pasta { get; init; } = string.Empty;

    /// <summary>Prefixo sem a numeracao, ja limpo de separadores no fim (ex.: "shot").</summary>
    public string NomeBase { get; init; } = string.Empty;

    public string Extensao { get; init; } = string.Empty;

    public int Digitos { get; init; }

    public int Primeiro { get; init; }

    public int Ultimo { get; init; }

    public int Quadros { get; init; }

    /// <summary>Faltam numeros no meio da faixa (still removido, render interrompido).</summary>
    public bool ComFalhas => Ultimo - Primeiro + 1 > Quadros;

    public long Bytes { get; init; }

    public IReadOnlyList<string> Arquivos { get; init; } = [];

    public static bool EhExtensaoDeImagem(string caminho) =>
        Extensoes.Contains(Path.GetExtension(caminho));

    /// <summary>
    /// Detecta a sequencia a qual o arquivo pertence. Retorna null quando o arquivo
    /// nao e imagem, nao tem numeracao ou tem menos de <see cref="MinimoQuadros"/> irmaos.
    /// </summary>
    public static ImageSequence? Detectar(string caminho)
    {
        if (!File.Exists(caminho) || !EhExtensaoDeImagem(caminho))
        {
            return null;
        }

        var nome = Path.GetFileName(caminho);
        var encontrado = Padrao().Match(nome);
        if (!encontrado.Success)
        {
            return null;
        }

        var prefixo = encontrado.Groups["prefixo"].Value;
        var digitos = encontrado.Groups["numero"].Value.Length;
        var extensao = encontrado.Groups["ext"].Value;
        var pasta = Path.GetDirectoryName(caminho);
        if (string.IsNullOrEmpty(pasta))
        {
            return null;
        }

        var quadros = new List<(int Numero, string Caminho, long Bytes)>();

        try
        {
            foreach (var arquivo in Directory.EnumerateFiles(pasta))
            {
                var nomeIrmao = Path.GetFileName(arquivo);
                if (!nomeIrmao.StartsWith(prefixo, StringComparison.OrdinalIgnoreCase)
                    || !nomeIrmao.EndsWith(extensao, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var irmao = Padrao().Match(nomeIrmao);
                if (!irmao.Success
                    || irmao.Groups["numero"].Value.Length != digitos
                    || !string.Equals(irmao.Groups["prefixo"].Value, prefixo, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!int.TryParse(irmao.Groups["numero"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var numero))
                {
                    continue;
                }

                long tamanho = 0;
                try
                {
                    tamanho = new FileInfo(arquivo).Length;
                }
                catch
                {
                    // arquivo em uso/rede caiu: entra na lista mesmo sem tamanho
                }

                quadros.Add((numero, arquivo, tamanho));
            }
        }
        catch (Exception ex)
        {
            Log.Error($"Falha ao listar sequencia em {pasta}", ex);
            return null;
        }

        if (quadros.Count < MinimoQuadros)
        {
            return null;
        }

        quadros.Sort((a, b) => a.Numero.CompareTo(b.Numero));

        return new ImageSequence
        {
            Pasta = pasta,
            NomeBase = LimparPrefixo(prefixo),
            Extensao = extensao.ToLowerInvariant(),
            Digitos = digitos,
            Primeiro = quadros[0].Numero,
            Ultimo = quadros[^1].Numero,
            Quadros = quadros.Count,
            Bytes = quadros.Sum(q => q.Bytes),
            Arquivos = quadros.Select(q => q.Caminho).ToList(),
        };
    }

    /// <summary>
    /// Lista de entrada do demuxer concat: aceita numeracao com furos sem truncar o video,
    /// coisa que o padrao %04d do image2 nao faz.
    /// </summary>
    public string MontarListaConcat(int fps)
    {
        var segundos = (1.0 / Math.Max(fps, 1)).ToString("0.######", CultureInfo.InvariantCulture);
        var texto = new StringBuilder();

        foreach (var arquivo in Arquivos)
        {
            var caminho = arquivo.Replace('\\', '/').Replace("'", @"'\''");
            texto.Append("file '").Append(caminho).Append('\'').Append('\n');
            texto.Append("duration ").Append(segundos).Append('\n');
        }

        return texto.ToString();
    }

    /// <summary>
    /// A sequencia nao e um arquivo so: para o planejador ela vira um clipe de
    /// N quadros com o fps escolhido, sem audio.
    /// </summary>
    public MediaInfo ComoVideo(MediaInfo primeiroQuadro, int fps) => new()
    {
        Caminho = Arquivos.Count > 0 ? Arquivos[0] : primeiroQuadro.Caminho,
        Largura = primeiroQuadro.Largura,
        Altura = primeiroQuadro.Altura,
        PixFmt = primeiroQuadro.PixFmt,
        Codec = primeiroQuadro.Codec,
        BitsPorAmostra = primeiroQuadro.BitsPorAmostra,
        Primarias = primeiroQuadro.Primarias,
        Transferencia = primeiroQuadro.Transferencia,
        EspacoCor = primeiroQuadro.EspacoCor,
        FaixaCor = primeiroQuadro.FaixaCor,
        TemAlpha = primeiroQuadro.TemAlpha,
        TemAudio = false,
        Fps = fps,
        DuracaoSegundos = fps > 0 ? (double)Quadros / fps : 0,
    };

    private static string LimparPrefixo(string prefixo) =>
        prefixo.Length == 0 ? "sequencia" : prefixo.TrimEnd('_', '-', '.', ' ');

    [GeneratedRegex(@"^(?<prefixo>.*?)(?<numero>\d{2,})(?<ext>\.[A-Za-z0-9]{2,5})$")]
    private static partial Regex Padrao();
}
