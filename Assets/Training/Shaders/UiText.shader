// The text shader of the project: TextMesh Pro's "Mobile/Distance Field" rewritten on the URP shader library.
//
// Why it exists: see UiImage.shader. The stock TextMesh Pro shaders are built on the legacy CG includes and are
// drawn in the left eye only with Single Pass Instanced / Multiview rendering in this Unity version.
// Properties, keywords and maths are those of TMP_SDF-Mobile.shader, so font materials, outlines, underlays,
// clipping and the TextMesh Pro material inspector keep working.
Shader "ConstructionVR/UI/Text"
{
    Properties
    {
        _FaceColor          ("Face Color", Color) = (1,1,1,1)
        _FaceDilate         ("Face Dilate", Range(-1,1)) = 0

        _OutlineColor       ("Outline Color", Color) = (0,0,0,1)
        _OutlineWidth       ("Outline Thickness", Range(0,1)) = 0
        _OutlineSoftness    ("Outline Softness", Range(0,1)) = 0

        _UnderlayColor      ("Border Color", Color) = (0,0,0,.5)
        _UnderlayOffsetX    ("Border OffsetX", Range(-1,1)) = 0
        _UnderlayOffsetY    ("Border OffsetY", Range(-1,1)) = 0
        _UnderlayDilate     ("Border Dilate", Range(-1,1)) = 0
        _UnderlaySoftness   ("Border Softness", Range(0,1)) = 0

        _WeightNormal       ("Weight Normal", float) = 0
        _WeightBold         ("Weight Bold", float) = .5

        _ShaderFlags        ("Flags", float) = 0
        _ScaleRatioA        ("Scale RatioA", float) = 1
        _ScaleRatioB        ("Scale RatioB", float) = 1
        _ScaleRatioC        ("Scale RatioC", float) = 1

        _MainTex            ("Font Atlas", 2D) = "white" {}
        _TextureWidth       ("Texture Width", float) = 512
        _TextureHeight      ("Texture Height", float) = 512
        _GradientScale      ("Gradient Scale", float) = 5
        _ScaleX             ("Scale X", float) = 1
        _ScaleY             ("Scale Y", float) = 1
        _PerspectiveFilter  ("Perspective Correction", Range(0, 1)) = 0.875
        _Sharpness          ("Sharpness", Range(-1,1)) = 0

        _VertexOffsetX      ("Vertex OffsetX", float) = 0
        _VertexOffsetY      ("Vertex OffsetY", float) = 0

        _ClipRect           ("Clip Rect", vector) = (-32767, -32767, 32767, 32767)
        _MaskSoftnessX      ("Mask SoftnessX", float) = 0
        _MaskSoftnessY      ("Mask SoftnessY", float) = 0

        _StencilComp        ("Stencil Comparison", Float) = 8
        _Stencil            ("Stencil ID", Float) = 0
        _StencilOp          ("Stencil Operation", Float) = 0
        _StencilWriteMask   ("Stencil Write Mask", Float) = 255
        _StencilReadMask    ("Stencil Read Mask", Float) = 255

        _CullMode           ("Cull Mode", Float) = 0
        _ColorMask          ("Color Mask", Float) = 15

        // 4 = LEqual (hidden behind what stands in front, like any object), 8 = Always (drawn over the world:
        // the panels that go with the learner). Set on the material, not by the canvas.
        [Enum(UnityEngine.Rendering.CompareFunction)] _UiZTest ("Depth test", Float) = 4
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
        }

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull [_CullMode]
        ZWrite Off
        Lighting Off
        ZTest [_UiZTest]
        Blend One OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            HLSLPROGRAM
            #pragma vertex VertShader
            #pragma fragment PixShader
            #pragma target 3.5

            #pragma shader_feature __ OUTLINE_ON
            #pragma shader_feature __ UNDERLAY_ON UNDERLAY_INNER

            #pragma multi_compile __ UNITY_UI_CLIP_RECT
            #pragma multi_compile __ UNITY_UI_ALPHACLIP

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            #if defined(UNDERLAY_ON) || defined(UNDERLAY_INNER)
                #define TEXT_UNDERLAY 1
            #endif

            struct vertex_t
            {
                float4 vertex    : POSITION;
                float3 normal    : NORMAL;
                half4  color     : COLOR;
                float4 texcoord0 : TEXCOORD0;
                float2 texcoord1 : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct pixel_t
            {
                float4 vertex       : SV_POSITION;
                half4  faceColor    : COLOR;
                half4  outlineColor : COLOR1;
                float4 texcoord0    : TEXCOORD0;        // Texture UV, Mask UV
                half4  param        : TEXCOORD1;        // Scale(x), BiasIn(y), BiasOut(z), Bias(w)
                half4  mask         : TEXCOORD2;        // Position in clip space(xy), Softness(zw)
                #ifdef TEXT_UNDERLAY
                float4 texcoord1     : TEXCOORD3;       // Texture UV, alpha, reserved
                half2  underlayParam : TEXCOORD4;       // Scale(x), Bias(y)
                #endif
                UNITY_VERTEX_OUTPUT_STEREO
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            half4 _FaceColor;
            float _FaceDilate;
            half4 _OutlineColor;
            float _OutlineWidth;
            float _OutlineSoftness;
            half4 _UnderlayColor;
            float _UnderlayOffsetX;
            float _UnderlayOffsetY;
            float _UnderlayDilate;
            float _UnderlaySoftness;
            float _WeightNormal;
            float _WeightBold;
            float _ScaleRatioA;
            float _ScaleRatioC;
            float _TextureWidth;
            float _TextureHeight;
            float _GradientScale;
            float _ScaleX;
            float _ScaleY;
            float _PerspectiveFilter;
            float _Sharpness;
            float _VertexOffsetX;
            float _VertexOffsetY;
            float4 _ClipRect;
            float _MaskSoftnessX;
            float _MaskSoftnessY;
            float _UIMaskSoftnessX;
            float _UIMaskSoftnessY;
            int _UIVertexColorAlwaysGammaSpace;

            // UnityUI.cginc
            float3 UiGammaToLinear(float3 value)
            {
                float3 low = 0.0849710 * value - 0.000163029;
                float3 high = value * (value * (value * 0.265885 + 0.736584) - 0.00980184) + 0.00319697;
                const float3 split = (float3)0.0725490;
                return (value < split) ? low : high;
            }

            pixel_t VertShader(vertex_t input)
            {
                pixel_t output = (pixel_t)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                float bold = step(input.texcoord0.w, 0);

                float4 vert = input.vertex;
                vert.x += _VertexOffsetX;
                vert.y += _VertexOffsetY;
                float4 vPosition = TransformObjectToHClip(vert.xyz);

                float2 pixelSize = vPosition.w;
                pixelSize /= float2(_ScaleX, _ScaleY) * abs(mul((float2x2)UNITY_MATRIX_P, _ScreenParams.xy));

                float scale = rsqrt(dot(pixelSize, pixelSize));
                scale *= abs(input.texcoord0.w) * _GradientScale * (_Sharpness + 1);
                if (UNITY_MATRIX_P[3][3] == 0)
                {
                    float3 normalWS = TransformObjectToWorldNormal(input.normal.xyz);
                    float3 viewDirWS = normalize(_WorldSpaceCameraPos - TransformObjectToWorld(vert.xyz));
                    scale = lerp(abs(scale) * (1 - _PerspectiveFilter), scale, abs(dot(normalWS, viewDirWS)));
                }

                float weight = lerp(_WeightNormal, _WeightBold, bold) / 4.0;
                weight = (weight + _FaceDilate) * _ScaleRatioA * 0.5;

                float layerScale = scale;

                scale /= 1 + (_OutlineSoftness * _ScaleRatioA * scale);
                float bias = (0.5 - weight) * scale - 0.5;
                float outline = _OutlineWidth * _ScaleRatioA * 0.5 * scale;

                #ifndef UNITY_COLORSPACE_GAMMA
                if (_UIVertexColorAlwaysGammaSpace)
                {
                    input.color.rgb = UiGammaToLinear(input.color.rgb);
                }
                #endif

                float opacity = input.color.a;
                #ifdef TEXT_UNDERLAY
                opacity = 1.0;
                #endif

                half4 faceColor = half4(input.color.rgb, opacity) * _FaceColor;
                faceColor.rgb *= faceColor.a;

                half4 outlineColor = _OutlineColor;
                outlineColor.a *= opacity;
                outlineColor.rgb *= outlineColor.a;
                outlineColor = lerp(faceColor, outlineColor, sqrt(min(1.0, (outline * 2))));

                #ifdef TEXT_UNDERLAY
                layerScale /= 1 + ((_UnderlaySoftness * _ScaleRatioC) * layerScale);
                float layerBias = (.5 - weight) * layerScale - .5 - ((_UnderlayDilate * _ScaleRatioC) * .5 * layerScale);

                float x = -(_UnderlayOffsetX * _ScaleRatioC) * _GradientScale / _TextureWidth;
                float y = -(_UnderlayOffsetY * _ScaleRatioC) * _GradientScale / _TextureHeight;
                float2 layerOffset = float2(x, y);
                #endif

                // UV of the masking texture
                float4 clampedRect = clamp(_ClipRect, -2e10, 2e10);
                float2 maskUV = (vert.xy - clampedRect.xy) / (clampedRect.zw - clampedRect.xy);

                output.vertex = vPosition;
                output.faceColor = faceColor;
                output.outlineColor = outlineColor;
                output.texcoord0 = float4(input.texcoord0.x, input.texcoord0.y, maskUV.x, maskUV.y);
                output.param = half4(scale, bias - outline, bias + outline, bias);

                const half2 maskSoftness = half2(max(_UIMaskSoftnessX, _MaskSoftnessX), max(_UIMaskSoftnessY, _MaskSoftnessY));
                output.mask = half4(vert.xy * 2 - clampedRect.xy - clampedRect.zw, 0.25 / (0.25 * maskSoftness + pixelSize.xy));

                #ifdef TEXT_UNDERLAY
                output.texcoord1 = float4(input.texcoord0.xy + layerOffset, input.color.a, 0);
                output.underlayParam = half2(layerScale, layerBias);
                #endif

                return output;
            }

            half4 PixShader(pixel_t input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                half d = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.texcoord0.xy).a * input.param.x;
                half4 c = input.faceColor * saturate(d - input.param.w);

                #ifdef OUTLINE_ON
                c = lerp(input.outlineColor, input.faceColor, saturate(d - input.param.z));
                c *= saturate(d - input.param.y);
                #endif

                #ifdef UNDERLAY_ON
                d = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.texcoord1.xy).a * input.underlayParam.x;
                c += half4(_UnderlayColor.rgb * _UnderlayColor.a, _UnderlayColor.a) * saturate(d - input.underlayParam.y) * (1 - c.a);
                #endif

                #ifdef UNDERLAY_INNER
                half sd = saturate(d - input.param.z);
                d = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.texcoord1.xy).a * input.underlayParam.x;
                c += half4(_UnderlayColor.rgb * _UnderlayColor.a, _UnderlayColor.a) * (1 - saturate(d - input.underlayParam.y)) * sd * (1 - c.a);
                #endif

                // clipping with soft edges (instead of UnityGet2DClipping)
                #ifdef UNITY_UI_CLIP_RECT
                half2 m = saturate((_ClipRect.zw - _ClipRect.xy - abs(input.mask.xy)) * input.mask.zw);
                c *= m.x * m.y;
                #endif

                #ifdef TEXT_UNDERLAY
                c *= input.texcoord1.z;
                #endif

                #ifdef UNITY_UI_ALPHACLIP
                clip(c.a - 0.001);
                #endif

                return c;
            }
            ENDHLSL
        }
    }

    Fallback "TextMeshPro/Mobile/Distance Field"
    CustomEditor "TMPro.EditorUtilities.TMP_SDFShaderGUI"
}
