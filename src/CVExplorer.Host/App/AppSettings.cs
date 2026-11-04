using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CVExplorer.Host;

public sealed class WindowBounds
{
    public int X { get; set; } = 120;
    public int Y { get; set; } = 90;
    public int Width { get; set; } = 1280;
    public int Height { get; set; } = 820;
    public bool Maximized { get; set; }
}

public sealed class AppSettings
{
    public string Idioma { get; set; } = "pt-BR";

    /// <summary>Raizes varridas na auto-deteccao de projetos.</summary>
    public List<string> Raizes { get; set; } = new();

    /// <summary>
    /// Projetos ja visitados, do mais recente para o mais antigo.
    /// Projetos ficam em qualquer lugar, entao esta lista e o atalho confiavel.
    /// </summary>
    public List<string> ProjetosRecentes { get; set; } = new();

    public string? UltimaPasta { get; set; }

    public int TamanhoMiniatura { get; set; } = 256;

    public bool MostrarOcultos { get; set; }

    /// <summary>Caminho explicito do ffmpeg. Vazio = procurar embutido e depois no PATH.</summary>
    public string? CaminhoFfmpeg { get; set; }

    /// <summary>
    /// Colar video no projeto transcodifica em vez de copiar: o original nunca entra
    /// no projeto, so o arquivo convertido.
    /// </summary>
    public bool ConverterVideoAoColar { get; set; } = true;

    /// <summary>Perguntar "converter ou colar o original?" ao colar um video.</summary>
    public bool PerguntarAntesDeConverter { get; set; } = true;

    /// <summary>auto | prores422hq | prores422 | prores4444 | dnxhr_hqx | proxy</summary>
    public string PerfilVideoPadrao { get; set; } = "auto";

    /// <summary>Threads simultaneas na busca. Latencia de rede pede paralelismo moderado.</summary>
    public int ParalelismoBusca { get; set; } = 8;

    public bool VerificarAtualizacoes { get; set; } = true;

    public string CanalAtualizacao { get; set; } = "win";

    public WindowBounds Janela { get; set; } = new();

    public int TamanhoGrade { get; set; } = 168;

    [JsonIgnore]
    public string IdiomaEfetivo =>
        string.IsNullOrWhiteSpace(Idioma) ? "pt-BR" : Idioma;
}

internal static class Json
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static readonly JsonSerializerOptions Compact = new(Options)
    {
        WriteIndented = false,
    };
}
