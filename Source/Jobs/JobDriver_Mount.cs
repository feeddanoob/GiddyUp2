﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Verse;
using Verse.AI;
using GiddyUp.Utilities;
using GiddyUp.Storage;
using RimWorld;

namespace GiddyUp.Jobs
{
    public class JobDriver_Mount : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return true;
        }
        public Pawn Mount { get { return job.targetA.Thing as Pawn; } }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            job.canBashDoors = true;
            job.canBashFences = true;
            this.FailOnDespawnedNullOrForbidden(TargetIndex.A);
            this.FailOnDowned(TargetIndex.A);

            yield return letMountParticipate();
            //yield return Toils_General.Wait(1);//wait one tick to ensure animal is waiting to get mounted before proceding. 
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            if(this.pawn.interactions != null)
            {
                yield return Toils_Interpersonal.WaitToBeAbleToInteract(this.pawn);
            }
            yield return TalkToAnimal(TargetIndex.A);
        }
        private Toil letMountParticipate()
        {
            Toil toil = new Toil();

            toil.defaultCompleteMode = ToilCompleteMode.Never;
            toil.initAction = delegate
            {
                Mount.jobs.StopAll();
                Mount.pather.StopDead();
                Job jobAnimal = new Job(ResourceBank.JobDefOf.Mounted, pawn);
                jobAnimal.count = 1;
                Mount.jobs.TryTakeOrderedJob(jobAnimal);
                ReadyForNextToil();
            };
            return toil;
        }

        private Toil TalkToAnimal(TargetIndex tameeInd)
        {
            Toil toil = new Toil();
            toil.AddFailCondition(delegate { return Mount.CurJob.def != ResourceBank.JobDefOf.Mounted; });
            //toil.AddFailCondition(delegate { return Mount.CurJob.targetA.Thing != pawn; });
            toil.initAction = delegate
            {
                Pawn actor = toil.GetActor();
                if(actor.interactions != null)
                {
                    actor.interactions.TryInteractWith(Mount, InteractionDefOf.AnimalChat);
                }
            };
            toil.defaultCompleteMode = ToilCompleteMode.Delay;
            toil.defaultDuration = 150;
            toil.AddFinishAction(delegate
            {
                FinishAction();
            });
            return toil;
        }

        public void FinishAction()
        {
            if (Mount.CurJob != null && Mount.CurJob.def == ResourceBank.JobDefOf.Mounted)
            {
                ExtendedPawnData pawnData = Setup._extendedDataStorage.GetExtendedDataFor(this.pawn.thingIDNumber);
                ExtendedPawnData animalData = Setup._extendedDataStorage.GetExtendedDataFor(Mount.thingIDNumber);
                pawnData.Mount = Mount;
                TextureUtility.setDrawOffset(pawnData);
            }
        }
    }
}