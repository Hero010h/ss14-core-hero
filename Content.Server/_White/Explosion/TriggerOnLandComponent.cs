﻿namespace Content.Server._White.Explosion;

[RegisterComponent]
public sealed partial class TriggerOnLandComponent : Component
{
    [DataField]
    public float Delay = 1f;
}
