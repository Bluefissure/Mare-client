using Dalamud.Interface.Utility.Raii;
using MareSynchronos.API.Dto.CharaData;
using MareSynchronos.MareConfiguration.Models;
using MareSynchronos.Services.CharaData.Models;
using System.Text;

namespace MareSynchronos.UI;

internal sealed partial class CharaDataHubUi
{
	private static string GetAccessTypeString(AccessTypeDto dto) => dto switch
	{
		AccessTypeDto.AllPairs => "所有配对",
		AccessTypeDto.ClosePairs => "直接配对",
		AccessTypeDto.Individuals => "特定",
		AccessTypeDto.Public => "所有人"
	};

	private static string GetShareTypeString(ShareTypeDto dto) => dto switch
	{
		ShareTypeDto.Private => "仅代码",
		ShareTypeDto.Shared => "公开"
	};

	private static string GetWorldDataTooltipText(PoseEntryExtended poseEntry)
	{
		if (!poseEntry.HasWorldData) return "姿势中不包含位置数据.";
		return poseEntry.WorldDataDescriptor;
	}


	private void GposeMetaInfoAction(Action<CharaDataMetaInfoExtendedDto?> gposeActionDraw, string actionDescription, CharaDataMetaInfoExtendedDto? dto, bool hasValidGposeTarget, bool isSpawning)
	{
		StringBuilder sb = new StringBuilder();

		sb.AppendLine(actionDescription);
		bool isDisabled = false;

		void AddErrorStart(StringBuilder sb)
		{
			sb.Append(UiSharedService.TooltipSeparator);
			sb.AppendLine("无法执行:");
		}

		if (dto == null)
		{
			if (!isDisabled) AddErrorStart(sb);
			sb.AppendLine("- 未找到元数据");
			isDisabled = true;
		}
		if (!dto?.CanBeDownloaded ?? false)
		{
			if (!isDisabled) AddErrorStart(sb);
			sb.AppendLine("- 角色无法下载");
			isDisabled = true;
		}
		if (!_uiSharedService.IsInGpose)
		{
			if (!isDisabled) AddErrorStart(sb);
			sb.AppendLine("- 需要在GPose中");
			isDisabled = true;
		}
		if (!hasValidGposeTarget && !isSpawning)
		{
			if (!isDisabled) AddErrorStart(sb);
			sb.AppendLine("- GPose目标无效");
			isDisabled = true;
		}
		if (isSpawning && !_charaDataManager.BrioAvailable)
		{
			if (!isDisabled) AddErrorStart(sb);
			sb.AppendLine("- 需要安装Brio.");
			isDisabled = true;
		}

		using (ImRaii.Group())
		{
			using var dis = ImRaii.Disabled(isDisabled);
			gposeActionDraw.Invoke(dto);
		}
		if (sb.Length > 0)
		{
			UiSharedService.AttachToolTip(sb.ToString());
		}
	}

	private void GposePoseAction(Action poseActionDraw, string poseDescription, bool hasValidGposeTarget)
	{
		StringBuilder sb = new StringBuilder();

		sb.AppendLine(poseDescription);
		bool isDisabled = false;

		void AddErrorStart(StringBuilder sb)
		{
			sb.Append(UiSharedService.TooltipSeparator);
			sb.AppendLine("无法执行:");
		}

		if (!_uiSharedService.IsInGpose)
		{
			if (!isDisabled) AddErrorStart(sb);
			sb.AppendLine("- 需要在GPose中.");
			isDisabled = true;
		}
		if (!hasValidGposeTarget)
		{
			if (!isDisabled) AddErrorStart(sb);
			sb.AppendLine("- GPose目标无效.");
			isDisabled = true;
		}
		if (!_charaDataManager.BrioAvailable)
		{
			if (!isDisabled) AddErrorStart(sb);
			sb.AppendLine("- 需要安装Brio.");
			isDisabled = true;
		}

		using (ImRaii.Group())
		{
			using var dis = ImRaii.Disabled(isDisabled);
			poseActionDraw.Invoke();
		}
		if (sb.Length > 0)
		{
			UiSharedService.AttachToolTip(sb.ToString());
		}
	}

	private void SetWindowSizeConstraints(bool? inGposeTab = null)
	{
		SizeConstraints = new()
		{
			MinimumSize = new((inGposeTab ?? false) ? 400 : 1000, 500),
			MaximumSize = new((inGposeTab ?? false) ? 400 : 1000, 2000)
		};
	}

	private void UpdateFilteredFavorites()
	{
		_ = Task.Run(async () =>
		{
			if (_charaDataManager.DownloadMetaInfoTask != null)
			{
				await _charaDataManager.DownloadMetaInfoTask.ConfigureAwait(false);
			}
			Dictionary<string, (CharaDataFavorite, CharaDataMetaInfoExtendedDto?, bool)> newFiltered = [];
			foreach (var favorite in _configService.Current.FavoriteCodes)
			{
				var uid = favorite.Key.Split(":")[0];
				var note = _serverConfigurationManager.GetNoteForUid(uid) ?? string.Empty;
				bool hasMetaInfo = _charaDataManager.TryGetMetaInfo(favorite.Key, out var metaInfo);
				bool addFavorite =
					(string.IsNullOrEmpty(_filterCodeNote)
						|| (note.Contains(_filterCodeNote, StringComparison.OrdinalIgnoreCase)
						|| uid.Contains(_filterCodeNote, StringComparison.OrdinalIgnoreCase)))
					&& (string.IsNullOrEmpty(_filterDescription)
						|| (favorite.Value.CustomDescription.Contains(_filterDescription, StringComparison.OrdinalIgnoreCase)
						|| (metaInfo != null && metaInfo!.Description.Contains(_filterDescription, StringComparison.OrdinalIgnoreCase))))
					&& (!_filterPoseOnly
						|| (metaInfo != null && metaInfo!.HasPoses))
					&& (!_filterWorldOnly
						|| (metaInfo != null && metaInfo!.HasWorldData));
				if (addFavorite)
				{
					newFiltered[favorite.Key] = (favorite.Value, metaInfo, hasMetaInfo);
				}
			}

			_filteredFavorites = newFiltered;
		});
	}

	private void UpdateFilteredItems()
	{
		if (_charaDataManager.GetSharedWithYouTask == null)
		{
			_filteredDict = _charaDataManager.SharedWithYouData
				.SelectMany(k => k.Value)
				.Where(k =>
					(!_sharedWithYouDownloadableFilter || k.CanBeDownloaded)
					&& (string.IsNullOrEmpty(_sharedWithYouDescriptionFilter) || k.Description.Contains(_sharedWithYouDescriptionFilter, StringComparison.OrdinalIgnoreCase)))
				.GroupBy(k => k.Uploader)
				.ToDictionary(k =>
				{
					var note = _serverConfigurationManager.GetNoteForUid(k.Key.UID);
					if (note == null) return k.Key.AliasOrUID;
					return $"{note} ({k.Key.AliasOrUID})";
				}, k => k.ToList(), StringComparer.OrdinalIgnoreCase)
				.Where(k => (string.IsNullOrEmpty(_sharedWithYouOwnerFilter) || k.Key.Contains(_sharedWithYouOwnerFilter, StringComparison.OrdinalIgnoreCase)))
				.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase).ToDictionary();
		}
	}
}