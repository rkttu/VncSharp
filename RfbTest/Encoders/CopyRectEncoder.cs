using RfbTest.Diagnostics;
using RfbTest.Protocol;

namespace RfbTest.Encoders;

/// <summary>
/// CopyRect 인코딩 구현
/// 화면의 한 영역을 다른 위치로 복사할 때 사용하는 매우 효율적인 인코딩
/// </summary>
public static class CopyRectEncoder
{
    /// <summary>
    /// CopyRect 인코딩 데이터 생성
    /// </summary>
    /// <param name="srcX">원본 X 좌표</param>
    /// <param name="srcY">원본 Y 좌표</param>
    /// <returns>CopyRect 인코딩 데이터 (4 bytes)</returns>
    public static byte[] Encode(ushort srcX, ushort srcY)
    {
        RfbLogger.Log($"[CopyRect] Encoding: Source ({srcX}, {srcY})");
        
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);

        // CopyRect 인코딩은 단순히 원본 좌표만 전송 (big-endian)
        writer.Write(SwapBytes(srcX));
        writer.Write(SwapBytes(srcY));

        var result = ms.ToArray();
        RfbLogger.Log($"[CopyRect] Encoded {result.Length} bytes (expected 4 bytes)");
        
        return result;
    }

    /// <summary>
    /// CopyRect가 유용한지 판단하는 헬퍼 메서드
    /// 화면의 두 영역을 비교하여 동일한지 확인
    /// </summary>
    /// <param name="frameBuffer">전체 프레임버퍼</param>
    /// <param name="bufferWidth">버퍼 너비</param>
    /// <param name="bufferHeight">버퍼 높이</param>
    /// <param name="destX">대상 영역 X 좌표</param>
    /// <param name="destY">대상 영역 Y 좌표</param>
    /// <param name="srcX">원본 영역 X 좌표</param>
    /// <param name="srcY">원본 영역 Y 좌표</param>
    /// <param name="width">영역 너비</param>
    /// <param name="height">영역 높이</param>
    /// <param name="threshold">일치도 임계값 (0.0 ~ 1.0, 기본 0.95 = 95% 일치)</param>
    /// <returns>두 영역이 충분히 유사하면 true</returns>
    public static bool IsRegionSimilar(byte[] frameBuffer, int bufferWidth, int bufferHeight,
        int destX, int destY, int srcX, int srcY, int width, int height, double threshold = 0.95)
    {
        if (srcX < 0 || srcY < 0 || destX < 0 || destY < 0)
            return false;

        if (srcX + width > bufferWidth || srcY + height > bufferHeight)
            return false;

        if (destX + width > bufferWidth || destY + height > bufferHeight)
            return false;

        int bytesPerPixel = 4;
        int totalPixels = width * height;
        int matchingPixels = 0;
        int samplingRate = Math.Max(1, totalPixels / 1000); // 최대 1000개 픽셀 샘플링

        for (int y = 0; y < height; y += samplingRate)
        {
            for (int x = 0; x < width; x += samplingRate)
            {
                int srcOffset = ((srcY + y) * bufferWidth + (srcX + x)) * bytesPerPixel;
                int destOffset = ((destY + y) * bufferWidth + (destX + x)) * bytesPerPixel;

                if (srcOffset + bytesPerPixel <= frameBuffer.Length &&
                    destOffset + bytesPerPixel <= frameBuffer.Length)
                {
                    bool pixelMatch = true;
                    for (int i = 0; i < bytesPerPixel; i++)
                    {
                        if (frameBuffer[srcOffset + i] != frameBuffer[destOffset + i])
                        {
                            pixelMatch = false;
                            break;
                        }
                    }

                    if (pixelMatch)
                        matchingPixels++;
                }
            }
        }

        int sampledPixels = ((height + samplingRate - 1) / samplingRate) * 
                           ((width + samplingRate - 1) / samplingRate);
        double similarity = sampledPixels > 0 ? (double)matchingPixels / sampledPixels : 0.0;

        RfbLogger.Log($"[CopyRect] Region similarity: {similarity:P2} (threshold: {threshold:P2})");
        
        return similarity >= threshold;
    }

    /// <summary>
    /// 프레임버퍼에서 반복되는 영역 찾기 (CopyRect 최적화용)
    /// </summary>
    /// <param name="frameBuffer">전체 프레임버퍼</param>
    /// <param name="bufferWidth">버퍼 너비</param>
    /// <param name="bufferHeight">버퍼 높이</param>
    /// <param name="destX">검색할 대상 영역 X</param>
    /// <param name="destY">검색할 대상 영역 Y</param>
    /// <param name="width">영역 너비</param>
    /// <param name="height">영역 높이</param>
    /// <param name="searchRadius">검색 반경 (픽셀)</param>
    /// <returns>일치하는 원본 좌표 (없으면 null)</returns>
    public static (ushort srcX, ushort srcY)? FindMatchingRegion(byte[] frameBuffer, 
        int bufferWidth, int bufferHeight, int destX, int destY, int width, int height, 
        int searchRadius = 100)
    {
        // 검색 영역 제한
        int minX = Math.Max(0, destX - searchRadius);
        int maxX = Math.Min(bufferWidth - width, destX + searchRadius);
        int minY = Math.Max(0, destY - searchRadius);
        int maxY = Math.Min(bufferHeight - height, destY + searchRadius);

        int step = Math.Max(1, searchRadius / 20); // 검색 스텝

        for (int srcY = minY; srcY <= maxY; srcY += step)
        {
            for (int srcX = minX; srcX <= maxX; srcX += step)
            {
                // 자기 자신은 제외
                if (srcX == destX && srcY == destY)
                    continue;

                if (IsRegionSimilar(frameBuffer, bufferWidth, bufferHeight,
                    destX, destY, srcX, srcY, width, height, 0.98))
                {
                    RfbLogger.Log($"[CopyRect] Found matching region: Dest({destX},{destY}) <- Src({srcX},{srcY})");
                    return ((ushort)srcX, (ushort)srcY);
                }
            }
        }

        return null;
    }

    /// <summary>
    /// 바이트 스왑 (Big-Endian 변환)
    /// </summary>
    private static ushort SwapBytes(ushort value)
    {
        if (BitConverter.IsLittleEndian)
            return (ushort)((value >> 8) | (value << 8));
        return value;
    }
}
