using System.Text.Json;

namespace CVExplorer.Host;

public sealed class SettingsStore
{
    private readonly object _gate = new();
    private AppSettings _current = new();

    public AppSettings Current
    {
        get { lock (_gate) { return _current; } }
    }

    public AppSettings Load()
    {
        AppPaths.EnsureCreated();

        if (File.Exists(AppPaths.SettingsFile))
        {
            try
            {
                var json = File.ReadAllText(AppPaths.SettingsFile);
                _current = JsonSerializer.Deserialize<AppSettings>(json, Json.Options) ?? new AppSettings();
            }
            catch (Exception ex)
            {
                Log.Error("Falha ao ler config.json, usando padroes", ex);
                _current = new AppSettings();
            }
        }

        _current.ParalelismoBusca = Math.Clamp(_current.ParalelismoBusca, 1, 32);
        _current.TamanhoMiniatura = Math.Clamp(_current.TamanhoMiniatura, 96, 512);
        _current.TamanhoGrade = Math.Clamp(_current.TamanhoGrade, 96, 320);
        return _current;
    }

    /// <summary>
    /// Atualiza a MESMA instancia em memoria (servicos guardam a referencia) e persiste
    /// de forma atomica.
    /// </summary>
    public void Save(AppSettings settings)
    {
        lock (_gate)
        {
            var c = _current;
            c.Idioma = settings.Idioma;
            c.Raizes = settings.Raizes ?? new List<string>();
            c.ProjetosRecentes = settings.ProjetosRecentes ?? new List<string>();
            c.UltimaPasta = settings.UltimaPasta;
            c.TamanhoMiniatura = Math.Clamp(settings.TamanhoMiniatura, 96, 512);
            c.MostrarOcultos = settings.MostrarOcultos;
            c.CaminhoFfmpeg = settings.CaminhoFfmpeg;
            c.ConverterVideoAoColar = settings.ConverterVideoAoColar;
            c.PerguntarAntesDeConverter = settings.PerguntarAntesDeConverter;
            c.PerfilVideoPadrao = settings.PerfilVideoPadrao;
            c.ParalelismoBusca = Math.Clamp(settings.ParalelismoBusca, 1, 32);
            c.VerificarAtualizacoes = settings.VerificarAtualizacoes;
            c.CanalAtualizacao = settings.CanalAtualizacao;
            c.Janela = settings.Janela ?? new WindowBounds();
            c.TamanhoGrade = Math.Clamp(settings.TamanhoGrade, 96, 320);

            WriteAtomic(AppPaths.SettingsFile, JsonSerializer.Serialize(c, Json.Options));
        }
    }

    public void SaveCurrent() => Save(Current);

    private static void WriteAtomic(string path, string content)
    {
        var dir = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(dir);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, content);
        File.Move(tmp, path, overwrite: true);
    }
}
