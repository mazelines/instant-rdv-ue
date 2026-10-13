# Unreal Engine용 InstantRDV

[English](README.md) | [日本語](README.ja.md) | [한국어](README.ko.md)

이 브랜치에는 Unreal Engine 5.8에서 6.0으로 전환한 InstantRDV 기술 데모의 플러그인과 샘플 프로젝트가 포함되어 있습니다.

InstantRDV는 래스터화 파이프라인의 출력으로 GPU 복셀 장면을 점진적으로 구성하고 유지합니다. RdvGI는 InstantRDV를 기반으로 구현한 실시간 전역 조명(GI) 데모입니다. 하드웨어 레이 트레이싱은 사용하지 않습니다.

## 데모 영상

[![YouTube에서 InstantRDV 기술 데모 보기](https://i.ytimg.com/vi/Mr9syMFeEFI/hqdefault.jpg)](https://www.youtube.com/watch?v=Mr9syMFeEFI)

▶ [YouTube에서 보기](https://www.youtube.com/watch?v=Mr9syMFeEFI)

Unreal Engine 샘플 장면에서 RdvGI를 껐을 때와 켰을 때의 비교입니다.

| RdvGI 끄기 | RdvGI 켜기 |
|---|---|
| ![RdvGI를 끈 샘플 장면](docs/images/rdvgi_off.png) | ![RdvGI를 켠 샘플 장면](docs/images/rdvgi_on.png) |

## 용어

- **RDV — Raster Derived Voxel**: 래스터 패스의 출력으로 구성한 복셀 장면입니다.
- **BBV — Bitmask Brick Voxel**: 복셀의 점유 상태를 비트마스크로 저장하는 브릭 단위의 복셀 그리드입니다.
- **RdvGI**: InstantRDV를 기반으로 구현한 실시간 전역 조명입니다.
- **VSP — Visible Surface Probe**: GI를 계산하기 위해 보이는 표면에 프로브를 배치하는 시스템입니다.

## InstantRDV: 래스터화 결과로 GPU 복셀 장면 구성하기

일반적인 복셀화에는 지오메트리를 추가로 래스터화하는 패스나 CPU에서 GPU로 장면 데이터를 전송하는 과정이 필요합니다. InstantRDV는 MainView 렌더링 과정에서 이미 생성된 DepthBuffer를 사용합니다. 깊이 정보로 월드 공간의 표면 위치를 복원하고 컴퓨트 셰이더로 BBV를 갱신하므로, 추가 지오메트리 패스나 CPU 측 복셀 생성 과정이 필요하지 않습니다. 현재 데모 구현은 MainView 입력만 사용합니다.

설계상 입력이 MainView의 DepthBuffer로 제한되지는 않습니다. ShadowMap, GBuffer 데이터, 사용자 정의 표면 캐시 등 월드 공간의 표면 위치를 복원할 수 있는 래스터 출력이라면 BBV 갱신에 사용할 수 있습니다.

SceneDepth는 지오메트리의 점유 정보를, SceneColor는 BrickRadiance를 제공합니다. GBuffer 데이터를 비롯한 다른 래스터 출력도 같은 방식으로 활용할 수 있습니다.

![래스터 출력에서 BBV를 갱신하는 흐름](docs/images/raster-to-bbv.svg)

BBV 복셀 점유 상태와 브릭 방사휘도의 디버그 화면입니다.

| 복셀 점유 상태 | 브릭 방사휘도 |
|---|---|
| ![BBV 복셀 점유 상태 시각화](docs/images/bbv_visualize_voxel.png) | ![BBV 브릭 방사휘도 시각화](docs/images/bbv_visualize_brick_radiance.png) |

### BBV: Bitmask Brick Voxel

BBV는 복셀을 브릭으로 묶어 고주파 지오메트리 정보와 저주파 머티리얼 정보를 서로 다른 해상도로 표현합니다. 공간을 브릭 그리드로 나누고, 각 브릭에 복셀 점유 상태를 비트마스크로 저장합니다. 그리드는 ToroidalGrid 방식으로 카메라를 따라 이동하며, 빠르게 접근할 수 있도록 조밀하게 배치됩니다. 데모에서는 브릭 하나가 `8 x 8 x 8`개의 복셀을 포함하며 512비트로 표현됩니다. `64³`개의 브릭으로 구성된 그리드는 지오메트리를 `512³` 복셀 해상도로 표현합니다.

BBV의 복셀 구조는 GDC 2025의 [Global Illumination in 'Once Human': A Hybrid Approach for 16km Open World](https://gdcvault.com/AI/gdc-25/)에서 소개한 비트마스크 기반 브릭 복셀 표현과 유사합니다.

BBV는 브릭마다 저주파 데이터도 저장합니다. 데모에서는 SceneColor에서 얻은 방사휘도를 브릭 단위의 거친 해상도로 저장하고, RdvGI 계산 시 BBV 레이 트레이싱의 교차 지점에서 샘플링합니다.

DepthBuffer에서 복원한 표면은 복셀 **주입(injection)** 과정을 통해 점유 정보에 추가됩니다. 카메라 이동이나 관측 범위의 변화로 더 이상 관측되지 않는 영역은 **제거(removal)** 과정을 통해 정리됩니다. 이러한 점진적 갱신으로 BBV를 실제 장면과 실시간으로 동기화합니다.

### BBV 복셀 주입

DepthBuffer의 측면 뷰에서 복원한 표면 샘플을 BBV 브릭 그리드에 직접 주입합니다. 컴퓨트 셰이더는 Wave Intrinsics로 쓰기 횟수를 줄인 뒤, 원자적 연산으로 점유 상태를 갱신합니다.

![BBV 복셀 주입](docs/images/bbv-voxel-injection.svg)

RDV는 MainView의 DepthBuffer처럼 월드 공간의 표면을 복원할 수 있는 데이터를 사용할 수 있습니다. 예를 들어 방향성 광원의 ShadowMap을 추가 래스터 입력으로 사용하면, 광원 뷰에서 처음 교차한 표면의 깊이로 점유 정보를 주입할 수 있습니다. 그러면 MainView에서 가려진 지오메트리도 그림자를 만드는 물체로 관측되는 영역에서 갱신할 수 있습니다.

![MainView와 방향성 광원의 ShadowMap을 사용한 BBV 주입](docs/images/bbv-multiview-injection.svg)

### BBV 복셀 제거

주입만으로는 동적인 장면을 추적할 수 없습니다. BBV는 복셀을 현재 카메라 뷰에 투영하고, 복셀 깊이와 SceneDepth 샘플을 비교해 더 이상 점유되지 않은 영역을 제거합니다. 복셀 깊이가 SceneDepth보다 작으면 점유 정보를 제거하고, 그렇지 않으면 유지합니다.

![BBV 복셀 제거](docs/images/bbv-voxel-removal.svg)

### BBV 레이 트레이싱

BBV는 컴퓨트 셰이더에서 복셀 레이 트레이싱을 지원합니다. 광선을 따라 BBV 점유 정보를 검사해 표면과의 교차를 찾습니다. VSP 가시성 캡처, 프로브 재배치, 디버그 시각화에 이 레이 트레이싱을 사용하며, 하드웨어 레이 트레이싱용 장면은 필요하지 않습니다.

## RdvGI: RDV 위에서 동작하는 프로브 기반 GI

RdvGI는 BBV 레이 트레이싱을 사용하는 GI 구현입니다. Enshrouded GI 및 SurfelGI와 유사하게, 보이는 표면을 바탕으로 프로브를 갱신합니다.

보이는 표면을 기준으로 프로브를 갱신하는 방식은 [Realtime Global Illumination in Enshrouded](https://www.youtube.com/watch?v=57F1ezwH7Mk)에서 소개한 Frustum Probe 접근법을 참고했습니다.

VSP는 DepthBuffer로 관측한 표면 셀을 기준으로 프로브를 희소하게 배치하고 재사용합니다. 각 프로브는 BBV 레이 트레이싱으로 방사휘도와 하늘 가시성을 팔면체 맵에 캡처한 뒤, 결과를 L1 구면 조화 함수(SH)로 투영합니다. 이 결과는 캐스케이드 방식의 IrradianceVolume으로 전파됩니다. 머티리얼에서는 이 볼륨을 삼선형 샘플링해 간접 확산 조사도와 하늘 가시성을 반영한 이미지 기반 조명(IBL)을 구합니다.

프로브의 광선 시작점은 연결된 표면 바깥으로 재배치됩니다. 캡처에는 재배치된 시작점을 사용하므로, GI 계산 시 DDGI 방식의 프로브 유효성 가중치나 가시성 가중 보간을 사용하지 않습니다. 대신 캐스케이드 선택, 경계 디더링, 하드웨어 삼선형 필터링, SH 계산을 수행합니다.

![RdvGI Visible Surface Probe 갱신 흐름](docs/images/rdvgi-vsp-pipeline.svg)

ActiveProbe의 팔면체 맵에는 BBV 레이 트레이싱으로 얻은 방사휘도와 하늘 가시성이 저장됩니다.

![ActiveProbe 팔면체 맵](docs/images/activeprobe_octahedralmap.png)

ActiveProbe와 IrradianceVolume의 디버그 화면입니다.

| ActiveProbe | IrradianceVolume |
|---|---|
| ![VSP ActiveProbe 시각화](docs/images/vsp_visualize_active_probe.png) | ![캐스케이드 IrradianceVolume 시각화](docs/images/vsp_visualize_irradiancevolume.png) |

## GPU 갱신 흐름

InstantRDV는 선택된 MainView 하나를 입력으로 사용합니다. BBV 지오메트리와 VSP는 BasePass 이전에 갱신됩니다. BrickRadiance는 조명 처리 이후, 톤 매핑 이전에 갱신됩니다. 각 갱신은 ViewFamily 안에서 해당 작업을 담당하는 뷰 하나를 기준으로 한 번씩 실행됩니다.

1. **Toroidal BBV 추적**: BBV 그리드가 카메라를 따라 이동합니다. 그리드에 새로 들어오는 브릭은 초기화합니다.
2. **표면 샘플 구성**: MainView의 SceneDepth로 월드 공간의 표면 위치를 복원합니다. ReducedSurface 경로는 깊이, 법선, 법선 신뢰도를 저해상도 표면 샘플로 저장합니다. 지오메트리, BrickRadiance, VSP 갱신이 이 샘플을 공유합니다.
3. **지오메트리의 점진적 갱신**: 표면 샘플로 BBV 복셀을 주입합니다. 제거 단계에서는 현재 깊이 표면보다 앞에 남아 있는 복셀을 지웁니다. 주입 단계에서는 같은 점유 비트마스크 워드에 대한 갱신을 Wave Intrinsics로 모은 뒤 원자적 연산을 수행합니다.
4. **Visible Surface Probe 갱신**: 보이는 표면을 VSP 셀에 대응시키고, 보이는 셀만 모아 목록을 구성합니다. 필요한 프로브를 프로브 풀에서 할당하고, 표면 앵커와 BBV 레이 트레이싱으로 프로브 시작점을 재배치합니다.
5. **프로브 캡처**: 각 활성 프로브는 팔면체 맵의 `6 x 6`개 방향으로 BBV 점유 정보를 추적합니다. 표면과 교차하면 이전 프레임의 BrickRadiance를 읽고, 교차하지 않으면 하늘 가시성을 기록합니다. 두 결과 모두 ActiveProbe 팔면체 맵에 저장합니다. 작업량은 전체 그리드 크기보다 활성 프로브와 광선 요청 수에 따라 달라집니다.
6. **IrradianceVolume 갱신**: 팔면체 맵을 적분해 L1 SH로 변환하고, 프로브가 속한 셀의 IrradianceVolume을 갱신합니다. 프로브가 없는 셀에는 주변 SH 값을 체커보드 방식으로 전파합니다.
7. **BrickRadiance 갱신**: 조명 처리 이후, 톤 매핑 이전의 SceneColor를 점유 표면과 연결합니다. 브릭마다 누적하고 평균을 낸 방사휘도는 다음 프레임의 VSP 광선 결과 처리에 사용됩니다.

머티리얼 셰이더는 갱신 패스와 별도로 캐스케이드 IrradianceVolume을 평가합니다. 캐스케이드를 선택하고 경계 디더링을 적용한 뒤, 표면 법선으로 L1 SH를 계산해 확산 조사도와 하늘 가시성을 반영한 IBL을 구합니다.

## Unreal Engine 연동

### 개요

InstantRDV는 `FSceneViewExtensionBase`를 사용하는 Unreal Engine 플러그인으로 구현되어 있습니다. BasePass 이전 콜백에서 BBV 지오메트리와 VSP 갱신을 추가하고, `BeforeDOF` 후처리 패스에서 BrickRadiance를 갱신합니다. 머티리얼 함수는 플러그인이 관리하는 GPU 리소스를 샘플링합니다.

### 디버그 메뉴

`Tools > Debug > Instant-RDV > Instant-RDV Debug`를 엽니다. 메뉴는 Runtime, BBV, VSP, Debug 섹션으로 구성됩니다. Runtime에서는 InstantRDV, RdvGI, ReducedSurfaceBuffer를 켜거나 끌 수 있습니다. BBV와 VSP에서는 갱신 경로와 계산 옵션을 설정합니다. Debug에서는 BBV, ActiveProbe, IrradianceVolume을 시각화합니다.

![Instant-RDV Debug 메뉴 위치](docs/images/debugmenu_location.png)

![Instant-RDV Debug 메뉴 구성](docs/images/debugmenu_layout.png)

### 레벨 설정 액터

`InstantRdvSettingsActor`는 `Plugins > Instant-RDV > InstantRdv > Public`에 있는 액터 클래스입니다. 레벨에 배치한 뒤 Details 패널에서 레벨별 설정을 변경할 수 있습니다. `Enabled`는 BBV를 포함한 InstantRDV를 켜거나 끄고, `Gi Enabled`는 VSP 갱신과 머티리얼의 GI 출력을 켜거나 끕니다. `Gi Enabled`는 BBV 갱신에 영향을 주지 않습니다. BBV, VSP, 렌더링, LOD, 재배치에 관한 레벨별 매개변수를 설정할 수 있습니다.

![InstantRdvSettingsActor 배치 위치](docs/images/settingsactor_location.png)

![InstantRdvSettingsActor 매개변수](docs/images/settingsactor_param.png)

콘솔 변수(CVar)로 실행 중에 기능을 끌 수 있습니다.

- `r.InstantRdv.Enable`: InstantRDV의 핵심 기능을 제어합니다.
- `r.InstantRdv.Gi.Enable`: RdvGI를 제어합니다.

두 콘솔 변수의 기본값은 켜짐입니다. 반면 설정 액터의 `Enabled`와 `Gi Enabled` 기본값은 꺼짐입니다. 레벨에 배치한 액터에서 필요한 기능을 켜기 전까지는 관련 처리가 실행되지 않습니다.

### 머티리얼 함수

`MF_Irdv_SampleGi`는 플러그인 Content에 포함된 머티리얼 함수입니다. 머티리얼 그래프에서 이 함수를 호출하고, Absolute World Position을 `InSamplePosition`에, VertexNormalWS를 `InSampleNormal`에 연결합니다. 함수의 Custom 노드는 `instant_rdv_material.ush`를 포함하고 `InstantRdvMaterialTryEvaluateIndirectLighting`을 호출합니다.

`Irradiance` 출력은 입사 조사도 `E`이며, `Irradiance/PI`는 `E / PI`입니다. Emissive를 통해 램버트 확산광을 더하려면 Base Color에 `Irradiance/PI`를 곱합니다. `SkyVisibility`는 SkyLight와 IBL에 적용하는 차폐 계수입니다.

![MF_Irdv_SampleGi 위치](docs/images/customnode_content_mf.png)

![MF_Irdv_SampleGi를 사용하는 머티리얼 그래프](docs/images/customnode_sampling_gi.png)

## 구현 참고 사항

- 콘솔 변수로 MainView 주입과 제거, 방사휘도 갱신, VSP 갱신, 저해상도 표면 처리 경로를 각각 관찰할 수 있습니다.
- VSP는 프로브 풀, 활성 프로브 목록, 광선 요청 및 결과 데이터, 프로브 아틀라스, IrradianceVolume을 GPU 리소스로 관리합니다.
- GI가 꺼져 있어도 VSP는 GPU 리소스를 유지합니다.
- MainView 바깥의 BrickRadiance는 갱신되지 않습니다. 방사휘도가 높은 브릭은 화면 밖으로 이동한 뒤에도 남아 있습니다.

## 초기 매개변수

| 항목 | 기본값 |
|---|---:|
| BBV 그리드 | `64 x 64 x 64` 브릭 |
| BBV 브릭 크기 | `300 cm` |
| VSP 그리드 | 캐스케이드마다 `16 x 16 x 16` 셀 |
| VSP 셀 크기 | `200 cm` |
| VSP 캐스케이드 수 | `5` |
| 희소 프로브 풀 | 모든 캐스케이드가 공유하는 프로브 `8192`개 |

## 라이선스

MIT 라이선스를 따릅니다. [LICENSE](LICENSE)를 참조하세요.
