using System;

namespace Jumpy
{
	static class Extensions
	{
		public static Vector3 Round( this Vector3 vector3, int decimalPlaces = 2 )
		{
			float multiplier = 1;
			for ( int i = 0; i < decimalPlaces; i++ )
			{
				multiplier *= 10f;
			}
			return new Vector3(
				float.Round( vector3.x * multiplier ) / multiplier,
				float.Round( vector3.y * multiplier ) / multiplier,
				float.Round( vector3.z * multiplier ) / multiplier );
		}

		// Overshoots and settles back.
		public static float EaseOutBack( this float t )
		{
			if ( t <= 0f ) return 0f;
			if ( t >= 1f ) return 1f;

			const float c1 = 1.70158f;
			const float c3 = c1 + 1f;
			float u = t - 1f;
			return 1f + c3 * u * u * u + c1 * u * u;
		}

		// Fast start, soft landing, no overshoot.
		public static float EaseOutCubic( this float t )
		{
			t = float.Clamp( t, 0f, 1f );
			float u = 1f - t;
			return 1f - u * u * u;
		}
	}
}
