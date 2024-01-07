using Sandbox;

namespace Jumpy
{
	public partial class MovingEntity : AnimatedEntity
	{
		public float Speed;

		public override void Spawn()
		{
			base.Spawn();

			EnableLagCompensation = true;

			SetupPhysicsFromModel( PhysicsMotionType.Static );
		}

		[GameEvent.Tick.Server]
		public void Move()
		{
			Position += Vector3.Right * Speed * Time.Delta;

			if ( Position.y > 5000 || Position.y < -5000 )
				Delete();
		}
	}
}
