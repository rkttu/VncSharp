using RfbTest.Encoders;
using RfbTest.Protocol;

namespace RfbTest.Examples;

/// <summary>
/// RRE (Rise-and-Run-length Encoding) 사용 예제
/// </summary>
public static class RreExample
{
    /// <summary>
    /// 예제 1: 단순한 UI 화면 (RRE가 가장 효율적인 경우)
    /// </summary>
    public static async Task DemoSimpleUIAsync(RfbClient client)
    {
        // 시나리오: 흰색 배경에 몇 개의 컬러 버튼이 있는 UI
        // 800x600 화면
        ushort width = 800;
        ushort height = 600;

        // 프레임버퍼 생성 (흰색 배경)
        var frameBuffer = CreateFrameBuffer(width, height, 255, 255, 255);

        // 빨간색 버튼 추가 (100, 100, 200x50)
        DrawRectangle(frameBuffer, width, 100, 100, 200, 50, 255, 0, 0);

        // 파란색 버튼 추가 (100, 200, 200x50)
        DrawRectangle(frameBuffer, width, 100, 200, 200, 50, 0, 0, 255);

        // 녹색 버튼 추가 (100, 300, 200x50)
        DrawRectangle(frameBuffer, width, 100, 300, 200, 50, 0, 255, 0);

        if (client.SupportsRRE)
        {
            // RRE 인코딩으로 전송
            // 배경색(흰색) + 3개의 서브사각형(버튼)
            // 예상 크기: 4(개수) + 4(배경색) + 3 * 12(서브사각형) = 44 bytes
            // Raw 크기: 800 * 600 * 4 = 1,920,000 bytes
            // 압축률: 99.998%
            await client.SendFramebufferUpdateAsync(frameBuffer, 0, 0, width, height,
                RfbProtocol.EncodingType.RRE, isFullBuffer: true);
        }
        else
        {
            await client.SendFramebufferUpdateAsync(frameBuffer, 0, 0, width, height,
                isFullBuffer: true);
        }
    }

    /// <summary>
    /// 예제 2: 텍스트 편집기 화면
    /// </summary>
    public static async Task DemoTextEditorAsync(RfbClient client)
    {
        // 시나리오: 회색 배경에 흰색 텍스트 영역
        ushort width = 1024;
        ushort height = 768;

        var frameBuffer = CreateFrameBuffer(width, height, 192, 192, 192); // 회색 배경

        // 텍스트 영역 (흰색)
        DrawRectangle(frameBuffer, width, 50, 50, 900, 650, 255, 255, 255);

        // 툴바 버튼들 (여러 색상)
        DrawRectangle(frameBuffer, width, 60, 10, 30, 30, 200, 200, 200); // 파일
        DrawRectangle(frameBuffer, width, 100, 10, 30, 30, 200, 200, 200); // 편집
        DrawRectangle(frameBuffer, width, 140, 10, 30, 30, 200, 200, 200); // 보기

        if (client.SupportsRRE)
        {
            // RRE가 효율적인지 확인
            bool isEfficient = RreEncoder.IsRreEfficient(frameBuffer, width, 0, 0, width, height);

            if (isEfficient)
            {
                await client.SendFramebufferUpdateAsync(frameBuffer, 0, 0, width, height,
                    RfbProtocol.EncodingType.RRE, isFullBuffer: true);
            }
            else
            {
                // RRE가 비효율적이면 다른 인코딩 사용
                await client.SendFramebufferUpdateAsync(frameBuffer, 0, 0, width, height,
                    client.GetPreferredEncoding(), isFullBuffer: true);
            }
        }
    }

    /// <summary>
    /// 예제 3: 지능형 인코딩 선택 (콘텐츠에 따라 최적 인코딩 선택)
    /// </summary>
    public static async Task DemoIntelligentEncodingAsync(RfbClient client, byte[] frameBuffer,
        ushort x, ushort y, ushort width, ushort height)
    {
        var selectedEncoding = client.GetPreferredEncoding();

        // RRE가 효율적인지 확인
        if (client.SupportsRRE)
        {
            bool isRreEfficient = RreEncoder.IsRreEfficient(frameBuffer, 
                (int)client.FramebufferWidth, x, y, width, height, maxSubrects: 50);

            if (isRreEfficient)
            {
                selectedEncoding = RfbProtocol.EncodingType.RRE;
            }
        }

        // 선택된 인코딩으로 전송
        await client.SendFramebufferUpdateAsync(frameBuffer, x, y, width, height,
            selectedEncoding, isFullBuffer: true);
    }

    /// <summary>
    /// 예제 4: 다이얼로그 박스 (RRE 최적 사용 사례)
    /// </summary>
    public static async Task DemoDialogBoxAsync(RfbClient client)
    {
        // 시나리오: 400x300 다이얼로그
        ushort width = 400;
        ushort height = 300;

        // 회색 배경
        var frameBuffer = CreateFrameBuffer(width, height, 240, 240, 240);

        // 제목 표시줄 (파란색)
        DrawRectangle(frameBuffer, width, 0, 0, 400, 30, 0, 120, 215);

        // 확인 버튼 (녹색)
        DrawRectangle(frameBuffer, width, 250, 250, 60, 30, 0, 200, 0);

        // 취소 버튼 (빨간색)
        DrawRectangle(frameBuffer, width, 320, 250, 60, 30, 200, 0, 0);

        // 콘텐츠 영역 (흰색)
        DrawRectangle(frameBuffer, width, 10, 40, 380, 200, 255, 255, 255);

        if (client.SupportsRRE)
        {
            // RRE로 전송
            // 배경 + 4개의 서브사각형
            // 매우 효율적 (99% 이상 압축)
            await client.SendFramebufferUpdateAsync(frameBuffer, 0, 0, width, height,
                RfbProtocol.EncodingType.RRE, isFullBuffer: true);
        }
    }

    /// <summary>
    /// 예제 5: 영역별 최적 인코딩 (하이브리드 접근)
    /// </summary>
    public static async Task DemoHybridEncodingAsync(RfbClient client, byte[] frameBuffer,
        int bufferWidth, int bufferHeight)
    {
        // 화면을 여러 영역으로 나누고 각각 최적 인코딩 선택

        var regions = new[]
        {
            new { X = 0, Y = 0, W = 200, H = 600, Type = "UI" },           // 좌측 UI (RRE 적합)
            new { X = 200, Y = 0, W = 824, H = 600, Type = "Content" },    // 콘텐츠 (Hextile 적합)
        };

        foreach (var region in regions)
        {
            RfbProtocol.EncodingType encoding;

            if (region.Type == "UI" && client.SupportsRRE)
            {
                // UI 영역: RRE가 효율적인지 확인
                bool isRreEfficient = RreEncoder.IsRreEfficient(frameBuffer, bufferWidth,
                    region.X, region.Y, region.W, region.H);

                encoding = isRreEfficient ? RfbProtocol.EncodingType.RRE : client.GetPreferredEncoding();
            }
            else
            {
                // 콘텐츠 영역: 기본 선호 인코딩 사용
                encoding = client.GetPreferredEncoding();
            }

            await client.SendFramebufferUpdateAsync(frameBuffer,
                (ushort)region.X, (ushort)region.Y,
                (ushort)region.W, (ushort)region.H,
                encoding, isFullBuffer: true);
        }
    }

    /// <summary>
    /// 예제 6: 성능 비교 (Raw vs RRE)
    /// </summary>
    public static void DemoPerformanceComparison()
    {
        // 800x600 단순 UI 생성
        ushort width = 800;
        ushort height = 600;

        var frameBuffer = CreateFrameBuffer(width, height, 255, 255, 255);
        DrawRectangle(frameBuffer, width, 100, 100, 200, 50, 255, 0, 0);
        DrawRectangle(frameBuffer, width, 100, 200, 200, 50, 0, 0, 255);
        DrawRectangle(frameBuffer, width, 100, 300, 200, 50, 0, 255, 0);

        // Raw 크기
        int rawSize = width * height * 4;

        // RRE 인코딩
        var rreData = RreEncoder.EncodeRegion(frameBuffer, width, height, 0, 0, width, height);

        Console.WriteLine($"╔════════════════════════════════════════════════════════");
        Console.WriteLine($"║ RRE vs RAW 성능 비교");
        Console.WriteLine($"╠════════════════════════════════════════════════════════");
        Console.WriteLine($"║ 화면 크기: {width}x{height}");
        Console.WriteLine($"║ Raw 크기: {rawSize:N0} bytes");
        Console.WriteLine($"║ RRE 크기: {rreData.Length:N0} bytes");
        Console.WriteLine($"║ 압축률: {(1.0 - (double)rreData.Length / rawSize):P2}");
        Console.WriteLine($"║ 크기 감소: {rawSize - rreData.Length:N0} bytes");
        Console.WriteLine($"╚════════════════════════════════════════════════════════");
    }

    // 헬퍼 메서드들

    private static byte[] CreateFrameBuffer(int width, int height, byte r, byte g, byte b)
    {
        var buffer = new byte[width * height * 4];

        for (int i = 0; i < width * height; i++)
        {
            int offset = i * 4;
            buffer[offset] = b;     // B
            buffer[offset + 1] = g; // G
            buffer[offset + 2] = r; // R
            buffer[offset + 3] = 255; // A
        }

        return buffer;
    }

    private static void DrawRectangle(byte[] buffer, int bufferWidth,
        int x, int y, int width, int height, byte r, byte g, byte b)
    {
        for (int row = y; row < y + height && row < buffer.Length / (bufferWidth * 4); row++)
        {
            for (int col = x; col < x + width && col < bufferWidth; col++)
            {
                int offset = (row * bufferWidth + col) * 4;
                if (offset + 3 < buffer.Length)
                {
                    buffer[offset] = b;     // B
                    buffer[offset + 1] = g; // G
                    buffer[offset + 2] = r; // R
                    buffer[offset + 3] = 255; // A
                }
            }
        }
    }
}
