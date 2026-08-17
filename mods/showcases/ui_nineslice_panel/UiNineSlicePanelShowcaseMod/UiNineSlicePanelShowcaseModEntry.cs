using System;
using System.Threading.Tasks;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using Ludots.UI.Runtime;
using Ludots.UI.Surface;
using UiShowcaseCoreMod.Showcase;

namespace UiNineSlicePanelShowcaseMod;

public sealed class UiNineSlicePanelShowcaseModEntry : IMod
{
	private IUiSurfaceHost? _host;
	private UiSurfaceLeaseHandle _lease;

	public void OnLoad(IModContext context)
	{
		context.Log("[UiNineSlicePanelShowcaseMod] Loaded.");
		context.OnEvent(GameEvents.GameStart, OnGameStartAsync);
	}

	public void OnUnload()
	{
		if (_lease.IsValid && _host != null)
		{
			_host.Release(_lease);
		}
		_host = null;
	}

	private Task OnGameStartAsync(ScriptContext context)
	{
		IUiSurfaceHost host = context.Get(CoreServiceKeys.UiSurfaceHost) as IUiSurfaceHost
			?? throw new InvalidOperationException("UiSurfaceHost service is missing from ScriptContext.");
		IUiTextMeasurer textMeasurer = (IUiTextMeasurer)context.Get(CoreServiceKeys.UiTextMeasurer);
		IUiImageSizeProvider imageSizeProvider = (IUiImageSizeProvider)context.Get(CoreServiceKeys.UiImageSizeProvider);

		_host = host;
		_lease = host.Acquire(new UiSurfaceLeaseRequest(
			"UiShowcase.NineSlicePanel",
			UiSurfaceSegment.Main,
			priority: 10,
			exclusive: true));

		host.Publish(
			_lease,
			UiShowcaseFactory.CreateNineSlicePanelContribution(
				textMeasurer,
				imageSizeProvider,
				() => host.Invalidate(_lease)));
		return Task.CompletedTask;
	}
}
