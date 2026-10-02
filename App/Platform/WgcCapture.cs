using System.Diagnostics;
using System.Runtime.InteropServices;
using GaugeOverlay.Core.Domain;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Windows.Foundation.Metadata;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Security.Authorization.AppCapabilityAccess;
using WinRT;

namespace GaugeOverlay.App.Platform;

/// <summary>Windows.Graphics.Capture of the game window, only where the yellow border can be turned off
/// (Windows 11: borderless access granted and IsBorderRequired = false). Create with <see cref="TryCreate"/>.</summary>
public sealed partial class WgcCapture : ICaptureSource
{
    private readonly IntPtr _hwnd;
    private readonly ID3D11Device _device;
    private readonly RegionCopier _copier;
    private readonly IDirect3DDevice _winrtDevice;
    private readonly GraphicsCaptureItem _item;
    private readonly Direct3D11CaptureFramePool _pool;
    private readonly GraphicsCaptureSession _session;
    private readonly AutoResetEvent _arrived = new(false);
    private Windows.Graphics.SizeInt32 _size;

    /// <summary>A borderless capture of <paramref name="hwnd"/>, or null when the border can't be disabled
    /// (always on Windows 10) or the window can't be captured.</summary>
    public static WgcCapture? TryCreate(IntPtr hwnd)
    {
        if (hwnd == 0 || !OperatingSystem.IsWindowsVersionAtLeast(10, 0, 20348) || !BorderlessGranted()) return null;
        try { return new WgcCapture(hwnd); }
        catch (Exception e) when (e is COMException or SharpGen.Runtime.SharpGenException or InvalidOperationException) { return null; }
    }

    /// <summary>True only if a capture started now would have no border.</summary>
    [System.Runtime.Versioning.SupportedOSPlatform("windows10.0.20348.0")]
    private static bool BorderlessGranted()
    {
        if (!ApiInformation.IsPropertyPresent("Windows.Graphics.Capture.GraphicsCaptureSession", "IsBorderRequired")
            || !GraphicsCaptureSession.IsSupported())
            return false;
        var status = GraphicsCaptureAccess.RequestAccessAsync(GraphicsCaptureAccessKind.Borderless).AsTask().GetAwaiter().GetResult();
        return status == AppCapabilityAccessStatus.Allowed;
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows10.0.20348.0")]
    private WgcCapture(IntPtr hwnd)
    {
        _hwnd = hwnd;
        D3D11.D3D11CreateDevice(null, DriverType.Hardware, DeviceCreationFlags.BgraSupport, [FeatureLevel.Level_11_0, FeatureLevel.Level_10_1, FeatureLevel.Level_10_0], out ID3D11Device device, out ID3D11DeviceContext context).CheckError();
        _device = device;
        _copier = new RegionCopier(_device, context);
        _winrtDevice = CreateWinRtDevice(_device);
        _item = CreateItem(hwnd);
        _size = _item.Size;
        _pool = Direct3D11CaptureFramePool.CreateFreeThreaded(_winrtDevice, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, _size);
        _pool.FrameArrived += OnFrameArrived;
        _session = _pool.CreateCaptureSession(_item);
        _session.IsBorderRequired = false;
        if (_session.IsBorderRequired) throw new InvalidOperationException("Border can't be disabled.");
        _session.IsCursorCaptureEnabled = false;
        _session.StartCapture();
    }

    public bool TryCapture(IReadOnlyList<PixelRect> regions, IReadOnlyList<Image> into, TimeSpan timeout, out long timestamp)
    {
        timestamp = 0;
        if (!_arrived.WaitOne(timeout)) return false;
        Direct3D11CaptureFrame? frame = null;
        while (_pool.TryGetNextFrame() is { } next) { frame?.Dispose(); frame = next; }
        if (frame is null) return false;
        using (frame)
        {
            if (frame.ContentSize.Width != _size.Width || frame.ContentSize.Height != _size.Height)
            {
                _size = frame.ContentSize;   // window resized: next frames come at the new size
                _pool.Recreate(_winrtDevice, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, _size);
                return false;
            }
            // The window's texture starts at its visible frame (extended frame bounds), not its client area.
            if (DwmGetWindowAttribute(_hwnd, DWMWA_EXTENDED_FRAME_BOUNDS, out var fb, 16) != 0) return false;
            using var tex = TextureOf(frame.Surface);
            if (!_copier.Copy(tex, new PixelRect(fb.Left, fb.Top, _size.Width, _size.Height), regions)) return false;
            timestamp = (long)((Int128)frame.SystemRelativeTime.Ticks * Stopwatch.Frequency / TimeSpan.TicksPerSecond);
        }
        _copier.ReadBack(into);
        return true;
    }

    // Free-threaded: may run on any thread, including once more after Dispose has started.
    private void OnFrameArrived(Direct3D11CaptureFramePool sender, object args)
    {
        try { _arrived.Set(); }
        catch (ObjectDisposedException) { }
    }

    public void Dispose()
    {
        _pool.FrameArrived -= OnFrameArrived;
        _session.Dispose();
        _pool.Dispose();
        _winrtDevice.Dispose();
        _copier.Dispose();
        _device.Dispose();
        _arrived.Dispose();
    }

    private static unsafe IDirect3DDevice CreateWinRtDevice(ID3D11Device device)
    {
        using var dxgi = device.QueryInterface<IDXGIDevice>();
        Marshal.ThrowExceptionForHR(CreateDirect3D11DeviceFromDXGIDevice(dxgi.NativePointer, out var inspectable));
        try { return MarshalInterface<IDirect3DDevice>.FromAbi(inspectable); }
        finally { Marshal.Release(inspectable); }
    }

    private static unsafe GraphicsCaptureItem CreateItem(IntPtr hwnd)
    {
        var factory = ActivationFactory.Get("Windows.Graphics.Capture.GraphicsCaptureItem");
        Marshal.ThrowExceptionForHR(Marshal.QueryInterface(factory.ThisPtr, in IID_IGraphicsCaptureItemInterop, out var interop));
        try
        {
            IntPtr item;
            Guid iid = IID_IGraphicsCaptureItem;
            // IGraphicsCaptureItemInterop::CreateForWindow is the first method after IUnknown.
            var createForWindow = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, Guid*, IntPtr*, int>)(*(*(void***)interop + 3));
            Marshal.ThrowExceptionForHR(createForWindow(interop, hwnd, &iid, &item));
            try { return GraphicsCaptureItem.FromAbi(item); }
            finally { Marshal.Release(item); }
        }
        finally { Marshal.Release(interop); }
    }

    private static unsafe ID3D11Texture2D TextureOf(IDirect3DSurface surface)
    {
        var unknown = MarshalInterface<IDirect3DSurface>.FromManaged(surface);
        try
        {
            Marshal.ThrowExceptionForHR(Marshal.QueryInterface(unknown, in IID_IDirect3DDxgiInterfaceAccess, out var access));
            try
            {
                IntPtr tex;
                Guid iid = typeof(ID3D11Texture2D).GUID;
                // IDirect3DDxgiInterfaceAccess::GetInterface is the first method after IUnknown.
                var getInterface = (delegate* unmanaged[Stdcall]<IntPtr, Guid*, IntPtr*, int>)(*(*(void***)access + 3));
                Marshal.ThrowExceptionForHR(getInterface(access, &iid, &tex));
                return new ID3D11Texture2D(tex);
            }
            finally { Marshal.Release(access); }
        }
        finally { Marshal.Release(unknown); }
    }

    private static readonly Guid IID_IGraphicsCaptureItemInterop = new("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356");
    private static readonly Guid IID_IGraphicsCaptureItem = new("79C3F95B-31F7-4EC2-A464-632EF5D30760");
    private static readonly Guid IID_IDirect3DDxgiInterfaceAccess = new("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1");
    private const uint DWMWA_EXTENDED_FRAME_BOUNDS = 9;

    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }

    [LibraryImport("d3d11.dll")]
    private static partial int CreateDirect3D11DeviceFromDXGIDevice(IntPtr dxgiDevice, out IntPtr inspectable);
    [LibraryImport("dwmapi.dll")]
    private static partial int DwmGetWindowAttribute(IntPtr hwnd, uint attribute, out RECT value, int size);
}
