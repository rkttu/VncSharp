using RfbTest.Diagnostics;

namespace RfbTest.Encoders;

/// <summary>
/// Hextile 인코딩 구현
/// 16x16 타일 기반 압축 인코딩
/// </summary>
public static class HextileEncoder
{
    private const int TileSize = 16;

    /// <summary>
    /// Hextile 서브인코딩 플래그
    /// </summary>
    [Flags]
    private enum SubEncoding : byte
    {
        Raw = 0x01,                    // 타일을 Raw로 인코딩
        BackgroundSpecified = 0x02,    // 배경색 지정
        ForegroundSpecified = 0x04,    // 전경색 지정
        AnySubrects = 0x08,            // 서브사각형 존재
        SubrectsColoured = 0x10        // 서브사각형마다 색상 지정
    }

    /// <summary>
    /// 프레임버퍼를 Hextile 인코딩으로 변환
    /// </summary>
    public static byte[] EncodeRegion(byte[] frameBuffer, int bufferWidth, int bufferHeight, 
        int x, int y, int width, int height)
    {
        RfbLogger.Log($"[Hextile] Encoding region ({x},{y}) {width}x{height} from buffer {bufferWidth}x{bufferHeight}");
        
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);

        int tileCount = 0;
        int rawTiles = 0;
        int solidTiles = 0;
        int compressedTiles = 0;

        // 타일 단위로 인코딩
        for (int tileY = y; tileY < y + height; tileY += TileSize)
        {
            for (int tileX = x; tileX < x + width; tileX += TileSize)
            {
                int tileW = Math.Min(TileSize, x + width - tileX);
                int tileH = Math.Min(TileSize, y + height - tileY);

                var tileType = EncodeTile(writer, frameBuffer, bufferWidth, bufferHeight, 
                    tileX, tileY, tileW, tileH);
                
                tileCount++;
                switch (tileType)
                {
                    case "Raw": rawTiles++; break;
                    case "Solid": solidTiles++; break;
                    case "Compressed": compressedTiles++; break;
                }
            }
        }

        var result = ms.ToArray();
        RfbLogger.Log($"[Hextile] Encoded {tileCount} tiles: {solidTiles} solid, {compressedTiles} compressed, {rawTiles} raw");
        RfbLogger.Log($"[Hextile] Output size: {result.Length} bytes (expected raw: {width * height * 4} bytes)");
        
        return result;
    }

    /// <summary>
    /// 단일 타일 인코딩
    /// </summary>
    private static string EncodeTile(BinaryWriter writer, byte[] frameBuffer, 
        int bufferWidth, int bufferHeight, int tileX, int tileY, int tileW, int tileH)
    {
        // 타일에서 픽셀 추출
        var tilePixels = ExtractTilePixels(frameBuffer, bufferWidth, tileX, tileY, tileW, tileH);

        // 배경색 분석
        uint backgroundColor = AnalyzeBackgroundColor(tilePixels);
        
        // 배경색과 다른 픽셀들 찾기
        var subrects = FindSubrects(tilePixels, tileW, tileH, backgroundColor);

        // 인코딩 방식 결정
        if (subrects.Count == 0)
        {
            // 단색 타일 - 배경색만 지정
            EncodeSolidTile(writer, backgroundColor);
            return "Solid";
        }
        else if (subrects.Count > tileW * tileH / 4)
        {
            // 서브사각형이 너무 많으면 Raw로 인코딩
            EncodeRawTile(writer, tilePixels, tileW, tileH);
            return "Raw";
        }
        else
        {
            // Hextile 방식으로 인코딩
            EncodeHextileTile(writer, backgroundColor, subrects, tileW, tileH);
            return "Compressed";
        }
    }

    /// <summary>
    /// 타일 픽셀 추출
    /// </summary>
    private static uint[] ExtractTilePixels(byte[] frameBuffer, int bufferWidth, 
        int tileX, int tileY, int tileW, int tileH)
    {
        var pixels = new uint[tileW * tileH];
        int bytesPerPixel = 4;

        for (int y = 0; y < tileH; y++)
        {
            for (int x = 0; x < tileW; x++)
            {
                int bufferOffset = ((tileY + y) * bufferWidth + (tileX + x)) * bytesPerPixel;
                
                // BGRA 형식으로 읽기
                byte b = frameBuffer[bufferOffset];
                byte g = frameBuffer[bufferOffset + 1];
                byte r = frameBuffer[bufferOffset + 2];
                byte a = frameBuffer[bufferOffset + 3];

                // 32bit RGBA로 변환 (네트워크 바이트 오더)
                pixels[y * tileW + x] = (uint)((r << 16) | (g << 8) | b);
            }
        }

        return pixels;
    }

    /// <summary>
    /// 배경색 분석 (가장 많이 나타나는 색)
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

        return colorCounts.OrderByDescending(x => x.Value).First().Key;
    }

    /// <summary>
    /// 서브사각형 찾기
    /// </summary>
    private static List<Subrect> FindSubrects(uint[] pixels, int width, int height, uint backgroundColor)
    {
        var subrects = new List<Subrect>();
        var processed = new bool[width * height];

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int idx = y * width + x;
                
                if (processed[idx] || pixels[idx] == backgroundColor)
                    continue;

                // 같은 색의 사각형 영역 찾기
                var rect = FindMaxRectangle(pixels, processed, width, height, x, y, pixels[idx]);
                subrects.Add(rect);
            }
        }

        return subrects;
    }

    /// <summary>
    /// 최대 사각형 찾기 (같은 색)
    /// </summary>
    private static Subrect FindMaxRectangle(uint[] pixels, bool[] processed, 
        int width, int height, int startX, int startY, uint color)
    {
        int maxW = 1;
        int maxH = 1;

        // 가로로 확장
        for (int x = startX + 1; x < width; x++)
        {
            if (pixels[startY * width + x] != color || processed[startY * width + x])
                break;
            maxW++;
        }

        // 세로로 확장
        bool canExpand = true;
        for (int y = startY + 1; y < height && canExpand; y++)
        {
            // 이 행 전체가 같은 색인지 확인
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

        return new Subrect
        {
            X = (byte)startX,
            Y = (byte)startY,
            Width = (byte)maxW,
            Height = (byte)maxH,
            Color = color
        };
    }

    /// <summary>
    /// 단색 타일 인코딩
    /// </summary>
    private static void EncodeSolidTile(BinaryWriter writer, uint color)
    {
        writer.Write((byte)SubEncoding.BackgroundSpecified);
        WritePixel(writer, color);
    }

    /// <summary>
    /// Raw 타일 인코딩
    /// </summary>
    private static void EncodeRawTile(BinaryWriter writer, uint[] pixels, int width, int height)
    {
        writer.Write((byte)SubEncoding.Raw);
        
        foreach (var pixel in pixels)
        {
            WritePixel(writer, pixel);
        }
    }

    /// <summary>
    /// Hextile 타일 인코딩
    /// </summary>
    private static void EncodeHextileTile(BinaryWriter writer, uint backgroundColor, 
        List<Subrect> subrects, int tileW, int tileH)
    {
        // 플래그 결정
        var flags = SubEncoding.BackgroundSpecified | SubEncoding.AnySubrects;

        // 전경색이 하나만 있는지 확인
        var colors = subrects.Select(s => s.Color).Distinct().ToList();
        bool singleForeground = colors.Count == 1;

        if (singleForeground)
        {
            flags |= SubEncoding.ForegroundSpecified;
        }
        else
        {
            flags |= SubEncoding.SubrectsColoured;
        }

        // 플래그 쓰기
        writer.Write((byte)flags);

        // 배경색 쓰기
        WritePixel(writer, backgroundColor);

        // 전경색 쓰기 (단일 전경색인 경우)
        if (singleForeground)
        {
            WritePixel(writer, colors[0]);
        }

        // 서브사각형 개수
        writer.Write((byte)subrects.Count);

        // 서브사각형 쓰기
        foreach (var rect in subrects)
        {
            if (!singleForeground)
            {
                WritePixel(writer, rect.Color);
            }

            // 좌표와 크기 (4bit씩)
            byte xy = (byte)((rect.X << 4) | rect.Y);
            byte wh = (byte)(((rect.Width - 1) << 4) | (rect.Height - 1));
            
            writer.Write(xy);
            writer.Write(wh);
        }
    }

    /// <summary>
    /// 픽셀 쓰기 (32bpp)
    /// </summary>
    private static void WritePixel(BinaryWriter writer, uint pixel)
    {
        // RGB (24bit) - Alpha 제외
        writer.Write((byte)(pixel & 0xFF));         // Blue
        writer.Write((byte)((pixel >> 8) & 0xFF));  // Green
        writer.Write((byte)((pixel >> 16) & 0xFF)); // Red
        writer.Write((byte)0);                      // Alpha/Padding
    }

    /// <summary>
    /// 서브사각형 정보
    /// </summary>
    private class Subrect
    {
        public byte X { get; set; }
        public byte Y { get; set; }
        public byte Width { get; set; }
        public byte Height { get; set; }
        public uint Color { get; set; }
    }
}
