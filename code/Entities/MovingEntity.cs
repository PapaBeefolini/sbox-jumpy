using Sandbox;

namespace Jumpy
{
	public class MovingEntity : Component
	{
		[Property] public float MinSpeed = 350;
		[Property] public float MaxSpeed = 350;

		public float Speed { get; set; }

		protected override void OnAwake()
		{
			Speed = Game.Random.Float( MinSpeed, MaxSpeed );
		}

		protected override void OnFixedUpdate()
		{
			if ( !Networking.IsHost || Manager.Instance.IsGameOver )
				return;

			WorldPosition += Vector3.Right * Speed * Time.Delta;

			if ( float.Abs( WorldPosition.y ) > 5000 )
				GameObject.Destroy();
		}
	}
}
