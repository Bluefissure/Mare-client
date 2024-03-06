using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility.Raii;
using ImGuiNET;
using MareSynchronos.PlayerData.Pairs;
using MareSynchronos.Services.Mediator;
using MareSynchronos.WebAPI;
using System.Numerics;

namespace MareSynchronos.UI.Components.Popup;

internal class ReportPopupHandler : IPopupHandler
{
    private readonly ApiController _apiController;
    private readonly UiSharedService _uiSharedService;
    private Pair? _reportedPair;
    private string _reportReason = string.Empty;

    public ReportPopupHandler(ApiController apiController, UiSharedService uiSharedService)
    {
        _apiController = apiController;
        _uiSharedService = uiSharedService;
    }

    public Vector2 PopupSize => new(500, 500);

    public bool ShowClose => true;

    public void DrawContent()
    {
        using (ImRaii.PushFont(_uiSharedService.UidFont))
            UiSharedService.TextWrapped("举报 " + _reportedPair!.UserData.AliasOrUID + " 月海档案");

        ImGui.InputTextMultiline("##reportReason", ref _reportReason, 500, new Vector2(500 - ImGui.GetStyle().ItemSpacing.X * 2, 200));
        UiSharedService.TextWrapped($"注意:发送举报将立即禁用对象用户的档案显示.{Environment.NewLine}" +
            $"举报会被提交给你当前连接到的Mare服务器的管理人员.{Environment.NewLine}" +
            $"举报中会包含你的联系方式 (Discord用户名).{Environment.NewLine}" +
            $"视违规严重情况,该用户的档案和账号将被删除或封禁.");
        UiSharedService.ColorTextWrapped("滥用举报或虚假举报可能导致Mare账号注销或禁止使用Mare服务.", ImGuiColors.DalamudRed);
        UiSharedService.ColorTextWrapped("本举报仅针对不合适的月海档案,请勿用来举报骚扰或其他行为. " +
            "针对非档案的举报将不被受理.", ImGuiColors.DalamudYellow);

        using (ImRaii.Disabled(string.IsNullOrEmpty(_reportReason)))
        {
            if (UiSharedService.NormalizedIconTextButton(FontAwesomeIcon.ExclamationTriangle, "发送举报"))
            {
                ImGui.CloseCurrentPopup();
                var reason = _reportReason;
                _ = _apiController.UserReportProfile(new(_reportedPair.UserData, reason));
            }
        }
    }

    public void Open(OpenReportPopupMessage msg)
    {
        _reportedPair = msg.PairToReport;
        _reportReason = string.Empty;
    }
}