﻿using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Content.Server.Database;
using Content.Shared._White;
using Content.Shared.CCVar;
using Content.Shared.Players.PlayTimeTracking;
using Robust.Shared.Asynchronous;
using Robust.Shared.Collections;
using Robust.Shared.Configuration;
using Robust.Shared.Exceptions;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Server.Players.PlayTimeTracking;

public sealed class GlobalPlayTimeTrackingManager : IPlayTimeTrackingManager
{
    [Dependency] private readonly IServerDbManager _db = default!;
    [Dependency] private readonly IServerNetManager _net = default!;
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly ITaskManager _task = default!;
    [Dependency] private readonly IRuntimeLog _runtimeLog = default!;

    private ISawmill _sawmill = default!;

    // List of players that need some kind of update (refresh timers or resend).
    private ValueList<ICommonSession> _playersDirty;

    // DB auto-saving logic.
    private TimeSpan _saveInterval;
    private TimeSpan _lastSave;

    private HttpClient _httpClient =  new();

    private string _apiUrl = string.Empty;
    private string _apiKey = string.Empty;

    // List of pending DB save operations.
    // We must block server shutdown on these to avoid losing data.
    private readonly List<Task> _pendingSaveTasks = new();

    private readonly Dictionary<ICommonSession, PlayTimeData> _playTimeData = new();

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);


    public event CalcPlayTimeTrackersCallback? CalcTrackers;

    public void Initialize()
    {
        _sawmill = Logger.GetSawmill("play_time");

        _net.RegisterNetMessage<MsgPlayTime>();

        _cfg.OnValueChanged(CCVars.PlayTimeSaveInterval, f => _saveInterval = TimeSpan.FromSeconds(f), true);

        _cfg.OnValueChanged(WhiteCVars.TimeTrackerApiUrl, newValue => _apiUrl = newValue, true);
        _cfg.OnValueChanged(WhiteCVars.TimeTrackerApiKey, newValue => _apiKey = newValue, true);

        _sawmill.Info("Using global PlayTimeTracker");
    }

    public void Shutdown()
    {
        Save();

        _task.BlockWaitOnTask(Task.WhenAll(_pendingSaveTasks));
    }

    public void Update()
    {
        // NOTE: This is run **out** of simulation. This is intentional.

        UpdateDirtyPlayers();

        if (_timing.RealTime < _lastSave + _saveInterval)
            return;

        Save();
    }

    private void UpdateDirtyPlayers()
    {
        if (_playersDirty.Count == 0)
            return;

        var time = _timing.RealTime;

        foreach (var player in _playersDirty)
        {
            if (!_playTimeData.TryGetValue(player, out var data))
                continue;

            DebugTools.Assert(data.IsDirty);

            if (data.NeedRefreshTackers)
            {
                RefreshSingleTracker(player, data, time);
            }

            if (data.NeedSendTimers)
            {
                SendPlayTimes(player);
                data.NeedSendTimers = false;
            }

            data.IsDirty = false;
        }

        _playersDirty.Clear();
    }

    public IReadOnlyDictionary<string, TimeSpan> GetPlayTimes(ICommonSession session)
    {
        return GetTrackerTimes(session);
    }

    private void RefreshSingleTracker(ICommonSession dirty, PlayTimeData data, TimeSpan time)
    {
        DebugTools.Assert(data.Initialized);

        FlushSingleTracker(data, time);

        data.NeedRefreshTackers = false;

        data.ActiveTrackers.Clear();

        // Fetch new trackers.
        // Inside try catch to avoid state corruption from bad callback code.
        try
        {
            CalcTrackers?.Invoke(dirty, data.ActiveTrackers);
        }
        catch (Exception e)
        {
            _runtimeLog.LogException(e, "PlayTime CalcTrackers");
            data.ActiveTrackers.Clear();
        }
    }

    /// <summary>
    /// Flush all trackers for all players.
    /// </summary>
    /// <seealso cref="FlushTracker"/>
    public void FlushAllTrackers()
    {
        var time = _timing.RealTime;

        foreach (var data in _playTimeData.Values)
        {
            FlushSingleTracker(data, time);
        }
    }

    /// <summary>
    /// Flush time tracker information for a player,
    /// so APIs like <see cref="GetPlayTimeForTracker"/> return up-to-date info.
    /// </summary>
    /// <seealso cref="FlushAllTrackers"/>
    public void FlushTracker(ICommonSession player)
    {
        var time = _timing.RealTime;
        var data = _playTimeData[player];

        FlushSingleTracker(data, time);
    }

    private static void FlushSingleTracker(PlayTimeData data, TimeSpan time)
    {
        var delta = time - data.LastUpdate;
        data.LastUpdate = time;

        // Flush active trackers into semi-permanent storage.
        foreach (var active in data.ActiveTrackers)
        {
            AddTimeToTracker(data, active, delta);
        }
    }

    private void SendPlayTimes(ICommonSession pSession)
    {
        var roles = GetTrackerTimes(pSession);

        var msg = new MsgPlayTime
        {
            Trackers = roles
        };

        _net.ServerSendMessage(msg, pSession.Channel);
    }

    /// <summary>
    /// Save all modified time trackers for all players to the database.
    /// </summary>
    public async void Save()
    {
        FlushAllTrackers();

        _lastSave = _timing.RealTime;

        TrackPending(DoSaveAsync());
    }

    /// <summary>
    /// Save all modified time trackers for a player to the database.
    /// </summary>
    public async void SaveSession(ICommonSession session)
    {
        // This causes all trackers to refresh, ah well.
        FlushAllTrackers();

        TrackPending(DoSaveSessionAsync(session));
    }

    /// <summary>
    /// Track a database save task to make sure we block server shutdown on it.
    /// </summary>
    private async void TrackPending(Task task)
    {
        _pendingSaveTasks.Add(task);

        try
        {
            await task;
        }
        finally
        {
            _pendingSaveTasks.Remove(task);
        }
    }

    private async Task DoSaveAsync()
    {
        var log = new List<PlayTimeUpdate>();

        foreach (var (player, data) in _playTimeData)
        {
            foreach (var tracker in data.DbTrackersDirty)
            {
                log.Add(new PlayTimeUpdate(player.UserId, tracker, data.TrackerTimes[tracker]));
            }

            data.DbTrackersDirty.Clear();
        }

        if (log.Count == 0)
            return;

        // NOTE: we do replace updates here, not incremental additions.
        // This means that if you're playing on two servers at the same time, they'll step on each other's feet.
        // This is considered fine.
        await UpdatePlayTimes(log);

        _sawmill.Debug($"Saved {log.Count} trackers");
    }

    private async Task UpdatePlayTimes(List<PlayTimeUpdate> update)
    {
        foreach (var playTimeUpdate in update)
        {
            var query = $"{_apiUrl}set/?uid={playTimeUpdate.User}&key={_apiKey}&tracker={playTimeUpdate.Tracker}&newtime={playTimeUpdate.Time}";

            await _httpClient.GetAsync(query);
        }
    }

    private async Task DoSaveSessionAsync(ICommonSession session)
    {
        var log = new List<PlayTimeUpdate>();

        var data = _playTimeData[session];

        foreach (var tracker in data.DbTrackersDirty)
        {
            log.Add(new PlayTimeUpdate(session.UserId, tracker, data.TrackerTimes[tracker]));
        }

        data.DbTrackersDirty.Clear();

        // NOTE: we do replace updates here, not incremental additions.
        // This means that if you're playing on two servers at the same time, they'll step on each other's feet.
        // This is considered fine.
        await UpdatePlayTimes(log);

        _sawmill.Debug($"Saved {log.Count} trackers for {session.Name}");
    }

    private sealed class PlayTimeDto
    {
        [JsonPropertyName("tracker")]
        public string Tracker { get; set; } = default!;

        [JsonPropertyName("time_spent")]
        public TimeSpan TimeSpent { get; set; } = default!;
    }

    public async Task LoadData(ICommonSession session, CancellationToken cancel)
    {
        var data = new PlayTimeData();
        _playTimeData.Add(session, data);

        var query = $"{_apiUrl}get/?uid={session.UserId}&key={_apiKey}";
        query = WebUtility.UrlDecode(query);

        var response = await _httpClient.GetAsync(query, cancel);

        if (!response.IsSuccessStatusCode)
        {
            throw new Exception("Play time tracker api shits itself");
        }

        List<PlayTimeDto>? playTimes = null!;

        var content = await response.Content.ReadAsStringAsync(cancel);

        try
        {
            playTimes = JsonSerializer.Deserialize<List<PlayTimeDto>>(content, SerializerOptions );
        }
        catch (JsonException)
        {
            _sawmill.Error($"Can't load data for user {session.Name} with UserId {session.UserId} \n Data: {content}");
        }

        cancel.ThrowIfCancellationRequested();

        if (playTimes != null)
        {
            foreach (var timer in playTimes)
            {
                data.TrackerTimes.Add(timer.Tracker, timer.TimeSpent);
            }
        }

        data.Initialized = true;

        QueueRefreshTrackers(session);
        QueueSendTimers(session);
    }

    public void ClientDisconnected(ICommonSession session)
    {
        SaveSession(session);

        _playTimeData.Remove(session);
    }

    public void AddTimeToTracker(ICommonSession id, string tracker, TimeSpan time)
    {
        if (!_playTimeData.TryGetValue(id, out var data) || !data.Initialized)
            throw new InvalidOperationException("Play time info is not yet loaded for this player!");

        AddTimeToTracker(data, tracker, time);
    }

    private static void AddTimeToTracker(PlayTimeData data, string tracker, TimeSpan time)
    {
        ref var timer = ref CollectionsMarshal.GetValueRefOrAddDefault(data.TrackerTimes, tracker, out _);
        timer += time;

        data.DbTrackersDirty.Add(tracker);
    }

    public void AddTimeToOverallPlaytime(ICommonSession id, TimeSpan time)
    {
        AddTimeToTracker(id, PlayTimeTrackingShared.TrackerOverall, time);
    }

    public TimeSpan GetOverallPlaytime(ICommonSession id)
    {
        return GetPlayTimeForTracker(id, PlayTimeTrackingShared.TrackerOverall);
    }

    public bool TryGetTrackerTimes(ICommonSession id, [NotNullWhen(true)] out Dictionary<string, TimeSpan>? time)
    {
        time = null;

        if (!_playTimeData.TryGetValue(id, out var data) || !data.Initialized)
        {
            return false;
        }

        time = data.TrackerTimes;
        return true;
    }

    public Dictionary<string, TimeSpan> GetTrackerTimes(ICommonSession id)
    {
        if (!_playTimeData.TryGetValue(id, out var data) || !data.Initialized)
            throw new InvalidOperationException("Play time info is not yet loaded for this player!");

        return data.TrackerTimes;
    }

    public TimeSpan GetPlayTimeForTracker(ICommonSession id, string tracker)
    {
        if (!_playTimeData.TryGetValue(id, out var data) || !data.Initialized)
            throw new InvalidOperationException("Play time info is not yet loaded for this player!");

        return data.TrackerTimes.GetValueOrDefault(tracker);
    }

    /// <summary>
    /// Queue for play time trackers to be refreshed on a player, in case the set of active trackers may have changed.
    /// </summary>
    public void QueueRefreshTrackers(ICommonSession player)
    {
        if (DirtyPlayer(player) is { } data)
            data.NeedRefreshTackers = true;
    }

    /// <summary>
    /// Queue for play time information to be sent to a client, for showing in UIs etc.
    /// </summary>
    public void QueueSendTimers(ICommonSession player)
    {
        if (DirtyPlayer(player) is { } data)
            data.NeedSendTimers = true;
    }

    private PlayTimeData? DirtyPlayer(ICommonSession player)
    {
        if (!_playTimeData.TryGetValue(player, out var data) || !data.Initialized)
            return null;

        if (!data.IsDirty)
        {
            data.IsDirty = true;
            _playersDirty.Add(player);
        }

        return data;
    }

    /// <summary>
    /// Play time info for a particular player.
    /// </summary>
    private sealed class PlayTimeData
    {
        // Queued update flags
        public bool IsDirty;
        public bool NeedRefreshTackers;
        public bool NeedSendTimers;

        // Active tracking info
        public readonly HashSet<string> ActiveTrackers = new();
        public TimeSpan LastUpdate;

        // Stored tracked time info.

        /// <summary>
        /// Have we finished retrieving our data from the DB?
        /// </summary>
        public bool Initialized;

        public readonly Dictionary<string, TimeSpan> TrackerTimes = new();

        /// <summary>
        /// Set of trackers which are different from their DB values and need to be saved to DB.
        /// </summary>
        public readonly HashSet<string> DbTrackersDirty = new();
    }
}
