using Sandbox;
using System;

namespace Jumpy
{
	public sealed class CameraShake : Component
	{
		[Property] public float Speed { get; set; } = 0.5f;
		[Property] public float Amount { get; set; } = 2.0f;
		private float startTime { get; set; }
		private Rotation startRotation { get; set; }


		protected override void OnStart()
		{
			startTime = Time.Now;
			startRotation = WorldRotation;
		}


		protected override void OnUpdate()
		{
			float angle = MathF.Sin( (Time.Now - startTime) * Speed ) * Amount;
			WorldRotation = startRotation * Rotation.From( angle / 4, angle, 0 );
		}
	}
}
