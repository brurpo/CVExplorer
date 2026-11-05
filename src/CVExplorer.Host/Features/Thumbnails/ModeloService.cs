using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace CVExplorer.Host.Features.Thumbnails;

public sealed class ModeloPreparado
{
    public string Caminho { get; init; } = string.Empty;

    public string Ext { get; init; } = string.Empty;

    public bool Convertido { get; init; }

    public string? Erro { get; init; }
}

/// <summary>
/// Entrega o arquivo de modelo 3D em algo que o three.js saiba ler. GLB, glTF, OBJ, FBX, STL,
/// USDZ e DAE vão direto (DAE pelo ColladaLoader, os outros pelos loaders do próprio three).
///
/// USD (.usd/.usda/.usdc) e Alembic (.abc) não têm loader no navegador: quando o Blender está
/// instalado, ele converte uma vez para GLB num cache e essa versão é servida — o Blender não
/// fica aberto, só roda em segundo plano na primeira vez que o arquivo aparece.
/// </summary>
public sealed class ModeloService
{
    private static readonly HashSet<string> Diretos = new(StringComparer.OrdinalIgnoreCase)
    {
        ".glb", ".gltf", ".obj", ".fbx", ".stl", ".usdz", ".dae",
    };

    private static readonly HashSet<string> PrecisamBlender = new(StringComparer.OrdinalIgnoreCase)
    {
        ".usd", ".usda", ".usdc", ".abc",
    };

    private readonly ConcurrentDictionary<string, Task<ModeloPreparado>> _emAndamento = new();
    private string? _blender;
    private bool _procurouBlender;

    public static bool Suportado(string caminho) =>
        Diretos.Contains(Path.GetExtension(caminho)) || PrecisamBlender.Contains(Path.GetExtension(caminho));

    /// <summary>Precisa passar pelo Blender para virar algo que o navegador desenhe?</summary>
    public static bool PrecisaConverter(string caminho) => PrecisamBlender.Contains(Path.GetExtension(caminho));

    /// <summary>
    /// Devolve o caminho a servir para o visualizador. Nunca lança: em falha vem o motivo
    /// em <see cref="ModeloPreparado.Erro"/> e a interface mostra o ícone do tipo.
    /// </summary>
    public Task<ModeloPreparado> PrepararAsync(string caminho, CancellationToken ct = default)
    {
        var ext = Path.GetExtension(caminho).ToLowerInvariant();
        if (Diretos.Contains(ext))
        {
            return Task.FromResult(new ModeloPreparado { Caminho = caminho, Ext = ext.TrimStart('.') });
        }

        if (!PrecisamBlender.Contains(ext))
        {
            return Task.FromResult(new ModeloPreparado
            {
                Caminho = caminho,
                Ext = ext.TrimStart('.'),
                Erro = $"sem visualizador para {ext}",
            });
        }

        var chave = ChaveCache(caminho);
        return _emAndamento.GetOrAdd(chave, _ => ConverterAsync(caminho, ext, chave, ct));
    }

    private async Task<ModeloPreparado> ConverterAsync(string caminho, string ext, string chave, CancellationToken ct)
    {
        var destino = Path.Combine(AppPaths.ModelosDir, chave + ".glb");
        Directory.CreateDirectory(AppPaths.ModelosDir);

        if (File.Exists(destino) && File.GetLastWriteTimeUtc(destino) >= File.GetLastWriteTimeUtc(caminho))
        {
            return new ModeloPreparado { Caminho = destino, Ext = "glb", Convertido = true };
        }

        var blender = Blender();
        if (blender is null)
        {
            return new ModeloPreparado
            {
                Caminho = caminho,
                Ext = ext.TrimStart('.'),
                Erro = $"precisa do Blender instalado para abrir {ext}",
            };
        }

        var script = Path.Combine(AppPaths.ModelosDir, "importar-modelo.py");
        if (!File.Exists(script))
        {
            await File.WriteAllTextAsync(script, ScriptConversao, Encoding.UTF8, ct).ConfigureAwait(false);
        }

        try
        {
            var psi = new ProcessStartInfo(blender)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            foreach (var arg in new[]
            {
                "-b", "--factory-startup", "-noaudio",
                "--python", script, "--",
                caminho, destino, ext,
            })
            {
                psi.ArgumentList.Add(arg);
            }

            using var processo = Process.Start(psi);
            if (processo is null)
            {
                return new ModeloPreparado { Caminho = caminho, Ext = ext.TrimStart('.'), Erro = "não consegui abrir o Blender" };
            }

            var erro = await processo.StandardError.ReadToEndAsync(ct).ConfigureAwait(false);
            await processo.StandardOutput.ReadToEndAsync(ct).ConfigureAwait(false);
            await processo.WaitForExitAsync(ct).ConfigureAwait(false);

            if (File.Exists(destino) && new FileInfo(destino).Length > 0)
            {
                return new ModeloPreparado { Caminho = destino, Ext = "glb", Convertido = true };
            }

            Log.Error($"Blender não gerou o GLB de {caminho}: {erro}");
            return new ModeloPreparado
            {
                Caminho = caminho,
                Ext = ext.TrimStart('.'),
                Erro = "o Blender não conseguiu converter este arquivo",
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Log.Error($"Falha ao converter {caminho}", ex);
            return new ModeloPreparado { Caminho = caminho, Ext = ext.TrimStart('.'), Erro = ex.Message };
        }
        finally
        {
            _emAndamento.TryRemove(chave, out _);
        }
    }

    /// <summary>Blender instalado: variável de ambiente, PATH ou as pastas padrão do instalador.</summary>
    public string? Blender()
    {
        if (_procurouBlender)
        {
            return _blender;
        }

        _procurouBlender = true;

        var doAmbiente = Environment.GetEnvironmentVariable("CVEXPLORER_BLENDER");
        if (!string.IsNullOrWhiteSpace(doAmbiente) && File.Exists(doAmbiente))
        {
            return _blender = doAmbiente;
        }

        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                 .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidato = Path.Combine(dir.Trim(), "blender.exe");
                if (File.Exists(candidato))
                {
                    return _blender = candidato;
                }
            }
            catch
            {
                // entrada de PATH inválida
            }
        }

        var raizes = new[]
        {
            @"C:\Program Files\Blender Foundation",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs"),
        };

        foreach (var raiz in raizes)
        {
            try
            {
                if (!Directory.Exists(raiz))
                {
                    continue;
                }

                // Prefere a versão mais nova (ordem decrescente do nome da pasta).
                var encontrado = Directory.GetDirectories(raiz, "Blender*")
                    .Concat(Directory.GetDirectories(raiz, "*lender*"))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderByDescending(d => d, StringComparer.OrdinalIgnoreCase)
                    .Select(d => Path.Combine(d, "blender.exe"))
                    .FirstOrDefault(File.Exists);

                if (encontrado is not null)
                {
                    return _blender = encontrado;
                }
            }
            catch
            {
                // pasta sem acesso
            }
        }

        return null;
    }

    private static string ChaveCache(string caminho)
    {
        var info = new FileInfo(caminho);
        var bruto = $"{caminho.ToLowerInvariant()}|{info.Length}|{info.LastWriteTimeUtc.Ticks}";
        return Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(bruto)))[..20].ToLowerInvariant();
    }

    /// <summary>
    /// Script do Blender: importa o arquivo e exporta GLB. Sempre parte de um arquivo vazio,
    /// senão o cubo padrão do Blender entraria no modelo convertido.
    /// </summary>
    private const string ScriptConversao = """
        import sys
        import bpy

        argumentos = sys.argv[sys.argv.index("--") + 1:]
        origem, destino, ext = argumentos[0], argumentos[1], argumentos[2].lower()

        bpy.ops.wm.read_factory_settings(use_empty=True)

        if ext in (".usd", ".usda", ".usdc", ".usdz"):
            bpy.ops.wm.usd_import(filepath=origem)
        elif ext == ".abc":
            bpy.ops.wm.alembic_import(filepath=origem)
        elif ext == ".dae":
            bpy.ops.wm.collada_import(filepath=origem)
        else:
            raise SystemExit("formato sem importador: " + ext)

        bpy.context.view_layer.update()
        bpy.ops.export_scene.gltf(filepath=destino, export_format="GLB", use_selection=False)
        """;
}
