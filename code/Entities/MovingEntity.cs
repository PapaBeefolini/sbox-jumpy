using Sandbox;

namespace Jumpy
{
	public partial class MovingEntity : ModelEntity
	{
		public float Speed;

		public override void Spawn()
		{
			base.Spawn();

			SetupPhysicsFromModel( PhysicsMotionType.Static );
		}
		
		[Event.Tick.Server]
		public void Move()
		{
			Position += Vector3.Right * Speed * Time.Delta;

			if ( Position.y > 5000 || Position.y < -5000 )
				Delete();
		}
	}
}
