using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using System.Linq;
//using Multiplayer.API;
using Verse;
using Verse.AI;
using Settings = GiddyUp.ModSettings_GiddyUp;

namespace GiddyUp
{
	public class MountUtility
	{
		public static HashSet<PawnKindDef> allWildAnimals = new HashSet<PawnKindDef>(), allDomesticAnimals = new HashSet<PawnKindDef>();
		public enum GiveJobMethod { Inject, Try, Instant, Think };
		enum ListToUse { Local, Foreign, Domestic };
		
		//Centralized funnel for all the various mounting calls
		public static ThinkResult? GiveMountJob(Pawn rider, Pawn animal, GiveJobMethod giveJobMethod = GiveJobMethod.Inject, ThinkResult? thinkResult = null, Job currentJob = null)
		{
			//Immediately mount the pawn on the animal. This is done when the mount job finishes, or emulating that it has already finished such as when pawns come in pre-mounted
			if (giveJobMethod == GiveJobMethod.Instant)
			{
				//First check if the pawn had a mount to begin with...
				ExtendedPawnData pawnData = ExtendedDataStorage.GUComp[rider.thingIDNumber];
				if (animal == null) animal = pawnData.reservedMount;
				
				//If they did...
				if (animal != null)
				{
					//Instantly mount, as if the mount jobdriver had just finished
					pawnData.Mount = animal;
					
					//Set the animal job and state
					if (animal.CurJob?.def != ResourceBank.JobDefOf.Mounted)
					{
						animal.mindState.duty = new PawnDuty(DutyDefOf.Defend);
						if (animal.jobs == null) animal.jobs = new Pawn_JobTracker(animal);
						animal.jobs.TryTakeOrderedJob(new Job(ResourceBank.JobDefOf.Mounted, rider) { count = 1});
					}
					
					//Sanity check the xData
					pawnData.ReserveMount = animal;
					ExtendedDataStorage.GUComp[animal.thingIDNumber].reservedBy = rider;
				}
			}
			//This prompts them to mount up before they carry out another job they were planning to do
			else if (giveJobMethod == GiveJobMethod.Inject)
			{
				if (currentJob != null) rider.jobs?.jobQueue?.EnqueueFirst(currentJob);
				if (thinkResult != null) return new ThinkResult(new Job(ResourceBank.JobDefOf.Mount, animal) {count = 1}, thinkResult.Value.SourceNode, thinkResult.Value.Tag, false);
			}
			//This has them mount after they're done doing their current task
			else if (giveJobMethod == GiveJobMethod.Try)
			{
				animal.jobs.StopAll();
				animal.jobs.EndCurrentJob(JobCondition.InterruptForced, false, false); //The StopAll above will trigger the WaitForRider job. This will stop it.
				animal.pather.StopDead();
				rider.jobs.TryTakeOrderedJob(new Job(ResourceBank.JobDefOf.Mount, animal) {count = 1});
			}
			//TODO: May be possible to merge this and Inject together
			else if (giveJobMethod == GiveJobMethod.Think)
			{
				var job = new Job(ResourceBank.JobDefOf.Mount, animal) {count = 1};
				rider.jobs?.StartJob(job, JobCondition.InterruptOptional, null, true, false);
			}
			
			return null;
		}
		public static bool GenerateMounts(ref List<Pawn> list, IncidentParms parms)
		{
			//if (MP.enabled) return false; // Best we can do for now
			Map map = parms.target as Map;
			if (map == null)
			{
				Caravan caravan = (Caravan)parms.target;
				int tile = caravan.Tile;
				map = Current.Game.FindMap(tile);
				if (map == null) return false;
			}

			int mountChance = Settings.enemyMountChance;
			int mountChancePreInd = Settings.enemyMountChancePreInd;
			float domesticWeight = Settings.nonWildWeight;
			float localWeight = Settings.inBiomeWeight;
			float foreignWeight = Settings.outBiomeWeight;
			
			mountChance = GetMountChance(parms, mountChance, mountChancePreInd);
			if (mountChance == -1) return false; //wrong faction

			//Setup working list
			PawnKindDef[] wildAnimals;
			PawnKindDef[] domesticAnimals;
			PawnKindDef[] localAnimals;
			
			FactionRestrictions factionRules = parms.faction?.def?.GetModExtension<FactionRestrictions>();
			if (factionRules != null)
			{
				//Override working list
				wildAnimals = factionRules.allowedWildAnimals;
				domesticAnimals = factionRules.allowedNonWildAnimals;
				localAnimals = map.Biome.AllWildAnimals.
					Where(x => wildAnimals.Contains(x) && map.mapTemperature.SeasonAcceptableFor(x.race) && 
					Settings.mountableCache.Contains(x.shortHash) && parms.points > x.combatPower * 2f).ToArray();

				//Override mount chance
				if (factionRules.mountChance > -1) mountChance = factionRules.mountChance;

				//Apply weights if needed
				if (wildAnimals.Length == 0) localWeight = foreignWeight = 0;
				else if (factionRules.wildAnimalWeight >= 0) foreignWeight = factionRules.wildAnimalWeight;

				if (domesticAnimals.Length == 0) domesticWeight = 0;
				else if (factionRules.nonWildAnimalWeight >= 0) foreignWeight = factionRules.nonWildAnimalWeight;
			}
			else
			{
				wildAnimals = allWildAnimals.ToArray();
				domesticAnimals = allDomesticAnimals.ToArray();
				localAnimals = map.Biome.AllWildAnimals.
					Where(x => map.mapTemperature.SeasonAcceptableFor(x.race) && Settings.mountableCache.Contains(x.shortHash) && parms.points > x.combatPower * 2f).ToArray();
			}

			//Setup weight ranges.
			float totalWeight = localWeight + foreignWeight + domesticWeight; //EG 100
			localWeight /= totalWeight * 100f; //EG 20
			foreignWeight /= totalWeight * 100f; //EG 20+10 = 30
			foreignWeight += localWeight;
			float averageCommonality = AverageAnimalCommonality(map.Biome);

			var length = list.Count;
			for (int i = 0; i < length; i++)
			{
				Pawn pawn = list[i];
				PawnKindDef pawnKindDef = null;

				if (!pawn.RaceProps.Humanlike || pawn.kindDef == PawnKindDefOf.Slave) continue;

				int random = Rand.Range(1, 100);

				CustomMounts modExtension = pawn.kindDef.GetModExtension<CustomMounts>();
				if (modExtension != null)
				{
					if (modExtension.mountChance <= random) continue;

					Rand.PushState();
					bool found = modExtension.possibleMounts.TryRandomElementByWeight((KeyValuePair<PawnKindDef, int> mount) => mount.Value, out KeyValuePair<PawnKindDef, int> selectedMount);
					Rand.PopState();
					if (found) pawnKindDef = selectedMount.Key;
				}
				else
				{
					if (mountChance <= random) continue;
					int pawnHandlingLevel = pawn.skills.GetSkill(SkillDefOf.Animals).Level;
					if (pawnHandlingLevel >= Settings.minHandlingLevel) continue;

					PawnKindDef[] workingList;
					bool domestic = false;
					switch (DetermineList(localWeight, foreignWeight, random))
					{
						case ListToUse.Local: workingList = localAnimals; break;
						case ListToUse.Foreign: workingList = wildAnimals; break;
						default: workingList = domesticAnimals; domestic = true; break;
					}

					if (domestic) workingList.Where(x => Settings.mountableCache.Contains(x.shortHash)).
						TryRandomElementByWeight(def => def.race.BaseMarketValue / def.race.GetStatValueAbstract(StatDefOf.CaravanRidingSpeedFactor), out pawnKindDef);
					else workingList.Where(x => map.mapTemperature.SeasonAcceptableFor(x.race) && Settings.mountableCache.Contains(x.shortHash) && parms.points > x.combatPower * 2f).
						TryRandomElementByWeight(def => CalculateCommonality(def, map.Biome, pawnHandlingLevel, averageCommonality), out pawnKindDef);
				}

				//Validate and spawn
				if (pawnKindDef == null) 
				{
					if (Settings.logging) Log.Warning("[Giddy-up] Could not find any suitable animal for " + pawn.thingIDNumber);
					return false;
				}
				Pawn animal = PawnGenerator.GeneratePawn(pawnKindDef, parms.faction);
				GenSpawn.Spawn(animal, pawn.Position, map, parms.spawnRotation);

				//Set their training
				animal.playerSettings = new Pawn_PlayerSettings(animal);
				animal.training.Train(TrainableDefOf.Obedience, pawn);

				//Mount up
				GiveMountJob(pawn, animal, GiveJobMethod.Instant);
				list.Add(animal);
			}
			return true;
		}
		
		static ListToUse DetermineList(float localWeight, float foreignWeight, int random)
		{
			if (random < foreignWeight) return ListToUse.Local;
			else if (random >= localWeight && random < foreignWeight) return ListToUse.Foreign;
			return ListToUse.Domestic;
		}
		static float AverageAnimalCommonality(BiomeDef biome)
		{
			float sum = 0;
			float count = 0f;
			foreach (PawnKindDef animalKind in biome.AllWildAnimals)
			{
				sum += biome.CommonalityOfAnimal(animalKind);
				++count;
			}
			return sum / count;
		}
		static float CalculateCommonality(PawnKindDef def, BiomeDef biome, int pawnHandlingLevel, float averageCommonality = 0)
		{
			float commonality;
			if (averageCommonality == 0) commonality = biome.CommonalityOfAnimal(def);
			else commonality = averageCommonality;

			//minimal level to get bonus. 
			pawnHandlingLevel = pawnHandlingLevel > 5 ? pawnHandlingLevel - 5 : 0;

			//Common animals more likely when pawns have low handling, and rare animals more likely when pawns have high handling.  
			float commonalityAdjusted = commonality * ((15f - (float)commonality)) / 15f + (1 - commonality) * ((float)pawnHandlingLevel) / 15f;
			//Wildness decreases the likelyhood of the mount being picked. Handling level mitigates this. 
			float wildnessPenalty = 1 - (def.RaceProps.wildness * ((15f - (float)pawnHandlingLevel) / 15f));

			//Log.Message("name: " + def.defName + ", commonality: " + commonality + ", pawnHandlingLevel: " + pawnHandlingLevel + ", wildness: " + def.RaceProps.wildness + ", commonalityBonus: " + commonalityAdjusted + ", wildnessPenalty: " + wildnessPenalty + ", result: " + commonalityAdjusted * wildnessPenalty);
			return commonalityAdjusted * wildnessPenalty;
		}
		static int GetMountChance(IncidentParms parms, int mountChance, int mountChancePreInd)
		{
			if (parms.faction == null) return -1;
			if (parms.faction.def.techLevel < TechLevel.Industrial) return mountChancePreInd;
			else if (parms.faction.def != FactionDefOf.Mechanoid) return mountChance;
			return -1;
		}
	}
}