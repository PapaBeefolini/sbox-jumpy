using Sandbox;

namespace Jumpy
{
	public class Train : MovingEntity
	{
		private static readonly string[] models =
		{
			"models/train_01.vmdl",
			"models/train_02.vmdl",
		};

		protected override void OnStart()
		{
			SetAppearance( Game.Random.FromArray( models ) );
		}

		[Rpc.Broadcast( NetFlags.OwnerOnly )]
		private void SetAppearance( string modelPath )
		{
			ModelRenderer renderer = Components.Get<ModelRenderer>();
			ModelCollider modelCollider = Components.Get<ModelCollider>();
			if ( !renderer.IsValid() || !modelCollider.IsValid() )
				return;

			renderer.Model = Model.Load( modelPath );
			modelCollider.Model = renderer.Model;
		}
	}
}
