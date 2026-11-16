// Surface of a ConstructionVR.Player.PlanarMirror: shows the reflection texture of the eye that is
// rendering (single-pass instanced stereo), sampled at the pixel's own screen position.
Shader "ConstructionVR/PlanarMirror"
{
    Properties
    {
        _LeftEyeTex ("Left eye reflection", 2D) = "black" {}
        _RightEyeTex ("Right eye reflection", 2D) = "black" {}
        _Tint ("Tint", Color) = (0.93, 0.95, 0.96, 1)
        [Toggle] _FlipY ("Flip vertically", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        Pass
        {
            Name "Mirror"
            Tags { "LightMode" = "UniversalForward" }
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_LeftEyeTex);
            SAMPLER(sampler_LeftEyeTex);
            TEXTURE2D(_RightEyeTex);
            SAMPLER(sampler_RightEyeTex);

            CBUFFER_START(UnityPerMaterial)
                half4 _Tint;
                float _FlipY;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 screenPos : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.screenPos = ComputeScreenPos(output.positionCS);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.screenPos.xy / input.screenPos.w;
                if (_FlipY > 0.5)
                {
                    uv.y = 1.0 - uv.y;
                }

                half4 left = SAMPLE_TEXTURE2D(_LeftEyeTex, sampler_LeftEyeTex, uv);
                half4 right = SAMPLE_TEXTURE2D(_RightEyeTex, sampler_RightEyeTex, uv);
                half3 colour = (unity_StereoEyeIndex == 0 ? left.rgb : right.rgb) * _Tint.rgb;
                return half4(colour, 1);
            }
            ENDHLSL
        }
    }
}
