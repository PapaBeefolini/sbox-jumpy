HEADER
{
	Description = "Toon water with depth-based intersection foam";
	Version = 1;
}

FEATURES
{
	#include "common/features.hlsl"
}

MODES
{
	Forward();
	Depth();
}

COMMON
{
	#define S_SPECULAR 1
	#define BLEND_MODE_ALREADY_SET 1
	#include "common/shared.hlsl"
}

struct VertexInput
{
	#include "common/vertexinput.hlsl"
};

struct PixelInput
{
	#include "common/pixelinput.hlsl"
};

VS
{
	#include "common/vertex.hlsl"

	PixelInput MainVs( VertexInput i )
	{
		PixelInput o = ProcessVertex( i );
		return FinalizeVertex( o );
	}
}

PS
{
	RenderState( BlendEnable, true );
	RenderState( SrcBlend, SRC_ALPHA );
	RenderState( DstBlend, INV_SRC_ALPHA );
	RenderState( BlendOp, ADD );
	RenderState( SrcBlendAlpha, ONE );
	RenderState( DstBlendAlpha, INV_SRC_ALPHA );
	RenderState( BlendOpAlpha, ADD );
	RenderState( DepthWriteEnable, false );

	#define DEPTH_STATE_ALREADY_SET 1
	#define BLEND_MODE_ALREADY_SET 1
	#define S_TRANSLUCENT 1

	#include "common/pixel.hlsl"
	#include "common/classes/Depth.hlsl"
	#include "procedural.hlsl"

	#define FOAM_TAPS 32

	float3 ShallowColor < Default3( 0.25, 0.60, 0.92 ); UiType( Color ); UiGroup( "Colour,10/10" ); >;
	float3 DeepColor    < Default3( 0.05, 0.20, 0.60 ); UiType( Color ); UiGroup( "Colour,10/20" ); >;
	float3 FoamColor    < Default3( 1.00, 1.00, 1.00 ); UiType( Color ); UiGroup( "Colour,10/30" ); >;

	float DepthFadeDistance < Default( 20.0 ); Range( 1.0, 512.0 ); UiGroup( "Colour,10/40" ); >;
	float ColorBands        < Default( 3.0 );  Range( 1.0, 12.0 );  UiGroup( "Colour,10/50" ); >;
	float ShallowOpacity    < Default( 0.60 ); Range( 0.0, 1.0 );   UiGroup( "Colour,10/60" ); >;
	float DeepOpacity       < Default( 0.95 ); Range( 0.0, 1.0 );   UiGroup( "Colour,10/70" ); >;

	float FoamSpread      < Default( 16.0 ); Range( 0.0, 128.0 ); UiGroup( "Foam,20/05" ); >;
	float FoamWidth       < Default( 4.0 );  Range( 0.0, 128.0 ); UiGroup( "Foam,20/10" ); >;
	float FoamCutoff      < Default( 0.5 );  Range( 0.0, 1.0 );   UiGroup( "Foam,20/20" ); >;
	float FoamSoftness    < Default( 0.10 ); Range( 0.001, 0.5 ); UiGroup( "Foam,20/30" ); >;
	float FoamStrength    < Default( 0.60 ); Range( 0.0, 1.0 );   UiGroup( "Foam,20/32" ); >;
	float FoamOpacity     < Default( 0.75 ); Range( 0.0, 1.0 );   UiGroup( "Foam,20/34" ); >;
	float FoamNoiseAmount < Default( 0.30 ); Range( 0.0, 1.0 );   UiGroup( "Foam,20/40" ); >;
	float FoamNoiseScale  < Default( 22.0 ); Range( 1.0, 256.0 ); UiGroup( "Foam,20/50" ); >;
	float FoamSpeed       < Default( 0.35 ); Range( 0.0, 4.0 );   UiGroup( "Foam,20/60" ); >;

	float SecondaryFoam     < Default( 0.65 ); Range( 0.0, 1.0 );  UiGroup( "Foam,20/70" ); >;
	float SecondaryDistance < Default( 0.28 ); Range( 0.0, 1.0 );  UiGroup( "Foam,20/80" ); >;
	float SecondaryWidth    < Default( 0.10 ); Range( 0.01, 0.5 ); UiGroup( "Foam,20/90" ); >;

	float RippleStrength < Default( 0.10 ); Range( 0.0, 1.0 );   UiGroup( "Surface,30/10" ); >;
	float RippleScale    < Default( 55.0 ); Range( 1.0, 256.0 ); UiGroup( "Surface,30/20" ); >;
	float RippleSpeed    < Default( 0.20 ); Range( 0.0, 4.0 );   UiGroup( "Surface,30/30" ); >;
	float RippleCutoff   < Default( 0.55 ); Range( -1.0, 1.0 );  UiGroup( "Surface,30/40" ); >;

	float WaveStrength < Default( 0.12 ); Range( 0.0, 2.0 );   UiGroup( "Surface,30/50" ); >;
	float WaveScale    < Default( 60.0 ); Range( 1.0, 256.0 ); UiGroup( "Surface,30/60" ); >;
	float WaveSpeed    < Default( 0.30 ); Range( 0.0, 4.0 );   UiGroup( "Surface,30/70" ); >;

	float FresnelStrength < Default( 0.06 ); Range( 0.0, 1.0 );  UiGroup( "Surface,30/80" ); >;
	float FresnelPower    < Default( 5.0 );  Range( 0.5, 16.0 ); UiGroup( "Surface,30/90" ); >;
	float Flatness        < Default( 0.75 ); Range( 0.0, 1.0 );  UiGroup( "Surface,30/95" ); >;
	float WaterRoughness  < Default( 0.70 ); Range( 0.0, 1.0 );  UiGroup( "Surface,30/96" ); >;

	float WaterNoise( float2 vUv, float flSpeed )
	{
		float flTime = g_flTime * flSpeed;
		float a = Simplex2D( vUv + float2( flTime, flTime * 0.7 ) );
		float b = Simplex2D( vUv * 1.9 - float2( flTime * 0.6, flTime * 0.35 ) );
		return a * 0.65 + b * 0.35;
	}

	float3 WorldToScreenPixel( float3 vWorldPos )
	{
		float4 vProj = Position4WsToPs( float4( vWorldPos, 1.0 ) );
		float2 vUv = ( vProj.xy / vProj.w ) * 0.5 + 0.5;
		vUv.y = 1.0 - vUv.y;
		return float3( vUv * g_vViewportSize, vProj.w );
	}

	float4 MainPs( PixelInput i ) : SV_Target
	{
		float2 vScreenPos = i.vPositionSs.xy;
		float3 vPositionWs = g_vCameraPositionWs + i.vPositionWithOffsetWs.xyz;

		float3 vSceneWs = Depth::GetWorldPosition( vScreenPos );
		float flSubmersion = max( 0.0, vPositionWs.z - vSceneWs.z );

		float3 vPlaneNormal = i.vNormalWs;
		float3 vTangent = normalize( abs( vPlaneNormal.z ) < 0.99 ? cross( vPlaneNormal, float3( 0, 0, 1 ) ) : float3( 1, 0, 0 ) );
		float3 vBitangent = cross( vPlaneNormal, vTangent );

		float flLandWeight = 0.0;
		float flTotalWeight = 0.0;

		[unroll]
		for ( int k = 0; k < FOAM_TAPS; k++ )
		{
			float flAngle = k * 2.39996323;
			float flRadius = FoamSpread * sqrt( ( k + 0.5 ) / FOAM_TAPS );
			float flWeight = 1.0 - flRadius / max( FoamSpread, 0.01 );

			float3 vTapWs = vPositionWs + ( vTangent * cos( flAngle ) + vBitangent * sin( flAngle ) ) * flRadius;
			float3 vTapPx = WorldToScreenPixel( vTapWs );

			if ( vTapPx.z <= 0.0 ||
				 any( vTapPx.xy < 0.0 ) || any( vTapPx.xy > g_vViewportSize - 1.0 ) )
				continue;

			float3 vTapSceneWs = Depth::GetWorldPosition( vTapPx.xy );

			float flIsLand = smoothstep( -1.5, 1.5, vTapSceneWs.z - ( vPositionWs.z - FoamWidth ) );
			flIsLand *= ( length( vTapSceneWs.xy - vTapWs.xy ) < FoamSpread * 1.5 ) ? 1.0 : 0.0;

			flTotalWeight += flWeight;
			flLandWeight += flWeight * flIsLand;
		}

		float flCoverage = flLandWeight / max( flTotalWeight, 1e-4 );
		float flShore = saturate( 1.0 - 2.0 * flCoverage );
		flShore += WaterNoise( vPositionWs.xy / max( FoamNoiseScale, 0.01 ), FoamSpeed ) * FoamNoiseAmount;

		float flFoam = 1.0 - smoothstep( FoamCutoff - FoamSoftness, FoamCutoff + FoamSoftness, flShore );

		float flRingCentre = FoamCutoff + SecondaryDistance;
		float flRing = 1.0 - smoothstep( SecondaryWidth - FoamSoftness, SecondaryWidth + FoamSoftness, abs( flShore - flRingCentre ) );
		flRing *= SecondaryFoam;

		float flFoamTotal = saturate( flFoam + flRing );

		float flDepthT = saturate( flSubmersion / max( DepthFadeDistance, 0.01 ) );
		float flBands = max( 1.0, floor( ColorBands ) );
		float flBanded = floor( flDepthT * flBands + 0.5 ) / flBands;

		float3 vWaterColor = lerp( ShallowColor, DeepColor, flBanded );
		float flAlpha = lerp( ShallowOpacity, DeepOpacity, flBanded );

		float flRipple = 0.0;
		if ( RippleStrength > 0.0 )
		{
			float n = WaterNoise( vPositionWs.xy / max( RippleScale, 0.01 ), RippleSpeed );
			flRipple = smoothstep( RippleCutoff, RippleCutoff + 0.06, n ) * RippleStrength;
		}

		Material m = Material::Init( i );

		float2 vWaveUv = vPositionWs.xy / max( WaveScale, 0.01 );
		const float flEpsilon = 0.35;
		float h  = WaterNoise( vWaveUv, WaveSpeed );
		float hx = WaterNoise( vWaveUv + float2( flEpsilon, 0.0 ), WaveSpeed );
		float hy = WaterNoise( vWaveUv + float2( 0.0, flEpsilon ), WaveSpeed );

		float3 vNormalWs = normalize( i.vNormalWs + float3( ( h - hx ), ( h - hy ), 0.0 ) * WaveStrength );
		m.Normal = normalize( lerp( vNormalWs, i.vNormalWs, flFoamTotal ) );

		float3 vCameraDir = CalculatePositionToCameraDirWs( i.vPositionWithOffsetWs.xyz + g_vHighPrecisionLightingOffsetWs.xyz );
		float flFresnel = pow( 1.0 - saturate( dot( m.Normal, vCameraDir ) ), FresnelPower );
		flFresnel *= FresnelStrength * ( 1.0 - flFoamTotal );

		float flWhite = saturate( flFoamTotal * FoamStrength + flRipple + flFresnel );

		m.Albedo = lerp( vWaterColor, FoamColor, flWhite );
		m.Roughness = WaterRoughness;
		m.Metalness = 0.0;
		m.AmbientOcclusion = 1.0;
		m.Opacity = saturate( lerp( flAlpha, FoamOpacity, flFoamTotal ) );

		if ( DepthNormals::WantsDepthNormals() )
			return DepthNormals::Output( m.Normal, m.Roughness, m.Opacity );

		float4 vColor = ShadingModelStandard::Shade( i, m );
		vColor.rgb = lerp( vColor.rgb, m.Albedo, Flatness );
		vColor.rgb = Fog::Apply( vPositionWs, vScreenPos, vColor.rgb );
		vColor.a = m.Opacity;

		return vColor;
	}
}
