// A glow drawn over a model (the thing the hand ray points at, the control the current step is about) and the
// see-through copy of a carried thing at the place it has to be put. The model's own meshes are drawn once
// more with this shader, pushed out a hair along the normals: a soft fill and a bright rim.
// Written on the URP shader library: shaders on the legacy CG includes are drawn in one eye only in this
// Unity version (see UiImage.shader).
Shader "ConstructionVR/Interaction/Highlight"
{
    Properties
    {
        _Color ("Color", Color) = (1, 1, 1, 1)
        _Fill ("Fill opacity", Range(0, 1)) = 0.16
        _Rim ("Rim opacity", Range(0, 1)) = 0.85
        _RimPower ("Rim sharpness", Range(0.5, 8)) = 2.2
        _Push ("Push out (metres)", Range(0, 0.01)) = 0.0015
        _Pulse ("Pulse depth", Range(0, 1)) = 0
    }

    SubShader
    {
        Tags { "Queue" = "Transparent+50" "RenderType" = "Transparent" "IgnoreProjector" = "True" }

        Pass
        {
            Name "Highlight"

            Cull Back
            ZTest LEqual
            ZWrite Off
            Offset -1, -1
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float3 viewWS : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            CBUFFER_START(UnityPerMaterial)
            half4 _Color;
            half _Fill;
            half _Rim;
            half _RimPower;
            float _Push;
            half _Pulse;
            CBUFFER_END

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz) + normalWS * _Push;
                output.positionCS = TransformWorldToHClip(positionWS);
                output.normalWS = normalWS;
                output.viewWS = GetWorldSpaceViewDir(positionWS);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                half facing = saturate(dot(normalize(input.normalWS), normalize(input.viewWS)));
                half rim = pow(1.0 - facing, _RimPower);
                half pulse = 1.0 - _Pulse * (0.5 + 0.5 * sin(_Time.y * 5.0));
                half alpha = saturate(_Fill + _Rim * rim) * pulse * _Color.a;
                return half4(_Color.rgb, alpha);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
