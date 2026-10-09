// 고양이 섬 기본 재질: 무광, 부드러운 명암 경계, 검지 않은 그림자, 바닥 근처 접촉 그늘.
// 털 텍스처와 노멀맵(구운 조각 디테일)을 받는다. 눈처럼 반짝여야 하는 곳은 _Gloss 로 작은 하이라이트.
Shader "CatIsland/SoftLit"
{
    Properties
    {
        _BaseColor ("Base Color", Color) = (1,1,1,1)
        _BaseMap ("Base Map", 2D) = "white" {}
        [Normal] _BumpMap ("Normal Map", 2D) = "bump" {}
        _BumpScale ("Normal Scale", Range(0,2)) = 1
        _ShadowTint ("Shadow Tint", Color) = (0.86,0.80,0.92,1)
        _Wrap ("Wrap", Range(0,1)) = 0.45
        _RimStrength ("Rim", Range(0,1)) = 0.18
        _GroundAO ("Ground AO", Range(0,1)) = 0.22
        _Gloss ("Gloss", Range(0,1)) = 0
        _Emission ("Self Light", Range(0,1)) = 0
        [Toggle(_WORLDUV)] _WorldUV ("World UV (ground)", Float) = 0
        // 사진 고양이 털 색 (CatCoat): 털 마스크 RGBA = 기본·무늬(포인트)·흰색·두 번째 색 비율, 원래 색 → 새 색
        [Toggle(_COATMASK)] _UseCoatMask ("Coat Mask", Float) = 0
        _CoatMask ("Coat Mask", 2D) = "black" {}
        _OldBase ("Old Base", Color) = (1,1,1,1)
        _OldDark ("Old Dark", Color) = (0,0,0,1)
        _OldWhite ("Old White", Color) = (1,1,1,1)
        _OldSecond ("Old Second", Color) = (1,0.5,0,1)
        _NewBase ("New Base", Color) = (1,1,1,1)
        _NewDark ("New Dark", Color) = (0,0,0,1)
        _NewWhite ("New White", Color) = (1,1,1,1)
        _NewSecond ("New Second", Color) = (1,0.5,0,1)
        // 고양이를 가리는 용품 (OccluderFade): _FADE 를 켜고 이 비율만큼만 점무늬로 그린다
        _Fade ("Fade", Range(0,1)) = 1
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BaseColor;
            half4 _ShadowTint;
            half _BumpScale;
            half _Wrap;
            half _RimStrength;
            half _GroundAO;
            half _Gloss;
            half _Emission;
            half4 _OldBase, _OldDark, _OldWhite, _OldSecond, _NewBase, _NewDark, _NewWhite, _NewSecond;
            half _Fade;
        CBUFFER_END

        #include "Curve.hlsl"
        // 점무늬로 비치기 (불투명 그대로, 정렬 없이): 4x4 바이어 행렬 문턱보다 _Fade 가 작은 픽셀은 버린다.
        // 켜진 재질(_FADE)만 discard 를 가진다 - 다른 물체는 타일 GPU 의 숨은 면 제거를 그대로 쓴다
        void FadeClip(float2 pixel)
        {
        #if defined(_FADE)
            uint2 q = (uint2)pixel & 3;
            const float b[16] = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };
            clip(_Fade - (b[q.y * 4 + q.x] + 0.5) / 16.0);
        #endif
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma shader_feature_local _NORMALMAP
            #pragma multi_compile_local _ _WORLDUV   // (런타임에 코드로 만든 재질만 쓰므로 shader_feature 면 빌드에서 빠진다: 땅·마루·러그 무늬)
            #pragma multi_compile_local _ _COATMASK
            #pragma multi_compile_local _ _FADE
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            TEXTURE2D(_BumpMap); SAMPLER(sampler_BumpMap);
            TEXTURE2D(_CoatMask); SAMPLER(sampler_CoatMask);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 tangentOS : TANGENT;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float4 tangentWS : TEXCOORD2;
                float2 uv : TEXCOORD3;
                half fogFactor : TEXCOORD4;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings vert(Attributes v)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                o.positionWS = CurveWorld(TransformObjectToWorld(v.positionOS.xyz));
                VertexNormalInputs n = GetVertexNormalInputs(v.normalOS, v.tangentOS);
                o.normalWS = n.normalWS;
                o.tangentWS = float4(n.tangentWS, v.tangentOS.w * GetOddNegativeScale());
            #if _WORLDUV
                o.uv = TransformObjectToWorld(v.positionOS.xyz).xz * _BaseMap_ST.xy + _BaseMap_ST.zw;   // (땅: 세상 좌표로 크게 깔아 반복이 안 보이게)
            #else
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
            #endif
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                FadeClip(i.positionCS.xy);
                float3 n = normalize(i.normalWS);
            #if _NORMALMAP
                half3 nTS = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, i.uv), _BumpScale);
                float3 t = normalize(i.tangentWS.xyz);
                float3 b = cross(n, t) * i.tangentWS.w;
                n = normalize(TransformTangentToWorld(nTS, half3x3(t, b, n)));
            #endif
                float3 v = normalize(GetWorldSpaceViewDir(i.positionWS));
                float4 shadowCoord = TransformWorldToShadowCoord(i.positionWS);
                Light light = GetMainLight(shadowCoord);

                half ndl = dot(n, light.direction);
                half wrapped = saturate((ndl + _Wrap) / (1.0h + _Wrap));
                wrapped = smoothstep(0.0h, 1.0h, wrapped);
                half lit = wrapped * lerp(0.35h, 1.0h, light.shadowAttenuation);

                half3 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv).rgb * _BaseColor.rgb;
            #if defined(_COATMASK)
                // 품종 털 무늬는 그대로, 색만 바꾼다: 비율대로 섞은 새 색 x 원래 털결(밝기 비) + 칸에 없는 색(볼터치 등)은 원래대로
                half4 w = SAMPLE_TEXTURE2D(_CoatMask, sampler_CoatMask, i.uv);
                half rest = saturate(1.0h - w.r - w.g - w.b - w.a);
                half3 oldMix = w.r * _OldBase.rgb + w.g * _OldDark.rgb + w.b * _OldWhite.rgb + w.a * _OldSecond.rgb;
                half3 newMix = w.r * _NewBase.rgb + w.g * _NewDark.rgb + w.b * _NewWhite.rgb + w.a * _NewSecond.rgb;
                half lumA = dot(albedo, half3(0.2126h, 0.7152h, 0.0722h));
                half lumO = dot(oldMix, half3(0.2126h, 0.7152h, 0.0722h)) + rest * lumA;
                half fur = clamp(lumA / max(lumO, 0.02h), 0.75h, 1.25h);
                albedo = newMix * fur + rest * albedo;
            #endif
                half3 ambient = SampleSH(n) * _ShadowTint.rgb;
                half3 col = albedo * (ambient + light.color * lit);

                half rim = pow(1.0h - saturate(dot(n, v)), 3.0h) * _RimStrength;
                col += rim * light.color * albedo;

                // 눈: 작고 선명한 하이라이트 (동물의 숲식 유광 눈)
                half3 h = normalize(light.direction + v);
                half spec = pow(saturate(dot(n, h)), lerp(8.0h, 160.0h, _Gloss)) * _Gloss * 1.6h;
                col += spec * light.color * light.shadowAttenuation;

                half ao = lerp(1.0h - _GroundAO, 1.0h, saturate(i.positionWS.y * 4.0h));
                col *= ao;
                col = lerp(col, albedo, _Emission);

                col = MixFog(col, i.fogFactor);
                return half4(col, 1.0h);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings { float4 positionCS : SV_POSITION; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                float3 positionWS = CurveWorld(TransformObjectToWorld(v.positionOS.xyz));
                float3 normalWS = TransformObjectToWorldNormal(v.normalOS);
            #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 lightDir = normalize(_LightPosition - positionWS);
            #else
                float3 lightDir = _LightDirection;
            #endif
                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDir));
            #if UNITY_REVERSED_Z
                positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
            #else
                positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
            #endif
                o.positionCS = positionCS;
                return o;
            }

            half4 frag(Varyings i) : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile_local _ _FADE

            struct Attributes { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                o.positionCS = TransformWorldToHClip(CurveWorld(TransformObjectToWorld(v.positionOS.xyz)));
                return o;
            }

            half4 frag(Varyings i) : SV_Target { FadeClip(i.positionCS.xy); return 0; }
            ENDHLSL
        }
    }
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
