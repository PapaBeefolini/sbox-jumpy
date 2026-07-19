using Sandbox;

namespace Jumpy
{
	public class Car : MovingEntity
	{
		private static readonly string[] models =
		{
			"models/car-lowpoly-1.vmdl",
			"models/car-lowpoly-2.vmdl",
			"models/car-lowpoly-3.vmdl",
			"models/car-lowpoly-4.vmdl",
			"models/car-lowpoly-5.vmdl",
			"models/car-lowpoly-6.vmdl",
		};

		private static readonly Color[] colors =
		{
			new Color( 0.6f, 0.05f, 0.05f ),
			new Color( 0.05f, 0.6f, 0.05f ),
			new Color( 0.05f, 0.05f, 0.6f ),
			new Color( 0.6f, 0.05f, 0.6f ),
			new Color( 0.6f, 0.6f, 0.6f ),
			new Color( 0.05f, 0.6f, 0.6f ),
			new Color( 0.6f, 0.6f, 0.05f ),
		};

		protected override void OnStart()
		{
			SetAppearance( Game.Random.FromArray( models ), Game.Random.FromArray( colors ) );
		}

		[Rpc.Broadcast( NetFlags.OwnerOnly )]
		private void SetAppearance( string modelPath, Color color )
		{
			var renderer = Components.Get<ModelRenderer>();
			renderer.Model = Model.Load( modelPath );
			renderer.Tint = color;

			Components.Get<ModelCollider>().Model = renderer.Model;
		}
	}
}
