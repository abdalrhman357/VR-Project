using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

namespace Seb.Fluid.Simulation
{
	public class Spawner3D : MonoBehaviour
	{
		[Header("Spawn Mode")]
		[Tooltip("When enabled, uses Exact Particle Count instead of density.")]
		public bool useExactCount = false;

		[Tooltip("Exact total number of particles to spawn (shared equally across all regions). Only used when Use Exact Count is enabled.")]
		public int exactParticleCount = 50000;

		[Header("Density Mode (used when Use Exact Count is off)")]
		public int particleSpawnDensity = 600;

		[Header("Spawn Settings")]
		public float3 initialVel;
		public float jitterStrength;
		public bool showSpawnBounds;
		public SpawnRegion[] spawnRegions;

		[Header("Debug Info")]
		public int debug_num_particles;
		public float debug_spawn_volume;


		public SpawnData GetSpawnData()
		{
			List<float3> allPoints     = new();
			List<float3> allVelocities = new();

			if (spawnRegions == null || spawnRegions.Length == 0)
				return new SpawnData { points = allPoints.ToArray(), velocities = allVelocities.ToArray() };

			foreach (SpawnRegion region in spawnRegions)
			{
				int particlesPerAxis = GetParticlesPerAxis(region);
				(float3[] points, float3[] velocities) = SpawnCube(particlesPerAxis, region.centre, Vector3.one * region.size);
				allPoints.AddRange(points);
				allVelocities.AddRange(velocities);
			}

			return new SpawnData { points = allPoints.ToArray(), velocities = allVelocities.ToArray() };
		}

		// Returns particlesPerAxis for a given region, respecting the active spawn mode.
		int GetParticlesPerAxis(SpawnRegion region)
		{
			if (useExactCount)
			{
				// Split the exact count equally across all regions
				int countPerRegion = Mathf.Max(1, exactParticleCount / spawnRegions.Length);
				return Mathf.Max(1, (int)Math.Cbrt(countPerRegion));
			}
			return region.CalculateParticleCountPerAxis(particleSpawnDensity);
		}

		(float3[] p, float3[] v) SpawnCube(int numPerAxis, Vector3 centre, Vector3 size)
		{
			int numPoints        = numPerAxis * numPerAxis * numPerAxis;
			float3[] points      = new float3[numPoints];
			float3[] velocities  = new float3[numPoints];

			int i = 0;
			for (int x = 0; x < numPerAxis; x++)
			{
				for (int y = 0; y < numPerAxis; y++)
				{
					for (int z = 0; z < numPerAxis; z++)
					{
						float tx = x / (numPerAxis - 1f);
						float ty = y / (numPerAxis - 1f);
						float tz = z / (numPerAxis - 1f);

						float px = (tx - 0.5f) * size.x + centre.x;
						float py = (ty - 0.5f) * size.y + centre.y;
						float pz = (tz - 0.5f) * size.z + centre.z;

						float3 jitter = UnityEngine.Random.insideUnitSphere * jitterStrength;
						points[i]     = new float3(px, py, pz) + jitter;
						velocities[i] = initialVel;
						i++;
					}
				}
			}

			return (points, velocities);
		}

		void OnValidate()
		{
			debug_spawn_volume  = 0;
			debug_num_particles = 0;

			if (spawnRegions == null) return;

			foreach (SpawnRegion region in spawnRegions)
			{
				debug_spawn_volume += region.Volume;
				int numPerAxis      = GetParticlesPerAxis(region);
				debug_num_particles += numPerAxis * numPerAxis * numPerAxis;
			}
		}

		void OnDrawGizmos()
		{
			if (showSpawnBounds && !Application.isPlaying && spawnRegions != null)
			{
				foreach (SpawnRegion region in spawnRegions)
				{
					Gizmos.color = region.debugDisplayCol;
					Gizmos.DrawWireCube(region.centre, Vector3.one * region.size);
				}
			}
		}

		[System.Serializable]
		public struct SpawnRegion
		{
			public Vector3 centre;
			public float   size;
			public Color   debugDisplayCol;

			public float Volume => size * size * size;

			public int CalculateParticleCountPerAxis(int particleDensity)
			{
				int targetParticleCount = (int)(Volume * particleDensity);
				return Mathf.Max(1, (int)Math.Cbrt(targetParticleCount));
			}
		}

		public struct SpawnData
		{
			public float3[] points;
			public float3[] velocities;
		}
	}
}
