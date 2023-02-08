﻿using System;
using UnityEngine;
using Verse;

namespace GiddyUp
{
    public class Area_GU : Area
    {
        String label;
        Color color = Color.magenta;
        public Area_GU() { }
        public Area_GU(AreaManager areaManager, string label) : base(areaManager)
        {
            this.color = new Color(Rand.Value, Rand.Value, Rand.Value);
            this.label = label;
        }
        public override string Label
        {
            get
            {
                return label;
            }
        }
        public override Color Color
        {
            get
            {
                return color;
            }
        }
        public override int ListPriority
        {
            get
            {
                return 300;
            }
        }
        public override string GetUniqueLoadID()
        {
            return label; //only one such area, so label is sufficient. 
        }
        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look<string>(ref this.label, "label", null, false);
            Scribe_Values.Look<Color>(ref this.color, "color", default(Color), false);
        }
        //Equiv version of the vanilla GetLabeled method, but it avoids iterating the list twice
        public static bool GetGUAreasFast(Map map, out Area areaNoMount, out Area areaDropAnimal)
        {
            var list = map.areaManager.areas;
            var length = list.Count;
            areaNoMount = null;
            areaDropAnimal = null;
            for (int i = 0; i < length; i++)
            {
                var area = list[i];
                var label = area.Label;
                if (label == GiddyUp.ResourceBank.NOMOUNT_LABEL) areaNoMount = area;
                else if (label == GiddyUp.ResourceBank.DROPANIMAL_LABEL) areaDropAnimal = area;
            }
            return areaNoMount != null;
        }
    }
}