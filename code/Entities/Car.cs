using Sandbox;
using Sandbox.Physics;
using System.Diagnostics;

namespace Jumpy
{
	public partial class Car : MovingEntity
	{
		private string[] models = new string[]
		{
			"models/car-lowpoly-1.vmdl",
			"models/car-lowpoly-2.vmdl",
			"models/car-lowpoly-3.vmdl",
			"models/car-lowpoly-4.vmdl",
			"models/car-lowpoly-5.vmdl",
			"models/car-lowpoly-6.vmdl",
		};
		private Color[] colors = new Color[]
		{
			new Color( 0.6f, 0.2f, 0.2f ),
			new Color( 0.2f, 0.6f, 0.2f ),
			new Color( 0.2f, 0.2f, 0.6f ),
			new Color( 0.6f, 0.2f, 0.6f ),
			new Color( 0.6f, 0.6f, 0.6f ),
			new Color( 0.2f, 0.6f, 0.6f ),
		};
		private Sound engineSound;

		public override void Spawn()
		{
			base.Spawn();
			
			Speed = Game.Random.Int(1000, 1200);

			SetModel( models[Game.Random.Int( models.Length - 1 )] );
			RenderColor = colors[Game.Random.Int( colors.Length - 1 )];

			engineSound = Sound.FromEntity( "engine.loop", this );

			Tags.Add( "car" );
		}

		protected override void OnDestroy()
		{
			base.OnDestroy();
			engineSound.SetVolume( 0.0f );
			engineSound.Stop();
		}
	}
}
