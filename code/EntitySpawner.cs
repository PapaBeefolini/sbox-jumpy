using Sandbox;
using System;
using System.Threading.Tasks;

namespace Jumpy
{
	public partial class EntitySpawner : Entity
	{
		public async Task SpawnEntities<T>( float delayMin, float delayMax, bool flipped = false ) where T : MovingEntity, new()
		{
			while ( true )
			{
				await Task.DelaySeconds( Game.Random.Float( delayMin, delayMax ) );

				T entity = new()
				{
					Position = Position,
					Rotation = Rotation
				};

				if ( flipped )
					entity.Speed = -entity.Speed;
			}
		}
	}
}
