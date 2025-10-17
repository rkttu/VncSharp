using System.Drawing;
using System.Drawing.Imaging;
using RfbTest.Contracts;
using RfbTest.Diagnostics;
using SharpDX;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using Device = SharpDX.Direct3D11.Device;
using MapFlags = SharpDX.Direct3D11.MapFlags;

namespace RfbTest.ScreenCaptures;

/// <summary>
/// Windows Desktop Duplication API를 사용한 화면 캡처
/// </summary>
public class WindowsScreenCapture : IScreenCapture
{
    private readonly Device _device;
    private readonly OutputDuplication _outputDuplication;
    private readonly Texture2D _screenTexture;
    private readonly int _width;
    private readonly int _height;
    private bool _disposed;
    private byte[]? _lastCapturedFrame; // 마지막으로 캡처된 프레임 저장
    private int _newFrameCount = 0; // 새 프레임 카운터
    private int _cachedFrameCount = 0; // 캐시된 프레임 카운터

    /// <summary>
    /// 캡처된 화면의 너비
    /// </summary>
    public int Width => _width;

    /// <summary>
    /// 캡처된 화면의 높이
    /// </summary>
    public int Height => _height;

    public WindowsScreenCapture(int outputIndex = 0)
    {
        try
        {
            // Direct3D 11 디바이스 생성
            _device = new Device(SharpDX.Direct3D.DriverType.Hardware);

            // 어댑터와 출력 가져오기
            using var adapter = _device.QueryInterface<SharpDX.DXGI.Device>().Adapter;
            var output = adapter.GetOutput(outputIndex);
            var output1 = output.QueryInterface<Output1>();

            // Desktop Duplication 초기화
            _outputDuplication = output1.DuplicateOutput(_device);

            // 화면 크기 가져오기
            var bounds = output.Description.DesktopBounds;
            _width = bounds.Right - bounds.Left;
            _height = bounds.Bottom - bounds.Top;

            // 스테이징 텍스처 생성 (CPU로 읽기 위한 텍스처)
            var textureDesc = new Texture2DDescription
            {
                CpuAccessFlags = CpuAccessFlags.Read,
                BindFlags = BindFlags.None,
                Format = Format.B8G8R8A8_UNorm,
                Width = _width,
                Height = _height,
                OptionFlags = ResourceOptionFlags.None,
                MipLevels = 1,
                ArraySize = 1,
                SampleDescription = { Count = 1, Quality = 0 },
                Usage = ResourceUsage.Staging
            };

            _screenTexture = new Texture2D(_device, textureDesc);

            RfbLogger.Log($"[WindowsScreenCapture] Initialized: {_width}x{_height}");

            output1.Dispose();
            output.Dispose();
        }
        catch (Exception ex)
        {
            RfbLogger.LogError("[WindowsScreenCapture] Initialization failed", ex);
            Dispose();
            throw;
        }
    }

    /// <summary>
    /// 현재 화면을 캡처하여 byte[] 배열로 반환 (BGRA 포맷)
    /// 변경이 없으면 마지막 프레임 반환
    /// </summary>
    public byte[]? CaptureFrame()
    {
        if (_disposed)
            return null;

        try
        {
            SharpDX.DXGI.Resource? screenResource = null;
            OutputDuplicateFrameInformation frameInfo;
            bool newFrameAcquired = false;

            try
            {
                // 프레임 획득 (타임아웃 16ms - 약 60 FPS)
                // 타임아웃을 0이 아닌 값으로 설정하면 화면 변경을 기다림
                var result = _outputDuplication.TryAcquireNextFrame(16, out frameInfo, out screenResource);

                if (result.Success && screenResource != null)
                {
                    newFrameAcquired = true;
                    _newFrameCount++;
                    
                    if (_newFrameCount % 30 == 0) // 30프레임마다 로그
                    {
                        RfbLogger.Log($"[WindowsScreenCapture] ✓ NEW frames captured: {_newFrameCount}, AccumulatedFrames={frameInfo.AccumulatedFrames}");
                    }
                    
                    // 텍스처로 변환
                    using var screenTexture2D = screenResource.QueryInterface<Texture2D>();

                    // CPU 액세스 가능한 스테이징 텍스처로 복사
                    _device.ImmediateContext.CopyResource(screenTexture2D, _screenTexture);

                    // 텍스처 데이터를 byte[]로 복사
                    var dataBox = _device.ImmediateContext.MapSubresource(_screenTexture, 0, MapMode.Read, MapFlags.None);

                    try
                    {
                        int stride = dataBox.RowPitch;
                        int bufferSize = _width * _height * 4; // BGRA = 4 bytes per pixel
                        byte[] buffer = new byte[bufferSize];

                        // 행 단위로 복사 (stride가 width와 다를 수 있음)
                        for (int y = 0; y < _height; y++)
                        {
                            IntPtr sourcePtr = dataBox.DataPointer + y * stride;
                            int destOffset = y * _width * 4;
                            System.Runtime.InteropServices.Marshal.Copy(sourcePtr, buffer, destOffset, _width * 4);
                        }

                        // 마지막 프레임 저장
                        _lastCapturedFrame = buffer;
                        return buffer;
                    }
                    finally
                    {
                        _device.ImmediateContext.UnmapSubresource(_screenTexture, 0);
                    }
                }
                else
                {
                    // 새 프레임이 없으면 마지막 프레임 반환
                    _cachedFrameCount++;
                    
                    if (_cachedFrameCount % 100 == 0) // 100프레임마다 로그
                    {
                        RfbLogger.Log($"[WindowsScreenCapture] ⚠ Using CACHED frame (no changes). New: {_newFrameCount}, Cached: {_cachedFrameCount}");
                    }
                    
                    return _lastCapturedFrame;
                }
            }
            finally
            {
                // 리소스 해제
                screenResource?.Dispose();

                if (newFrameAcquired)
                {
                    try
                    {
                        _outputDuplication.ReleaseFrame();
                    }
                    catch
                    {
                        // ReleaseFrame 실패는 무시
                    }
                }
            }
        }
        catch (SharpDXException ex) when (ex.ResultCode.Code == SharpDX.DXGI.ResultCode.WaitTimeout.Result.Code)
        {
            // 타임아웃 - 마지막 프레임 반환
            return _lastCapturedFrame;
        }
        catch (SharpDXException ex) when (ex.ResultCode.Code == SharpDX.DXGI.ResultCode.AccessLost.Result.Code)
        {
            // 액세스 손실 (해상도 변경, 모니터 변경 등)
            RfbLogger.Log("[WindowsScreenCapture] Access lost - display configuration changed");
            return _lastCapturedFrame;
        }
        catch (Exception ex)
        {
            RfbLogger.LogError("[WindowsScreenCapture] Error capturing frame", ex);
            return _lastCapturedFrame;
        }
    }

    /// <summary>
    /// 화면을 캡처하여 Bitmap으로 반환
    /// </summary>
    public Bitmap? CaptureBitmap()
    {
        var buffer = CaptureFrame();
        if (buffer == null)
            return null;

        var bitmap = new Bitmap(_width, _height, PixelFormat.Format32bppArgb);
        var bitmapData = bitmap.LockBits(
            new Rectangle(0, 0, _width, _height),
            ImageLockMode.WriteOnly,
            PixelFormat.Format32bppArgb);

        try
        {
            System.Runtime.InteropServices.Marshal.Copy(buffer, 0, bitmapData.Scan0, buffer.Length);
            return bitmap;
        }
        finally
        {
            bitmap.UnlockBits(bitmapData);
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        RfbLogger.Log($"[WindowsScreenCapture] Stats - New frames: {_newFrameCount}, Cached frames: {_cachedFrameCount}");

        _screenTexture?.Dispose();
        _outputDuplication?.Dispose();
        _device?.Dispose();

        RfbLogger.Log("[WindowsScreenCapture] Disposed");
    }
}
