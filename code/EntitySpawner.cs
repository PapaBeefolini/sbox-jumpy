using Sandbox;
using System.Threading.Tasks;

namespace Jumpy
{
	public sealed class EntitySpawner : Component
	{
		public async Task SpawnEntities( GameObject prefab, float delayMin, float delayMax, bool flipped = false )
		{
			while ( this.IsValid() )
			{
				if ( !Networking.IsHost )
					return;

				var entity = prefab.Clone( Transform.Position, Transform.Rotation );
				var movingEntity = entity.Components.Get<MovingEntity>();
				if ( movingEntity is not null && flipped )
				{
					movingEntity.Speed = -movingEntity.Speed;
				}
				entity.SetParent( GameObject );
				entity.NetworkSpawn();

				await Task.DelaySeconds( Game.Random.Float( delayMin, delayMax ) );
			}
		}
	}
}
