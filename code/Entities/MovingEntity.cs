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


		protected override void OnUpdate()
		{
			Move();
		}


		void Move()
		{
			Transform.Position += Vector3.Right * Speed * Time.Delta;

			if ( Transform.Position.y > 5000 || Transform.Position.y < -5000 )
				GameObject.Destroy();
		}
	}
}
