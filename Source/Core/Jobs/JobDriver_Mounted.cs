﻿using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using Verse;
using Verse.AI;
using Settings = GiddyUp.ModSettings_GiddyUp;

namespace GiddyUp.Jobs
{
	public class JobDriver_Mounted : JobDriver
	{
		public static HashSet<JobDef> allowedJobs;
		public Pawn rider;
		ExtendedPawnData riderData;
		Map map;
		public bool isTrained, interrupted, isParking;
		IntVec3 dismountingAt;
		IntVec3 riderOriginalDestinaton;
		int ticker = 30;
		enum DismountReason { False, Interrupted, BadState, LeftMap, NotSpawned, WrongMount, BadJob, ForbiddenAreaAndCannotPark, Parking };

		public override IEnumerable<Toil> MakeNewToils()
		{
			this.FailOnDespawnedNullOrForbidden(TargetIndex.A);
			rider = job.targetA.Thing as Pawn;
			riderData = rider.GetGUData();
			isTrained = pawn.training != null && pawn.training.HasLearned(TrainableDefOf.Obedience);
			map = Map;
			yield return WaitForRider();
			yield return DelegateMovement();
		}
		public override bool TryMakePreToilReservations(bool errorOnFailed)
		{
			return true;
		}
		DismountReason RiderShouldDismount(ExtendedPawnData riderData)
		{
			if (interrupted || riderData == null || riderData.mount == null || riderData.ID != rider.thingIDNumber) return DismountReason.Interrupted;

			if (isParking)
			{
				if (rider.pather.nextCell == dismountingAt)
				{
					rider.pather.StartPath(riderOriginalDestinaton, PathEndMode.OnCell); //Resume original work
					return DismountReason.Parking;
				}
			}
			
			if (--ticker != 0) return DismountReason.False;
			ticker = 30; //Check every twice per second
			
			//Check physical and mental health
			if (rider.Downed || rider.Dead || pawn.Downed || pawn.Dead || 
				pawn.HasAttachment(ThingDefOf.Fire) || rider.HasAttachment(ThingDefOf.Fire) || rider.GetPosture() != PawnPosture.Standing ||
				pawn.InMentalState || (rider.InMentalState && rider.MentalState.def != MentalStateDefOf.PanicFlee)
			)
			{
				return DismountReason.BadState;
			}

			//This will move the mount off the map, assuming their rider left the map as well
			if (!rider.Spawned)
			{
				var riderIsColonist = rider.IsColonist;
				if (!riderIsColonist || rider.GetCaravan() != null)
				{
					pawn.ExitMap(riderIsColonist, CellRect.WholeMap(map).GetClosestEdge(pawn.Position));
					return DismountReason.LeftMap;
				}
				else return DismountReason.NotSpawned;
			}

			var allowedJob = allowedJobs.Contains(rider.CurJobDef);
			var riderDestinaton = rider.pather.Destination.Cell;
			map.GetGUAreas(out Area areaNoMount, out Area areaDropAnimal);

			if (!rider.Drafted)
			{
				//If the mount's non-drafted rider is heading towards a forbidden area, they'll need to dismount
				if (!isParking && Settings.rideAndRollEnabled && (!allowedJob || !riderDestinaton.CanRideAt(areaNoMount)))
				{
					if (FindPlaceToDismount(areaDropAnimal, riderDestinaton, out dismountingAt))
					{
						riderOriginalDestinaton = riderDestinaton;
						rider.pather.StartPath(dismountingAt, PathEndMode.OnCell);
						isParking = true;
					}
					else return DismountReason.ForbiddenAreaAndCannotPark;
				}
			}
			else
			{
				if (!allowedJob && rider.Position.DistanceTo(rider.pather.Destination.Cell) < 8) return DismountReason.BadJob;
				if (!pawn.Faction.def.isPlayer) return DismountReason.False;
			}
			
			if (Settings.caravansEnabled)
			{
				var riderMindstateDef = rider.mindState?.duty?.def;
				if (riderMindstateDef == DutyDefOf.TravelOrWait || 
					riderMindstateDef == DutyDefOf.TravelOrLeave || 
					riderMindstateDef == DutyDefOf.PrepareCaravan_GatherAnimals || 
					riderMindstateDef == DutyDefOf.PrepareCaravan_GatherDownedPawns)
				{
					return riderData.reservedMount == pawn ? DismountReason.False : DismountReason.WrongMount;
				}
				
				if (rider.Position.CloseToEdge(map, 10)) return DismountReason.False; //Caravan just entered map and has not picked a job yet on this tick.
			}
			return DismountReason.False;
		}
		bool CheckReason(DismountReason dismountReason, out DismountReason reason)
		{
			reason = dismountReason;
			return dismountReason != DismountReason.False;
		}
		Toil WaitForRider()
		{
			return new Toil()
			{
				defaultCompleteMode = ToilCompleteMode.Never,
				tickAction = delegate
				{
					if (rider == null || rider.Dead || !rider.Spawned || rider.Downed || rider.InMentalState)
					{
						interrupted = true;
						ReadyForNextToil();
						return;
					}

					if (riderData.mount != null && riderData.mount == pawn)
					{
						ReadyForNextToil();
					}

					var curJobDef = rider.CurJobDef;
					if (curJobDef != ResourceBank.JobDefOf.Mount && 
						curJobDef != JobDefOf.Vomit && 
						curJobDef != JobDefOf.Wait_MaintainPosture && 
						curJobDef != JobDefOf.SocialRelax && 
						curJobDef != JobDefOf.Wait && 
						riderData.mount == null)
					{
						if (Settings.logging) Log.Message("[Giddy-Up] Animal " + pawn.thingIDNumber + " is no longer waiting for " + rider.Name.ToString());
						interrupted = true;
						ReadyForNextToil();
					}

				}
			};
		}
		Toil DelegateMovement()
		{
			return new Toil()
			{
				defaultCompleteMode = ToilCompleteMode.Never,
				tickAction = delegate
				{
					if (CheckReason(RiderShouldDismount(riderData), out DismountReason dismountReason))
					{
						if (Settings.logging) Log.Message("[Giddy-Up] Pawn " + pawn.thingIDNumber + " dismounting for reason: " + dismountReason.ToString());
						ReadyForNextToil();
						return;
					}
					pawn.Drawer.tweener = rider.Drawer.tweener; //Could probably just be set once, but reloading could cause issues?
					pawn.Position = rider.Position;
					pawn.Rotation = rider.Rotation;
					if (isTrained) TryAttackEnemy(rider);
				},
				finishActions = new List<Action>() { (delegate
				{
					//Check mount first. If it's null then they must have dismounted outside the driver's control
					if (riderData.mount != null) rider.Dismount(pawn, riderData, false, isParking ? dismountingAt : default(IntVec3));
				})}
			};
		}
		void TryAttackEnemy(Pawn rider)
		{
			Thing targetThing = null;
			bool confirmedHostile = false;
			
			//The mount has something targetted but not the rider, so pass the target
			if (rider.TargetCurrentlyAimingAt != null)
			{
				targetThing = rider.TargetCurrentlyAimingAt.Thing;
			}
			//The rider is already trying to attack something
			else if (rider.CurJobDef == JobDefOf.AttackMelee && rider.CurJob.targetA.Thing.HostileTo(rider))
			{
				targetThing = rider.CurJob.targetA.Thing;
				confirmedHostile = true;
			}

			if (targetThing != null && (confirmedHostile || targetThing.HostileTo(rider)))
			{
				var verb = pawn.meleeVerbs?.TryGetMeleeVerb(targetThing);
				if (verb == null || !verb.CanHitTarget(targetThing))
				{
					pawn.TryStartAttack(targetThing); //Try start ranged attack if possible
				}
				else pawn.meleeVerbs.TryMeleeAttack(targetThing);
			}
		}
		bool FindPlaceToDismount(Area areaDropAnimal, IntVec3 riderDestinaton, out IntVec3 parkLoc)
		{
			if (areaDropAnimal == null) TryParkAnimalPen(out parkLoc);
			else parkLoc = areaDropAnimal.GetClosestAreaLoc(riderDestinaton);

			//Invalide the results if not reachable
			if (!map.reachability.CanReach(rider.Position, parkLoc, PathEndMode.OnCell, TraverseParms.For(TraverseMode.PassDoors, Danger.Deadly, false)))
			{
				parkLoc = IntVec3.Invalid;
			}

			//Dropoff is too far away, setup a hitching point instead
			if (parkLoc.DistanceTo(riderDestinaton) > Settings.autoHitchDistance)
			{
				Predicate<IntVec3> freeCell = delegate(IntVec3 cell)
				{
					return (cell.Standable(map) && 
						cell.GetDangerFor(pawn, map) == Danger.None && 
						!cell.Fogged(map) &&
						cell.InBounds(map) &&
						pawn.CanReserveAndReach(cell, PathEndMode.OnCell, Danger.None, 1, -1, null, false));
				};
				if (!CellFinder.TryFindRandomCellNear(riderDestinaton, map, 4, freeCell, out parkLoc, 16))
				{
					parkLoc = IntVec3.Invalid;
				}
			}
			//Validate results
			if (parkLoc == IntVec3.Invalid)
			{
				if (Prefs.DevMode) Log.Message("[Giddy-Up] Pawn " + pawn.Name.ToString() + " could not ride their mount to their job but could not find any places to dismount. Immediately dismounting.");
			}
			//Looks good, begin pathing
			else return true;
			return false;

			#region Embedded methods
			void TryParkAnimalPen(out IntVec3 parkLoc)
			{
				parkLoc = IntVec3.Invalid;
				var pen = AnimalPenUtility.GetPenAnimalShouldBeTakenTo(rider, pawn, out string failReason, true, true, false, true);
				if (pen != null)
				{
					parkLoc = AnimalPenUtility.FindPlaceInPenToStand(pen, rider);
				}
			}
			#endregion
		}
	}
}