Shader "Flood Tutorial/Shallow Water URP"
{
    Properties { [NoScaleOffset]_StateTex("State",2D)="black"{} _Color("Color",Color)=(0.015,0.25,0.72,0.78) _HeightScale("Height",Float)=1 _MinimumWater("Minimum",Float)=0.006 }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite On
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_StateTex); SAMPLER(sampler_StateTex);
            float4 _StateTex_TexelSize; half4 _Color; float _HeightScale,_MinimumWater;
            struct Attributes { float3 positionOS:POSITION; float2 uv:TEXCOORD0; };
            struct Varyings { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; float3 normalWS:TEXCOORD1; float3 worldPos:TEXCOORD2; };
            float4 State(float2 uv) { return SAMPLE_TEXTURE2D_LOD(_StateTex,sampler_StateTex,uv,0); }
            float Height(float2 uv) { float4 s=State(uv); return (s.r+s.g)*_HeightScale; }
            Varyings Vert(Attributes i)
            {
                Varyings o; i.positionOS.y += Height(i.uv)+0.012;
                o.positionCS=TransformObjectToHClip(i.positionOS); o.worldPos=TransformObjectToWorld(i.positionOS); o.uv=i.uv;
                float l=Height(i.uv-float2(_StateTex_TexelSize.x,0)); float r=Height(i.uv+float2(_StateTex_TexelSize.x,0));
                float b=Height(i.uv-float2(0,_StateTex_TexelSize.y)); float t=Height(i.uv+float2(0,_StateTex_TexelSize.y));
                o.normalWS=TransformObjectToWorldNormal(normalize(float3(l-r,2*_StateTex_TexelSize.x,b-t))); return o;
            }
            half4 Frag(Varyings i):SV_Target
            {
                float water=State(i.uv).g; clip(water-_MinimumWater);
                half3 n=normalize(i.normalWS); half3 v=normalize(GetCameraPositionWS()-i.worldPos);
                half fresnel=pow(1-saturate(dot(n,v)),3); half light=saturate(dot(n,normalize(half3(0.3,0.85,0.35))));
                half3 color=_Color.rgb*(.55+.35*light)+fresnel*.35; return half4(color,saturate(_Color.a+water*.18));
            }
            ENDHLSL
        }
    }
}
