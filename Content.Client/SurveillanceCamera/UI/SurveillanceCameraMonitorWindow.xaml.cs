using Content.Client.Pinpointer.UI;
using Content.Client.Resources;
using Content.Client.Stylesheets;
using Content.Client.UserInterface.Controls;
using Content.Shared.SurveillanceCamera;
using Robust.Client.AutoGenerated;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.XAML;
using Robust.Shared.Graphics;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Client.SurveillanceCamera.UI;

[GenerateTypedNameReferences]
public sealed partial class SurveillanceCameraMonitorWindow : FancyWindow
{
    private readonly IPrototypeManager _prototypeManager;
    private readonly IEntityManager _entManager;

    public event Action<NetEntity>? CameraSelected;
    public event Action? CameraRefresh;
    public event Action? SubnetRefresh;
    public event Action? CameraSwitchTimer;
    public event Action? CameraDisconnect;

    private string _currentAddress = string.Empty;
    private bool _isSwitching;
    private readonly FixedEye _defaultEye = new();
    private Dictionary<NetEntity, CameraData> _cameras = new();
    private Texture? _blipTexture;
    private NetEntity? _trackedEntity;

    public SurveillanceCameraMonitorWindow(EntityUid? mapUid)
    {
        RobustXamlLoader.Load(this);

        _prototypeManager = IoCManager.Resolve<IPrototypeManager>();
        var resourceCache = IoCManager.Resolve<IResourceCache>();
        _entManager = IoCManager.Resolve<IEntityManager>();
        var spriteSystem = _entManager.System<SpriteSystem>();

        // This could be done better. I don't want to deal with stylesheets at the moment.
        var texture = resourceCache.GetTexture("/Textures/Interface/Nano/square_black.png");
        var shader = _prototypeManager.Index<ShaderPrototype>("CameraStatic").Instance().Duplicate();

        _blipTexture = spriteSystem.Frame0(new SpriteSpecifier.Texture(new ResPath("/Textures/Interface/NavMap/beveled_circle.png")));

        CameraView.ViewportSize = new Vector2i(500, 500);
        CameraView.Eye = _defaultEye; // sure
        CameraViewBackground.Stretch = TextureRect.StretchMode.Scale;
        CameraViewBackground.Texture = texture;
        CameraViewBackground.ShaderOverride = shader;

        SubnetRefreshButton.OnPressed += _ => SubnetRefresh!();
        CameraRefreshButton.OnPressed += _ => CameraRefresh!();
        CameraDisconnectButton.OnPressed += _ => CameraDisconnect!();

        NavMap.TrackedEntitySelectedAction += SetTrackedEntityFromNavMap;

        _entManager = IoCManager.Resolve<IEntityManager>();

        if (_entManager.TryGetComponent<TransformComponent>(mapUid, out var xform))
            NavMap.MapUid = xform.GridUid;
        else
            NavMap.Visible = false;
    }

    // Sunrise-start
    private void SetTrackedEntityFromNavMap(NetEntity? netEntity)
    {
        NavMap.Focus = _trackedEntity;

        if (netEntity != null)
        {
            CameraSelected!(netEntity.Value);
        }
    }

    public void ShowCameras(Dictionary<NetEntity, CameraData> cameras, EntityCoordinates? monitorCoords)
    {
        ClearAllCamerasPoint();

        _cameras = cameras;

        foreach (var camera in cameras)
        {
            NavMap.LocalizedNames.TryAdd(camera.Key, camera.Value.Name);

            var coordinates = _entManager.GetCoordinates(camera.Value.Coordinates);

            if (NavMap.Visible && _blipTexture != null)
            {
                NavMap.TrackedEntities.TryAdd(camera.Key,
                    new NavMapBlip(coordinates,
                        _blipTexture,
                        camera.Key == _trackedEntity ? Color.LimeGreen : camera.Value.SubnetColor,
                        camera.Key == _trackedEntity));

                NavMap.Focus = _trackedEntity;
            }
        }

        // Show monitor point
        if (monitorCoords != null)
            NavMap.TrackedCoordinates.Add(monitorCoords.Value, (true, StyleNano.PointMagenta));
    }
    // Sunrise-end


    // The UI class should get the eye from the entity, and then
    // pass it here so that the UI can change its view.
    public void UpdateState(IEye? eye, string activeAddress, NetEntity? activeCamera)
    {
        _currentAddress = activeAddress;
        _trackedEntity = activeCamera;
        SetCameraView(eye);
    }


    private void SetCameraView(IEye? eye)
    {
        var eyeChanged = eye != CameraView.Eye || CameraView.Eye == null;
        CameraView.Eye = eye ?? _defaultEye;
        CameraView.Visible = !eyeChanged && !_isSwitching;
        CameraDisconnectButton.Disabled = eye == null;

        if (eye != null)
        {
            if (!eyeChanged)
            {
                return;
            }

            _isSwitching = true;
            CameraViewBackground.Visible = true;
            CameraStatus.Text = Loc.GetString("surveillance-camera-monitor-ui-status",
                ("status", Loc.GetString("surveillance-camera-monitor-ui-status-connecting")),
                ("address", _currentAddress));
            CameraSwitchTimer!();
        }
        else
        {
            CameraViewBackground.Visible = true;
            CameraStatus.Text = Loc.GetString("surveillance-camera-monitor-ui-status-disconnected");
        }
    }

    public void OnSwitchTimerComplete()
    {
        _isSwitching = false;
        CameraView.Visible = CameraView.Eye != _defaultEye;
        CameraViewBackground.Visible = CameraView.Eye == _defaultEye;
        CameraStatus.Text = Loc.GetString("surveillance-camera-monitor-ui-status",
                            ("status", Loc.GetString("surveillance-camera-monitor-ui-status-connected")),
                            ("address", _currentAddress));
    }

    private void ClearAllCamerasPoint()
    {
        NavMap.TrackedCoordinates.Clear();
        NavMap.TrackedEntities.Clear();
    }
}
