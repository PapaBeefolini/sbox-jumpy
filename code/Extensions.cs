namespace Jumpy
{
	static class Extensions
	{
		public static Vector3 Round( this Vector3 vector, int decimalPlaces = 2 )
		{
			return new Vector3(
				float.Round( vector.x, decimalPlaces ),
				float.Round( vector.y, decimalPlaces ),
				float.Round( vector.z, decimalPlaces ) );
		}

		// Overshoots and settles back. Sandbox.Utility.Easing has no Back variant.
		public static float EaseOutBack( this float t )
		{
			if ( t <= 0f ) return 0f;
			if ( t >= 1f ) return 1f;

			const float c1 = 1.70158f;
			const float c3 = c1 + 1f;
			float u = t - 1f;
			return 1f + c3 * u * u * u + c1 * u * u;
		}

		// Easing.EaseOut is quadratic; this is the softer cubic landing.
		public static float EaseOutCubic( this float t )
		{
			t = float.Clamp( t, 0f, 1f );
			float u = 1f - t;
			return 1f - u * u * u;
		}
	}
}
