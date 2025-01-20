using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using ImGuiNET;
using MareSynchronos.Services.CharaData.Models;

namespace MareSynchronos.UI;

internal sealed partial class CharaDataHubUi
{
    private string _joinLobbyId = string.Empty;
    private void DrawGposeTogether()
    {
        if (!_charaDataManager.BrioAvailable)
        {
            ImGuiHelpers.ScaledDummy(5);
            UiSharedService.DrawGroupedCenteredColorText("你必须安装Brio才能使用在线GPose.", ImGuiColors.DalamudRed);
            ImGuiHelpers.ScaledDummy(5);
        }

        if (!_uiSharedService.ApiController.IsConnected)
        {
            ImGuiHelpers.ScaledDummy(5);
            UiSharedService.DrawGroupedCenteredColorText("必须连接到服务器才能使用在线GPose.", ImGuiColors.DalamudRed);
            ImGuiHelpers.ScaledDummy(5);
        }

        _uiSharedService.BigText("在线GPose");
        DrawHelpFoldout("GPose together is a way to do multiplayer GPose sessions and collaborations." + UiSharedService.DoubleNewLine
            + "GPose together requires Brio to function. Only Brio is also supported for the actual posing interactions. Attempting to pose using other tools will lead to conflicts and exploding characters." + UiSharedService.DoubleNewLine
            + "To use GPose together you either create or join a GPose Together Lobby. After you and other people have joined, make sure that everyone is on the same map. "
            + "It is not required for you to be on the same server, DC or instance. Users that are on the same map will be drawn as moving purple wisps in the overworld, so you can easily find each other." + UiSharedService.DoubleNewLine
            + "Once you are close to each other you can initiate GPose. You must either assign or spawn characters for each of the lobby users. Their own poses and positions to their character will be automatically applied." + Environment.NewLine
            + "Pose and location data during GPose are updated approximately every 10-20s.");

        using var disabled = ImRaii.Disabled(!_charaDataManager.BrioAvailable || !_uiSharedService.ApiController.IsConnected);

        UiSharedService.DistanceSeparator();
        _uiSharedService.BigText("大厅设置");
        if (string.IsNullOrEmpty(_charaDataGposeTogetherManager.CurrentGPoseLobbyId))
        {
            if (_uiSharedService.IconTextButton(FontAwesomeIcon.Plus, "创建新的在线GPose大厅"))
            {
                _charaDataGposeTogetherManager.CreateNewLobby();
            }
            ImGuiHelpers.ScaledDummy(5);
            ImGui.SetNextItemWidth(250);
            ImGui.InputTextWithHint("##lobbyId", "在线GPose大厅ID", ref _joinLobbyId, 30);
            if (_uiSharedService.IconTextButton(FontAwesomeIcon.ArrowRight, "加入在线GPose大厅"))
            {
                _charaDataGposeTogetherManager.JoinGPoseLobby(_joinLobbyId);
                _joinLobbyId = string.Empty;
            }
            if (!string.IsNullOrEmpty(_charaDataGposeTogetherManager.LastGPoseLobbyId)
                && _uiSharedService.IconTextButton(FontAwesomeIcon.LongArrowAltRight, $"重新加入最近的在线GPose大厅 {_charaDataGposeTogetherManager.LastGPoseLobbyId}"))
            {
                _charaDataGposeTogetherManager.JoinGPoseLobby(_charaDataGposeTogetherManager.LastGPoseLobbyId);
            }
        }
        else
        {
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted("在线GPose大厅");
            ImGui.SameLine();
            UiSharedService.ColorTextWrapped(_charaDataGposeTogetherManager.CurrentGPoseLobbyId, ImGuiColors.ParsedGreen);
            ImGui.SameLine();
            if (_uiSharedService.IconButton(FontAwesomeIcon.Clipboard))
            {
                ImGui.SetClipboardText(_charaDataGposeTogetherManager.CurrentGPoseLobbyId);
            }
            UiSharedService.AttachToolTip("复制大厅ID到剪切板.");
            using (ImRaii.Disabled(!UiSharedService.CtrlPressed()))
            {
                if (_uiSharedService.IconTextButton(FontAwesomeIcon.ArrowLeft, "离开在线GPose大厅"))
                {
                    _charaDataGposeTogetherManager.LeaveGPoseLobby();
                }
            }
            UiSharedService.AttachToolTip("离开当前的在线GPose大厅." + UiSharedService.TooltipSeparator + "按住CTRL并点击.");
        }
        UiSharedService.DistanceSeparator();
        using (ImRaii.Disabled(string.IsNullOrEmpty(_charaDataGposeTogetherManager.CurrentGPoseLobbyId)))
        {
            if (_uiSharedService.IconTextButton(FontAwesomeIcon.ArrowUp, "发送更新的角色数据"))
            {
                _ = _charaDataGposeTogetherManager.PushCharacterDownloadDto();
            }
            UiSharedService.AttachToolTip("这将上传你的外貌, 姿势和位置数据给大厅中的所有用户.");
            if (!_uiSharedService.IsInGpose)
            {
                ImGuiHelpers.ScaledDummy(5);
                UiSharedService.DrawGroupedCenteredColorText("将用户分配到角色功能仅在GPose中可用.", ImGuiColors.DalamudYellow, 300);
            }
            UiSharedService.DistanceSeparator();
            ImGui.TextUnformatted("用户列表");
            var gposeCharas = _dalamudUtilService.GetGposeCharactersFromObjectTable();
            var self = _dalamudUtilService.GetPlayerCharacter();
            gposeCharas = gposeCharas.Where(c => c != null && !string.Equals(c.Name.TextValue, self.Name.TextValue, StringComparison.Ordinal)).ToList();

            using (ImRaii.Child("charaChild", new(0, 0), false, ImGuiWindowFlags.AlwaysAutoResize))
            {
                ImGuiHelpers.ScaledDummy(3);

                if (!_charaDataGposeTogetherManager.UsersInLobby.Any() && !string.IsNullOrEmpty(_charaDataGposeTogetherManager.CurrentGPoseLobbyId))
                {
                    UiSharedService.DrawGroupedCenteredColorText("只有你在", ImGuiColors.DalamudYellow);
                }
                else
                {
                    foreach (var user in _charaDataGposeTogetherManager.UsersInLobby)
                    {
                        DrawLobbyUser(user, gposeCharas);
                    }
                }
            }
        }
    }

    private void DrawLobbyUser(GposeLobbyUserData user,
        IEnumerable<Dalamud.Game.ClientState.Objects.Types.ICharacter?> gposeCharas)
    {
        using var id = ImRaii.PushId(user.UserData.UID);
        using var indent = ImRaii.PushIndent(5f);
        var sameMapAndServer = _charaDataGposeTogetherManager.IsOnSameMapAndServer(user);
        var width = ImGui.GetContentRegionAvail().X - 5;
        UiSharedService.DrawGrouped(() =>
        {
            var availWidth = ImGui.GetContentRegionAvail().X;
            ImGui.AlignTextToFramePadding();
            var note = _serverConfigurationManager.GetNoteForUid(user.UserData.UID);
            var userText = note == null ? user.UserData.AliasOrUID : $"{note} ({user.UserData.AliasOrUID})";
            UiSharedService.ColorText(userText, ImGuiColors.ParsedGreen);

            var buttonsize = _uiSharedService.GetIconButtonSize(FontAwesomeIcon.ArrowRight).X;
            var buttonsize2 = _uiSharedService.GetIconButtonSize(FontAwesomeIcon.Plus).X;
            ImGui.SameLine();
            ImGui.SetCursorPosX(availWidth - (buttonsize + buttonsize2 + ImGui.GetStyle().ItemSpacing.X));
            using (ImRaii.Disabled(!_uiSharedService.IsInGpose || user.CharaData == null || user.Address == nint.Zero))
            {
                if (_uiSharedService.IconButton(FontAwesomeIcon.ArrowRight))
                {
                    _ = _charaDataGposeTogetherManager.ApplyCharaData(user);
                }
            }
            UiSharedService.AttachToolTip("将新接收的数据应用到角色." + UiSharedService.TooltipSeparator + "注意: 如果按钮为灰色, 表明已应用了最新的数据.");
            ImGui.SameLine();
            using (ImRaii.Disabled(!_uiSharedService.IsInGpose || user.CharaData == null || sameMapAndServer.SameEverything))
            {
                if (_uiSharedService.IconButton(FontAwesomeIcon.Plus))
                {
                    _ = _charaDataGposeTogetherManager.SpawnAndApplyData(user);
                }
            }
            UiSharedService.AttachToolTip("生成一个新角色, 并将用户数据应用到该角色." + UiSharedService.TooltipSeparator + "注意: 如果按钮为灰色, " +
                "则用户尚未上传数据或你与他们在同一地图之中. 如果是后者的情况下, 你需要和他们配对并将数据应用在对应角色身上.");


            using (ImRaii.Group())
            {
                UiSharedService.ColorText("地图信息", ImGuiColors.DalamudGrey);
                ImGui.SameLine();
                _uiSharedService.IconText(FontAwesomeIcon.ExternalLinkSquareAlt, ImGuiColors.DalamudGrey);
            }
            UiSharedService.AttachToolTip(user.WorldDataDescriptor + UiSharedService.TooltipSeparator);

            ImGui.SameLine();
            _uiSharedService.IconText(FontAwesomeIcon.Map, sameMapAndServer.SameMap ? ImGuiColors.ParsedGreen : ImGuiColors.DalamudRed);
            if (ImGui.IsItemClicked(ImGuiMouseButton.Left) && user.WorldData != null)
            {
                _dalamudUtilService.SetMarkerAndOpenMap(new(user.WorldData.Value.PositionX, user.WorldData.Value.PositionY, user.WorldData.Value.PositionZ), user.Map);
            }
            UiSharedService.AttachToolTip((sameMapAndServer.SameMap ? "你们在同一张地图内." : "你们不在同一张地图内.") + UiSharedService.TooltipSeparator
                + "注意: 点击以在地图上显示用户位置." + Environment.NewLine
                + "注意: 你们必须处于同一张地图才能正确共享GPose数据.");

            ImGui.SameLine();
            _uiSharedService.IconText(FontAwesomeIcon.Globe, sameMapAndServer.SameServer ? ImGuiColors.ParsedGreen : ImGuiColors.DalamudRed);
            UiSharedService.AttachToolTip((sameMapAndServer.SameMap ? "你们在同一个服务器中." : "你们不在同一个服务器中.") + UiSharedService.TooltipSeparator
                + "注意: GPose同步不要求你们位于同一个服务器中, 但你可能会需要为每个其他用户生成一个角色.");

            ImGui.SameLine();
            _uiSharedService.IconText(FontAwesomeIcon.Running, sameMapAndServer.SameEverything ? ImGuiColors.ParsedGreen : ImGuiColors.DalamudRed);
            UiSharedService.AttachToolTip(sameMapAndServer.SameEverything ? "你们在同一个分线中." : "你们不在同一个分线中." + UiSharedService.TooltipSeparator +
                "注意: 你们不在同一分线, 但在同一张地图内, 将会显示为幽灵." + Environment.NewLine
                + "注意: GPose同步不要求你们位于同一个分线中, 但你可能会需要为每个其他用户生成一个角色.");

            using (ImRaii.Disabled(!_uiSharedService.IsInGpose))
            {
                ImGui.SetNextItemWidth(200);
                using (var combo = ImRaii.Combo("##character", string.IsNullOrEmpty(user.AssociatedCharaName) ? "尚未分配角色" : CharaName(user.AssociatedCharaName)))
                {
                    if (combo)
                    {
                        foreach (var chara in gposeCharas)
                        {
                            if (chara == null) continue;

                            if (ImGui.Selectable(CharaName(chara.Name.TextValue), chara.Address == user.Address))
                            {
                                user.AssociatedCharaName = chara.Name.TextValue;
                                user.Address = chara.Address;
                            }
                        }
                    }
                }
                ImGui.SameLine();
                using (ImRaii.Disabled(user.Address == nint.Zero))
                {
                    if (_uiSharedService.IconButton(FontAwesomeIcon.Trash))
                    {
                        user.AssociatedCharaName = string.Empty;
                        user.Address = nint.Zero;
                    }
                }
                UiSharedService.AttachToolTip("解除分配");
                if (_uiSharedService.IsInGpose && user.Address == nint.Zero)
                {
                    ImGui.SameLine();
                    _uiSharedService.IconText(FontAwesomeIcon.ExclamationTriangle, ImGuiColors.DalamudRed);
                    UiSharedService.AttachToolTip("用户尚未被分配到有效的角色上. 姿势数据不会被应用.");
                }
            }
        }, 5, width);
        ImGuiHelpers.ScaledDummy(5);
    }
}
