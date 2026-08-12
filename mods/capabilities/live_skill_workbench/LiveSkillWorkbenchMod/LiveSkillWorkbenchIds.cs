namespace LiveSkillWorkbenchMod;

public static class LiveSkillWorkbenchIds
{
	public const string Topic = "ludots.capability.liveSkillWorkbench.session";
	public const string WebUiSessionId = "live-skill-workbench";

	public const string StageEditCommand = "lsw.stageEdit";
	public const string DiscardEditsCommand = "lsw.discardEdits";
	public const string SelectCatalogItemCommand = "lsw.selectCatalogItem";
	public const string PrecheckCommand = "lsw.precheck";
	public const string ApplyNextCastCommand = "lsw.applyNextCast";

	public const string AssetIndexPath = "LiveSkillWorkbenchMod:Assets/live-skill-workbench-app/index.html";

	/// <summary>Staged edits are not classified until precheck runs.</summary>
	public const string ApplyModeNotClassified = "NotClassified";

	/// <summary>Apply path is explicitly unavailable (pipeline not bound).</summary>
	public const string ApplyModeNotSupported = "NotSupportedYet";

	public const string ApplyModeImmediate = "ImmediateCommand";
	public const string ApplyModeNextCast = "NextCastLiveApply";
	public const string ApplyModeMapReload = "MapReloadRequired";
	public const string ApplyModeEngineRestart = "EngineRestartRequired";

	public const string ApplyStatusNotPrechecked = "尚未预检；不会应用";
	public const string ApplyStatusReadyNextCast = "预检通过：可在安全帧应用到下一次释放";
	public const string ApplyStatusMapReload = "预检结论：需要重进地图";
	public const string ApplyStatusEngineRestart = "预检结论：需要重启游戏";
	public const string ApplyStatusApplied = "已在安全帧提交到运行时定义";

	public const string DiagnosticApplyNotSupported = "LSWUI0001";
	public const string DiagnosticPrecheckNotSupported = "LSWUI0002";
	public const string DiagnosticPipelineMissing = "LSWUI0008";
	public const string DiagnosticPrecheckRequired = "LSWUI0009";
	public const string DiagnosticUndoNotSupported = "LSWUI0003";
	public const string DiagnosticRedoNotSupported = "LSWUI0004";

	/// <summary>StageEdit rejected because the field descriptor is read-only.</summary>
	public const string DiagnosticFieldReadOnly = "LSWUI0005";

	/// <summary>StageEdit rejected because the finite value is below descriptor Min.</summary>
	public const string DiagnosticValueBelowMin = "LSWUI0006";

	/// <summary>StageEdit rejected because the finite value is above descriptor Max.</summary>
	public const string DiagnosticValueAboveMax = "LSWUI0007";
}
