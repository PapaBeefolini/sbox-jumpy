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

				GameObject entity = prefab.Clone( WorldPosition, WorldRotation );

				if ( flipped && entity.Components.Get<MovingEntity>() is MovingEntity moving )
					moving.Speed = -moving.Speed;

				entity.SetParent( GameObject.Parent );
				entity.NetworkSpawn();

				await Task.DelaySeconds( Game.Random.Float( delayMin, delayMax ) );
			}
		}
	}
}
