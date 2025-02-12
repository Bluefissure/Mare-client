﻿using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility.Raii;
using ImGuiNET;
using MareSynchronos.Services;
using MareSynchronos.Services.Events;
using MareSynchronos.Services.Mediator;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Globalization;
using System.Numerics;

namespace MareSynchronos.UI;

internal class EventViewerUI : WindowMediatorSubscriberBase
{
    private readonly EventAggregator _eventAggregator;
    private readonly UiSharedService _uiSharedService;
    private List<Event> _currentEvents = new();
    private Lazy<List<Event>> _filteredEvents;
    private string _filterFreeText = string.Empty;
    private string _filterCharacter = string.Empty;
    private string _filterUid = string.Empty;
    private string _filterSource = string.Empty;
    private string _filterEvent = string.Empty;

    private List<Event> CurrentEvents
    {
        get
        {
            return _currentEvents;
        }
        set
        {
            _currentEvents = value;
            _filteredEvents = RecreateFilter();
        }
    }

    public EventViewerUI(ILogger<EventViewerUI> logger, MareMediator mediator,
        EventAggregator eventAggregator, UiSharedService uiSharedService, PerformanceCollectorService performanceCollectorService) 
        : base(logger, mediator, "事件查看器", performanceCollectorService)
    {
        _eventAggregator = eventAggregator;
        _uiSharedService = uiSharedService;
        SizeConstraints = new()
        {
            MinimumSize = new(600, 500),
            MaximumSize = new(1000, 2000)
        };
        _filteredEvents = RecreateFilter();
    }

    private Lazy<List<Event>> RecreateFilter()
    {
        return new(() =>
            CurrentEvents.Where(f =>
                (string.IsNullOrEmpty(_filterFreeText)
                || (f.EventSource.Contains(_filterFreeText, StringComparison.OrdinalIgnoreCase)
                    || f.Character.Contains(_filterFreeText, StringComparison.OrdinalIgnoreCase)
                    || f.UID.Contains(_filterFreeText, StringComparison.OrdinalIgnoreCase)
                    || f.Message.Contains(_filterFreeText, StringComparison.OrdinalIgnoreCase)
                ))
                &&
                (string.IsNullOrEmpty(_filterUid)
                    || (f.UID.Contains(_filterUid, StringComparison.OrdinalIgnoreCase))
                )
                &&
                (string.IsNullOrEmpty(_filterSource)
                    || (f.EventSource.Contains(_filterSource, StringComparison.OrdinalIgnoreCase))
                )
                &&
                (string.IsNullOrEmpty(_filterCharacter)
                    || (f.Character.Contains(_filterCharacter, StringComparison.OrdinalIgnoreCase))
                )
                &&
                (string.IsNullOrEmpty(_filterEvent)
                    || (f.Message.Contains(_filterEvent, StringComparison.OrdinalIgnoreCase))
                )
             ).ToList());
    }

    private void ClearFilters()
    {
        _filterFreeText = string.Empty;
        _filterCharacter = string.Empty;
        _filterUid = string.Empty;
        _filterSource = string.Empty;
        _filterEvent = string.Empty;
        _filteredEvents = RecreateFilter();
    }

    public override void OnOpen()
    {
        CurrentEvents = _eventAggregator.EventList.Value.OrderByDescending(f => f.EventTime).ToList();
        ClearFilters();
    }

    protected override void DrawInternal()
    {
        using (ImRaii.Disabled(!_eventAggregator.NewEventsAvailable))
        {
            if (_uiSharedService.IconTextButton(FontAwesomeIcon.ArrowsToCircle, "刷新事件"))
            {
                CurrentEvents = _eventAggregator.EventList.Value.OrderByDescending(f => f.EventTime).ToList();
            }
        }

        if (_eventAggregator.NewEventsAvailable)
        {
            ImGui.SameLine();
            ImGui.AlignTextToFramePadding();
            UiSharedService.ColorTextWrapped("有新的事件,请点击刷新查看", ImGuiColors.DalamudYellow);
        }

        var buttonSize = _uiSharedService.GetIconTextButtonSize(FontAwesomeIcon.FolderOpen, "打开事件记录文件夹");
        var dist = ImGui.GetWindowContentRegionMax().X - buttonSize;
        ImGui.SameLine(dist);
        if (_uiSharedService.IconTextButton(FontAwesomeIcon.FolderOpen, "打开事件记录文件夹"))
        {
            ProcessStartInfo ps = new()
            {
                FileName = _eventAggregator.EventLogFolder,
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Normal
            };
            Process.Start(ps);
        }

        _uiSharedService.BigText("最近的事件");
        var foldOut = ImRaii.TreeNode("筛选");
        if (foldOut)
        {
            if (_uiSharedService.IconTextButton(FontAwesomeIcon.Ban, "清除筛选"))
            {
                ClearFilters();
            }
            bool changedFilter = false;
            ImGui.SetNextItemWidth(200);
            changedFilter |= ImGui.InputText("查找所有列", ref _filterFreeText, 50);
            ImGui.SetNextItemWidth(200);
            changedFilter |= ImGui.InputText("按来源筛选", ref _filterSource, 50);
            ImGui.SetNextItemWidth(200);
            changedFilter |= ImGui.InputText("按UID筛选", ref _filterUid, 50);
            ImGui.SetNextItemWidth(200);
            changedFilter |= ImGui.InputText("按角色筛选", ref _filterCharacter, 50);
            ImGui.SetNextItemWidth(200);
            changedFilter |= ImGui.InputText("按事件筛选", ref _filterEvent, 50);
            if (changedFilter) _filteredEvents = RecreateFilter();
        }
        foldOut.Dispose();

        var cursorPos = ImGui.GetCursorPosY();
        var max = ImGui.GetWindowContentRegionMax();
        var min = ImGui.GetWindowContentRegionMin();
        var width = max.X - min.X;
        var height = max.Y - cursorPos;
        using var table = ImRaii.Table("eventTable", 6, ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.ScrollY | ImGuiTableFlags.RowBg,
            new Vector2(width, height));
        if (table)
        {
            ImGui.TableSetupScrollFreeze(0, 1);
            ImGui.TableSetupColumn(string.Empty, ImGuiTableColumnFlags.NoSort);
            ImGui.TableSetupColumn("时间");
            ImGui.TableSetupColumn("来源");
            ImGui.TableSetupColumn("UID");
            ImGui.TableSetupColumn("角色");
            ImGui.TableSetupColumn("事件");
            ImGui.TableHeadersRow();
            foreach (var ev in _filteredEvents.Value)
            {
                var icon = ev.EventSeverity switch
                {
                    EventSeverity.Informational => FontAwesomeIcon.InfoCircle,
                    EventSeverity.Warning => FontAwesomeIcon.ExclamationTriangle,
                    EventSeverity.Error => FontAwesomeIcon.Cross,
                    _ => FontAwesomeIcon.QuestionCircle
                };

                var iconColor = ev.EventSeverity switch
                {
                    EventSeverity.Informational => new Vector4(),
                    EventSeverity.Warning => ImGuiColors.DalamudYellow,
                    EventSeverity.Error => ImGuiColors.DalamudRed,
                    _ => new Vector4()
                };

                ImGui.TableNextColumn();
                _uiSharedService.IconText(icon, iconColor == new Vector4() ? null : iconColor);
                UiSharedService.AttachToolTip(ev.EventSeverity.ToString());
                ImGui.TableNextColumn();
                ImGui.AlignTextToFramePadding();
                ImGui.TextUnformatted(ev.EventTime.ToString("G", CultureInfo.CurrentCulture));
                ImGui.TableNextColumn();
                ImGui.AlignTextToFramePadding();
                ImGui.TextUnformatted(ev.EventSource);
                ImGui.TableNextColumn();
                ImGui.AlignTextToFramePadding();
                ImGui.TextUnformatted(string.IsNullOrEmpty(ev.UID) ? "--" : ev.UID);
                ImGui.TableNextColumn();
                ImGui.AlignTextToFramePadding();
                ImGui.TextUnformatted(string.IsNullOrEmpty(ev.Character) ? "--" : ev.Character);
                ImGui.TableNextColumn();
                ImGui.AlignTextToFramePadding();
                var posX = ImGui.GetCursorPosX();
                var maxTextLength = ImGui.GetWindowContentRegionMax().X - posX;
                var textSize = ImGui.CalcTextSize(ev.Message).X;
                var msg = ev.Message;
                while (textSize > maxTextLength)
                {
                    msg = msg[..^5] + "...";
                    textSize = ImGui.CalcTextSize(msg).X;
                }
                ImGui.TextUnformatted(msg);
                if (!string.Equals(msg, ev.Message, StringComparison.Ordinal))
                {
                    UiSharedService.AttachToolTip(ev.Message);
                }
            }
        }
    }
}
