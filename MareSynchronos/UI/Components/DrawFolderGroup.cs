using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using ImGuiNET;
using MareSynchronos.API.Data.Extensions;
using MareSynchronos.API.Dto.Group;
using MareSynchronos.PlayerData.Pairs;
using MareSynchronos.Services.Mediator;
using MareSynchronos.UI.Handlers;
using MareSynchronos.WebAPI;
using System.Collections.Immutable;

namespace MareSynchronos.UI.Components;

public class DrawFolderGroup : DrawFolderBase
{
    private readonly ApiController _apiController;
    private readonly GroupFullInfoDto _groupFullInfoDto;
    private readonly IdDisplayHandler _idDisplayHandler;
    private readonly MareMediator _mareMediator;

    public DrawFolderGroup(string id, GroupFullInfoDto groupFullInfoDto, ApiController apiController,
        IImmutableList<DrawUserPair> drawPairs, IImmutableList<Pair> allPairs, TagHandler tagHandler, IdDisplayHandler idDisplayHandler,
        MareMediator mareMediator) :
        base(id, drawPairs, allPairs, tagHandler)
    {
        _groupFullInfoDto = groupFullInfoDto;
        _apiController = apiController;
        _idDisplayHandler = idDisplayHandler;
        _mareMediator = mareMediator;
    }

    protected override bool RenderIfEmpty => true;
    protected override bool RenderMenu => true;
    private bool IsModerator => IsOwner || _groupFullInfoDto.GroupUserInfo.IsModerator();
    private bool IsOwner => string.Equals(_groupFullInfoDto.OwnerUID, _apiController.UID, StringComparison.Ordinal);
    private bool IsPinned => _groupFullInfoDto.GroupUserInfo.IsPinned();

    protected override float DrawIcon()
    {
        ImGui.AlignTextToFramePadding();

        UiSharedService.NormalizedIcon(_groupFullInfoDto.GroupPermissions.IsDisableInvites() ? FontAwesomeIcon.Lock : FontAwesomeIcon.Users);
        if (_groupFullInfoDto.GroupPermissions.IsDisableInvites())
        {
            UiSharedService.AttachToolTip("同步贝 " + _groupFullInfoDto.GroupAliasOrGID + " 关闭了邀请功能");
        }

        using (ImRaii.PushStyle(ImGuiStyleVar.ItemSpacing, ImGui.GetStyle().ItemSpacing with { X = ImGui.GetStyle().ItemSpacing.X / 2f }))
        {
            ImGui.SameLine();
            ImGui.AlignTextToFramePadding();

            ImGui.TextUnformatted("[" + OnlinePairs.ToString() + "]");
        }
        UiSharedService.AttachToolTip(OnlinePairs + " 在线" + Environment.NewLine + TotalPairs + " 总计");

        ImGui.SameLine();
        if (IsOwner)
        {
            ImGui.AlignTextToFramePadding();
            UiSharedService.NormalizedIcon(FontAwesomeIcon.Crown);
            UiSharedService.AttachToolTip("你是 " + _groupFullInfoDto.GroupAliasOrGID + " 的所有者");
        }
        else if (IsModerator)
        {
            ImGui.AlignTextToFramePadding();
            UiSharedService.NormalizedIcon(FontAwesomeIcon.UserShield);
            UiSharedService.AttachToolTip("你是 " + _groupFullInfoDto.GroupAliasOrGID + " 的管理员");
        }
        else if (IsPinned)
        {
            ImGui.AlignTextToFramePadding();
            UiSharedService.NormalizedIcon(FontAwesomeIcon.Thumbtack);
            UiSharedService.AttachToolTip("你在 " + _groupFullInfoDto.GroupAliasOrGID + " 被置顶");
        }
        ImGui.SameLine();
        return ImGui.GetCursorPosX();
    }

    protected override void DrawMenu(float menuWidth)
    {
        ImGui.TextUnformatted("同步贝菜单 (" + _groupFullInfoDto.GroupAliasOrGID + ")");
        ImGui.Separator();

        ImGui.TextUnformatted("通用同步贝功能");
        if (UiSharedService.NormalizedIconTextButton(FontAwesomeIcon.Copy, "复制ID", menuWidth, true))
        {
            ImGui.CloseCurrentPopup();
            ImGui.SetClipboardText(_groupFullInfoDto.GroupAliasOrGID);
        }
        UiSharedService.AttachToolTip("复制同步贝ID到剪贴板");

        if (UiSharedService.NormalizedIconTextButton(FontAwesomeIcon.StickyNote, "复制备注", menuWidth, true))
        {
            ImGui.CloseCurrentPopup();
            ImGui.SetClipboardText(UiSharedService.GetNotes(DrawPairs.Select(k => k.Pair).ToList()));
        }
        UiSharedService.AttachToolTip("复制同步贝中的所有备注到剪贴板." + Environment.NewLine + "可以通过设置 -> 隐私 -> 从剪切板导入备注");

        if (UiSharedService.NormalizedIconTextButton(FontAwesomeIcon.ArrowCircleLeft, "离开同步贝", menuWidth, true) && UiSharedService.CtrlPressed())
        {
            _ = _apiController.GroupLeave(_groupFullInfoDto);
            ImGui.CloseCurrentPopup();
        }
        UiSharedService.AttachToolTip("按住CTRL并点击以离开同步贝" + (!string.Equals(_groupFullInfoDto.OwnerUID, _apiController.UID, StringComparison.Ordinal)
            ? string.Empty : Environment.NewLine + "警告: 该操作无法取消" + Environment.NewLine + "同步贝所有者离开同步贝将把同步贝权限移交给同步贝中的随机成员."));

        ImGui.Separator();
        ImGui.TextUnformatted("权限设置");
        var perm = _groupFullInfoDto.GroupUserPermissions;
        bool disableSounds = perm.IsDisableSounds();
        bool disableAnims = perm.IsDisableAnimations();
        bool disableVfx = perm.IsDisableVFX();

        if ((_groupFullInfoDto.GroupPermissions.IsPreferDisableAnimations() != disableAnims
            || _groupFullInfoDto.GroupPermissions.IsPreferDisableSounds() != disableSounds
            || _groupFullInfoDto.GroupPermissions.IsPreferDisableVFX() != disableVfx)
            && UiSharedService.NormalizedIconTextButton(FontAwesomeIcon.Check, "设为推荐权限", menuWidth, true))
        {
            perm.SetDisableVFX(_groupFullInfoDto.GroupPermissions.IsPreferDisableVFX());
            perm.SetDisableSounds(_groupFullInfoDto.GroupPermissions.IsPreferDisableSounds());
            perm.SetDisableAnimations(_groupFullInfoDto.GroupPermissions.IsPreferDisableAnimations());
            _ = _apiController.GroupChangeIndividualPermissionState(new(_groupFullInfoDto.Group, new(_apiController.UID), perm));
            ImGui.CloseCurrentPopup();
        }

        if (UiSharedService.NormalizedIconTextButton(disableSounds ? FontAwesomeIcon.VolumeUp : FontAwesomeIcon.VolumeOff, disableSounds ? "启用声音同步" : "禁用声音同步",
            menuWidth, true))
        {
            perm.SetDisableSounds(!disableSounds);
            _ = _apiController.GroupChangeIndividualPermissionState(new(_groupFullInfoDto.Group, new(_apiController.UID), perm));
            ImGui.CloseCurrentPopup();
        }

        if (UiSharedService.NormalizedIconTextButton(disableAnims ? FontAwesomeIcon.Running : FontAwesomeIcon.Stop, disableAnims ? "启用动画同步" : "禁用动画同步",
            menuWidth, true))
        {
            perm.SetDisableAnimations(!disableAnims);
            _ = _apiController.GroupChangeIndividualPermissionState(new(_groupFullInfoDto.Group, new(_apiController.UID), perm));
            ImGui.CloseCurrentPopup();
        }

        if (UiSharedService.NormalizedIconTextButton(disableVfx ? FontAwesomeIcon.Sun : FontAwesomeIcon.Circle, disableVfx ? "启用VFX同步" : "禁用VFX同步",
            menuWidth, true))
        {
            perm.SetDisableVFX(!disableVfx);
            _ = _apiController.GroupChangeIndividualPermissionState(new(_groupFullInfoDto.Group, new(_apiController.UID), perm));
            ImGui.CloseCurrentPopup();
        }

        if (IsModerator || IsOwner)
        {
            ImGui.Separator();
            ImGui.TextUnformatted("同步贝管理员功能");
            if (UiSharedService.NormalizedIconTextButton(FontAwesomeIcon.Cog, "打开管理员面板", menuWidth, true))
            {
                ImGui.CloseCurrentPopup();
                _mareMediator.Publish(new OpenSyncshellAdminPanel(_groupFullInfoDto));
            }
        }
    }

    protected override void DrawName(float width)
    {
        _idDisplayHandler.DrawGroupText(_id, _groupFullInfoDto, ImGui.GetCursorPosX(), () => width);
    }

    protected override float DrawRightSide(float currentRightSideX)
    {
        var spacingX = ImGui.GetStyle().ItemSpacing.X;

        FontAwesomeIcon pauseIcon = _groupFullInfoDto.GroupUserPermissions.IsPaused() ? FontAwesomeIcon.Play : FontAwesomeIcon.Pause;
        var pauseButtonSize = UiSharedService.NormalizedIconButtonSize(pauseIcon);

        var userCogButtonSize = UiSharedService.GetIconData(FontAwesomeIcon.UsersCog).NormalizedIconScale;

        var individualSoundsDisabled = _groupFullInfoDto.GroupUserPermissions.IsDisableSounds();
        var individualAnimDisabled = _groupFullInfoDto.GroupUserPermissions.IsDisableAnimations();
        var individualVFXDisabled = _groupFullInfoDto.GroupUserPermissions.IsDisableVFX();

        var infoIconPosDist = currentRightSideX - pauseButtonSize.X - spacingX;

        ImGui.SameLine(infoIconPosDist - userCogButtonSize.X);

        ImGui.AlignTextToFramePadding();

        UiSharedService.NormalizedIcon(FontAwesomeIcon.UsersCog, (_groupFullInfoDto.GroupPermissions.IsPreferDisableAnimations() != individualAnimDisabled
            || _groupFullInfoDto.GroupPermissions.IsPreferDisableSounds() != individualSoundsDisabled
            || _groupFullInfoDto.GroupPermissions.IsPreferDisableVFX() != individualVFXDisabled) ? ImGuiColors.DalamudYellow : null);
        if (ImGui.IsItemHovered())
        {
            ImGui.BeginTooltip();

            ImGui.TextUnformatted("同步贝设置");
            ImGuiHelpers.ScaledDummy(2f);

            UiSharedService.BooleanToColoredIcon(!individualSoundsDisabled, inline: false);
            ImGui.SameLine(40 * ImGuiHelpers.GlobalScale);
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted("同步声音");

            UiSharedService.BooleanToColoredIcon(!individualAnimDisabled, inline: false);
            ImGui.SameLine(40 * ImGuiHelpers.GlobalScale);
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted("同步动画");

            UiSharedService.BooleanToColoredIcon(!individualVFXDisabled, inline: false);
            ImGui.SameLine(40 * ImGuiHelpers.GlobalScale);
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted("同步VFX");

            ImGui.Separator();

            ImGuiHelpers.ScaledDummy(2f);
            ImGui.TextUnformatted("推荐设置");
            ImGuiHelpers.ScaledDummy(2f);

            UiSharedService.BooleanToColoredIcon(!_groupFullInfoDto.GroupPermissions.IsPreferDisableSounds(), inline: false);
            ImGui.SameLine(40 * ImGuiHelpers.GlobalScale);
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted("同步声音");

            UiSharedService.BooleanToColoredIcon(!_groupFullInfoDto.GroupPermissions.IsPreferDisableAnimations(), inline: false);
            ImGui.SameLine(40 * ImGuiHelpers.GlobalScale);
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted("同步动画");

            UiSharedService.BooleanToColoredIcon(!_groupFullInfoDto.GroupPermissions.IsPreferDisableVFX(), inline: false);
            ImGui.SameLine(40 * ImGuiHelpers.GlobalScale);
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted("同步VFX");

            ImGui.EndTooltip();
        }

        ImGui.SameLine();
        if (UiSharedService.NormalizedIconButton(pauseIcon))
        {
            var perm = _groupFullInfoDto.GroupUserPermissions;
            perm.SetPaused(!perm.IsPaused());
            _ = _apiController.GroupChangeIndividualPermissionState(new GroupPairUserPermissionDto(_groupFullInfoDto.Group, new(_apiController.UID), perm));
        }
        return currentRightSideX;
    }
}