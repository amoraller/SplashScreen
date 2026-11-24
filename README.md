# WPF Splash Screen

A standalone splash-screen component for WPF applications on Windows. It displays an image resource in a layered Win32 window and preserves per-pixel transparency while fading out.

This project was created to address bugs in the built-in WPF `SplashScreen`, including transparent image regions turning black during fade-out ([dotnet/wpf#11321](https://github.com/dotnet/wpf/issues/11321)).

## Requirements

- Windows
- .NET 10
- WPF

## Installation

Build the `SplashScreen` project and reference its assembly from your WPF application. The native Win32 declarations are generated at build time with [CsWin32](https://github.com/microsoft/CsWin32).

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

## License

This project is licensed under the MIT License. See [LICENSE](LICENSE) for details. The implementation is adapted from the [WPF SplashScreen](https://github.com/dotnet/wpf/blob/main/src/Microsoft.DotNet.Wpf/src/WindowsBase/System/Windows/SplashScreen.cs), Copyright .NET Foundation, also licensed under MIT.
