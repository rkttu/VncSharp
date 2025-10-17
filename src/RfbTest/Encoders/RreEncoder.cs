using RfbTest.Diagnostics;

namespace RfbTest.Encoders;

/// <summary>
/// RRE (Rise-and-Run-length Encoding) 인코딩 구현
/// 배경색 + 단색 사각형들로 화면을 표현하는 압축 방식
/// </summary>
public static class RreEncoder
{
    /// <summary>
    /// RRE 인코딩으로 프레임버퍼 영역 인코딩
    /// </summary>
    /// <param name="frameBuffer">전체 프레임버퍼</param>
    /// <param name="bufferWidth">버퍼 너비</param>
    /// <param name="bufferHeight">버퍼 높이</param>
    /// <param name="x">영역 X 좌표</param>
    /// <param name="y">영역 Y 좌표</param>
    /// <param name="width">영역 너비</param>
    /// <param name="height">영역 높이</param>
    /// <returns>RRE 인코딩된 데이터</returns>
    public static byte[] EncodeRegion(byte[] frameBuffer, int bufferWidth, int bufferHeight,
        int x, int y, int width, int height)
    {
        RfbLogger.Log($"[RRE] Encoding region ({x},{y}) {width}x{height} from buffer {bufferWidth}x{bufferHeight}");

        // 영역의 픽셀 데이터 추출
        var pixels = ExtractPixels(frameBuffer, bufferWidth, x, y, width, height);

        // 배경색 분석 (가장 많이 사용된 색)
        uint backgroundColor = AnalyzeBackgroundColor(pixels);

        // 서브사각형 찾기 (배경색이 아닌 영역들)
        var subrects = FindSubrectangles(pixels, width, height, backgroundColor);

        RfbLogger.Log($"[RRE] Background color: 0x{backgroundColor:X8}, Subrects: {subrects.Count}");

        // RRE 데이터 생성
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);

        // Number of subrectangles (4 bytes, big-endian)
        writer.Write(SwapBytes((uint)subrects.Count));

        // Background pixel value (4 bytes for 32bpp)
        WritePixel(writer, backgroundColor);

        // Subrectangles
        foreach (var rect in subrects)
        {
            // Pixel value (4 bytes)
            WritePixel(writer, rect.Color);

            // X position (2 bytes, big-endian)
            writer.Write(SwapBytes(rect.X));

            // Y position (2 bytes, big-endian)
            writer.Write(SwapBytes(rect.Y));

            // Width (2 bytes, big-endian)
            writer.Write(SwapBytes(rect.Width));

            // Height (2 bytes, big-endian)
            writer.Write(SwapBytes(rect.Height));
        }

        var result = ms.ToArray();
        int rawSize = width * height * 4;
        float compressionRatio = result.Length > 0 ? (float)result.Length / rawSize : 0;

        RfbLogger.Log($"[RRE] Encoded {result.Length} bytes (raw would be {rawSize} bytes, ratio: {compressionRatio:P1})");

        return result;
    }

    /// <summary>
    /// RRE 인코딩이 효율적인지 판단
    /// </summary>
    /// <param name="frameBuffer">프레임버퍼</param>
    /// <param name="bufferWidth">버퍼 너비</param>
    /// <param name="x">영역 X</param>
    /// <param name="y">영역 Y</param>
    /// <param name="width">영역 너비</param>
    /// <param name="height">영역 높이</param>
    /// <param name="maxSubrects">최대 서브사각형 개수 (이보다 많으면 비효율적)</param>
    /// <returns>RRE가 효율적이면 true</returns>
    public static bool IsRreEfficient(byte[] frameBuffer, int bufferWidth, 
        int x, int y, int width, int height, int maxSubrects = 50)
    {
        var pixels = ExtractPixels(frameBuffer, bufferWidth, x, y, width, height);
        uint backgroundColor = AnalyzeBackgroundColor(pixels);
        var subrects = FindSubrectangles(pixels, width, height, backgroundColor);

        // 서브사각형이 너무 많으면 비효율적
        if (subrects.Count > maxSubrects)
            return false;

        // 예상 RRE 크기
        int rreSize = 4 + 4 + (subrects.Count * 12); // header + bg + (color + x + y + w + h) * count

        // Raw 크기
        int rawSize = width * height * 4;

        // RRE가 Raw보다 50% 이상 작을 때만 효율적
        return rreSize < (rawSize * 0.5);
    }

    /// <summary>
    /// 영역에서 픽셀 추출
    /// </summary>
    private static uint[] ExtractPixels(byte[] frameBuffer, int bufferWidth,
        int x, int y, int width, int height)
    {
        var pixels = new uint[width * height];
        int bytesPerPixel = 4;

        for (int row = 0; row < height; row++)
        {
            for (int col = 0; col < width; col++)
            {
                int bufferOffset = ((y + row) * bufferWidth + (x + col)) * bytesPerPixel;

                // BGRA 형식으로 읽기
                byte b = frameBuffer[bufferOffset];
                byte g = frameBuffer[bufferOffset + 1];
                byte r = frameBuffer[bufferOffset + 2];
                byte a = frameBuffer[bufferOffset + 3];

                // 32bit RGBA로 변환
                pixels[row * width + col] = (uint)((r << 16) | (g << 8) | b);
            }
        }

        return pixels;
    }

    /// <summary>
    /// 배경색 분석 (가장 빈도가 높은 색)
    /// </summary>
    private static uint AnalyzeBackgroundColor(uint[] pixels)
    {
        var colorCounts = new Dictionary<uint, int>();

        foreach (var pixel in pixels)
        {
            if (colorCounts.ContainsKey(pixel))
                colorCounts[pixel]++;
            else
                colorCounts[pixel] = 1;
        }

        if (colorCounts.Count == 0)
            return 0;

        return colorCounts.OrderByDescending(x => x.Value).First().Key;
    }

    /// <summary>
    /// 서브사각형 찾기 (배경색이 아닌 영역들)
    /// </summary>
    private static List<Subrectangle> FindSubrectangles(uint[] pixels, int width, int height, uint backgroundColor)
    {
        var subrects = new List<Subrectangle>();
        var processed = new bool[width * height];

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int idx = y * width + x;

                if (processed[idx])
                    continue;

                uint color = pixels[idx];

                if (color == backgroundColor)
                {
                    processed[idx] = true;
                    continue;
                }

                // 같은 색의 최대 사각형 찾기
                var rect = FindMaxRectangle(pixels, processed, width, height, x, y, color);
                subrects.Add(rect);
            }
        }

        return subrects;
    }

    /// <summary>
    /// 같은 색으로 이루어진 최대 사각형 찾기
    /// </summary>
    private static Subrectangle FindMaxRectangle(uint[] pixels, bool[] processed,
        int width, int height, int startX, int startY, uint color)
    {
        // 가로로 최대한 확장
        int maxW = 1;
        for (int x = startX + 1; x < width; x++)
        {
            int idx = startY * width + x;
            if (pixels[idx] != color || processed[idx])
                break;
            maxW++;
        }

        // 세로로 최대한 확장
        int maxH = 1;
        bool canExpand = true;
        for (int y = startY + 1; y < height && canExpand; y++)
        {
            // 이 행의 해당 범위가 모두 같은 색인지 확인
            for (int x = startX; x < startX + maxW; x++)
            {
                int idx = y * width + x;
                if (pixels[idx] != color || processed[idx])
                {
                    canExpand = false;
                    break;
                }
            }
            if (canExpand)
                maxH++;
        }

        // 처리된 것으로 표시
        for (int y = startY; y < startY + maxH; y++)
        {
            for (int x = startX; x < startX + maxW; x++)
            {
                processed[y * width + x] = true;
            }
        }

        return new Subrectangle
        {
            X = (ushort)startX,
            Y = (ushort)startY,
            Width = (ushort)maxW,
            Height = (ushort)maxH,
            Color = color
        };
    }

    /// <summary>
    /// 픽셀 쓰기 (32bpp BGRA)
    /// </summary>
    private static void WritePixel(BinaryWriter writer, uint pixel)
    {
        // RGB 24bit + Alpha
        writer.Write((byte)(pixel & 0xFF));         // Blue
        writer.Write((byte)((pixel >> 8) & 0xFF));  // Green
        writer.Write((byte)((pixel >> 16) & 0xFF)); // Red
        writer.Write((byte)0);                      // Alpha/Padding
    }

    /// <summary>
    /// 바이트 스왑 (Big-Endian)
    /// </summary>
    private static ushort SwapBytes(ushort value)
    {
        if (BitConverter.IsLittleEndian)
            return (ushort)((value >> 8) | (value << 8));
        return value;
    }

    private static uint SwapBytes(uint value)
    {
        if (BitConverter.IsLittleEndian)
            return (value >> 24) | ((value & 0x00FF0000) >> 8) |
                   ((value & 0x0000FF00) << 8) | (value << 24);
        return value;
    }

    /// <summary>
    /// 서브사각형 정보
    /// </summary>
    private class Subrectangle
    {
        public ushort X { get; set; }
        public ushort Y { get; set; }
        public ushort Width { get; set; }
        public ushort Height { get; set; }
        public uint Color { get; set; }
    }
}
