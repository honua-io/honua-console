using Honua.Console.Native.Core.DependencyInjection;
using Honua.Console.Native.Services;
using Honua.Console.Shell.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Honua.Console.Native;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
			});

		builder.Services.AddMauiBlazorWebView();
		builder.Services.AddHonuaConsoleShell();
		builder.Services.AddHonuaConsoleNativeCore();
		builder.Services.AddSingleton<NativeSecureStorage>();
		builder.Services.AddSingleton<Honua.Console.Native.Core.Storage.IConsoleProfileStorage>(
			sp => sp.GetRequiredService<NativeSecureStorage>());
		builder.Services.AddSingleton<Honua.Console.Native.Core.Security.INativeSecretStore>(
			sp => sp.GetRequiredService<NativeSecureStorage>());

#if DEBUG
		builder.Services.AddBlazorWebViewDeveloperTools();
		builder.Logging.AddDebug();
#endif

		return builder.Build();
	}
}
