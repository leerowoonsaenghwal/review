#ifndef CATISLAND_CURVE_INCLUDED
#define CATISLAND_CURVE_INCLUDED
// 둥근 세상 (docs/ART_DIRECTION.md 4장): 카메라가 보는 곳(초점)에서 화면 안쪽으로 시작 거리를 넘으면
// y -= k * 거리^2. 모든 세상 재질과 그림자·깊이 패스가 같은 휨을 쓴다 (값은 IslandCamera 가 넣는다)
float4 _CurveCenterDir;   // xy = 초점(x,z), zw = 카메라가 보는 방향(x,z, 정규화)
float2 _CurveParams;      // x = k, y = 시작 거리
float3 CurveWorld(float3 p)
{
    float d = max(dot(p.xz - _CurveCenterDir.xy, _CurveCenterDir.zw) - _CurveParams.y, 0.0);
    p.y -= _CurveParams.x * d * d;
    return p;
}
#endif
