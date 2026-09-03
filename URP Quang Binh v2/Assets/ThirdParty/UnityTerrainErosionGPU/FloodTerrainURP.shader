Shader "Flood Tutorial/Shallow Water Terrain URP"
{
    Properties { [NoScaleOffset]_StateTex("State",2D)="black"{} _Color("Color",Color)=(0.26,0.2,0.12,1) _HeightScale("Height",Float)=1 }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_StateTex); SAMPLER(sampler_StateTex);
            float4 _StateTex_TexelSize; half4 _Color; float _HeightScale;
            struct Attributes { float3 positionOS:POSITION; float2 uv:TEXCOORD0; };
            struct Varyings { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; float3 normalWS:TEXCOORD1; };
            float Terrain(float2 uv) { return SAMPLE_TEXTURE2D_LOD(_StateTex,sampler_StateTex,uv,0).r * _HeightScale; }
            Varyings Vert(Attributes i)
            {
                Varyings o; i.positionOS.y += Terrain(i.uv);
                o.positionCS=TransformObjectToHClip(i.positionOS); o.uv=i.uv;
                float l=Terrain(i.uv-float2(_StateTex_TexelSize.x,0)); float r=Terrain(i.uv+float2(_StateTex_TexelSize.x,0));
                float b=Terrain(i.uv-float2(0,_StateTex_TexelSize.y)); float t=Terrain(i.uv+float2(0,_StateTex_TexelSize.y));
                o.normalWS=TransformObjectToWorldNormal(normalize(float3(l-r,2*_StateTex_TexelSize.x,b-t))); return o;
            }
            half4 Frag(Varyings i):SV_Target { half light=saturate(dot(normalize(i.normalWS),normalize(half3(0.3,0.85,0.35))))*.6+.4; return half4(_Color.rgb*light,1); }
            ENDHLSL
        }
    }
}
