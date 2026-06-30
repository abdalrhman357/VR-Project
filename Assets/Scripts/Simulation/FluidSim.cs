using System;
using UnityEngine;
using Seb.GPUSorting;
using Unity.Mathematics;
using System.Collections.Generic;
using Seb.Helpers;
using static Seb.Helpers.ComputeHelper;

namespace Seb.Fluid.Simulation
{
	public class FluidSim : MonoBehaviour
	{
		public event Action<FluidSim> SimulationInitCompleted;

		[Header("Time Step")] public float normalTimeScale = 1;
		public float slowTimeScale = 0.1f;
		public float maxTimestepFPS = 60;
		public int iterationsPerFrame = 3;

		[Header("Simulation Settings")] public float gravity = -10;
		public float smoothingRadius = 0.2f;
		public float targetDensity = 630;
		public float pressureMultiplier = 288;
		public float nearPressureMultiplier = 2.15f;
		public float viscosityStrength = 0;
		[Range(0, 1)] public float collisionDamping = 0.95f;

		[Header("Foam Settings")] public bool foamActive;
		public int maxFoamParticleCount = 1000;
		public float trappedAirSpawnRate = 70;
		public float spawnRateFadeInTime = 0.5f;
		public float spawnRateFadeStartTime = 0;
		public Vector2 trappedAirVelocityMinMax = new(5, 25);
		public Vector2 foamKineticEnergyMinMax = new(15, 80);
		public float bubbleBuoyancy = 1.5f;
		public int sprayClassifyMaxNeighbours = 5;
		public int bubbleClassifyMinNeighbours = 15;
		public float bubbleScale = 0.5f;
		public float bubbleChangeScaleSpeed = 7;

		[Header("Volumetric Render Settings")] public bool renderToTex3D;
		public int densityTextureRes;

		[Header("Bottom Hole")]
		[Tooltip("Radius of the hole in the bottom cap (0 = fully closed).")]
		[Range(0f, 5f)]
		public float holeRadius = 0f;
		[Tooltip("XZ offset of the hole centre relative to the cylinder axis.")]
		public Vector2 holeOffset = Vector2.zero;

		[Header("References")] public ComputeShader compute;
		public Spawner3D spawner;
		public Rendering.BucketRenderer bucketRenderer;

		[HideInInspector] public RenderTexture DensityMap;
		public Vector3 Scale => transform.localScale;

		public int InitialParticleCount { get; private set; }
		public int ActiveParticleCount { get; private set; }

		// Buffers
		public ComputeBuffer foamBuffer { get; private set; }
		public ComputeBuffer foamSortTargetBuffer { get; private set; }
		public ComputeBuffer foamCountBuffer { get; private set; }
		public ComputeBuffer positionBuffer { get; private set; }
		public ComputeBuffer velocityBuffer { get; private set; }
		public ComputeBuffer densityBuffer { get; private set; }
		public ComputeBuffer predictedPositionsBuffer;
		public ComputeBuffer debugBuffer { get; private set; }
		public ComputeBuffer activeCountBuffer { get; private set; }

		ComputeBuffer sortTarget_positionBuffer;
		ComputeBuffer sortTarget_velocityBuffer;
		ComputeBuffer sortTarget_predictedPositionsBuffer;

		// Kernel IDs
		const int externalForcesKernel        = 0;
		const int spatialHashKernel           = 1;
		const int reorderKernel               = 2;
		const int reorderCopybackKernel       = 3;
		const int densityKernel               = 4;
		const int pressureKernel              = 5;
		const int viscosityKernel             = 6;
		const int updatePositionsKernel       = 7;
		const int renderKernel                = 8;
		const int foamUpdateKernel            = 9;
		const int foamReorderCopyBackKernel   = 10;
		const int clearCountersKernel         = 11;

		SpatialHash spatialHash;

		// State
		bool isPaused;
		bool pauseNextFrame;
		float smoothRadiusOld;
		float simTimer;
		bool inSlowMode;
		Spawner3D.SpawnData spawnData;
		Dictionary<ComputeBuffer, string> bufferNameLookup;
		Vector3 prevCentre3;

		void Start()
		{
			Debug.Log("Controls: Space = Play/Pause, Q = SlowMode, R = Reset");
			isPaused = false;
			Initialize();
		}

		void Initialize()
		{
			spawnData = spawner.GetSpawnData();
			int numParticles = spawnData.points.Length;
			InitialParticleCount = numParticles;
			ActiveParticleCount = numParticles;

			spatialHash = new SpatialHash(numParticles);

			positionBuffer               = CreateStructuredBuffer<float3>(numParticles);
			predictedPositionsBuffer     = CreateStructuredBuffer<float3>(numParticles);
			velocityBuffer               = CreateStructuredBuffer<float3>(numParticles);
			densityBuffer                = CreateStructuredBuffer<float2>(numParticles);
			foamBuffer                   = CreateStructuredBuffer<FoamParticle>(maxFoamParticleCount);
			foamSortTargetBuffer         = CreateStructuredBuffer<FoamParticle>(maxFoamParticleCount);
			foamCountBuffer              = CreateStructuredBuffer<uint>(4096);
			debugBuffer                  = CreateStructuredBuffer<float3>(numParticles);
			sortTarget_positionBuffer             = CreateStructuredBuffer<float3>(numParticles);
			sortTarget_predictedPositionsBuffer   = CreateStructuredBuffer<float3>(numParticles);
			sortTarget_velocityBuffer             = CreateStructuredBuffer<float3>(numParticles);
			activeCountBuffer                     = CreateStructuredBuffer<uint>(1);

			bufferNameLookup = new Dictionary<ComputeBuffer, string>
			{
				{ positionBuffer,                           "Positions" },
				{ predictedPositionsBuffer,                 "PredictedPositions" },
				{ velocityBuffer,                           "Velocities" },
				{ densityBuffer,                            "Densities" },
				{ spatialHash.SpatialKeys,                  "SpatialKeys" },
				{ spatialHash.SpatialOffsets,               "SpatialOffsets" },
				{ spatialHash.SpatialIndices,               "SortedIndices" },
				{ sortTarget_positionBuffer,                "SortTarget_Positions" },
				{ sortTarget_predictedPositionsBuffer,      "SortTarget_PredictedPositions" },
				{ sortTarget_velocityBuffer,                "SortTarget_Velocities" },
				{ foamCountBuffer,                          "WhiteParticleCounters" },
				{ foamBuffer,                               "WhiteParticles" },
				{ foamSortTargetBuffer,                     "WhiteParticlesCompacted" },
				{ debugBuffer,                              "Debug" },
				{ activeCountBuffer,                        "ActiveParticleCount" }
			};

			SetInitialBufferData(spawnData);

			SetBuffers(compute, externalForcesKernel, bufferNameLookup, new ComputeBuffer[]
				{ positionBuffer, predictedPositionsBuffer, velocityBuffer });

			SetBuffers(compute, spatialHashKernel, bufferNameLookup, new ComputeBuffer[]
				{ spatialHash.SpatialKeys, spatialHash.SpatialOffsets, predictedPositionsBuffer, spatialHash.SpatialIndices });

			SetBuffers(compute, reorderKernel, bufferNameLookup, new ComputeBuffer[]
				{ positionBuffer, sortTarget_positionBuffer, predictedPositionsBuffer,
				  sortTarget_predictedPositionsBuffer, velocityBuffer, sortTarget_velocityBuffer,
				  spatialHash.SpatialIndices });

			SetBuffers(compute, reorderCopybackKernel, bufferNameLookup, new ComputeBuffer[]
				{ positionBuffer, sortTarget_positionBuffer, predictedPositionsBuffer,
				  sortTarget_predictedPositionsBuffer, velocityBuffer, sortTarget_velocityBuffer,
				  spatialHash.SpatialIndices });

			SetBuffers(compute, densityKernel, bufferNameLookup, new ComputeBuffer[]
				{ predictedPositionsBuffer, densityBuffer, spatialHash.SpatialKeys, spatialHash.SpatialOffsets });

			SetBuffers(compute, pressureKernel, bufferNameLookup, new ComputeBuffer[]
				{ predictedPositionsBuffer, densityBuffer, velocityBuffer,
				  spatialHash.SpatialKeys, spatialHash.SpatialOffsets, foamBuffer, foamCountBuffer, debugBuffer });

			SetBuffers(compute, viscosityKernel, bufferNameLookup, new ComputeBuffer[]
				{ predictedPositionsBuffer, densityBuffer, velocityBuffer,
				  spatialHash.SpatialKeys, spatialHash.SpatialOffsets });

			SetBuffers(compute, updatePositionsKernel, bufferNameLookup, new ComputeBuffer[]
				{ positionBuffer, velocityBuffer, activeCountBuffer });
				
			SetBuffers(compute, clearCountersKernel, bufferNameLookup, new ComputeBuffer[]
				{ activeCountBuffer });

			SetBuffers(compute, renderKernel, bufferNameLookup, new ComputeBuffer[]
				{ predictedPositionsBuffer, densityBuffer, spatialHash.SpatialKeys, spatialHash.SpatialOffsets });

			SetBuffers(compute, foamUpdateKernel, bufferNameLookup, new ComputeBuffer[]
				{ foamBuffer, foamCountBuffer, predictedPositionsBuffer, densityBuffer,
				  velocityBuffer, spatialHash.SpatialKeys, spatialHash.SpatialOffsets, foamSortTargetBuffer });

			SetBuffers(compute, foamReorderCopyBackKernel, bufferNameLookup, new ComputeBuffer[]
				{ foamBuffer, foamSortTargetBuffer, foamCountBuffer });

			compute.SetInt("numParticles", positionBuffer.count);
			compute.SetInt("MaxWhiteParticleCount", maxFoamParticleCount);

			UpdateSmoothingConstants();
			
			// Initialize prevCentre3 based on the initial transform
			if (bucketRenderer != null)
			{
				float simHalfH = Scale.y * 0.5f;
				float bucketTop    =  simHalfH * bucketRenderer.heightScale;
				float bucketBottom = -simHalfH - bucketRenderer.bottomPadding;
				float centreY      = (bucketTop + bucketBottom) * 0.5f;
				prevCentre3 = transform.position + Vector3.up * centreY;
			}
			else
			{
				prevCentre3 = transform.position;
			}

			if (renderToTex3D) RunSimulationFrame(0);

			SimulationInitCompleted?.Invoke(this);
		}

		void Update()
		{
			if (!isPaused)
			{
				float maxDeltaTime = maxTimestepFPS > 0 ? 1 / maxTimestepFPS : float.PositiveInfinity;
				float dt = Mathf.Min(Time.deltaTime * ActiveTimeScale, maxDeltaTime);
				RunSimulationFrame(dt);
			}

			if (pauseNextFrame) { isPaused = true; pauseNextFrame = false; }

			HandleInput();
		}

		void RunSimulationFrame(float frameDeltaTime)
		{
			float subStepDeltaTime = frameDeltaTime / iterationsPerFrame;
			
			Vector3 targetCentre = transform.position;
			if (bucketRenderer != null)
			{
				float simHalfH = Scale.y * 0.5f;
				float bucketTop    =  simHalfH * bucketRenderer.heightScale;
				float bucketBottom = -simHalfH - bucketRenderer.bottomPadding;
				float centreY      = (bucketTop + bucketBottom) * 0.5f;
				targetCentre = transform.position + Vector3.up * centreY;
			}
			Vector3 startCentre = prevCentre3;
			Vector3 endCentre = targetCentre;

			UpdateSettings(subStepDeltaTime, frameDeltaTime);

			for (int i = 0; i < iterationsPerFrame; i++)
			{
				simTimer += subStepDeltaTime;
				float t0 = (float)i / iterationsPerFrame;
				float t1 = (float)(i + 1) / iterationsPerFrame;
				compute.SetVector("prevCentre3", Vector3.Lerp(startCentre, endCentre, t0));
				compute.SetVector("centre3", Vector3.Lerp(startCentre, endCentre, t1));
				RunSimulationStep();
			}
			
			prevCentre3 = endCentre;

			if (foamActive)
			{
				Dispatch(compute, maxFoamParticleCount, kernelIndex: foamUpdateKernel);
				Dispatch(compute, maxFoamParticleCount, kernelIndex: foamReorderCopyBackKernel);
			}

			if (renderToTex3D) UpdateDensityMap();
		}

		void UpdateDensityMap()
		{
			float maxAxis = Mathf.Max(transform.localScale.x, transform.localScale.y, transform.localScale.z);
			int w = Mathf.RoundToInt(transform.localScale.x / maxAxis * densityTextureRes);
			int h = Mathf.RoundToInt(transform.localScale.y / maxAxis * densityTextureRes);
			int d = Mathf.RoundToInt(transform.localScale.z / maxAxis * densityTextureRes);
			CreateRenderTexture3D(ref DensityMap, w, h, d,
				UnityEngine.Experimental.Rendering.GraphicsFormat.R16_SFloat, TextureWrapMode.Clamp);
			compute.SetTexture(renderKernel, "DensityMap", DensityMap);
			compute.SetInts("densityMapSize", DensityMap.width, DensityMap.height, DensityMap.volumeDepth);
			Dispatch(compute, DensityMap.width, DensityMap.height, DensityMap.volumeDepth, renderKernel);
		}

		void RunSimulationStep()
		{
			Dispatch(compute, positionBuffer.count, kernelIndex: externalForcesKernel);
			Dispatch(compute, positionBuffer.count, kernelIndex: spatialHashKernel);
			spatialHash.Run();
			Dispatch(compute, positionBuffer.count, kernelIndex: reorderKernel);
			Dispatch(compute, positionBuffer.count, kernelIndex: reorderCopybackKernel);
			Dispatch(compute, positionBuffer.count, kernelIndex: densityKernel);
			Dispatch(compute, positionBuffer.count, kernelIndex: pressureKernel);
			if (viscosityStrength != 0) Dispatch(compute, positionBuffer.count, kernelIndex: viscosityKernel);

			// Clear counter before positions update
			Dispatch(compute, 1, kernelIndex: clearCountersKernel);
			
			Dispatch(compute, positionBuffer.count, kernelIndex: updatePositionsKernel);

			// Asynchronously read back the active particle count
			UnityEngine.Rendering.AsyncGPUReadback.Request(activeCountBuffer, (req) =>
			{
				if (!req.hasError && req.done)
				{
					var data = req.GetData<uint>();
					ActiveParticleCount = (int)data[0];
				}
			});
		}

		void UpdateSmoothingConstants()
		{
			float r = smoothingRadius;
			compute.SetFloat("K_SpikyPow2",     15 / (2 * Mathf.PI * Mathf.Pow(r, 5)));
			compute.SetFloat("K_SpikyPow3",     15 / (Mathf.PI      * Mathf.Pow(r, 6)));
			compute.SetFloat("K_SpikyPow2Grad", 15 / (Mathf.PI      * Mathf.Pow(r, 5)));
			compute.SetFloat("K_SpikyPow3Grad", 45 / (Mathf.PI      * Mathf.Pow(r, 6)));
		}

		void UpdateSettings(float stepDeltaTime, float frameDeltaTime)
		{
			if (smoothingRadius != smoothRadiusOld) { smoothRadiusOld = smoothingRadius; UpdateSmoothingConstants(); }

			Vector3 simBoundsSize   = transform.localScale;
			Vector3 simBoundsCentre = transform.position;

			// When a BucketRenderer is assigned, collision bounds = bucket inner wall.
			// The bucket inner wall = simR + radiusPadding, so we pass that directly
			// as boundsSize. This way changing Scale changes both equally.
			if (bucketRenderer != null)
			{
				float simHalfH = Scale.y * 0.5f;
				float innerR   = Scale.x * 0.5f + bucketRenderer.radiusPadding;

				float bucketTop    =  simHalfH * bucketRenderer.heightScale;
				float bucketBottom = -simHalfH - bucketRenderer.bottomPadding;
				float bucketHeight =  bucketTop - bucketBottom;
				float centreY      = (bucketTop + bucketBottom) * 0.5f;

				simBoundsSize   = new Vector3(innerR * 2f, bucketHeight, innerR * 2f);
				simBoundsCentre = transform.position + Vector3.up * centreY;
			}

			compute.SetFloat("deltaTime",              stepDeltaTime);
			compute.SetFloat("whiteParticleDeltaTime", frameDeltaTime);
			compute.SetFloat("simTime",                simTimer);
			compute.SetFloat("gravity",                gravity);
			compute.SetFloat("collisionDamping",       collisionDamping);
			compute.SetFloat("smoothingRadius",        smoothingRadius);
			compute.SetFloat("targetDensity",          targetDensity);
			compute.SetFloat("pressureMultiplier",     pressureMultiplier);
			compute.SetFloat("nearPressureMultiplier", nearPressureMultiplier);
			compute.SetFloat("viscosityStrength",      viscosityStrength);
			compute.SetVector("boundsSize", simBoundsSize);

			// Bottom hole
			compute.SetFloat("holeRadius",  holeRadius);
			compute.SetVector("holeOffset", new Vector4(holeOffset.x, holeOffset.y, 0, 0));

			compute.SetMatrix("localToWorld", transform.localToWorldMatrix);
			compute.SetMatrix("worldToLocal", transform.worldToLocalMatrix);

			float fadeInT = (spawnRateFadeInTime <= 0) ? 1
				: Mathf.Clamp01((simTimer - spawnRateFadeStartTime) / spawnRateFadeInTime);
			compute.SetVector("trappedAirParams",  new Vector3(trappedAirSpawnRate * fadeInT * fadeInT,
				trappedAirVelocityMinMax.x, trappedAirVelocityMinMax.y));
			compute.SetVector("kineticEnergyParams", foamKineticEnergyMinMax);
			compute.SetFloat("bubbleBuoyancy",              bubbleBuoyancy);
			compute.SetInt("sprayClassifyMaxNeighbours",    sprayClassifyMaxNeighbours);
			compute.SetInt("bubbleClassifyMinNeighbours",   bubbleClassifyMinNeighbours);
			compute.SetFloat("bubbleScaleChangeSpeed",      bubbleChangeScaleSpeed);
			compute.SetFloat("bubbleScale",                 bubbleScale);
		}

		void SetInitialBufferData(Spawner3D.SpawnData spawnData)
		{
			positionBuffer.SetData(spawnData.points);
			predictedPositionsBuffer.SetData(spawnData.points);
			velocityBuffer.SetData(spawnData.velocities);
			foamBuffer.SetData(new FoamParticle[foamBuffer.count]);
			debugBuffer.SetData(new float3[debugBuffer.count]);
			foamCountBuffer.SetData(new uint[foamCountBuffer.count]);
			simTimer = 0;
		}

		void HandleInput()
		{
			if (Input.GetKeyDown(KeyCode.Space))      isPaused = !isPaused;
			if (Input.GetKeyDown(KeyCode.RightArrow)) { isPaused = false; pauseNextFrame = true; }
			if (Input.GetKeyDown(KeyCode.Q))          inSlowMode = !inSlowMode;

			if (Input.GetKeyDown(KeyCode.R))
			{
				pauseNextFrame = true;
				SetInitialBufferData(spawnData);
				if (renderToTex3D) RunSimulationFrame(0);
			}
		}

		private float ActiveTimeScale => inSlowMode ? slowTimeScale : normalTimeScale;

		void OnDestroy()
		{
			if (bufferNameLookup != null)
			{
				foreach (var kvp in bufferNameLookup) Release(kvp.Key);
			}
			spatialHash?.Release();
		}

		public struct FoamParticle
		{
			public float3 position;
			public float3 velocity;
			public float lifetime;
			public float scale;
		}

		void OnDrawGizmos()
		{
			var m = Gizmos.matrix;
			Gizmos.matrix = transform.localToWorldMatrix;
			Gizmos.color  = new Color(0, 1, 0, 0.5f);

			const int segments  = 40;
			const float radius  = 0.5f;
			const float halfH   = 0.5f;

			for (int cap = 0; cap < 2; cap++)
			{
				float y = cap == 0 ? -halfH : halfH;
				for (int i = 0; i < segments; i++)
				{
					float a0 = (i       / (float)segments) * Mathf.PI * 2f;
					float a1 = ((i + 1) / (float)segments) * Mathf.PI * 2f;
					Gizmos.DrawLine(
						new Vector3(Mathf.Cos(a0) * radius, y, Mathf.Sin(a0) * radius),
						new Vector3(Mathf.Cos(a1) * radius, y, Mathf.Sin(a1) * radius));
				}
			}

			for (int i = 0; i < 8; i++)
			{
				float a = (i / 8f) * Mathf.PI * 2f;
				Gizmos.DrawLine(
					new Vector3(Mathf.Cos(a) * radius, -halfH, Mathf.Sin(a) * radius),
					new Vector3(Mathf.Cos(a) * radius,  halfH, Mathf.Sin(a) * radius));
			}

			Gizmos.matrix = m;
		}
	}
}
