# VNC 인코딩 구현 가이드

## 구현된 인코딩

### 1. Raw Encoding (기본)
- **압축률**: 0% (압축 없음)
- **속도**: 매우 빠름 (데이터 그대로 전송)
- **사용 사례**: 
  - 모든 클라이언트가 지원하는 기본 인코딩
  - 다른 인코딩이 비효율적일 때 폴백
- **데이터 크기**: width × height × 4 bytes

### 2. CopyRect Encoding ⭐⭐⭐⭐⭐
- **압축률**: 99.99% (좌표만 전송)
- **속도**: 매우 빠름
- **사용 사례**:
  - 창 드래그
  - 스크롤
  - 화면 영역 이동
  - 복제된 UI 요소
- **데이터 크기**: 4 bytes (srcX + srcY)
- **예제**: `CopyRectExample.cs` 참조

### 3. RRE (Rise-and-Run-length Encoding) ⭐⭐⭐⭐
- **압축률**: 90-99% (단순 UI)
- **속도**: 빠름
- **사용 사례**:
  - 단순한 UI 화면
  - 다이얼로그 박스
  - 텍스트 편집기
  - 단색 영역이 많은 화면
- **데이터 크기**: 
  - 헤더: 4 bytes (서브사각형 개수) + 4 bytes (배경색)
  - 각 서브사각형: 12 bytes (색상 4 + X 2 + Y 2 + W 2 + H 2)
  - 총: 8 + (12 × 서브사각형 개수)
- **효율성 판단**: 서브사각형이 50개 이하일 때 효율적
- **예제**: `RreExample.cs` 참조

### 4. Hextile Encoding ⭐⭐⭐⭐
- **압축률**: 50-80% (일반적인 화면)
- **속도**: 중간
- **사용 사례**:
  - 일반적인 데스크톱 화면
  - 복잡도가 중간인 이미지
  - 타일 기반 최적화
- **데이터 크기**: 가변 (16×16 타일 기반)
- **참고**: `HextileEncoder.cs` 참조

## 인코딩 선택 가이드

### 우선순위 (자동 선택 시)

```
1. CopyRect   - 영역 이동이 감지되면
2. Hextile    - 클라이언트가 지원하면 (일반적인 경우)
3. RRE        - 단순 UI이고 Hextile 미지원 시
4. Raw        - 모든 인코딩이 비효율적일 때
```

### 콘텐츠 유형별 최적 인코딩

| 콘텐츠 유형 | 1순위 | 2순위 | 3순위 |
|------------|-------|-------|-------|
| 창 드래그 | CopyRect | Hextile | Raw |
| 스크롤 | CopyRect | Hextile | Raw |
| 단순 UI | RRE | Hextile | Raw |
| 다이얼로그 | RRE | Hextile | Raw |
| 텍스트 편집기 | RRE | Hextile | Raw |
| 일반 데스크톱 | Hextile | RRE | Raw |
| 사진/비디오 | Hextile | Raw | - |
| 복잡한 그래픽 | Hextile | Raw | - |

## 성능 비교

### 예제: 800×600 단순 UI (흰색 배경 + 3개 버튼)

| 인코딩 | 데이터 크기 | 압축률 | 상대 속도 |
|--------|-------------|--------|-----------|
| Raw | 1,920,000 bytes | 0% | 100% (기준) |
| RRE | 44 bytes | 99.998% | 95% |
| Hextile | ~50,000 bytes | 97.4% | 80% |
| CopyRect* | 4 bytes | 99.9998% | 100% |

*CopyRect는 이동 시에만 적용 가능

### 예제: 1920×1080 복잡한 화면

| 인코딩 | 데이터 크기 | 압축률 | 상대 속도 |
|--------|-------------|--------|-----------|
| Raw | 8,294,400 bytes | 0% | 100% (기준) |
| RRE | ~500,000 bytes | 94% | 85% |
| Hextile | ~2,000,000 bytes | 76% | 75% |
| CopyRect* | 4 bytes | 99.9999% | 100% |

## 구현 상세

### RRE 알고리즘

1. **배경색 분석**
   - 전체 픽셀 중 가장 빈도가 높은 색상 선택
   - 배경색으로 지정

2. **서브사각형 찾기**
   - 배경색이 아닌 영역들을 스캔
   - 같은 색상의 인접 픽셀들을 하나의 사각형으로 그룹화
   - 가로/세로 방향으로 최대한 확장

3. **인코딩**
   ```
   [서브사각형 개수: 4 bytes]
   [배경색: 4 bytes]
   [서브사각형 1]
     - 색상: 4 bytes
     - X: 2 bytes
     - Y: 2 bytes
     - Width: 2 bytes
     - Height: 2 bytes
   [서브사각형 2]
   ...
   ```

### 효율성 검증

```csharp
// RRE가 효율적인지 확인
bool isEfficient = RreEncoder.IsRreEfficient(
    frameBuffer, bufferWidth, x, y, width, height,
    maxSubrects: 50  // 최대 50개까지 허용
);

if (isEfficient)
{
    await client.SendFramebufferUpdateAsync(frameBuffer, x, y, width, height,
        RfbProtocol.EncodingType.RRE, isFullBuffer: true);
}
else
{
    // 다른 인코딩 사용
    await client.SendFramebufferUpdateAsync(frameBuffer, x, y, width, height,
        client.GetPreferredEncoding(), isFullBuffer: true);
}
```

## 실전 사용 패턴

### 패턴 1: 지능형 인코딩 선택

```csharp
public async Task SendOptimizedUpdateAsync(RfbClient client, byte[] frameBuffer,
    ushort x, ushort y, ushort width, ushort height)
{
    // 1. CopyRect 가능 여부 확인
    if (client.SupportsCopyRect)
    {
        var match = CopyRectEncoder.FindMatchingRegion(
            frameBuffer, client.FramebufferWidth, client.FramebufferHeight,
            x, y, width, height);
        
        if (match.HasValue)
        {
            await client.SendCopyRectUpdateAsync(x, y, width, height,
                match.Value.srcX, match.Value.srcY);
            return;
        }
    }

    // 2. RRE 효율성 확인
    if (client.SupportsRRE)
    {
        bool isRreEfficient = RreEncoder.IsRreEfficient(
            frameBuffer, client.FramebufferWidth, x, y, width, height);
        
        if (isRreEfficient)
        {
            await client.SendFramebufferUpdateAsync(frameBuffer, x, y, width, height,
                RfbProtocol.EncodingType.RRE, isFullBuffer: true);
            return;
        }
    }

    // 3. 기본 선호 인코딩 사용
    await client.SendFramebufferUpdateAsync(frameBuffer, x, y, width, height,
        client.GetPreferredEncoding(), isFullBuffer: true);
}
```

### 패턴 2: 영역별 최적화

```csharp
// UI 영역과 콘텐츠 영역을 분리하여 최적 인코딩 적용
var uiRegion = new { X = 0, Y = 0, W = 200, H = 768 };
var contentRegion = new { X = 200, Y = 0, W = 1720, H = 768 };

// UI 영역: RRE 시도
if (client.SupportsRRE && RreEncoder.IsRreEfficient(...))
{
    await client.SendFramebufferUpdateAsync(..., RfbProtocol.EncodingType.RRE, ...);
}

// 콘텐츠 영역: Hextile 또는 기본 인코딩
await client.SendFramebufferUpdateAsync(..., client.GetPreferredEncoding(), ...);
```

## 미래 구현 계획

### 1. ZRLE (Zlib RLE) - 최우선 ⭐⭐⭐⭐⭐
- RRE + Zlib 압축
- 최고의 압축률 (일반적으로 80-95%)
- 대역폭 효율성이 가장 중요한 경우

### 2. Tight Encoding ⭐⭐⭐⭐
- JPEG + Zlib 조합
- 사진/비디오 콘텐츠에 최적
- TightVNC 표준

### 3. Cursor Pseudo-encoding ⭐⭐⭐
- 커서를 클라이언트에서 렌더링
- 네트워크 트래픽 감소
- 커서 움직임 최적화

## 참고 자료

- RFB Protocol Specification: https://github.com/rfbproto/rfbproto
- TigerVNC Implementation: https://github.com/TigerVNC/tigervnc
- RealVNC Documentation: https://www.realvnc.com/en/connect/docs/
