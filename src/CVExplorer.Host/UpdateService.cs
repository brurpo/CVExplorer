using Velopack;
using Velopack.Sources;

namespace CVExplorer.Host;

/// <summary>
/// Atualizacao automatica via GitHub Releases (Velopack).
/// Fora de uma instalacao Velopack (rodando do build), apenas informa que nao ha canal.
/// </summary>
public sealed class UpdateService
{
    private UpdateManager? _manager;
    private UpdateInfo? _pendente;

    public async Task<object> CheckAsync(bool baixar)
    {
        if (!AppInfo.RepositorioConfigurado)
        {
            return new { disponivel = false, motivo = "repositorio-nao-configurado" };
        }

        try
        {
            _manager ??= new UpdateManager(
                new GithubSource(AppInfo.RepoUrl, accessToken: null, prerelease: false));

            var update = await _manager.CheckForUpdatesAsync().ConfigureAwait(false);
            if (update is null)
            {
                return new { disponivel = false, motivo = "atualizado", versao = AppInfo.Versao };
            }

            _pendente = update;

            var result = new
            {
                disponivel = true,
                versao = update.TargetFullRelease.Version.ToString(),
                atual = AppInfo.Versao,
                baixado = false,
            };

            if (!baixar)
            {
                return result;
            }

            await _manager.DownloadUpdatesAsync(update).ConfigureAwait(false);
            return new { disponivel = true, versao = result.versao, atual = result.atual, baixado = true };
        }
        catch (Exception ex)
        {
            Log.Error("Falha ao verificar atualizacoes", ex);
            return new
            {
                disponivel = false,
                motivo = "erro",
                mensagem = ex.Message,
            };
        }
    }

    public async Task<object> ApplyAsync()
    {
        try
        {
            if (_manager is null || _pendente is null)
            {
                return new { ok = false, erro = "Nenhuma atualizacao baixada." };
            }

            await _manager.DownloadUpdatesAsync(_pendente).ConfigureAwait(false);
            _manager.ApplyUpdatesAndRestart(_pendente);
            return new { ok = true };
        }
        catch (Exception ex)
        {
            Log.Error("Falha ao aplicar atualizacao", ex);
            return new { ok = false, erro = ex.Message };
        }
    }
}
