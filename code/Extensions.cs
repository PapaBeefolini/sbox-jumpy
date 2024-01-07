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
				MathF.Round( vector3.x * multiplier ) / multiplier,
				MathF.Round( vector3.y * multiplier ) / multiplier,
				MathF.Round( vector3.z * multiplier ) / multiplier );
		}
	}
}
