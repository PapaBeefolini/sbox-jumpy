using Sandbox;

namespace Jumpy
{
	public sealed class CameraShake : Component
	{
		private const float traumaDecay = 1.8f;
		private const float noiseSpeed = 26f;

		// Decorrelated noise streams, one per axis. A shared oscillator reads as a pendulum swing.
		private const float seedPitch = 0f;
		private const float seedYaw = 137f;
		private const float seedRoll = 311f;

		[Property, Group( "Shake" )] public float MaxPitch { get; set; } = 1.6f;
		[Property, Group( "Shake" )] public float MaxYaw { get; set; } = 2.2f;
		[Property, Group( "Shake" )] public float MaxRoll { get; set; } = 3.0f;
		[Property, Group( "Shake" )] public float Scale { get; set; } = 1.0f;

		public float Trauma { get; private set; }

		private Rotation restRotation;
		private Rotation appliedRotation;
		private float seed;

		public void AddTrauma( float amount )
		{
			Trauma = float.Clamp( Trauma + amount, 0f, 1f );
		}

		public void Clear()
		{
			Trauma = 0f;
		}

		protected override void OnStart()
		{
			restRotation = WorldRotation;
			appliedRotation = WorldRotation;
			seed = Game.Random.Float( 0f, 1000f );
		}

		protected override void OnUpdate()
		{
			// Whoever else drives this camera (Frog.ResetCamera) writes WorldRotation directly.
			// If it no longer matches what we wrote last frame, they took control and their pose
			// becomes the new rest — that's what lets the shake layer over a camera it doesn't own.
			if ( WorldRotation != appliedRotation )
				restRotation = WorldRotation;

			Trauma = float.Max( 0f, Trauma - traumaDecay * Time.Delta );

			// Squared so the shake tails into stillness instead of switching off.
			float shake = Trauma * Trauma * Scale;
			float time = Time.Now * noiseSpeed;

			appliedRotation = restRotation * Rotation.From(
				MaxPitch * shake * Wobble( seed + seedPitch, time ),
				MaxYaw * shake * Wobble( seed + seedYaw, time ),
				MaxRoll * shake * Wobble( seed + seedRoll, time ) );

			WorldRotation = appliedRotation;
		}

		// Perlin returns 0..1; remap to a signed swing so it oscillates about the rest pose.
		private static float Wobble( float stream, float time )
		{
			return (Sandbox.Utility.Noise.Perlin( stream, time ) * 2f) - 1f;
		}
	}
}
