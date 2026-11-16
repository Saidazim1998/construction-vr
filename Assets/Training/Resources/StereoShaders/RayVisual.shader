// The beam of a controller or hand ray, written on the URP shader library.
// Stands in for the Interaction SDK's "Interaction/OculusRayVisual" (same properties), which is built on the
// legacy CG includes and is therefore drawn in the left eye only in this Unity version (see UiImage.shader).
// StereoShaderFix swaps it in at run time.
Shader "ConstructionVR/Interaction/RayVisual"
{
    Properties
    {
        _Color ("Color", Color) = (1,1,1,1)
        _Feather ("Feather", Float) = 4
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "IgnoreProjector" = "True" "RenderType" = "Transparent" }

        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha

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
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float along : TEXCOORD0;            // 0 at the start of the beam, 1 at its end
                UNITY_VERTEX_OUTPUT_STEREO
            };

            half4 _Color;
            float _Feather;

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.along = input.positionOS.z + 0.5;        // the beam is a unit cube stretched along z
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float fromMiddle = input.along - 0.5;
                half4 color = _Color;
                color.a = saturate(1 - _Feather * fromMiddle * fromMiddle);     // fades out towards both ends
                return color;
            }
            ENDHLSL
        }
    }
}
