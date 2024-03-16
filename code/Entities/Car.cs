using Sandbox;

namespace Jumpy
{
	public class Car : MovingEntity
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
			SetAppearance( models[Game.Random.Int( models.Length - 1 )], colors[Game.Random.Int( colors.Length - 1 )] );
		}


		[Broadcast(NetPermission.HostOnly)]
		private void SetAppearance( string modelPath, Color color )
		{
			var renderer = Components.Get<ModelRenderer>();
			renderer.Model = Model.Load( modelPath );
			renderer.Tint = color;

			var collider = Components.Get<ModelCollider>();
			collider.Model = renderer.Model;
		}
	}
}
