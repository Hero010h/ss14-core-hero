using System.Numerics;
using Content.Shared._White.Animations;
using Robust.Client.Animations;
using Robust.Client.GameObjects;
using Robust.Shared.Animations;
using Robust.Shared.GameStates;
using static Content.Shared._White.Animations.EmoteAnimationComponent;

namespace Content.Client._White.Animations;

public sealed class EmoteAnimationSystem : EntitySystem
{
    [Dependency] private readonly AnimationPlayerSystem _animationSystem = default!;

    private readonly Dictionary<string, Action<EntityUid>> _emoteList = new();

    public const string AnimationKey = "emoteAnimationKeyId";
    private const string AnimationKeyTurn = "emoteAnimationKeyId_rotate";

    //OnVerbsResponse?.Invoke(msg);
    public override void Initialize()
    {
        SubscribeLocalEvent<EmoteAnimationComponent, ComponentHandleState>(OnHandleState);

        // EmoteFlip animation
        _emoteList.Add("EmoteFlip", uid =>
        {
            if (_animationSystem.HasRunningAnimation(uid, AnimationKey))
                return;

            var baseAngle = Angle.Zero;
            if (EntityManager.TryGetComponent(uid, out SpriteComponent? sprite))
            {
                baseAngle = sprite.Rotation;
            }

            var animation = new Animation
            {
                Length = TimeSpan.FromMilliseconds(500),
                AnimationTracks =
                {
                    new AnimationTrackComponentProperty
                    {
                        ComponentType = typeof(SpriteComponent),
                        Property = nameof(SpriteComponent.Rotation),
                        InterpolationMode = AnimationInterpolationMode.Linear,
                        KeyFrames =
                        {
                            new AnimationTrackProperty.KeyFrame(Angle.FromDegrees(baseAngle.Degrees - 10), 0f),
                            new AnimationTrackProperty.KeyFrame(Angle.FromDegrees(baseAngle.Degrees + 180), 0.25f),
                            new AnimationTrackProperty.KeyFrame(Angle.FromDegrees(baseAngle.Degrees + 360), 0.25f),
                            new AnimationTrackProperty.KeyFrame(Angle.FromDegrees(baseAngle.Degrees), 0f),
                        }
                    }
                }
            };

            _animationSystem.Play(uid, animation, AnimationKey);
        });

        // EmoteJump animation
        _emoteList.Add("EmoteJump", uid =>
        {
            if (_animationSystem.HasRunningAnimation(uid, AnimationKey))
                return;

            var animation = new Animation
            {
                Length = TimeSpan.FromMilliseconds(250),
                AnimationTracks =
                {
                    new AnimationTrackComponentProperty
                    {
                        ComponentType = typeof(SpriteComponent),
                        Property = nameof(SpriteComponent.Offset),
                        InterpolationMode = AnimationInterpolationMode.Cubic,
                        KeyFrames =
                        {
                            new AnimationTrackProperty.KeyFrame(Vector2.Zero, 0f),
                            new AnimationTrackProperty.KeyFrame(new Vector2(0, 0.7f), 0.125f),
                            new AnimationTrackProperty.KeyFrame(Vector2.Zero, 0.125f),
                        }
                    }
                }
            };

            _animationSystem.Play(uid, animation, AnimationKey);
        });

        // EmoteTurn animation
        _emoteList.Add("EmoteTurn", uid =>
        {
            if (_animationSystem.HasRunningAnimation(uid, AnimationKeyTurn))
                return;

            var animation = new Animation
            {
                Length = TimeSpan.FromMilliseconds(900),
                AnimationTracks =
                {
                    new AnimationTrackComponentProperty
                    {
                        ComponentType = typeof(TransformComponent),
                        Property = nameof(TransformComponent.LocalRotation),
                        InterpolationMode = AnimationInterpolationMode.Linear,
                        KeyFrames =
                        {
                            new AnimationTrackProperty.KeyFrame(Angle.FromDegrees(0), 0f),
                            new AnimationTrackProperty.KeyFrame(Angle.FromDegrees(90), 0.075f),
                            new AnimationTrackProperty.KeyFrame(Angle.FromDegrees(180), 0.075f),
                            new AnimationTrackProperty.KeyFrame(Angle.FromDegrees(270), 0.075f),
                            new AnimationTrackProperty.KeyFrame(Angle.Zero, 0.075f),
                            new AnimationTrackProperty.KeyFrame(Angle.FromDegrees(90), 0.075f),
                            new AnimationTrackProperty.KeyFrame(Angle.FromDegrees(180), 0.075f),
                            new AnimationTrackProperty.KeyFrame(Angle.FromDegrees(270), 0.075f),
                            new AnimationTrackProperty.KeyFrame(Angle.Zero, 0.075f),
                        }
                    }
                }
            };

            _animationSystem.Play(uid, animation, AnimationKeyTurn);
        });
    }

    private void OnHandleState(EntityUid uid, EmoteAnimationComponent component, ref ComponentHandleState args)
    {
        if (args.Current is not EmoteAnimationComponentState state)
            return;

        component.AnimationId = state.AnimationId;
        if (_emoteList.TryGetValue(component.AnimationId, out var value))
        {
            value.Invoke(uid);
        }
    }
}
