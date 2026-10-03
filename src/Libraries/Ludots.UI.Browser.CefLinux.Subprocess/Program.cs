using System;
using System.Text;
using CefNet;
using Ludots.UI.Browser.CefLinux.Core;

// CEF renderer/gpu/utility 子进程宿主：CEF 以 --type=<process> 重新拉起本 exe，
// ExecuteProcess 在子进程内跑 CEF 消息循环直至退出；主进程误入时返回 -1。
CefMainArgs mainArgs = CefMainArgs.Create(Encoding.UTF8, Environment.GetCommandLineArgs());
return CefApi.ExecuteProcess(mainArgs, new CefLinuxProcessApp(scheduler: null), IntPtr.Zero);
