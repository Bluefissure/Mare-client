using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using MareSynchronos.API.Dto.User;
using MareSynchronos.PlayerData.Export;
using MareSynchronos.PlayerData.Handlers;
using MareSynchronos.PlayerData.Pairs;
using MareSynchronos.Services;
using MareSynchronos.Services.Mediator;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MareSynchronos.Interop.Ipc;

public class IpcProvider : IHostedService, IMediatorSubscriber
{
    private readonly ILogger<IpcProvider> _logger;
    private readonly IDalamudPluginInterface _pi;
    private readonly CharaDataManager _charaDataManager;
    private ICallGateProvider<string, IGameObject, bool>? _loadFileProvider;
    private ICallGateProvider<string, IGameObject, Task<bool>>? _loadFileAsyncProvider;
    private ICallGateProvider<List<nint>>? _handledGameAddresses;
    private readonly List<GameObjectHandler> _activeGameObjectHandlers = [];

    private readonly PairManager  _pairManager;
    private ICallGateProvider<string, string, string, object?>? _applyStatusesToPairRequest;

    public MareMediator Mediator { get; init; }

    public IpcProvider(ILogger<IpcProvider> logger, IDalamudPluginInterface pi,
        MareCharaFileManager mareCharaFileManager, DalamudUtilService dalamudUtil,
        MareMediator mareMediator, PairManager  pairManager)
        CharaDataManager charaDataManager, MareMediator mareMediator)
    {
        _logger = logger;
        _pi = pi;
        _charaDataManager = charaDataManager;
        Mediator = mareMediator;
        _pairManager = pairManager;

        Mediator.Subscribe<GameObjectHandlerCreatedMessage>(this, (msg) =>
        {
            if (msg.OwnedObject) return;
            _activeGameObjectHandlers.Add(msg.GameObjectHandler);
        });
        Mediator.Subscribe<GameObjectHandlerDestroyedMessage>(this, (msg) =>
        {
            if (msg.OwnedObject) return;
            _activeGameObjectHandlers.Remove(msg.GameObjectHandler);
        });
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting IpcProviderService");
        _loadFileProvider = _pi.GetIpcProvider<string, IGameObject, bool>("MareSynchronos.LoadMcdf");
        _loadFileProvider.RegisterFunc(LoadMcdf);
        _loadFileAsyncProvider = _pi.GetIpcProvider<string, IGameObject, Task<bool>>("MareSynchronos.LoadMcdfAsync");
        _loadFileAsyncProvider.RegisterFunc(LoadMcdfAsync);
        _handledGameAddresses = _pi.GetIpcProvider<List<nint>>("MareSynchronos.GetHandledAddresses");
        _handledGameAddresses.RegisterFunc(GetHandledAddresses);

        _applyStatusesToPairRequest = _pi.GetIpcProvider<string, string, string, object?>("MareSynchronos.ApplyStatusesToMarePlayers");
        _applyStatusesToPairRequest.RegisterAction(HandleApplyStatusesToPairRequest);

        _logger.LogInformation("Started IpcProviderService");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogDebug("Stopping IpcProvider Service");
        _loadFileProvider?.UnregisterFunc();
        _loadFileAsyncProvider?.UnregisterFunc();
        _handledGameAddresses?.UnregisterFunc();
        _applyStatusesToPairRequest?.UnregisterAction();
        Mediator.UnsubscribeAll(this);
        return Task.CompletedTask;
    }

    private async Task<bool> LoadMcdfAsync(string path, IGameObject target)
    {
        await ApplyFileAsync(path, target).ConfigureAwait(false);

        return true;
    }

    private bool LoadMcdf(string path, IGameObject target)
    {
        _ = Task.Run(async () => await ApplyFileAsync(path, target).ConfigureAwait(false)).ConfigureAwait(false);

        return true;
    }

    private async Task ApplyFileAsync(string path, IGameObject target)
    {
        _charaDataManager.LoadMcdf(path);
        await (_charaDataManager.LoadedMcdfHeader ?? Task.CompletedTask).ConfigureAwait(false);
        _charaDataManager.McdfApplyToTarget(target.Name.TextValue);
    }

    private List<nint> GetHandledAddresses()
    {
        return _activeGameObjectHandlers.Where(g => g.Address != nint.Zero).Select(g => g.Address).Distinct().ToList();
    }

        /// <summary>
    /// Handles the request from our clients moodles plugin to update another one of our pairs status.
    /// </summary>
    /// <param name="requester">The name of the player requesting the apply (SHOULD ALWAYS BE OUR CLIENT PLAYER) </param>
    /// <param name="recipient">The name of the player to apply the status to. (SHOULD ALWAYS BE A PAIR) </param>
    /// <param name="statuses">The list of statuses to apply to the recipient. </param>
    private void HandleApplyStatusesToPairRequest(string requester, string recipient, string statuses)
    {
        try
        {
            var pairUser = _pairManager.GetOnlineUserPairs().Find(p => p.PlayerName == recipient.Split('@')[0] && p.IsVisible)?.UserData;
            if (pairUser == null)
            {
                _logger.LogWarning("Received ApplyStatusesToPairRequest for {recipient} but could not find the UID for the pair", recipient);
                return;
            }
            // fetch the UID for the pair to apply for.
            _logger.LogDebug("Received ApplyStatusesToPair request to {recipient}, applying statuses", recipient);
            var dto = new ApplyMoodlesByStatusDto(pairUser, statuses);
            Mediator.Publish(new MoodlesApplyStatusToPair(dto));
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failure handling ApplyStatusesToPairRequest: ");
            throw;
        }

    }
}