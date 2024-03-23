using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.ImGuiFileDialog;
using MareSynchronos.MareConfiguration;
using MareSynchronos.PlayerData.Export;
using MareSynchronos.Services;
using MareSynchronos.Services.Mediator;
using Microsoft.Extensions.Logging;

namespace MareSynchronos.UI;

public class GposeUi : WindowMediatorSubscriberBase
{
    private readonly MareConfigService _configService;
    private readonly UiSharedService _uiSharedService;
    private readonly DalamudUtilService _dalamudUtil;
    private readonly FileDialogManager _fileDialogManager;
    private readonly MareCharaFileManager _mareCharaFileManager;
    private Task<long>? _expectedLength;
    private Task? _applicationTask;

    public GposeUi(ILogger<GposeUi> logger, MareCharaFileManager mareCharaFileManager,
        DalamudUtilService dalamudUtil, FileDialogManager fileDialogManager, MareConfigService configService,
        MareMediator mediator, PerformanceCollectorService performanceCollectorService, UiSharedService uiSharedService)
        : base(logger, mediator, "月海同步器集体动作导入窗口###MareSynchronosGposeUI", performanceCollectorService)
    {
        _mareCharaFileManager = mareCharaFileManager;
        _dalamudUtil = dalamudUtil;
        _fileDialogManager = fileDialogManager;
        _configService = configService;
        _uiSharedService = uiSharedService;
        Mediator.Subscribe<GposeStartMessage>(this, (_) => StartGpose());
        Mediator.Subscribe<GposeEndMessage>(this, (_) => EndGpose());
        IsOpen = _dalamudUtil.IsInGpose;
        this.SizeConstraints = new()
        {
            MinimumSize = new(200, 200),
            MaximumSize = new(400, 400)
        };
    }

    protected override void DrawInternal()
    {
        if (!_dalamudUtil.IsInGpose) IsOpen = false;

        if (!_mareCharaFileManager.CurrentlyWorking)
        {
            if (_uiSharedService.IconTextButton(FontAwesomeIcon.FolderOpen, "加载MCDF"))
            {
                _fileDialogManager.OpenFileDialog("选择MCDF文件", ".mcdf", (success, paths) =>
                {
                    if (!success) return;
                    if (paths.FirstOrDefault() is not string path) return;

                    _configService.Current.ExportFolder = Path.GetDirectoryName(path) ?? string.Empty;
                    _configService.Save();

                    _expectedLength = Task.Run(() => _mareCharaFileManager.LoadMareCharaFile(path));
                }, 1, Directory.Exists(_configService.Current.ExportFolder) ? _configService.Current.ExportFolder : null);
            }
            UiSharedService.AttachToolTip("将其应用于当前选定的集体动作角色");
            if (_mareCharaFileManager.LoadedCharaFile != null && _expectedLength != null)
            {
                UiSharedService.TextWrapped("已加载文件：" + _mareCharaFileManager.LoadedCharaFile.FilePath);
                UiSharedService.TextWrapped("文件描述： " + _mareCharaFileManager.LoadedCharaFile.CharaFileData.Description);
                if (_uiSharedService.IconTextButton(FontAwesomeIcon.Check, "应用加载的MCDF"))
                {
                    _applicationTask = Task.Run(async () => await _mareCharaFileManager.ApplyMareCharaFile(_dalamudUtil.GposeTargetGameObject, _expectedLength!.GetAwaiter().GetResult()).ConfigureAwait(false));
                }
                UiSharedService.AttachToolTip("将其应用于当前选定的集体动作角色");
                UiSharedService.ColorTextWrapped("警告：重新绘制或更改角色将恢复所有应用的mod。", ImGuiColors.DalamudYellow);
            }
            if (_applicationTask?.IsFaulted ?? false)
            {
                UiSharedService.ColorTextWrapped("读取MCDF文件失败. MCDF文件可能损坏. 请重新导出MCDF文件并重试.",
                    ImGuiColors.DalamudRed);
                UiSharedService.ColorTextWrapped("注意: 如果这是你的MCDF文件, 请尝试重新绘制自身角色, 稍后再导出文件. " +
                    "或让导出文件的玩家按此方法重新尝试导出.", ImGuiColors.DalamudYellow);
            }
        }
        else
        {
            UiSharedService.ColorTextWrapped("正在加载角色...", ImGuiColors.DalamudYellow);
        }
        UiSharedService.TextWrapped("提示：您可以在插件设置中禁用此窗口在进入集体动作时自动打开，使用命令“/Mare gpose”可以手动打开此窗口。");
    }

    private void EndGpose()
    {
        IsOpen = false;
        _applicationTask = null;
        _expectedLength = null;
        _mareCharaFileManager.ClearMareCharaFile();
    }

    private void StartGpose()
    {
        IsOpen = _configService.Current.OpenGposeImportOnGposeStart;
    }
}