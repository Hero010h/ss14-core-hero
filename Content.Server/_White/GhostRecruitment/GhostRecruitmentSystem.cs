using System.Linq;
using Content.Server.EUI;
using Content.Server.Ghost.Components;
using Content.Server.Humanoid.Components;
using Content.Server.Mind;
using Content.Server.Players;
using Content.Shared.Ghost;
using Content.Shared.Players;
using Content.Shared._White.GhostRecruitment;
using Robust.Server.GameObjects;
using Robust.Server.Player;
using Robust.Shared.Player;
using Robust.Shared.Random;
using Content.Shared.Roles;
using Content.Server.Roles;
using Content.Shared.Roles.Jobs;
using Content.Server.Station.Systems;
using Robust.Shared.Prototypes;
using Content.Server.Players.PlayTimeTracking;

namespace Content.Server._White.GhostRecruitment;

/// <summary>
/// responsible for recruiting guests for all sorts of roles
/// </summary>
public sealed class GhostRecruitmentSystem : EntitySystem
{
    [Dependency] private readonly EuiManager _eui = default!;
    [Dependency] private readonly MindSystem _mind = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly IPlayerManager _playerManager = default!;
    [Dependency] private readonly SharedRoleSystem _roleSystem = default!;
    [Dependency] private readonly StationSpawningSystem _spawningSystem = default!;
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly IPlayTimeTrackingManager _playTimeTracking = default!;

    private readonly Dictionary<ICommonSession, GhostRecruitmentEuiAccept> _openUis = new();

    /// <summary>
    /// starts recruiting ghosts, showing them a menu with a choice to recruit.
    /// </summary>
    /// <param name="recruitmentName">Name of recruitment. <see cref="GhostRecruitmentSpawnPointComponent"/></param>
    /// <param name="overallPlaytime">Minimal playtime to be eligible for recruitment.</param>
    public void StartRecruitment(string recruitmentName, TimeSpan? overallPlaytime)
    {
        var query = EntityQueryEnumerator<GhostComponent, ActorComponent>();
        while (query.MoveNext(out var uid, out _, out var actorComponent))
        {
            if (overallPlaytime != null && _playTimeTracking.GetOverallPlaytime(actorComponent.PlayerSession) < overallPlaytime)
                continue;

            OpenEui(uid, recruitmentName, actorComponent);
        }
    }

    /// <summary>
    /// if the ghost agrees, then he queues up for the role
    /// </summary>
    /// <param name="uid"></param>
    /// <param name="recruitmentName">name of recruitment. <see cref="GhostRecruitmentSpawnPointComponent"/></param>
    public void Recruit(EntityUid uid, string recruitmentName)
    {
        EnsureComp<GhostRecruitedComponent>(uid).RecruitmentName = recruitmentName;
    }

    /// <summary>
    /// Arranges the ghosts that agreed by roles.
    /// </summary>
    /// <param name="recruitmentName">name of recruitment. <see cref="GhostRecruitmentSpawnPointComponent"/></param>
    /// <param name="overallPlaytime">Minimal playtime to be eligible for recruitment.</param>
    /// <returns>is success?</returns>
    public bool EndRecruitment(string recruitmentName, TimeSpan? overallPlaytime)
    {
        var spawners = GetEventSpawners(recruitmentName).ToList();

        // We prioritize the queue, for example, the commander first, and then the engineer
        spawners = spawners.OrderBy(o => o.Item2.Priority).ThenBy(_ => _random.Next()).ToList();

        var count = 0;

        var maxCount = Math.Max(3, _playerManager.PlayerCount / 9);
        var query = EntityQueryEnumerator<GhostRecruitedComponent>();

        while (query.MoveNext(out var uid, out var ghostRecruitedComponent))
        {
            if (ghostRecruitedComponent.RecruitmentName != recruitmentName)
                continue;

            if (!TryComp<ActorComponent>(uid, out var actorComponent))
                continue;

            if (overallPlaytime != null && _playTimeTracking.GetOverallPlaytime(actorComponent.PlayerSession) < overallPlaytime)
                continue;

            // if there are too many recruited, then just skip
            if (count >= spawners.Count || count >= maxCount)
                continue;

            var (spawnerUid, spawnerComponent) = spawners[count];

            TransferMind(uid, spawnerUid, spawnerComponent);
            count++;

            EnsureComp<RecruitedComponent>(uid).RecruitmentName = recruitmentName;

            var ghostEvent = new GhostRecruitmentSuccessEvent(recruitmentName, actorComponent.PlayerSession);
            RaiseLocalEvent(uid, ghostEvent);
        }

        var ghostsEvent = new GhostsRecruitmentSuccessEvent(recruitmentName);
        RaiseLocalEvent(ghostsEvent);

        Cleanup(recruitmentName);
        return true;
    }

    public void Cleanup(string recruitmentName)
    {
        ClearEui(recruitmentName);

        var query = EntityQueryEnumerator<GhostRecruitedComponent>();

        while (query.MoveNext(out var uid, out var ghostRecruitedComponent))
        {
            if (ghostRecruitedComponent.RecruitmentName != recruitmentName)
                continue;

            RemComp<GhostRecruitedComponent>(uid);
        }
    }

    private void TransferMind(EntityUid from, EntityUid spawnerUid, GhostRecruitmentSpawnPointComponent? component = null)
    {
        if (!Resolve(spawnerUid, ref component) || !TryComp<ActorComponent>(from, out var actorComponent))
            return;

        var entityUid = Spawn(spawnerUid, component);

        if (!entityUid.HasValue)
            return;

        var userId = actorComponent.PlayerSession.UserId;
        var entityName = EntityManager.GetComponent<MetaDataComponent>((EntityUid) entityUid).EntityName;

        var newMind = _mind.CreateMind(userId, entityName);

        var job = new JobComponent { Prototype = component.JobId };

        _roleSystem.MindAddRole(newMind, job);
        _mind.SetUserId(newMind, userId);
        _mind.TransferTo(newMind, entityUid);

        _prototypeManager.TryIndex(job.Prototype, out var jobProto);
        if (jobProto != null)
            _spawningSystem.SetPdaAndIdCardData((EntityUid) entityUid, entityName, jobProto, null);
    }

    private EntityUid? Spawn(EntityUid spawnerUid, GhostRecruitmentSpawnPointComponent? component = null)
    {
        if (!Resolve(spawnerUid, ref component))
            return null;

        var uid = EntityManager.SpawnEntity(component.EntityPrototype, Transform(spawnerUid).Coordinates);

        if (HasComp<RandomHumanoidSpawnerComponent>(uid))
        {
            uid = new EntityUid((int) uid + 1);
        }

        return uid;
    }

    public IEnumerable<(EntityUid, GhostRecruitmentSpawnPointComponent)> GetEventSpawners(string recruitmentName)
    {
        var query = EntityQueryEnumerator<GhostRecruitmentSpawnPointComponent>();
        while (query.MoveNext(out var uid, out var component))
        {
            if (component.RecruitmentName == recruitmentName)
                yield return (uid, component);
        }
    }

    public IEnumerable<(EntityUid, GhostRecruitedComponent)> GetAllRecruited(string recruitmentName)
    {
        var query = EntityQueryEnumerator<GhostRecruitedComponent>();
        while (query.MoveNext(out var uid, out var component))
        {
            if (component.RecruitmentName == recruitmentName)
                yield return (uid, component);
        }
    }

    public void OpenEui(EntityUid uid, string recruitmentName, ActorComponent? actorComponent = null)
    {
        if (!Resolve(uid, ref actorComponent))
            return;
        var eui = new GhostRecruitmentEuiAccept(uid, recruitmentName, this);

        Logger.Debug("Added EUI to " + uid);
        if (_openUis.TryAdd(actorComponent.PlayerSession, eui))
            _eui.OpenEui(eui, actorComponent.PlayerSession);
    }

    public void ClearEui(string recruitmentName)
    {
        foreach (var (session, eui) in _openUis)
        {
            if (session.AttachedEntity != null)
                CloseEui(session.AttachedEntity.Value, recruitmentName);
        }
    }

    public void CloseEui(EntityUid uid, string recruitmentName, ActorComponent? actorComponent = null)
    {
        if (!Resolve(uid, ref actorComponent))
            return;


        var session = actorComponent.PlayerSession;

        if (!_openUis.ContainsKey(session))
            return;

        Logger.Debug("Removed EUI from " + uid);
        _openUis.Remove(session, out var eui);

        eui?.Close();
    }
}

