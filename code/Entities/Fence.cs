using Sandbox;

namespace Jumpy
{
	public sealed class Fence : Component
	{
		[Property] public float FlipAngle { get; set; } = 80.0f;

		[Property] public float FlipDuration { get; set; } = 0.2f;

		private Vector3 restLocalPosition;
		private Rotation restLocalRotation;
		private Vector3 hingeLocal;

		private float flip;

		protected override void OnStart()
		{
			restLocalPosition = LocalPosition;
			restLocalRotation = LocalRotation;

			BBox bounds = Components.Get<ModelRenderer>()?.Model?.Bounds ?? default;
			hingeLocal = new Vector3( bounds.Center.x, bounds.Center.y, bounds.Mins.z );
		}

		protected override void OnUpdate()
		{
			bool open = Manager.Instance is { IsGameActive: true };

			float step = FlipDuration > 0.0f ? Time.Delta / FlipDuration : 1.0f;
			flip = float.Clamp( flip + (open ? step : -step), 0.0f, 1.0f );

			float eased = flip * flip;

			Rotation r = restLocalRotation * Rotation.FromPitch( FlipAngle * eased );
			LocalRotation = r;
			LocalPosition = restLocalPosition + restLocalRotation * hingeLocal - r * hingeLocal;
		}
	}
}
