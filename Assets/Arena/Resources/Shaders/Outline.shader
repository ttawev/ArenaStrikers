// Toon-обводка героев («вывернутая оболочка»): модель рисуется второй раз изнанкой, раздутая по нормалям на _Width пикселей
// (при высоте экрана 1080; толщина на экране не зависит от расстояния). Спереди её закрывает сама модель — по краям остаётся контур.
// Лежит в Resources, чтобы попасть в сборку (материал создаётся кодом: HeroModel.Outline.cs).
Shader "Arena/Outline"
{
    Properties
    {
        _Color ("Color", Color) = (0.04, 0.05, 0.1, 1)
        _Width ("Width, px at 1080p", Float) = 2.5
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry+10" }
        Pass
        {
            Name "Outline"
            Tags { "LightMode"="SRPDefaultUnlit" }
            Cull Front
            ZWrite On
            ZTest LEqual
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _Width;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; UNITY_VERTEX_OUTPUT_STEREO };
            Varyings vert (Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float4 cs = TransformObjectToHClip(v.positionOS.xyz);
                float3 nWS = TransformObjectToWorldNormal(v.normalOS);
                float2 nCS = mul((float3x3)UNITY_MATRIX_VP, nWS).xy;
                float len = length(nCS);
                if (len > 1e-5)
                {
                    float px = _Width * _ScreenParams.y / 1080.0;               // толщина в пикселях этого экрана
                    cs.xy += (nCS / len) * (2.0 * px / _ScreenParams.xy) * cs.w; // сдвиг в экранном пространстве
                }
                o.positionCS = cs;
                return o;
            }
            half4 frag (Varyings i) : SV_Target { return _Color; }
            ENDHLSL
        }
    }
}
