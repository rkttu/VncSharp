using RfbTest.Diagnostics;
using System.Runtime.InteropServices;

namespace RfbTest.Protocol;

/// <summary>
/// 프레임버퍼 변경 감지 및 Dirty Rectangle 관리
/// </summary>
public class FrameBufferDiff
{
    private byte[]? _previousBuffer;
    private int _width;
    private int _height;
    private readonly int _tileSize;
    private readonly object _lock = new object();

    /// <summary>
    /// 변경된 영역 (Dirty Rectangle)
    /// </summary>
    public class DirtyRegion
    {
        public ushort X { get; set; }
        public ushort Y { get; set; }
        public ushort Width { get; set; }
        public ushort Height { get; set; }

        public bool IsEmpty => Width == 0 || Height == 0;

        public override string ToString() => $"[{X},{Y}] {Width}x{Height}";
    }

    public FrameBufferDiff(int width, int height, int tileSize = 64)
    {
        _width = width;
        _height = height;
        _tileSize = tileSize;
    }

    /// <summary>
    /// 프레임버퍼 크기 변경
    /// </summary>
    public void Resize(int newWidth, int newHeight)
    {
        lock (_lock)
        {
            _width = newWidth;
            _height = newHeight;
            _previousBuffer = null; // 크기 변경 시 이전 버퍼 리셋
        }
    }

    /// <summary>
    /// 변경된 영역 감지
    /// </summary>
    public DirtyRegion DetectChanges(byte[] currentBuffer)
    {
        lock (_lock)
        {
            // 첫 번째 프레임이면 전체 영역 반환
            if (_previousBuffer == null || _previousBuffer.Length != currentBuffer.Length)
            {
                RfbLogger.Log($"[FrameBufferDiff] First frame or size change, returning full region: {_width}x{_height}");
                _previousBuffer = new byte[currentBuffer.Length];
                Array.Copy(currentBuffer, _previousBuffer, currentBuffer.Length);
                return new DirtyRegion
                {
                    X = 0,
                    Y = 0,
                    Width = (ushort)_width,
                    Height = (ushort)_height
                };
            }

            // 변경된 타일 찾기
            var dirtyTiles = FindDirtyTiles(currentBuffer);

            if (dirtyTiles.Count == 0)
            {
                // 변경 없음
                RfbLogger.Log($"[FrameBufferDiff] No changes detected");
                return new DirtyRegion { X = 0, Y = 0, Width = 0, Height = 0 };
            }

            // 변경된 타일들을 포함하는 최소 사각형 계산
            var region = CalculateBoundingBox(dirtyTiles);
            
            RfbLogger.Log($"[FrameBufferDiff] Detected {dirtyTiles.Count} dirty tiles, bounding box: {region}");

            // 이전 버퍼 업데이트
            Array.Copy(currentBuffer, _previousBuffer, currentBuffer.Length);

            return region;
        }
    }

    /// <summary>
    /// 변경된 타일 찾기 (타일 기반 비교)
    /// </summary>
    private List<(int tileX, int tileY)> FindDirtyTiles(byte[] currentBuffer)
    {
        var dirtyTiles = new List<(int, int)>();
        int bytesPerPixel = 4; // 32bpp

        int tilesX = (_width + _tileSize - 1) / _tileSize;
        int tilesY = (_height + _tileSize - 1) / _tileSize;

        for (int ty = 0; ty < tilesY; ty++)
        {
            for (int tx = 0; tx < tilesX; tx++)
            {
                if (IsTileDirty(currentBuffer, tx, ty, bytesPerPixel))
                {
                    dirtyTiles.Add((tx, ty));
                }
            }
        }

        return dirtyTiles;
    }

    /// <summary>
    /// 특정 타일이 변경되었는지 확인
    /// </summary>
    private bool IsTileDirty(byte[] currentBuffer, int tileX, int tileY, int bytesPerPixel)
    {
        int startX = tileX * _tileSize;
        int startY = tileY * _tileSize;
        int endX = Math.Min(startX + _tileSize, _width);
        int endY = Math.Min(startY + _tileSize, _height);

        for (int y = startY; y < endY; y++)
        {
            int rowOffset = y * _width * bytesPerPixel;
            int pixelOffset = rowOffset + startX * bytesPerPixel;
            int rowBytes = (endX - startX) * bytesPerPixel;

            // 한 행씩 비교
            if (!AreEqual(_previousBuffer!, currentBuffer, pixelOffset, rowBytes))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 바이트 배열 영역 비교 (고속)
    /// </summary>
    private static bool AreEqual(byte[] a, byte[] b, int offset, int length)
    {
        for (int i = 0; i < length; i++)
        {
            if (a[offset + i] != b[offset + i])
                return false;
        }
        return true;
    }

    /// <summary>
    /// 변경된 타일들을 포함하는 최소 사각형 계산
    /// </summary>
    private DirtyRegion CalculateBoundingBox(List<(int tileX, int tileY)> dirtyTiles)
    {
        int minTileX = dirtyTiles.Min(t => t.tileX);
        int minTileY = dirtyTiles.Min(t => t.tileY);
        int maxTileX = dirtyTiles.Max(t => t.tileX);
        int maxTileY = dirtyTiles.Max(t => t.tileY);

        int x = minTileX * _tileSize;
        int y = minTileY * _tileSize;
        int right = Math.Min((maxTileX + 1) * _tileSize, _width);
        int bottom = Math.Min((maxTileY + 1) * _tileSize, _height);

        return new DirtyRegion
        {
            X = (ushort)x,
            Y = (ushort)y,
            Width = (ushort)(right - x),
            Height = (ushort)(bottom - y)
        };
    }

    /// <summary>
    /// 전체 화면을 dirty로 강제 설정
    /// </summary>
    public void ForceFullUpdate()
    {
        lock (_lock)
        {
            _previousBuffer = null;
        }
    }

    /// <summary>
    /// 특정 영역을 추출
    /// </summary>
    public static byte[] ExtractRegion(byte[] fullBuffer, int bufferWidth, int bufferHeight, 
        int x, int y, int width, int height)
    {
        int bytesPerPixel = 4;
        byte[] regionBuffer = new byte[width * height * bytesPerPixel];

        for (int row = 0; row < height; row++)
        {
            int srcOffset = ((y + row) * bufferWidth + x) * bytesPerPixel;
            int dstOffset = row * width * bytesPerPixel;
            int rowBytes = width * bytesPerPixel;

            Array.Copy(fullBuffer, srcOffset, regionBuffer, dstOffset, rowBytes);
        }

        return regionBuffer;
    }
}
