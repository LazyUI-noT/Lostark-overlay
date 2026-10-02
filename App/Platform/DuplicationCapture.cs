using System.Diagnostics;
using GaugeOverlay.Core.Domain;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace GaugeOverlay.App.Platform;

/// <summary>DXGI Desktop Duplication of one monitor. No border on any Windows version. Frames arrive only
/// when the screen changes; a static screen times out, which is normal.</summary>
public sealed class DuplicationCapture : ICaptureSource
{
    private readonly IntPtr _monitor;
    private ID3D11Device? _device;
    private IDXGIOutputDuplication? _dup;
    private RegionCopier? _copier;
    private PixelRect _bounds;
    private long _lastOpen;
    private static readonly TimeSpan ReopenEvery = TimeSpan.FromMilliseconds(500);

    /// <summary>Duplicates the monitor with this HMONITOR (see <see cref="GameWindow.MonitorOf"/>).</summary>
    public DuplicationCapture(IntPtr monitor)
    {
        _monitor = monitor;
        if (!Open()) throw new InvalidOperationException("Desktop Duplication is not available for this monitor.");
    }

    public bool TryCapture(IReadOnlyList<PixelRect> regions, IReadOnlyList<Image> into, TimeSpan timeout, out long timestamp)
    {
        timestamp = 0;
        // Unavailable (lock screen, UAC, another duplicator): retry at most every ReopenEvery, not on every call.
        if (_dup is null && (Stopwatch.GetElapsedTime(_lastOpen) < ReopenEvery || !Open())) return false;

        long deadline = Stopwatch.GetTimestamp() + (long)(timeout.TotalSeconds * Stopwatch.Frequency);
        OutduplFrameInfo info;
        IDXGIResource resource;
        while (true)
        {
            uint wait = (uint)Math.Max(0, Stopwatch.GetElapsedTime(Stopwatch.GetTimestamp(), deadline).TotalMilliseconds);
            var hr = _dup!.AcquireNextFrame(wait, out info, out resource);
            if (hr == Vortice.DXGI.ResultCode.WaitTimeout) return false;
            if (hr == Vortice.DXGI.ResultCode.AccessLost) { Close(); return false; }   // mode change, UAC, lock screen
            hr.CheckError();
            if (info.LastPresentTime != 0) break;
            // LastPresentTime 0 = only the mouse pointer moved; wait for the next desktop image.
            resource.Dispose();
            _dup.ReleaseFrame();
        }

        bool copied;
        try
        {
            using var desktop = resource.QueryInterface<ID3D11Texture2D>();
            copied = _copier!.Copy(desktop, _bounds, regions);
        }
        finally
        {
            resource.Dispose();
            _dup.ReleaseFrame();
        }
        if (!copied) return false;

        _copier.ReadBack(into);
        timestamp = info.LastPresentTime;   // QPC ticks = Stopwatch timestamp
        return true;
    }

    // ponytail: ignores display rotation (portrait monitors); add a rotated copy if anyone games in portrait.
    private bool Open()
    {
        _lastOpen = Stopwatch.GetTimestamp();
        using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();
        for (uint a = 0; factory.EnumAdapters1(a, out var adapter).Success; a++)
        using (adapter)
        {
            for (uint o = 0; adapter.EnumOutputs(o, out var output).Success; o++)
            using (output)
            {
                var desc = output.Description;
                if (desc.Monitor != _monitor) continue;
                try
                {
                    D3D11.D3D11CreateDevice(adapter, DriverType.Unknown, DeviceCreationFlags.BgraSupport,
                        [FeatureLevel.Level_11_0, FeatureLevel.Level_10_1, FeatureLevel.Level_10_0], out ID3D11Device device, out ID3D11DeviceContext context).CheckError();
                    _device = device;
                    _copier = new RegionCopier(device, context);
                    using var output1 = output.QueryInterface<IDXGIOutput1>();
                    _dup = output1.DuplicateOutput(_device);
                }
                catch (SharpGen.Runtime.SharpGenException)
                {
                    // E_ACCESSDENIED on the secure desktop (UAC, lock screen); retried on the next call.
                    Close();
                    return false;
                }
                var c = desc.DesktopCoordinates;
                _bounds = new(c.Left, c.Top, c.Right - c.Left, c.Bottom - c.Top);
                return true;
            }
        }
        return false;
    }

    private void Close()
    {
        _dup?.Dispose(); _dup = null;
        _copier?.Dispose(); _copier = null;
        _device?.Dispose(); _device = null;
    }

    public void Dispose() => Close();
}

/// <summary>Copies screen regions out of a GPU texture into small staging textures, then into
/// <see cref="Image"/> buffers. Shared by both capture sources.</summary>
internal sealed class RegionCopier(ID3D11Device device, ID3D11DeviceContext context) : IDisposable
{
    private readonly List<ID3D11Texture2D?> _staging = [];

    /// <summary>GPU copy of each region (screen pixels) from <paramref name="source"/>, whose top-left pixel
    /// sits at <paramref name="sourceBounds"/>.X/Y on screen. Copies nothing and returns false if any region isn't
    /// entirely inside the source (window moved to another monitor): stale pixels must never pass for a new frame.</summary>
    public bool Copy(ID3D11Texture2D source, PixelRect sourceBounds, IReadOnlyList<PixelRect> regions)
    {
        foreach (var r in regions)
            if (r.X < sourceBounds.X || r.Y < sourceBounds.Y || r.Right > sourceBounds.Right || r.Bottom > sourceBounds.Bottom)
                return false;
        for (int i = 0; i < regions.Count; i++)
        {
            var r = regions[i];
            var tex = Staging(i, r.Width, r.Height);
            var box = new Box(r.X - sourceBounds.X, r.Y - sourceBounds.Y, 0, r.Right - sourceBounds.X, r.Bottom - sourceBounds.Y, 1);
            context.CopySubresourceRegion(tex, 0, 0, 0, 0, source, 0, box);
        }
        return true;
    }

    /// <summary>CPU read of the regions copied by the last <see cref="Copy"/>, same order.</summary>
    public unsafe void ReadBack(IReadOnlyList<Image> into)
    {
        for (int i = 0; i < into.Count; i++)
        {
            var tex = _staging[i]!;
            var img = into[i];
            var map = context.Map(tex, 0, MapMode.Read);
            try
            {
                int rowBytes = img.Width * 4;
                for (int y = 0; y < img.Height; y++)
                    new ReadOnlySpan<byte>((byte*)map.DataPointer + y * map.RowPitch, rowBytes)
                        .CopyTo(img.Bgra.AsSpan(y * img.Stride, rowBytes));
            }
            finally { context.Unmap(tex, 0); }
        }
    }

    private ID3D11Texture2D Staging(int i, int w, int h)
    {
        while (_staging.Count <= i) _staging.Add(null);
        var t = _staging[i];
        if (t is not null && t.Description.Width == w && t.Description.Height == h) return t;
        t?.Dispose();
        t = device.CreateTexture2D(new Texture2DDescription(Format.B8G8R8A8_UNorm, (uint)w, (uint)h, 1, 1,
            BindFlags.None, ResourceUsage.Staging, CpuAccessFlags.Read));
        _staging[i] = t;
        return t;
    }

    public void Dispose()
    {
        foreach (var t in _staging) t?.Dispose();
        _staging.Clear();
        context.Dispose();
    }
}
