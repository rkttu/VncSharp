using RfbTest.Protocol;

namespace RfbTest.Encoders;

/// <summary>
/// RFB 프레임버퍼 인코더
/// </summary>
public class RfbEncoder
{
    /// <summary>
    /// Raw 인코딩: 압축 없이 픽셀 데이터를 그대로 전송
    /// </summary>
    public static byte[] EncodeRaw(byte[] frameBuffer, ushort x, ushort y, ushort width, ushort height, int bytesPerPixel = 4)
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);

        // FramebufferUpdate 메시지 헤더
        writer.Write((byte)RfbProtocol.ServerMessageType.FramebufferUpdate);
        writer.Write((byte)0); // padding
        writer.Write(SwapBytes((ushort)1)); // number-of-rectangles

        // Rectangle 헤더
        writer.Write(SwapBytes(x));
        writer.Write(SwapBytes(y));
        writer.Write(SwapBytes(width));
        writer.Write(SwapBytes(height));
        writer.Write(SwapBytes((int)RfbProtocol.EncodingType.Raw));

        // 픽셀 데이터 (Raw)
        // 실제 구현에서는 frameBuffer에서 x,y,width,height 영역만 추출
        int totalPixels = width * height;
        int dataSize = totalPixels * bytesPerPixel;
        
        if (frameBuffer.Length >= dataSize)
        {
            writer.Write(frameBuffer, 0, dataSize);
        }

        return ms.ToArray();
    }

    /// <summary>
    /// 테스트용 컬러 프레임버퍼 생성 (32bpp BGRA)
    /// </summary>
    public static byte[] CreateTestFrameBuffer(ushort width, ushort height, byte red, byte green, byte blue)
    {
        int totalPixels = width * height;
        byte[] buffer = new byte[totalPixels * 4]; // 32bpp

        for (int i = 0; i < totalPixels; i++)
        {
            int offset = i * 4;
            buffer[offset] = blue;      // B
            buffer[offset + 1] = green; // G
            buffer[offset + 2] = red;   // R
            buffer[offset + 3] = 255;   // A
        }

        return buffer;
    }

    /// <summary>
    /// 그라디언트 프레임버퍼 생성 (테스트용)
    /// </summary>
    public static byte[] CreateGradientFrameBuffer(ushort width, ushort height)
    {
        int totalPixels = width * height;
        byte[] buffer = new byte[totalPixels * 4];

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int offset = (y * width + x) * 4;
                
                byte red = (byte)(255 * x / width);
                byte green = (byte)(255 * y / height);
                byte blue = (byte)(128);

                buffer[offset] = blue;      // B
                buffer[offset + 1] = green; // G
                buffer[offset + 2] = red;   // R
                buffer[offset + 3] = 255;   // A
            }
        }

        return buffer;
    }

    private static ushort SwapBytes(ushort value) => (ushort)((value >> 8) | (value << 8));
    
    private static int SwapBytes(int value) => 
        (int)((uint)value >> 24) | (int)(((uint)value & 0x00FF0000) >> 8) | 
        (int)(((uint)value & 0x0000FF00) << 8) | (int)((uint)value << 24);
}
