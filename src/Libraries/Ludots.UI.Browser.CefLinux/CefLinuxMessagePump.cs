using System;
using System.Threading;

namespace Ludots.UI.Browser.CefLinux;

internal sealed class CefLinuxMessagePump : global::Ludots.UI.Browser.CefLinux.Core.CefLinuxBrowserProcessScheduler
{
	// 外部消息泵的兜底节拍：OnScheduleMessagePumpWork 之外的 CEF 内部到期工作最多延迟一拍。
	private const int PumpTickMilliseconds = 16;

	private readonly AutoResetEvent _pulse = new(initialState: false);
	private readonly ManualResetEventSlim _initializeCompleted = new(initialState: false);
	private readonly Thread _thread;
	private readonly Action _initialize;

	private volatile bool _stopRequested;

	public CefLinuxMessagePump(Action initialize)
	{
		_initialize = initialize;
		_thread = new Thread(Run)
		{
			IsBackground = true,
			Name = "Ludots.CefLinux.MessagePump"
		};
	}

	public Exception? InitializeFailure { get; private set; }

	public void Start()
	{
		_thread.Start();
	}

	public void WaitInitializeCompleted()
	{
		_initializeCompleted.Wait();
	}

	public void ScheduleMessagePumpWork(long delayMs)
	{
		_pulse.Set();
	}

	public void RequestShutdown()
	{
		_stopRequested = true;
		_pulse.Set();
	}

	public void Join()
	{
		_thread.Join();
	}

	private void Run()
	{
		try
		{
			_initialize();
		}
		catch (Exception exception)
		{
			InitializeFailure = exception;
		}

		_initializeCompleted.Set();
		if (InitializeFailure != null)
		{
			return;
		}

		while (!_stopRequested)
		{
			global::CefNet.CefApi.DoMessageLoopWork();
			_pulse.WaitOne(PumpTickMilliseconds);
		}

		global::CefNet.CefApi.Shutdown();
	}
}
