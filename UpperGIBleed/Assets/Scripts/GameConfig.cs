/// <summary>Tuning values. Mirrors web/config/config.js where the two share a mechanic.</summary>
public static class GameConfig
{
    public const float ScopeSpeed = 0.62f;         // scope units / s while ↑ or ↓ is held
    public const float ScopeAccel = 3.2f;
    public const float TipSpeed = 1.5f;            // rad / s while ← or → is held
    public const float TipAccel = 9.0f;
    public const float ProbeLag = 0.35f;           // s for the shaft to catch up with the twisting tip
    public const float TapHold = 0.09f;            // a very quick tap still moves the scope for this long
    public const float TipScreenRadius = 0.42f;    // tip circle radius as a fraction of the half-height of the view

    public const float CauteryContactSec = 1.6f;   // continuous contact needed to coagulate
    public const float CauteryDecay = 1.5f;        // progress lost per second when contact breaks
    public const float CauteryTol = 0.26f;         // world units from the lesion centre that count as contact
    public const float WashRadius = 0.55f;         // water-jet footprint on the wall (world units)
    public const float WashTol = 0.6f;             // the jet counts as "on the source" within this distance
    public const float WashClearRate = 5.0f;       // blood cleared per second at the jet centre
    public const float BandTol = 0.3f;
    public const int BandsLoaded = 6;
    public const float BandCooldown = 0.9f;
    public const float LossLimitMl = 1500f;        // patient decompensates here
    public const float ClearViewSecToSignOff = 3f; // how long the site must be seen clear after control
    public const float ClearCover = 0.18f;
    public const float FlowBase = 0.2f, FlowPerCell = 0.04f;
    public const int TrailWarmupTicks = 120;

    // scoring
    public const float ScoreTimeWeight = 550f, ScoreBloodWeight = 450f;
    public const float UnverifiedMultiplier = 0.6f;
    public const float LoopDifficulty = 0.15f;     // bleed rate multiplier added per full loop of the three cases
}
