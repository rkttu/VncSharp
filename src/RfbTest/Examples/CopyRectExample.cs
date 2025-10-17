using RfbTest.Encoders;
using RfbTest.Protocol;

namespace RfbTest.Examples;

/// <summary>
/// CopyRect 인코딩 사용 예제
/// </summary>
public static class CopyRectExample
{
    /// <summary>
    /// 예제 1: 창 드래그 시나리오 - 창이 이동할 때 CopyRect 사용
    /// </summary>
    public static async Task DemoWindowDragAsync(RfbClient client, byte[] frameBuffer, 
        int bufferWidth, int bufferHeight)
    {
        // 시나리오: 400x300 크기의 창이 (100, 100)에서 (150, 120)으로 드래그됨
        ushort windowWidth = 400;
        ushort windowHeight = 300;
        ushort oldX = 100;
        ushort oldY = 100;
        ushort newX = 150;
        ushort newY = 120;

        if (client.SupportsCopyRect)
        {
            // CopyRect로 창 이동 전송 (4 bytes만 전송)
            await client.SendCopyRectUpdateAsync(newX, newY, windowWidth, windowHeight, oldX, oldY);
            
            // 절약된 데이터: 400 * 300 * 4 = 480,000 bytes
            // 실제 전송: 4 bytes (원본 좌표만)
            // 압축률: 99.9992%
        }
        else
        {
            // CopyRect 미지원 시 일반 업데이트로 폴백
            await client.SendFramebufferUpdateAsync(frameBuffer, newX, newY, windowWidth, windowHeight, 
                isFullBuffer: true);
        }
    }

    /// <summary>
    /// 예제 2: 스크롤 시나리오 - 페이지가 위로 스크롤될 때
    /// </summary>
    public static async Task DemoScrollUpAsync(RfbClient client, byte[] frameBuffer, 
        int bufferWidth, int bufferHeight, int scrollPixels = 20)
    {
        // 스크롤 가능한 영역 전체
        ushort x = 0;
        ushort y = 0;
        ushort width = (ushort)bufferWidth;
        ushort height = (ushort)(bufferHeight - scrollPixels);

        if (client.SupportsCopyRect)
        {
            // 화면의 대부분을 위로 이동 (CopyRect 사용)
            await client.SendCopyRectUpdateAsync(x, y, width, height, x, (ushort)scrollPixels);
            
            // 하단의 새로 노출된 영역만 실제 데이터 전송
            ushort newContentY = (ushort)(bufferHeight - scrollPixels);
            await client.SendFramebufferUpdateAsync(frameBuffer, x, newContentY, width, (ushort)scrollPixels, 
                isFullBuffer: true);
        }
        else
        {
            // 전체 화면 업데이트로 폴백
            await client.SendFramebufferUpdateAsync(frameBuffer, x, y, (ushort)bufferWidth, (ushort)bufferHeight, 
                isFullBuffer: true);
        }
    }

    /// <summary>
    /// 예제 3: 자동 감지 및 최적화 - 프레임 간 유사 영역 자동 검출
    /// </summary>
    public static async Task DemoAutoDetectAsync(RfbClient client, byte[] currentFrameBuffer, 
        byte[] previousFrameBuffer, int bufferWidth, int bufferHeight)
    {
        if (!client.SupportsCopyRect)
        {
            // CopyRect 미지원 시 일반 업데이트
            await client.SendFramebufferUpdateAsync(currentFrameBuffer, 0, 0, 
                (ushort)bufferWidth, (ushort)bufferHeight, isFullBuffer: true);
            return;
        }

        // 변경된 영역 검출 (간단한 예제)
        var changedRegions = DetectChangedRegions(currentFrameBuffer, previousFrameBuffer, 
            bufferWidth, bufferHeight);

        foreach (var region in changedRegions)
        {
            // 현재 프레임에서 이 영역이 이전 프레임의 다른 위치와 유사한지 확인
            var matchingSource = CopyRectEncoder.FindMatchingRegion(
                previousFrameBuffer, bufferWidth, bufferHeight,
                region.X, region.Y, region.Width, region.Height,
                searchRadius: 100);

            if (matchingSource.HasValue)
            {
                // 유사한 영역 발견 - CopyRect 사용
                await client.SendCopyRectUpdateAsync(
                    (ushort)region.X, (ushort)region.Y, 
                    (ushort)region.Width, (ushort)region.Height,
                    matchingSource.Value.srcX, matchingSource.Value.srcY);
            }
            else
            {
                // 유사한 영역 없음 - 일반 업데이트
                await client.SendFramebufferUpdateAsync(currentFrameBuffer, 
                    (ushort)region.X, (ushort)region.Y, 
                    (ushort)region.Width, (ushort)region.Height, 
                    isFullBuffer: true);
            }
        }
    }

    /// <summary>
    /// 예제 4: 복합 업데이트 - CopyRect와 일반 인코딩 혼합
    /// </summary>
    public static async Task DemoMultiRectangleAsync(RfbClient client, byte[] frameBuffer,
        int bufferWidth, int bufferHeight)
    {
        // 여러 개의 사각형을 한 번에 전송할 수 있음
        // 실제로는 RfbClient에 배치 전송 메서드를 추가해야 함
        
        // 시나리오: 창 두 개가 각각 이동 + 새로운 창 하나 생성
        
        // 1. 첫 번째 창 이동 (CopyRect)
        await client.SendCopyRectUpdateAsync(200, 150, 300, 200, 180, 130);
        
        // 2. 두 번째 창 이동 (CopyRect)
        await client.SendCopyRectUpdateAsync(500, 300, 250, 180, 480, 280);
        
        // 3. 새로운 창 내용 (일반 인코딩)
        await client.SendFramebufferUpdateAsync(frameBuffer, 100, 400, 350, 250, 
            isFullBuffer: true);
    }

    // 헬퍼 메서드: 간단한 변경 영역 감지
    private static List<Region> DetectChangedRegions(byte[] current, byte[] previous, 
        int width, int height)
    {
        var regions = new List<Region>();
        
        // 실제 구현에서는 더 정교한 알고리즘 필요
        // 여기서는 간단한 예제로 전체 화면을 하나의 영역으로 반환
        if (!current.SequenceEqual(previous))
        {
            regions.Add(new Region { X = 0, Y = 0, Width = width, Height = height });
        }
        
        return regions;
    }

    private class Region
    {
        public int X { get; set; }
        public int Y { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
    }
}
