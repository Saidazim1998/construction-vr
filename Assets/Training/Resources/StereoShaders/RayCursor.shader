// The round cursor at the end of a ray, written on the URP shader library.
// Stands in for the Interaction SDK's "Interaction/OculusRayCursor" (same properties), which is built on the
// legacy CG includes and is therefore drawn in the left eye only in this Unity version (see UiImage.shader).
// StereoShaderFix swaps it in at run time.
Shader "ConstructionVR/Interaction/RayCursor"
{
    Properties
    {
        _Radius ("Radius", Float) = 0.5
        _OutlineThickness ("Outline Thickness", Float) = 0.05
        _Color ("Inner Color", Color) = (1,1,1,1)
        _OutlineColor ("Outline Color", Color) = (0.5,0.5,0.5,1)
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+10" "IgnoreProjector" = "True" }

        Cull Off
        ZTest LEqual
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha
        Offset -5, -5       // in front of the panel it lies on

        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            float _Radius;
            float _OutlineThickness;
            half4 _Color;
            half4 _OutlineColor;

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                // distance from the centre of the quad, and how many of them fit in one pixel (for smooth edges)
                float fromCentre = distance(input.uv, float2(0.5, 0.5));
                float perPixel = rcp(max(length(float2(ddx(fromCentre), ddy(fromCentre))), 1e-6));

                float outside = (fromCentre - _Radius) * perPixel;                      // > 0 beyond the disc
                float inOutline = (fromCentre + _OutlineThickness - _Radius) * perPixel; // > 0 inside the outline band

                half4 color = lerp(_Color, _OutlineColor, saturate(inOutline));
                color.a *= 1.0 - saturate(outside);
                return color;
            }
            ENDHLSL
        }
    }
}
