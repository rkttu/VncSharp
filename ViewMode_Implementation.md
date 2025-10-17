# VNC 클라이언트 화면 크기 변경 처리 구현

## 개요

VNC 클라이언트가 창 크기를 변경할 때 서버 측에서 렌더링을 어떻게 처리할지에 대한 3가지 모드를 구현했습니다.

## 구현된 기능

### 1. **ViewMode (보기 모드)**

#### Fit 모드 (화면에 맞춤) - 기본값
- 프레임버퍼의 종횡비(aspect ratio)를 유지하며 컨트롤 크기에 맞춤
- 남는 공간은 letterbox(검은 테두리)로 표시
- **단축키**: `Ctrl + 1`

#### Stretch 모드 (늘이기)
- 프레임버퍼를 컨트롤 전체 크기에 맞춰 늘림
- 종횡비가 변경될 수 있음 (왜곡 가능)
- **단축키**: `Ctrl + 2`

#### Actual Size 모드 (실제 크기 - 1:1)
- 프레임버퍼를 실제 픽셀 크기로 표시
- 팬닝(Panning)과 줌(Zoom) 기능 지원
- **단축키**: `Ctrl + 3`

### 2. **줌 기능 (Actual Size 모드에서만 작동)**

- **확대**: `Ctrl + +` 또는 메뉴에서 선택
- **축소**: `Ctrl + -` 또는 메뉴에서 선택
- **리셋**: `Ctrl + 0` (100%로 복귀)
- **마우스 휠**: `Ctrl + 휠`로도 줌 가능
- 줌 범위: 10% ~ 500%

### 3. **팬닝 기능 (Actual Size 모드에서만 작동)**

- **마우스 드래그**: 왼쪽 버튼으로 드래그하여 화면 이동
- **마우스 휠**: 수직 스크롤

## 사용 방법

### 메뉴 사용
```
보기(V) 메뉴:
├─ 화면에 맞춤 (Ctrl+1)          ? 기본 모드
├─ 늘이기 (Ctrl+2)
├─ 실제 크기 - 팬/줌 (Ctrl+3)
├─ ────────────
├─ 확대 (Ctrl++)
├─ 축소 (Ctrl+-)
└─ 줌 리셋 (Ctrl+0)
```

### 상태바 정보
프로그램 하단 상태바에서 현재 설정 확인:
```
[중지] 서버 | [없음] 비밀번호 | 클라이언트: 0 | FPS: 0 | 보기: Fit | 줌: 100%
```

## 구현 원리

### VNC 프로토콜 관점

이 구현은 **클라이언트 측 스케일링** 방식을 따릅니다:

1. **서버 프레임버퍼 크기는 고정** (1024x768)
2. 클라이언트(이 프로그램의 프리뷰)는 자체적으로 viewport를 관리
3. VNC 클라이언트가 창 크기를 변경해도 서버는 동일한 해상도로 데이터 전송
4. 각 클라이언트는 독립적으로 자신의 화면 크기를 결정

### 장점

? **VNC 표준 동작과 일치** - 대부분의 VNC 뷰어가 사용하는 방식  
? **서버 부하 최소화** - 프레임버퍼 크기 변경 없이 동작  
? **다중 클라이언트 지원** - 각 클라이언트가 다른 viewport 가능  
? **안정성** - 동적 해상도 변경의 복잡성 회피  

### 대안: Dynamic Resolution (구현 안 됨)

RFB 프로토콜의 `ExtendedDesktopSize` pseudo-encoding을 사용하면 클라이언트 요청에 따라 서버 프레임버퍼 크기를 동적으로 변경할 수 있습니다. 하지만:

? 프로토콜 복잡도 증가  
? 모든 클라이언트에 영향  
? 실제 화면 캡처 시 해상도 변경 어려움  

## 코드 구조

### FrameBufferControl.cs
```csharp
public enum ViewMode
{
    Fit,          // 비율 유지 + Letterbox
    Stretch,      // 전체 화면 늘이기
    ActualSize    // 1:1 + 팬/줌
}

public class FrameBufferControl : Control
{
    public ViewMode ViewMode { get; set; }
    public float ZoomLevel { get; set; }  // 0.1 ~ 5.0
    
    // 팬닝 상태
    private Point _panOffset;
    private bool _isPanning;
}
```

### MainFormWithView.cs
```csharp
// 뷰 모드 변경
private void SetViewMode(ViewMode mode)
{
    _frameBufferControl.ViewMode = mode;
    // 메뉴 체크 상태 업데이트
    // 상태바 라벨 업데이트
}

// 줌 조절
private void OnZoomIn(object? sender, EventArgs e)
{
    _frameBufferControl.ZoomLevel += 0.25f;
    UpdateZoomLabel();
}
```

## 테스트 방법

1. 프로그램 실행
2. `보기` 메뉴에서 각 모드 테스트:
   - **Fit 모드**: 창 크기 변경 시 종횡비 유지 확인
   - **Stretch 모드**: 창 크기에 맞춰 늘어나는지 확인
   - **Actual Size 모드**:
     - 마우스로 드래그하여 팬닝 테스트
     - `Ctrl + 휠`로 줌 테스트
     - 줌 후 팬닝 동작 확인

3. VNC 클라이언트로 연결 (`localhost:5900`)
   - 서버 프레임버퍼는 1024x768 고정
   - VNC 뷰어에서 창 크기 조절은 뷰어가 자체 처리

## 향후 개선 가능 사항

1. **스크롤바 추가**: Actual Size 모드에서 팬닝 위치 표시
2. **미니맵**: 전체 프레임버퍼 중 현재 보이는 영역 시각화
3. **줌 프리셋**: 50%, 100%, 200% 빠른 전환 버튼
4. **고품질 보간**: Fit/Stretch 모드에서 더 부드러운 스케일링
5. **ExtendedDesktopSize 지원**: 동적 해상도 변경 구현 (선택적)

## 참고자료

- [RFB Protocol 3.8 Specification](https://www.rfc-editor.org/rfc/rfc6143.html)
- VNC 표준 뷰어: TigerVNC, RealVNC, TightVNC
- ExtendedDesktopSize: Section 7.8.2 in RFB 3.8 spec
