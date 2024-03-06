using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility.Raii;
using ImGuiNET;
using MareSynchronos.API.Data.Enum;
using MareSynchronos.Interop.Ipc;
using MareSynchronos.Services;
using MareSynchronos.Services.Mediator;
using MareSynchronos.Utils;
using Microsoft.Extensions.Logging;
using System.Numerics;

namespace MareSynchronos.UI;

public class DataAnalysisUi : WindowMediatorSubscriberBase
{
    private readonly CharacterAnalyzer _characterAnalyzer;
    private readonly Progress<(string, int)> _conversionProgress = new();
    private readonly IpcManager _ipcManager;
    private readonly Dictionary<string, string[]> _texturesToConvert = new(StringComparer.Ordinal);
    private Dictionary<ObjectKind, Dictionary<string, CharacterAnalyzer.FileDataEntry>>? _cachedAnalysis;
    private CancellationTokenSource _conversionCancellationTokenSource = new();
    private string _conversionCurrentFileName = string.Empty;
    private int _conversionCurrentFileProgress = 0;
    private Task? _conversionTask;
    private bool _enableBc7ConversionMode = false;
    private bool _hasUpdate = false;
    private bool _modalOpen = false;
    private string _selectedFileTypeTab = string.Empty;
    private string _selectedHash = string.Empty;
    private ObjectKind _selectedObjectTab;
    private bool _showModal = false;

    public DataAnalysisUi(ILogger<DataAnalysisUi> logger, MareMediator mediator, CharacterAnalyzer characterAnalyzer, IpcManager ipcManager, PerformanceCollectorService performanceCollectorService)
        : base(logger, mediator, "月海角色数据分析", performanceCollectorService)
    {
        _characterAnalyzer = characterAnalyzer;
        _ipcManager = ipcManager;
        Mediator.Subscribe<CharacterDataAnalyzedMessage>(this, (_) =>
        {
            _hasUpdate = true;
        });
        SizeConstraints = new()
        {
            MinimumSize = new()
            {
                X = 800,
                Y = 600
            },
            MaximumSize = new()
            {
                X = 3840,
                Y = 2160
            }
        };

        _conversionProgress.ProgressChanged += ConversionProgress_ProgressChanged;
    }

    protected override void DrawInternal()
    {
        if (_conversionTask != null && !_conversionTask.IsCompleted)
        {
            _showModal = true;
            if (ImGui.BeginPopupModal("BC7 Conversion in Progress"))
            {
                ImGui.TextUnformatted("BC7 Conversion in progress: " + _conversionCurrentFileProgress + "/" + _texturesToConvert.Count);
                UiSharedService.TextWrapped("Current file: " + _conversionCurrentFileName);
                if (UiSharedService.NormalizedIconTextButton(FontAwesomeIcon.StopCircle, "Cancel conversion"))
                {
                    _conversionCancellationTokenSource.Cancel();
                }
                UiSharedService.SetScaledWindowSize(500);
                ImGui.EndPopup();
            }
            else
            {
                _modalOpen = false;
            }
        }
        else if (_conversionTask != null && _conversionTask.IsCompleted && _texturesToConvert.Count > 0)
        {
            _conversionTask = null;
            _texturesToConvert.Clear();
            _showModal = false;
            _modalOpen = false;
            _enableBc7ConversionMode = false;
        }

        if (_showModal && !_modalOpen)
        {
            ImGui.OpenPopup("BC7 Conversion in Progress");
            _modalOpen = true;
        }

        if (_hasUpdate)
        {
            _cachedAnalysis = _characterAnalyzer.LastAnalysis.DeepClone();
            _hasUpdate = false;
        }

        UiSharedService.TextWrapped("此窗口显示你的角色正在使用的并且与月海相关的所有文件及文件大小。");

        if (_cachedAnalysis!.Count == 0) return;

        bool isAnalyzing = _characterAnalyzer.IsAnalysisRunning;
        if (isAnalyzing)
        {
            UiSharedService.ColorTextWrapped($"分析中 {_characterAnalyzer.CurrentFile}/{_characterAnalyzer.TotalFiles}",
                ImGuiColors.DalamudYellow);
            if (UiSharedService.NormalizedIconTextButton(FontAwesomeIcon.StopCircle, "取消分析"))
            {
                _characterAnalyzer.CancelAnalyze();
            }
        }
        else
        {
            if (_cachedAnalysis!.Any(c => c.Value.Any(f => !f.Value.IsComputed)))
            {
                UiSharedService.ColorTextWrapped("分析中的某些条目的文件大小尚未确定，请按下面的按钮分析您的当前数据",
                    ImGuiColors.DalamudYellow);
                if (UiSharedService.NormalizedIconTextButton(FontAwesomeIcon.PlayCircle, "开始分析（缺失条目）"))
                {
                    _ = _characterAnalyzer.ComputeAnalysis(print: false);
                }
            }
            else
            {
                if (UiSharedService.NormalizedIconTextButton(FontAwesomeIcon.PlayCircle, "开始分析（重新计算所有条目）"))
                {
                    _ = _characterAnalyzer.ComputeAnalysis(print: false, recalculate: true);
                }
            }
        }

        ImGui.Separator();

        ImGui.TextUnformatted("文件总数：");
        ImGui.SameLine();
        ImGui.TextUnformatted(_cachedAnalysis!.Values.Sum(c => c.Values.Count).ToString());
        ImGui.SameLine();
        using (var font = ImRaii.PushFont(UiBuilder.IconFont))
        {
            ImGui.TextUnformatted(FontAwesomeIcon.InfoCircle.ToIconString());
        }
        if (ImGui.IsItemHovered())
        {
            string text = "";
            var groupedfiles = _cachedAnalysis.Values.SelectMany(f => f.Values).GroupBy(f => f.FileType, StringComparer.Ordinal);
            text = string.Join(Environment.NewLine, groupedfiles.OrderBy(f => f.Key, StringComparer.Ordinal)
                .Select(f => f.Key + "：" + f.Count() + "个文件，大小为： " + UiSharedService.ByteToString(f.Sum(v => v.OriginalSize))
                + ", 已压缩： " + UiSharedService.ByteToString(f.Sum(v => v.CompressedSize))));
            ImGui.SetTooltip(text);
        }
        ImGui.TextUnformatted("总大小（未压缩）：");
        ImGui.SameLine();
        ImGui.TextUnformatted(UiSharedService.ByteToString(_cachedAnalysis!.Sum(c => c.Value.Sum(c => c.Value.OriginalSize))));
        ImGui.TextUnformatted("总大小（已压缩）：");
        ImGui.SameLine();
        ImGui.TextUnformatted(UiSharedService.ByteToString(_cachedAnalysis!.Sum(c => c.Value.Sum(c => c.Value.CompressedSize))));

        ImGui.Separator();

        using var tabbar = ImRaii.TabBar("objectSelection");
        foreach (var kvp in _cachedAnalysis)
        {
            using var id = ImRaii.PushId(kvp.Key.ToString());
            string tabText = kvp.Key.ToString();
            if (kvp.Value.Any(f => !f.Value.IsComputed)) tabText += " (!)";
            using var tab = ImRaii.TabItem(tabText + "###" + kvp.Key.ToString());
            if (tab.Success)
            {
                var groupedfiles = kvp.Value.Select(v => v.Value).GroupBy(f => f.FileType, StringComparer.Ordinal).OrderBy(k => k.Key, StringComparer.Ordinal).ToList();

                ImGui.TextUnformatted(kvp.Key+ " 文件总数：");
                ImGui.SameLine();
                ImGui.TextUnformatted(kvp.Value.Count.ToString());
                ImGui.SameLine();

                using (var font = ImRaii.PushFont(UiBuilder.IconFont))
                {
                    ImGui.TextUnformatted(FontAwesomeIcon.InfoCircle.ToIconString());
                }
                if (ImGui.IsItemHovered())
                {
                    string text = "";
                    text = string.Join(Environment.NewLine, groupedfiles
                        .Select(f => f.Key + ": " + f.Count() + "个文件，大小：" + UiSharedService.ByteToString(f.Sum(v => v.OriginalSize))
                        + "，已压缩：" + UiSharedService.ByteToString(f.Sum(v => v.CompressedSize))));
                    ImGui.SetTooltip(text);
                }
                ImGui.TextUnformatted($"{kvp.Key} 大小（未压缩）：");
                ImGui.SameLine();
                ImGui.TextUnformatted(UiSharedService.ByteToString(kvp.Value.Sum(c => c.Value.OriginalSize)));
                ImGui.TextUnformatted($"{kvp.Key} 大小（已压缩）：");
                ImGui.SameLine();
                ImGui.TextUnformatted(UiSharedService.ByteToString(kvp.Value.Sum(c => c.Value.CompressedSize)));

                ImGui.Separator();
                if (_selectedObjectTab != kvp.Key)
                {
                    _selectedHash = string.Empty;
                    _selectedObjectTab = kvp.Key;
                    _selectedFileTypeTab = string.Empty;
                    _enableBc7ConversionMode = false;
                    _texturesToConvert.Clear();
                }

                using var fileTabBar = ImRaii.TabBar("fileTabs");

                foreach (IGrouping<string, CharacterAnalyzer.FileDataEntry>? fileGroup in groupedfiles)
                {
                    string fileGroupText = fileGroup.Key + " [" + fileGroup.Count() + "]";
                    var requiresCompute = fileGroup.Any(k => !k.IsComputed);
                    using var tabcol = ImRaii.PushColor(ImGuiCol.Tab, UiSharedService.Color(ImGuiColors.DalamudYellow), requiresCompute);
                    if (requiresCompute)
                    {
                        fileGroupText += " (!)";
                    }
                    ImRaii.IEndObject fileTab;
                    using (var textcol = ImRaii.PushColor(ImGuiCol.Text, UiSharedService.Color(new(0, 0, 0, 1)),
                        requiresCompute && !string.Equals(_selectedFileTypeTab, fileGroup.Key, StringComparison.Ordinal)))
                    {
                        fileTab = ImRaii.TabItem(fileGroupText + "###" + fileGroup.Key);
                    }

                    if (!fileTab) { fileTab.Dispose(); continue; }

                    if (!string.Equals(fileGroup.Key, _selectedFileTypeTab, StringComparison.Ordinal))
                    {
                        _selectedFileTypeTab = fileGroup.Key;
                        _selectedHash = string.Empty;
                        _enableBc7ConversionMode = false;
                        _texturesToConvert.Clear();
                    }

                    ImGui.TextUnformatted($"{fileGroup.Key} 文件");
                    ImGui.SameLine();
                    ImGui.TextUnformatted(fileGroup.Count().ToString());

                    ImGui.TextUnformatted($"{fileGroup.Key} 文件大小（未压缩）：");
                    ImGui.SameLine();
                    ImGui.TextUnformatted(UiSharedService.ByteToString(fileGroup.Sum(c => c.OriginalSize)));

                    ImGui.TextUnformatted($"{fileGroup.Key} 文件大小（已压缩）：");
                    ImGui.SameLine();
                    ImGui.TextUnformatted(UiSharedService.ByteToString(fileGroup.Sum(c => c.CompressedSize)));

                    if (string.Equals(_selectedFileTypeTab, "tex", StringComparison.Ordinal))
                    {
                        ImGui.Checkbox("启用BC7格式转换模式", ref _enableBc7ConversionMode);
                        if (_enableBc7ConversionMode)
                        {
                            UiSharedService.ColorText("警告BC7格式转换：", ImGuiColors.DalamudYellow);
                            ImGui.SameLine();
                            UiSharedService.ColorText("将纹理转换为BC7格式是不可逆转的！", ImGuiColors.DalamudRed);
                            UiSharedService.ColorTextWrapped("- 将纹理转换为BC7格式将大幅减小它们的大小（已压缩和未压缩）。建议用于高分辨率（4k+）纹理。" +
                            Environment.NewLine + "- 一些纹理，尤其是使用颜色集的纹理，可能不适合BC7格式的转换，可能产生色彩扭曲、细节损失、曝光失真。" +
                            Environment.NewLine + "- 在转换纹理之前，请确保拥有正在转换的mod的原始文件，以便在出现问题后重新导入。" +
                            Environment.NewLine + "- 转换将自动转换所有找到的纹理重复项（文件路径超过1个的条目）。" +
                            Environment.NewLine + "- 将纹理转换为BC7格式是一项非常复杂的工作，根据要转换的纹理数量，需要一段时间才能完成。"
                                , ImGuiColors.DalamudYellow);
                            if (_texturesToConvert.Count > 0 && UiSharedService.NormalizedIconTextButton(FontAwesomeIcon.PlayCircle, "Start conversion of " + _texturesToConvert.Count + " texture(s)"))
                            {
                                _conversionCancellationTokenSource = _conversionCancellationTokenSource.CancelRecreate();
                                _conversionTask = _ipcManager.Penumbra.ConvertTextureFiles(_logger, _texturesToConvert, _conversionProgress, _conversionCancellationTokenSource.Token);
                            }
                        }
                    }

                    ImGui.Separator();
                    DrawTable(fileGroup);

                    fileTab.Dispose();
                }
            }
        }

        ImGui.Separator();

        ImGui.TextUnformatted("选中的文件：");
        ImGui.SameLine();
        UiSharedService.ColorText(_selectedHash, ImGuiColors.DalamudYellow);

        if (_cachedAnalysis[_selectedObjectTab].TryGetValue(_selectedHash, out CharacterAnalyzer.FileDataEntry? item))
        {
            var filePaths = item.FilePaths;
            ImGui.TextUnformatted("本地文件路径：");
            ImGui.SameLine();
            UiSharedService.TextWrapped(filePaths[0]);
            if (filePaths.Count > 1)
            {
                ImGui.SameLine();
                ImGui.TextUnformatted($"(另外还有 {filePaths.Count - 1} 个)");
                ImGui.SameLine();
                UiSharedService.FontText(FontAwesomeIcon.InfoCircle.ToIconString(), UiBuilder.IconFont);
                UiSharedService.AttachToolTip(string.Join(Environment.NewLine, filePaths.Skip(1)));
            }

            var gamepaths = item.GamePaths;
            ImGui.TextUnformatted("游戏使用路径：");
            ImGui.SameLine();
            UiSharedService.TextWrapped(gamepaths[0]);
            if (gamepaths.Count > 1)
            {
                ImGui.SameLine();
                ImGui.TextUnformatted($"(另外还有 {gamepaths.Count - 1} 个)");
                ImGui.SameLine();
                UiSharedService.FontText(FontAwesomeIcon.InfoCircle.ToIconString(), UiBuilder.IconFont);
                UiSharedService.AttachToolTip(string.Join(Environment.NewLine, gamepaths.Skip(1)));
            }
        }
    }

    public override void OnOpen()
    {
        _hasUpdate = true;
        _selectedHash = string.Empty;
        _enableBc7ConversionMode = false;
        _texturesToConvert.Clear();
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        _conversionProgress.ProgressChanged -= ConversionProgress_ProgressChanged;
    }

    private void ConversionProgress_ProgressChanged(object? sender, (string, int) e)
    {
        _conversionCurrentFileName = e.Item1;
        _conversionCurrentFileProgress = e.Item2;
    }

    private void DrawTable(IGrouping<string, CharacterAnalyzer.FileDataEntry> fileGroup)
    {
        using var table = ImRaii.Table("Analysis", string.Equals(fileGroup.Key, "tex", StringComparison.Ordinal) ?
            (_enableBc7ConversionMode ? 7 : 6) : 5, ImGuiTableFlags.Sortable | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY | ImGuiTableFlags.SizingFixedFit,
            new Vector2(0, 300));
        if (!table.Success) return;
        ImGui.TableSetupColumn("哈希");
        ImGui.TableSetupColumn("文件路径");
        ImGui.TableSetupColumn("游戏路径");
        ImGui.TableSetupColumn("原始大小");
        ImGui.TableSetupColumn("压缩大小");
        if (string.Equals(fileGroup.Key, "tex", StringComparison.Ordinal))
        {
            ImGui.TableSetupColumn("格式");
            if (_enableBc7ConversionMode) ImGui.TableSetupColumn("转换为BC7格式");
        }
        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableHeadersRow();

        var sortSpecs = ImGui.TableGetSortSpecs();
        if (sortSpecs.SpecsDirty)
        {
            var idx = sortSpecs.Specs.ColumnIndex;

            if (idx == 0 && sortSpecs.Specs.SortDirection == ImGuiSortDirection.Ascending)
                _cachedAnalysis![_selectedObjectTab] = _cachedAnalysis[_selectedObjectTab].OrderBy(k => k.Key, StringComparer.Ordinal).ToDictionary(d => d.Key, d => d.Value, StringComparer.Ordinal);
            if (idx == 0 && sortSpecs.Specs.SortDirection == ImGuiSortDirection.Descending)
                _cachedAnalysis![_selectedObjectTab] = _cachedAnalysis[_selectedObjectTab].OrderByDescending(k => k.Key, StringComparer.Ordinal).ToDictionary(d => d.Key, d => d.Value, StringComparer.Ordinal);
            if (idx == 1 && sortSpecs.Specs.SortDirection == ImGuiSortDirection.Ascending)
                _cachedAnalysis![_selectedObjectTab] = _cachedAnalysis[_selectedObjectTab].OrderBy(k => k.Value.FilePaths.Count).ToDictionary(d => d.Key, d => d.Value, StringComparer.Ordinal);
            if (idx == 1 && sortSpecs.Specs.SortDirection == ImGuiSortDirection.Descending)
                _cachedAnalysis![_selectedObjectTab] = _cachedAnalysis[_selectedObjectTab].OrderByDescending(k => k.Value.FilePaths.Count).ToDictionary(d => d.Key, d => d.Value, StringComparer.Ordinal);
            if (idx == 2 && sortSpecs.Specs.SortDirection == ImGuiSortDirection.Ascending)
                _cachedAnalysis![_selectedObjectTab] = _cachedAnalysis[_selectedObjectTab].OrderBy(k => k.Value.GamePaths.Count).ToDictionary(d => d.Key, d => d.Value, StringComparer.Ordinal);
            if (idx == 2 && sortSpecs.Specs.SortDirection == ImGuiSortDirection.Descending)
                _cachedAnalysis![_selectedObjectTab] = _cachedAnalysis[_selectedObjectTab].OrderByDescending(k => k.Value.GamePaths.Count).ToDictionary(d => d.Key, d => d.Value, StringComparer.Ordinal);
            if (idx == 3 && sortSpecs.Specs.SortDirection == ImGuiSortDirection.Ascending)
                _cachedAnalysis![_selectedObjectTab] = _cachedAnalysis[_selectedObjectTab].OrderBy(k => k.Value.OriginalSize).ToDictionary(d => d.Key, d => d.Value, StringComparer.Ordinal);
            if (idx == 3 && sortSpecs.Specs.SortDirection == ImGuiSortDirection.Descending)
                _cachedAnalysis![_selectedObjectTab] = _cachedAnalysis[_selectedObjectTab].OrderByDescending(k => k.Value.OriginalSize).ToDictionary(d => d.Key, d => d.Value, StringComparer.Ordinal);
            if (idx == 4 && sortSpecs.Specs.SortDirection == ImGuiSortDirection.Ascending)
                _cachedAnalysis![_selectedObjectTab] = _cachedAnalysis[_selectedObjectTab].OrderBy(k => k.Value.CompressedSize).ToDictionary(d => d.Key, d => d.Value, StringComparer.Ordinal);
            if (idx == 4 && sortSpecs.Specs.SortDirection == ImGuiSortDirection.Descending)
                _cachedAnalysis![_selectedObjectTab] = _cachedAnalysis[_selectedObjectTab].OrderByDescending(k => k.Value.CompressedSize).ToDictionary(d => d.Key, d => d.Value, StringComparer.Ordinal);
            if (string.Equals(fileGroup.Key, "tex", StringComparison.Ordinal) && idx == 5 && sortSpecs.Specs.SortDirection == ImGuiSortDirection.Ascending)
                _cachedAnalysis![_selectedObjectTab] = _cachedAnalysis[_selectedObjectTab].OrderBy(k => k.Value.Format).ToDictionary(d => d.Key, d => d.Value, StringComparer.Ordinal);
            if (string.Equals(fileGroup.Key, "tex", StringComparison.Ordinal) && idx == 5 && sortSpecs.Specs.SortDirection == ImGuiSortDirection.Descending)
                _cachedAnalysis![_selectedObjectTab] = _cachedAnalysis[_selectedObjectTab].OrderByDescending(k => k.Value.Format).ToDictionary(d => d.Key, d => d.Value, StringComparer.Ordinal);

            sortSpecs.SpecsDirty = false;
        }

        foreach (var item in fileGroup)
        {
            using var text = ImRaii.PushColor(ImGuiCol.Text, new Vector4(0, 0, 0, 1), string.Equals(item.Hash, _selectedHash, StringComparison.Ordinal));
            using var text2 = ImRaii.PushColor(ImGuiCol.Text, new Vector4(1, 1, 1, 1), !item.IsComputed);
            ImGui.TableNextColumn();
            if (!item.IsComputed)
            {
                ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg1, UiSharedService.Color(ImGuiColors.DalamudRed));
                ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, UiSharedService.Color(ImGuiColors.DalamudRed));
            }
            if (string.Equals(_selectedHash, item.Hash, StringComparison.Ordinal))
            {
                ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg1, UiSharedService.Color(ImGuiColors.DalamudYellow));
                ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, UiSharedService.Color(ImGuiColors.DalamudYellow));
            }
            ImGui.TextUnformatted(item.Hash);
            if (ImGui.IsItemClicked()) _selectedHash = item.Hash;
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(item.FilePaths.Count.ToString());
            if (ImGui.IsItemClicked()) _selectedHash = item.Hash;
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(item.GamePaths.Count.ToString());
            if (ImGui.IsItemClicked()) _selectedHash = item.Hash;
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(UiSharedService.ByteToString(item.OriginalSize));
            if (ImGui.IsItemClicked()) _selectedHash = item.Hash;
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(UiSharedService.ByteToString(item.CompressedSize));
            if (ImGui.IsItemClicked()) _selectedHash = item.Hash;
            if (string.Equals(fileGroup.Key, "tex", StringComparison.Ordinal))
            {
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(item.Format.Value);
                if (ImGui.IsItemClicked()) _selectedHash = item.Hash;
                if (_enableBc7ConversionMode)
                {
                    ImGui.TableNextColumn();
                    if (string.Equals(item.Format.Value, "BC7", StringComparison.Ordinal))
                    {
                        ImGui.TextUnformatted("");
                        continue;
                    }
                    var filePath = item.FilePaths[0];
                    bool toConvert = _texturesToConvert.ContainsKey(filePath);
                    if (ImGui.Checkbox("###convert" + item.Hash, ref toConvert))
                    {
                        if (toConvert && !_texturesToConvert.ContainsKey(filePath))
                        {
                            _texturesToConvert[filePath] = item.FilePaths.Skip(1).ToArray();
                        }
                        else if (!toConvert && _texturesToConvert.ContainsKey(filePath))
                        {
                            _texturesToConvert.Remove(filePath);
                        }
                    }
                }
            }
        }
    }
}