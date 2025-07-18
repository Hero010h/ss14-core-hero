﻿using Robust.Shared.Serialization;

namespace Content.Shared._White.TTS;

[Serializable, NetSerializable]
// ReSharper disable once InconsistentNaming
public sealed class RequestTTSEvent(string text) : EntityEventArgs
{
    public string Text { get; } = text;
}