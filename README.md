# WPF Splash Screen

A standalone splash-screen component for WPF applications on Windows. It displays an image resource in a layered Win32 window and preserves per-pixel transparency while fading out.

This project was created to address bugs in the built-in WPF `SplashScreen`, including transparent image regions turning black during fade-out ([dotnet/wpf#11321](https://github.com/dotnet/wpf/issues/11321)).

## Requirements

- Windows
- .NET 10
- WPF

## Installation

Install the package from NuGet:

```powershell
dotnet add package Amoraller.Windows.SplashScreen
```

The native Win32 declarations are generated at build time with [CsWin32](https://github.com/microsoft/CsWin32).

## Usage

Add the splash image to your WPF application with **Build Action** set to **Resource**. Then show it during application startup:

```csharp
using Amoraller.Windows;

var splashScreen = new SplashScreen("Splash.png");
splashScreen.Show(autoClose: true);
```

With `autoClose: true`, the splash screen closes with a 300 ms fade-out. To control its lifetime yourself:

```csharp
var splashScreen = new SplashScreen("Splash.png");
splashScreen.Show(autoClose: false);

// When the application is ready:
var fadeOutDuration = TimeSpan.FromMilliseconds(300);
splashScreen.Close(fadeOutDuration);

// Dispose after the fade-out has completed. Calling Dispose immediately
// stops the fade and closes the splash screen at once.
await Task.Delay(fadeOutDuration);
splashScreen.Dispose();
```

To keep the splash screen above other windows, pass `topMost: true`:

```csharp
splashScreen.Show(autoClose: true, topMost: true);
```

The image resource is resolved from the entry assembly by default. An overload accepts the resource assembly explicitly:

```csharp
var splashScreen = new SplashScreen(typeof(App).Assembly, "Splash.png");
```

## Publishing

The GitHub Actions workflow publishes the package when a GitHub Release is published or when the workflow is started manually. It uses NuGet Trusted Publishing (GitHub OIDC), so no long-lived NuGet API key needs to be stored in GitHub secrets.

To enable publishing, add a Trusted Publishing policy on nuget.org for:

- Repository owner: `amoraller`
- Repository: `SplashScreen`
- Workflow file: `publish-nuget.yml` (filename only; do not include `.github/workflows/`)
- Environment: leave empty
- Package scope: `Amoraller.Windows.SplashScreen`, allowing new packages and new versions

For a GitHub Release, use a tag such as `v1.0.0`. For a manual run, provide the package version without the `v` prefix.

## License

This project is licensed under the MIT License. See [LICENSE](LICENSE) for details. The implementation is adapted from the [WPF SplashScreen](https://github.com/dotnet/wpf/blob/main/src/Microsoft.DotNet.Wpf/src/WindowsBase/System/Windows/SplashScreen.cs), Copyright .NET Foundation, also licensed under MIT.
