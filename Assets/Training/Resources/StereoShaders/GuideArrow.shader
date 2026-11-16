// The guide arrow (and the plate behind its caption): a plain coloured shape with a little shading that
// stays faintly visible through walls and equipment, so the learner can find what the current step is about.
// Written on the URP shader library: shaders on the legacy CG includes are drawn in one eye only in this
// Unity version (see UiImage.shader).
Shader "ConstructionVR/Guide/Arrow"
{
    Properties
    {
        _Color ("Color", Color) = (0.18, 0.83, 0.63, 1)
        _Hidden ("Opacity behind things", Range(0, 1)) = 0.3
        _Shade ("Shading", Range(0, 1)) = 0.4
    }

    SubShader
    {
        Tags { "Queue" = "Transparent+100" "RenderType" = "Transparent" "IgnoreProjector" = "True" }

        Cull Off
        Blend SrcAlpha OneMinusSrcAlpha

        HLSLINCLUDE
        #pragma target 3.5
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
            half light : TEXCOORD0;
            UNITY_VERTEX_OUTPUT_STEREO
        };

        half4 _Color;
        half _Hidden;
        half _Shade;

        Varyings Vert(Attributes input)
        {
            Varyings output = (Varyings)0;
            UNITY_SETUP_INSTANCE_ID(input);
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
            output.positionCS = TransformObjectToHClip(input.positionOS.xyz);

            // lit from above and a little from the side, whichever way the surface faces the viewer
            float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
            half facing = abs(dot(normalWS, normalize(float3(0.35, 0.85, 0.4))));
            output.light = 1.0 - _Shade + _Shade * facing;
            return output;
        }

        half4 FragVisible(Varyings input) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            return half4(_Color.rgb * input.light, _Color.a);
        }

        half4 FragHidden(Varyings input) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            return half4(_Color.rgb * input.light, _Color.a * _Hidden);
        }
        ENDHLSL

        // What is in plain view: solid, and marked in the stencil so that the faint pass leaves it alone.
        Pass
        {
            Name "Visible"
            Tags { "LightMode" = "UniversalForward" }

            ZTest LEqual
            ZWrite On
            Stencil
            {
                Ref 64
                ReadMask 64
                WriteMask 64
                Comp Always
                Pass Replace
            }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragVisible
            ENDHLSL
        }

        // What is behind something else: faint.
        Pass
        {
            Name "Hidden"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            ZTest Greater
            ZWrite Off
            Stencil
            {
                Ref 64
                ReadMask 64
                Comp NotEqual
            }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragHidden
            ENDHLSL
        }
    }
}
