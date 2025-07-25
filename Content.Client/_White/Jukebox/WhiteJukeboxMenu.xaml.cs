﻿using System.Linq;
using Content.Shared._White.Jukebox;
using Robust.Client.AutoGenerated;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Client.UserInterface.XAML;
using Robust.Shared.Timing;

namespace Content.Client._White.Jukebox;

[GenerateTypedNameReferences]
public sealed partial class WhiteJukeboxMenu : DefaultWindow
{
    [Dependency] private readonly IEntityManager _entityManager = default!;
    private readonly JukeboxSystem _jukeboxSystem;

    private readonly EntityUid _jukeboxEntity;
    private readonly WhiteJukeboxComponent _component;

    private readonly List<JukeboxSongEntry> _defaultSongsEntries = new() { };
    private readonly List<JukeboxSongEntry> _tapeSongsEntries = new() { };

    public WhiteJukeboxMenu(EntityUid jukeboxEntity, WhiteJukeboxComponent component)
    {
        RobustXamlLoader.Load(this);
        IoCManager.InjectDependencies(this);

        _jukeboxSystem = _entityManager.System<JukeboxSystem>();

        _jukeboxEntity = jukeboxEntity;
        _component = component;

        /*TabContainer.SetTabTitle(DefaultSongsTab, "Прямо с завода");*/
        TabContainer.SetTabTitle(TapeSongsTab, "Песенки с кассеты");

        /*PopulateDefaultSongsContainer(DefaultSongsContainer);*/
        PopulateTapeSongsContainer(TapeSongsContainer);
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        CurrenSongLabel.Text = _component.PlayingSongData == null ? "..." : _component.PlayingSongData.SongName!;

        RepeatButton.Pressed = _component.Playing;

        base.FrameUpdate(args);
    }

    private void PopulateDefaultSongsContainer(BoxContainer defaultSongsContainer)
    {
        var tapes = _component.DefaultSongsContainer.ContainedEntities.ToList();

        foreach (var tapeUid in tapes)
        {
            if (_entityManager.TryGetComponent<TapeComponent>(tapeUid, out var tape))
            {
                foreach (var song in tape.Songs)
                {
                    var songEntry = new JukeboxSongEntry(song, RequestSongPlay);

                    _defaultSongsEntries.Add(songEntry);
                }
            }
        }

        _defaultSongsEntries.Sort((x,y ) => string.Compare(x.Name!, y.Name!, StringComparison.Ordinal));

        foreach (var defaultSongsEntry in _defaultSongsEntries)
        {
            defaultSongsContainer.AddChild(defaultSongsEntry);
        }
    }

    private void PopulateTapeSongsContainer(BoxContainer tapeSongsContainer)
    {
        var tapes = _component.TapeContainer.ContainedEntities.ToList();

        foreach (var tapeUid in tapes)
        {
            if (_entityManager.TryGetComponent<TapeComponent>(tapeUid, out var tape))
            {
                foreach (var song in tape.Songs)
                {
                    var songEntry = new JukeboxSongEntry(song, RequestSongPlay);

                    _tapeSongsEntries.Add(songEntry);
                }
            }
        }

        foreach (var tapeSongsEntry in _tapeSongsEntries)
        {
            tapeSongsContainer.AddChild(tapeSongsEntry);
        }
    }

    private void RequestSongPlay(JukeboxSong song)
    {
        _jukeboxSystem.RequestSongToPlay(_jukeboxEntity, _component, song);
    }
}
