using System.Text.Json;

namespace CVExplorer.Host.Features.Projects;

/// <summary>
/// Arquivo marcador gravado na raiz do projeto. E o que define, sem ambiguidade,
/// que uma pasta e um projeto (e nao apenas uma pasta com subpastas parecidas).
/// </summary>
public sealed class ProjectMarker
{
    public const string FileName = ".cvproj";
    public const int CurrentSchema = 1;

    public int Schema { get; set; } = CurrentSchema;

    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Nome { get; set; } = string.Empty;

    /// <summary>PACOTE_GRAFICO | CHAMADAS | ABERTURA | EDITORIA | LIVRE</summary>
    public string Tipo { get; set; } = "LIVRE";

    public string? Cliente { get; set; }

    public string App { get; set; } = "CVExplorer";

    public string Modelo { get; set; } = "padrao";

    public DateTimeOffset CriadoEm { get; set; } = DateTimeOffset.Now;
}

public sealed class DetectedProject
{
    public string Caminho { get; set; } = string.Empty;

    public string Nome { get; set; } = string.Empty;

    /// <summary>marcador | estrutura</summary>
    public string Origem { get; set; } = "marcador";

    public ProjectMarker? Marcador { get; set; }

    public bool Registrado => Marcador is not null;
}

/// <summary>Atalho para uma pasta dentro do projeto (ex.: PROJETOS/MAYA, PREVIEW).</summary>
public sealed class ProjectShortcut
{
    public string Nome { get; set; } = string.Empty;

    public string Caminho { get; set; } = string.Empty;

    /// <summary>Agrupamento exibido no menu (nome da pasta-mae).</summary>
    public string Grupo { get; set; } = string.Empty;
}

public sealed class ProjectService
{
    private static readonly string[] EstruturaConhecida =
    {
        "PACOTE_GRAFICO", "CHAMADAS", "ABERTURA", "EDITORIA", "SAIDA", "ELEMENTOS", "PROJETOS",
    };

    public ProjectMarker? TryRead(string projectPath)
    {
        try
        {
            var file = Path.Combine(projectPath, ProjectMarker.FileName);
            if (!File.Exists(file))
            {
                return null;
            }

            var marker = JsonSerializer.Deserialize<ProjectMarker>(File.ReadAllText(file), Json.Options);
            if (marker is null)
            {
                return null;
            }

            marker.Nome = string.IsNullOrWhiteSpace(marker.Nome)
                ? Path.GetFileName(projectPath.TrimEnd(Path.DirectorySeparatorChar))
                : marker.Nome;
            return marker;
        }
        catch (Exception ex)
        {
            Log.Error($"Falha ao ler marcador em {projectPath}", ex);
            return null;
        }
    }

    /// <summary>Cria (ou reaproveita) o marcador na raiz indicada.</summary>
    public ProjectMarker Register(string projectPath, string? nome = null, string? tipo = null, string? cliente = null)
    {
        var full = Path.GetFullPath(projectPath);
        if (!Directory.Exists(full))
        {
            throw new DirectoryNotFoundException($"Pasta nao encontrada: {full}");
        }

        var existing = TryRead(full);
        if (existing is not null)
        {
            return existing;
        }

        var marker = new ProjectMarker
        {
            Nome = string.IsNullOrWhiteSpace(nome) ? Path.GetFileName(full.TrimEnd(Path.DirectorySeparatorChar)) : nome.Trim(),
            Tipo = string.IsNullOrWhiteSpace(tipo) ? "LIVRE" : tipo.Trim().ToUpperInvariant(),
            Cliente = string.IsNullOrWhiteSpace(cliente) ? null : cliente.Trim(),
        };

        var path = Path.Combine(full, ProjectMarker.FileName);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(marker, Json.Options));
        File.Move(tmp, path, overwrite: true);

        try
        {
            File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.Hidden);
        }
        catch (Exception ex)
        {
            Log.Error($"Nao foi possivel ocultar o marcador em {full}", ex);
        }

        return marker;
    }

    /// <summary>
    /// Procura projetos sob as raizes informadas: primeiro por marcador, e tambem por
    /// estrutura reconhecivel (pastas antigas que ainda nao tem .cvproj).
    /// </summary>
    public List<DetectedProject> Detect(
        IEnumerable<string> roots,
        int maxDepth,
        CancellationToken ct = default)
    {
        var found = new Dictionary<string, DetectedProject>(StringComparer.OrdinalIgnoreCase);

        foreach (var root in roots.Where(r => !string.IsNullOrWhiteSpace(r)))
        {
            ct.ThrowIfCancellationRequested();
            if (!Directory.Exists(root))
            {
                continue;
            }

            var options = new EnumerationOptions
            {
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.System,
                RecurseSubdirectories = false,
            };

            var queue = new Queue<(string Path, int Depth)>();
            queue.Enqueue((root, 0));

            while (queue.Count > 0)
            {
                ct.ThrowIfCancellationRequested();
                var (dir, depth) = queue.Dequeue();

                // Marcador: nao desce mais, o projeto ja foi identificado.
                var marker = TryRead(dir);
                if (marker is not null)
                {
                    found[dir] = new DetectedProject
                    {
                        Caminho = dir,
                        Nome = marker.Nome,
                        Origem = "marcador",
                        Marcador = marker,
                    };
                    continue;
                }

                string[] children;
                try
                {
                    children = Directory.GetDirectories(dir, "*", options);
                }
                catch (Exception ex)
                {
                    Log.Error($"Falha ao listar {dir}", ex);
                    continue;
                }

                if (depth > 0 && LooksLikeProject(children))
                {
                    found[dir] = new DetectedProject
                    {
                        Caminho = dir,
                        Nome = Path.GetFileName(dir.TrimEnd(Path.DirectorySeparatorChar)),
                        Origem = "estrutura",
                    };
                    continue;
                }

                if (depth >= maxDepth)
                {
                    continue;
                }

                foreach (var child in children)
                {
                    var name = Path.GetFileName(child);
                    if (name.StartsWith('.') || name.Equals("$RECYCLE.BIN", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    queue.Enqueue((child, depth + 1));
                }
            }
        }

        return found.Values
            .OrderByDescending(p => p.Registrado)
            .ThenBy(p => p.Nome, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static bool LooksLikeProject(IEnumerable<string> children)
    {
        var names = new HashSet<string>(
            children.Select(c => Path.GetFileName(c)),
            StringComparer.OrdinalIgnoreCase);

        var hits = EstruturaConhecida.Count(names.Contains);
        return hits >= 2;
    }

    /// <summary>
    /// Projetos ficam em qualquer lugar: o que vale e subir a partir da pasta atual
    /// procurando o marcador. Chamado a cada navegacao para a interface saber onde esta.
    /// </summary>
    public DetectedProject? Resolve(string caminho, int maxNiveis = 40)
    {
        if (string.IsNullOrWhiteSpace(caminho))
        {
            return null;
        }

        var dir = Directory.Exists(caminho) ? caminho : Path.GetDirectoryName(caminho);
        var nivel = 0;

        while (!string.IsNullOrEmpty(dir) && nivel++ < maxNiveis)
        {
            var marcador = TryRead(dir);
            if (marcador is not null)
            {
                return new DetectedProject
                {
                    Caminho = dir,
                    Nome = marcador.Nome,
                    Origem = "marcador",
                    Marcador = marcador,
                };
            }

            var pai = Path.GetDirectoryName(dir);
            if (string.IsNullOrEmpty(pai) || string.Equals(pai, dir, StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            dir = pai;
        }

        return null;
    }

    private static readonly HashSet<string> Contenedores = new(StringComparer.OrdinalIgnoreCase)
    {
        "PACOTE_GRAFICO", "CHAMADAS", "ABERTURA", "EDITORIA", "SAIDA", "REFERENCIAS",
    };

    /// <summary>
    /// Pastas reais do projeto para virar atalho no menu do topo.
    /// Le apenas o que existe: nada de lista fixa que nao corresponde ao disco.
    /// </summary>
    public List<ProjectShortcut> Shortcuts(string raiz, int maxItens = 28)
    {
        var lista = new List<ProjectShortcut>();
        var vistos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var opcoes = new EnumerationOptions
        {
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.System | FileAttributes.Hidden | FileAttributes.ReparsePoint,
        };

        void Adicionar(string grupo, DirectoryInfo dir)
        {
            if (lista.Count < maxItens && vistos.Add(dir.FullName))
            {
                lista.Add(new ProjectShortcut { Nome = dir.Name, Caminho = dir.FullName, Grupo = grupo });
            }
        }

        try
        {
            var info = new DirectoryInfo(raiz);
            if (!info.Exists)
            {
                return lista;
            }

            var topo = info.EnumerateDirectories("*", opcoes)
                .OrderBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            foreach (var dir in topo)
            {
                Adicionar("Projeto", dir);
            }

            foreach (var container in topo.Where(d => Contenedores.Contains(d.Name)))
            {
                List<DirectoryInfo> filhos;
                try
                {
                    filhos = container.EnumerateDirectories("*", opcoes)
                        .OrderBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase)
                        .ToList();
                }
                catch (Exception ex)
                {
                    Log.Error($"Falha ao listar atalhos de {container.FullName}", ex);
                    continue;
                }

                foreach (var filho in filhos)
                {
                    Adicionar(container.Name, filho);

                    // Um nivel a mais apenas dentro de PROJETOS (AE, PSD, AI, BLENDER, MAYA...).
                    if (filho.Name.Equals("PROJETOS", StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            foreach (var software in filho.EnumerateDirectories("*", opcoes)
                                         .OrderBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase))
                            {
                                Adicionar("PROJETOS", software);
                            }
                        }
                        catch (Exception ex)
                        {
                            Log.Error($"Falha ao listar softwares em {filho.FullName}", ex);
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error($"Falha ao montar atalhos de {raiz}", ex);
        }

        return lista;
    }
}
