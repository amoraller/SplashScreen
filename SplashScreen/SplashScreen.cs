using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Resources;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Amoraller.Windows;

public sealed unsafe class SplashScreen : IDisposable
{
    private const string WindowClassPrefix = "SplashScreenWindow_";
    private static readonly TimeSpan DefaultFadeOutDuration = TimeSpan.FromMilliseconds(300);
    private static readonly WNDPROC WindowProcedureCallback = WindowProcedure;

    private readonly string _resourceName;
    private readonly HINSTANCE _instance;
    private readonly ResourceManager _resourceManager;
    private HWND _window;
    private HBITMAP _bitmap;
    private string? _windowClassName;
    private bool _classRegistered;
    private Dispatcher? _dispatcher;
    private DispatcherTimer? _fadeTimer;
    private TimeSpan _fadeDuration;
    private DateTime _fadeEnd;
    private BLENDFUNCTION _blendFunction;
    private bool _disposed;

    public SplashScreen(string resourceName)
        : this(Assembly.GetEntryAssembly() ?? throw new InvalidOperationException("The entry assembly could not be determined."), resourceName)
    {
    }

    public SplashScreen(Assembly resourceAssembly, string resourceName)
    {
        ArgumentNullException.ThrowIfNull(resourceAssembly);
        ArgumentException.ThrowIfNullOrEmpty(resourceName);

        _resourceName = resourceName.ToLowerInvariant();
        _instance = (HINSTANCE)Marshal.GetHINSTANCE(resourceAssembly.ManifestModule);
        _resourceManager = new ResourceManager($"{resourceAssembly.GetName().Name}.g", resourceAssembly);
    }

    public void Show(bool autoClose) => Show(autoClose, topMost: false);

    public void Show(bool autoClose, bool topMost)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_window.IsNull)
        {
            return;
        }

        using Stream imageStream = GetResourceStream()
            ?? throw new FileNotFoundException($"Unable to find splash screen resource '{_resourceName}'.");

        BitmapDecoder decoder = BitmapDecoder.Create(
            imageStream,
            BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad);
        if (decoder.Frames.Count == 0)
        {
            throw new InvalidDataException($"The splash screen resource '{_resourceName}' contains no image frames.");
        }

        BitmapSource image = new FormatConvertedBitmap(decoder.Frames[0], PixelFormats.Pbgra32, null, 0);
        image.Freeze();

        _dispatcher = Dispatcher.CurrentDispatcher;
        try
        {
            CreateLayeredWindow(image, topMost);
        }
        catch
        {
            DestroyResources();
            throw;
        }

        if (autoClose)
        {
            _dispatcher.BeginInvoke(
                DispatcherPriority.Loaded,
                new Action(() => Close(DefaultFadeOutDuration)));
        }
    }

    public void Close(TimeSpan fadeoutDuration)
    {
        if (_disposed)
        {
            return;
        }

        Dispatcher? dispatcher = _dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            CloseCore(fadeoutDuration);
        }
        else
        {
            dispatcher.Invoke(() => CloseCore(fadeoutDuration), DispatcherPriority.Normal);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Dispatcher? dispatcher = _dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            DisposeCore();
        }
        else
        {
            dispatcher.Invoke(DisposeCore, DispatcherPriority.Normal);
        }
    }

    private UnmanagedMemoryStream? GetResourceStream() =>
        _resourceManager.GetStream(_resourceName, CultureInfo.CurrentUICulture);

    private static LRESULT WindowProcedure(HWND window, uint message, WPARAM wParam, LPARAM lParam) =>
        PInvoke.DefWindowProc(window, message, wParam, lParam);

    private unsafe void CreateLayeredWindow(BitmapSource image, bool topMost)
    {
        int width = image.PixelWidth;
        int height = image.PixelHeight;
        if (width <= 0 || height <= 0)
        {
            throw new InvalidDataException("The splash screen image must have non-zero dimensions.");
        }

        int stride = checked(width * 4);
        byte[] pixels = new byte[checked(stride * height)];
        image.CopyPixels(pixels, stride, 0);

        BITMAPINFO bitmapInfo = new()
        {
            bmiHeader = new BITMAPINFOHEADER
            {
                biSize = (uint)sizeof(BITMAPINFOHEADER),
                biWidth = width,
                biHeight = -height,
                biPlanes = 1,
                biBitCount = 32,
                biCompression = 0,
                biSizeImage = (uint)pixels.Length
            }
        };

        void* bitmapBits = null;
        _bitmap = PInvoke.CreateDIBSection(
            default,
            &bitmapInfo,
            DIB_USAGE.DIB_RGB_COLORS,
            &bitmapBits,
            HANDLE.Null,
            0);
        if (_bitmap.IsNull || bitmapBits is null)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        Marshal.Copy(pixels, 0, (nint)bitmapBits, pixels.Length);
        _windowClassName = WindowClassPrefix + Guid.NewGuid().ToString("N");
        fixed (char* className = _windowClassName)
        {
            WNDCLASSEXW windowClass = new()
            {
                cbSize = (uint)Marshal.SizeOf<WNDCLASSEXW>(),
                style = WNDCLASS_STYLES.CS_HREDRAW | WNDCLASS_STYLES.CS_VREDRAW,
                lpszClassName = className,
                lpfnWndProc = WindowProcedureCallback,
                hInstance = _instance
            };

            if (PInvoke.RegisterClassEx(windowClass) == 0)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }
        }

        _classRegistered = true;

        int x = (PInvoke.GetSystemMetrics(SYSTEM_METRICS_INDEX.SM_CXSCREEN) - width) / 2;
        int y = (PInvoke.GetSystemMetrics(SYSTEM_METRICS_INDEX.SM_CYSCREEN) - height) / 2;
        WINDOW_EX_STYLE extendedStyle = WINDOW_EX_STYLE.WS_EX_TOOLWINDOW
            | WINDOW_EX_STYLE.WS_EX_LAYERED
            | (topMost ? WINDOW_EX_STYLE.WS_EX_TOPMOST : 0);

        fixed (char* className = _windowClassName)
        fixed (char* windowTitle = "Splash screen")
        {
            _window = PInvoke.CreateWindowEx(
                extendedStyle,
                (PCWSTR)className,
                (PCWSTR)windowTitle,
                WINDOW_STYLE.WS_POPUP | WINDOW_STYLE.WS_VISIBLE,
                x,
                y,
                width,
                height,
                HWND.Null,
                default,
                _instance,
                null);
        }
        if (_window.IsNull)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        _blendFunction = new BLENDFUNCTION
        {
            BlendOp = (byte)PInvoke.AC_SRC_OVER,
            SourceConstantAlpha = 255,
            AlphaFormat = (byte)PInvoke.AC_SRC_ALPHA
        };

        HDC screenContext = PInvoke.GetDC(HWND.Null);
        if (screenContext == (HDC)0)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        HDC memoryContext = PInvoke.CreateCompatibleDC(screenContext);
        if (memoryContext == (HDC)0)
        {
            PInvoke.ReleaseDC(HWND.Null, screenContext);
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        HGDIOBJ previousBitmap = PInvoke.SelectObject(memoryContext, _bitmap);
        if (previousBitmap.IsNull)
        {
            PInvoke.DeleteDC(memoryContext);
            PInvoke.ReleaseDC(HWND.Null, screenContext);
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        try
        {
            if (!PInvoke.UpdateLayeredWindow(
                _window,
                screenContext,
                new System.Drawing.Point(x, y),
                new SIZE(width, height),
                memoryContext,
                new System.Drawing.Point(0, 0),
                default,
                _blendFunction,
                UPDATE_LAYERED_WINDOW_FLAGS.ULW_ALPHA))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }
        }
        finally
        {
            PInvoke.SelectObject(memoryContext, previousBitmap);
            PInvoke.DeleteDC(memoryContext);
            PInvoke.ReleaseDC(HWND.Null, screenContext);
        }
    }

    private void CloseCore(TimeSpan fadeoutDuration)
    {
        if (_fadeTimer is not null || _window.IsNull)
        {
            return;
        }

        if (fadeoutDuration <= TimeSpan.Zero)
        {
            DestroyResources();
            return;
        }

        if (PInvoke.SetActiveWindow(_window).IsNull)
        {
            DestroyResources();
            return;
        }

        _fadeDuration = fadeoutDuration;
        _fadeEnd = DateTime.UtcNow + fadeoutDuration;
        _fadeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
        _fadeTimer.Tick += Fadeout_Tick;
        _fadeTimer.Start();
    }

    private void Fadeout_Tick(object? sender, EventArgs e)
    {
        DateTime now = DateTime.UtcNow;
        if (now >= _fadeEnd)
        {
            DestroyResources();
            return;
        }

        double progress = (_fadeEnd - now).TotalMilliseconds / _fadeDuration.TotalMilliseconds;
        _blendFunction.SourceConstantAlpha = (byte)(255 * progress);
        if (!PInvoke.UpdateLayeredWindow(
            _window,
            default,
            null,
            null,
            default,
            null,
            default,
            _blendFunction,
            UPDATE_LAYERED_WINDOW_FLAGS.ULW_ALPHA))
        {
            int error = Marshal.GetLastWin32Error();
            DestroyResources();
            throw new Win32Exception(error);
        }
    }

    private void DisposeCore()
    {
        DestroyResources();
        _resourceManager.ReleaseAllResources();
        _disposed = true;
    }

    private void DestroyResources()
    {
        _fadeTimer?.Stop();
        if (_fadeTimer is not null)
        {
            _fadeTimer.Tick -= Fadeout_Tick;
            _fadeTimer = null;
        }

        if (!_window.IsNull)
        {
            if (PInvoke.IsWindow(_window) && !PInvoke.DestroyWindow(_window))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            _window = HWND.Null;
        }

        if (!_bitmap.IsNull)
        {
            if (!PInvoke.DeleteObject(_bitmap))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            _bitmap = HBITMAP.Null;
        }

        if (_classRegistered && _windowClassName is not null)
        {
            fixed (char* className = _windowClassName)
            {
                if (!PInvoke.UnregisterClass((PCWSTR)className, _instance))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }
            }

            _classRegistered = false;
            _windowClassName = null;
        }
    }
}
