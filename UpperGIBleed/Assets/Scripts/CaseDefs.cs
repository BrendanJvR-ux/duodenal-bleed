using System;
using UnityEngine;

public enum LesionKind { Ulcer, Varix }
public enum Tool { Wash = 0, Cautery = 1, Banding = 2 }

/// <summary>One playable case: where the scope starts, where the lesion is, which attachment treats it, and its text.</summary>
[Serializable]
public class CaseDef
{
    public string id, shortName, title, where, vignette;
    public float startZ, startTipDeg = 60;
    public LesionKind kind;
    public string style;                 // "gastric" | "duodenal" for ulcers
    public float lesionDeg, lesionW, lesionRadius;
    public int flowDir = -1;             // +1: blood runs deeper (down the oesophagus); -1: it pools back
    public float bleedMl, emit, parSec;
    public Tool intended;
    public string sourceName;
    public bool washOk = true, cauteryOk, bandingOk;
    public string cueReveal, cueCautery, cueBanding, cueBandingMiss;
    public string noteWash, noteCautery, noteBanding;
    public VaricesDef varices;

    public bool Suitable(Tool t) => t == Tool.Wash ? washOk : t == Tool.Cautery ? cauteryOk : bandingOk;
}

[Serializable]
public class VaricesDef
{
    public float w0, w1; public int bleed;
    public float[] cordDeg, cordPhase;
}

public static class Cases
{
    public static readonly CaseDef[] All =
    {
        new CaseDef
        {
            id = "varices", shortName = "Oesophagus", title = "Oesophageal variceal bleed",
            where = "Distal oesophagus, about 4 cm above the gastro-oesophageal junction",
            vignette = "Fictional patient with cirrhosis and portal hypertension, vomiting fresh blood. Varices run down the distal oesophagus and one has ruptured. A band-ligation cap is loaded on the scope.",
            startZ = 1.6f, startTipDeg = 60, kind = LesionKind.Varix, lesionDeg = 200, lesionW = 5.9f, lesionRadius = 0.13f,
            varices = new VaricesDef { w0 = 4.3f, w1 = 7.0f, bleed = 3, cordDeg = new float[] { 20, 80, 140, 200, 260, 320 }, cordPhase = new float[] { 0.5f, 2.0f, 4.1f, 1.3f, 3.3f, 5.2f } },
            flowDir = 1, bleedMl = 22, emit = 1.3f, parSec = 50, intended = Tool.Banding, sourceName = "varix",
            cauteryOk = false, bandingOk = true,
            cueReveal = "Blood spurts from a ruptured varix: this is the source.",
            cueCautery = "Cautery: not for varices. The wall is paper thin and thermal injury can make it bleed more. Band it.",
            cueBandingMiss = "Band fired with no varix in the cap: that band is wasted and leaves a nodule of normal mucosa.",
            noteWash = "Fresh blood spurting from the ruptured varix confirmed an active bleed.",
            noteCautery = "Cautery does not belong on a varix: the wall is thin, so thermal injury can deepen the bleed or ulcerate. Banding strangulates the vessel.",
            noteBanding = "Banding captures the varix in the cap and strangulates it, which is the standard endoscopic treatment for a bleeding oesophageal varix.",
        },
        new CaseDef
        {
            id = "gastric", shortName = "Stomach", title = "Gastric ulcer bleed",
            where = "Lesser curve of the stomach, just before the incisura angularis",
            vignette = "Fictional patient on regular anti-inflammatories with black stools and dizziness. The scope is in the gastric body and a pool of blood has collected in the dependent fundus.",
            startZ = 9.4f, startTipDeg = 60, kind = LesionKind.Ulcer, style = "gastric", lesionDeg = 285, lesionW = 14.0f, lesionRadius = 0.28f,
            flowDir = -1, bleedMl = 14, emit = 0.8f, parSec = 55, intended = Tool.Cautery, sourceName = "ulcer",
            cauteryOk = true, bandingOk = false,
            cueReveal = "Fresh blood wells up from the ulcer base: this is the source.",
            cueBanding = "Banding: a gastric ulcer is a flat crater in a thick wall. There is nothing to suck into the cap, and a band here risks perforation. Try another attachment.",
            cueBandingMiss = "Banding: nothing in the cap, so there is no lesion to capture here.",
            noteWash = "Fresh blood welling from the ulcer base showed it was still actively bleeding.",
            noteCautery = "Thermal contact on the ulcer base coagulates the exposed vessel.",
            noteBanding = "A flat, fibrotic gastric ulcer cannot be sucked into the cap, so banding cannot work and risks perforation. Banding is for varices.",
        },
        new CaseDef
        {
            id = "duodenal", shortName = "Duodenum", title = "Duodenal bulb ulcer bleed",
            where = "Posterior wall of the duodenal bulb (D1)",
            vignette = "Fictional patient with epigastric pain and melaena. The scope has just passed the pylorus into the duodenal bulb, and fresh blood is pooling ahead.",
            startZ = 18.75f, startTipDeg = 60, kind = LesionKind.Ulcer, style = "duodenal", lesionDeg = 150, lesionW = 21.2f, lesionRadius = 0.2f,
            flowDir = -1, bleedMl = 16, emit = 0.95f, parSec = 50, intended = Tool.Cautery, sourceName = "ulcer",
            cauteryOk = true, bandingOk = false,
            cueReveal = "Fresh blood wells up from the crater: this is the source.",
            cueBanding = "Banding: the ulcer is flat and fibrotic, so it cannot be captured in the cap, and the duodenal wall is thin. Try another attachment.",
            cueBandingMiss = "Banding: nothing in the cap, so there is no lesion to capture here.",
            noteWash = "Fresh blood welling back from the crater showed it was still actively bleeding.",
            noteCautery = "Thermal contact on the crater coagulates the exposed vessel.",
            noteBanding = "A flat, fibrotic duodenal ulcer cannot be sucked into the cap, and the thin duodenal wall makes banding unsafe. Banding is for varices.",
        },
    };
}
