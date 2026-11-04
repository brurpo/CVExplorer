using System.Text.Json;

namespace CVExplorer.Host.Features.Projects;

/// <summary>
/// No da arvore de pastas padrao. O token <see cref="QuadroToken"/> marca os nos
/// que se repetem por quadro (ex.: EDITORIA/{QUADRO}/ELEMENTOS).
/// </summary>
public sealed class TemplateNode
{
    public string Nome { get; set; } = string.Empty;

    public List<TemplateNode> Filhos { get; set; } = new();
}

public static class TemplateCatalog
{
    public const string QuadroToken = "{QUADRO}";

    /// <summary>
    /// Estrutura de referencia dos projetos (PACOTE_GRAFICO, CHAMADAS, ABERTURA, EDITORIA...).
    /// Fica em %LOCALAPPDATA%\CVExplorer\modelo-projeto.json e pode ser editada pelo usuario.
    /// </summary>
    public static List<TemplateNode> Default() => new()
    {
        new TemplateNode
        {
            Nome = "PACOTE_GRAFICO",
            Filhos = PacotePadrao(comMaya: true, comReferencias: false),
        },
        new TemplateNode
        {
            Nome = "CHAMADAS",
            Filhos = PacotePadrao(comMaya: false, comReferencias: false),
        },
        new TemplateNode
        {
            Nome = "ABERTURA",
            Filhos = PacotePadrao(comMaya: true, comReferencias: false),
        },
        new TemplateNode
        {
            Nome = "EDITORIA",
            Filhos = new List<TemplateNode>
            {
                new()
                {
                    Nome = QuadroToken,
                    Filhos = new List<TemplateNode>
                    {
                        new()
                        {
                            Nome = "ELEMENTOS",
                            Filhos = new List<TemplateNode>
                            {
                                new() { Nome = "AUDIO" },
                                new() { Nome = "MOV" },
                                new() { Nome = "SEQUENCIAS" },
                                new() { Nome = "IMAGENS" },
                                new() { Nome = "FONTS" },
                            },
                        },
                        new()
                        {
                            Nome = "PROJETOS",
                            Filhos = new List<TemplateNode>
                            {
                                new() { Nome = "AE" },
                                new() { Nome = "PSD" },
                                new() { Nome = "AI" },
                            },
                        },
                        new()
                        {
                            Nome = "REFERENCIAS",
                            Filhos = new List<TemplateNode>
                            {
                                new() { Nome = "VIDEOS" },
                                new() { Nome = "IMAGENS" },
                            },
                        },
                    },
                },
            },
        },
        new TemplateNode
        {
            Nome = "SAIDA",
            Filhos = new List<TemplateNode> { new() { Nome = "CHAMADAS" } },
        },
        new TemplateNode { Nome = "REFERENCIAS" },
    };

    private static List<TemplateNode> PacotePadrao(bool comMaya, bool comReferencias)
    {
        var projetos = new List<TemplateNode>
        {
            new() { Nome = "AE" },
            new() { Nome = "PSD" },
            new() { Nome = "AI" },
            new() { Nome = "BLENDER" },
        };
        if (comMaya)
        {
            projetos.Add(new TemplateNode { Nome = "MAYA" });
        }

        var filhos = new List<TemplateNode>
        {
            new()
            {
                Nome = "ELEMENTOS",
                Filhos = new List<TemplateNode>
                {
                    new() { Nome = "AUDIO" },
                    new() { Nome = "MOV" },
                    new() { Nome = "SEQUENCIAS" },
                    new() { Nome = "IMAGENS" },
                    new() { Nome = "FONTS" },
                },
            },
            new() { Nome = "PROJETOS", Filhos = projetos },
        };

        if (comReferencias)
        {
            filhos.Add(new TemplateNode
            {
                Nome = "REFERENCIAS",
                Filhos = new List<TemplateNode> { new() { Nome = "VIDEOS" }, new() { Nome = "IMAGENS" } },
            });
        }

        filhos.Add(new TemplateNode { Nome = "PREVIEW" });
        return filhos;
    }

    /// <summary>Todas as pastas da arvore como caminhos relativos com "/" (usado pela UI).</summary>
    public static List<string> Flatten(List<TemplateNode> nodes)
    {
        var result = new List<string>();
        void Walk(IEnumerable<TemplateNode> level, string prefix)
        {
            foreach (var node in level)
            {
                if (string.IsNullOrWhiteSpace(node.Nome))
                {
                    continue;
                }

                var path = prefix.Length == 0 ? node.Nome : prefix + "/" + node.Nome;
                result.Add(path);
                Walk(node.Filhos, path);
            }
        }

        Walk(nodes, string.Empty);
        return result;
    }

    public static List<TemplateNode> LoadOrSeed(string filePath)
    {
        try
        {
            if (File.Exists(filePath))
            {
                var nodes = JsonSerializer.Deserialize<List<TemplateNode>>(File.ReadAllText(filePath), Json.Options);
                if (nodes is { Count: > 0 })
                {
                    return nodes;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error("Falha ao ler modelo-projeto.json, usando padrao", ex);
        }

        var defaults = Default();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
            File.WriteAllText(filePath, JsonSerializer.Serialize(defaults, Json.Options));
        }
        catch (Exception ex)
        {
            Log.Error("Falha ao gravar modelo-projeto.json padrao", ex);
        }

        return defaults;
    }
}
