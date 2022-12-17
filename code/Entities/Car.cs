using Sandbox;

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
			
			Speed = Game.Random.Int(1200, 1400);

			SetModel( models[Game.Random.Int( models.Length - 1 )] );
			RenderColor = colors[Game.Random.Int( colors.Length - 1 )];

			using ( Prediction.Off() )
				engineSound = Sound.FromEntity( "sounds/engine-loop.sound", this );
		}

		protected override void OnDestroy()
		{
			base.OnDestroy();
			engineSound.Stop();
		}
	}
}
