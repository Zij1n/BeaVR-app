using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using NetMQ;
using NetMQ.Sockets;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Receives a JSON point list from the server and renders it as a pooled 3D guidance marker set.
/// Expected payload:
/// {
///   "frame": "tracking_space",
///   "sequence": 1,
///   "points": [
///     {
///       "position": { "x": 0.1, "y": 1.0, "z": 0.2 },
///       "color": { "r": 1.0, "g": 0.5, "b": 0.2, "a": 0.9 },
///       "size": 0.015
///     }
///   ]
/// }
/// </summary>
public sealed class FeatherPointStream : MonoBehaviour
{
    const string BootstrapObjectName = "FeatherPointStream";
    const string NetworkLoaderObjectName = "NetworkConfigsLoader";
    const string TrackingSpaceObjectName = "TrackingSpace";
    const string FeatherRootName = "FeatherGuidanceRoot";
    const int MaxSupportedPoints = 64;

    static FeatherPointStream _instance;

    [SerializeField] private float staleTimeoutSeconds = 0.35f;
    [SerializeField] private float defaultPointSize = 0.012f;
    [SerializeField] private bool enablePayloadLogging = false;

    private readonly object _messageLock = new object();
    private readonly List<FeatherPointVisual> _pointPool = new List<FeatherPointVisual>(MaxSupportedPoints);
    private readonly MaterialPropertyBlock _propertyBlock = new MaterialPropertyBlock();

    private NetworkManager _networkManager;
    private Transform _visualRoot;
    private Material _pointMaterial;
    private SubscriberSocket _socket;
    private Thread _receiverThread;

    private bool _connectionEstablished;
    private bool _hasPendingPayload;
    private bool _loggedMissingTrackingSpace;
    private bool _threadShouldRun;
    private float _lastPayloadReceivedAt = float.NegativeInfinity;
    private float _lastParseErrorAt = float.NegativeInfinity;
    private int _lastSequence = int.MinValue;
    private string _communicationAddress;
    private string _pendingPayloadJson;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (_instance != null)
        {
            return;
        }

        var existing = FindFirstObjectByType<FeatherPointStream>();
        if (existing != null)
        {
            _instance = existing;
            return;
        }

        GameObject bootstrapObject = new GameObject(BootstrapObjectName);
        _instance = bootstrapObject.AddComponent<FeatherPointStream>();
        DontDestroyOnLoad(bootstrapObject);
    }

    void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }

        _instance = this;
        DontDestroyOnLoad(gameObject);
    }

    void Update()
    {
        EnsureDependencies();

        if (_networkManager == null)
        {
            HideAllPoints();
            return;
        }

        if (_networkManager.ForceDisconnect)
        {
            DisconnectNetMQ();
            return;
        }

        string featherAddress = _networkManager.getFeatherAddress();
        if (!_connectionEstablished)
        {
            StartReceiver(featherAddress);
        }
        else if (!string.Equals(_communicationAddress, featherAddress, StringComparison.Ordinal))
        {
            DisconnectNetMQ();
            StartReceiver(featherAddress);
        }

        DrainPendingPayload();

        if (Time.unscaledTime - _lastPayloadReceivedAt > staleTimeoutSeconds)
        {
            HideAllPoints();
        }
    }

    void OnDestroy()
    {
        DisconnectNetMQ();
        DestroyPointMaterial();
    }

    void OnApplicationQuit()
    {
        DisconnectNetMQ();
    }

    public void ConnectNetMQ()
    {
        if (_networkManager == null)
        {
            EnsureDependencies();
        }

        if (_networkManager != null)
        {
            StartReceiver(_networkManager.getFeatherAddress());
        }
    }

    public void DisconnectNetMQ()
    {
        _threadShouldRun = false;

        if (_receiverThread != null)
        {
            try
            {
                _receiverThread.Join(250);
            }
            catch (Exception exception)
            {
                Debug.LogError("FeatherPointStream: Error while waiting for receiver thread - " + exception.Message);
            }
            finally
            {
                _receiverThread = null;
            }
        }

        if (_socket != null)
        {
            try
            {
                _socket.Close();
                _socket.Dispose();
            }
            catch (Exception exception)
            {
                Debug.LogError("FeatherPointStream: Error while closing feather socket - " + exception.Message);
            }
            finally
            {
                _socket = null;
            }
        }

        lock (_messageLock)
        {
            _pendingPayloadJson = null;
            _hasPendingPayload = false;
        }

        _connectionEstablished = false;
        _communicationAddress = null;
        HideAllPoints();
    }

    void EnsureDependencies()
    {
        if (_networkManager == null)
        {
            GameObject networkLoader = GameObject.Find(NetworkLoaderObjectName);
            if (networkLoader != null)
            {
                _networkManager = networkLoader.GetComponent<NetworkManager>();
            }
        }

        EnsureVisualRoot();
        EnsurePointMaterial();
    }

    void EnsureVisualRoot()
    {
        GameObject trackingSpace = GameObject.Find(TrackingSpaceObjectName);
        if (trackingSpace == null)
        {
            if (!_loggedMissingTrackingSpace)
            {
                Debug.LogWarning("FeatherPointStream: TrackingSpace not found. Feather visuals will wait for scene setup.");
                _loggedMissingTrackingSpace = true;
            }

            return;
        }

        _loggedMissingTrackingSpace = false;

        if (_visualRoot == null)
        {
            var rootObject = new GameObject(FeatherRootName);
            _visualRoot = rootObject.transform;
            _visualRoot.SetParent(trackingSpace.transform, false);
            _visualRoot.localPosition = Vector3.zero;
            _visualRoot.localRotation = Quaternion.identity;
            _visualRoot.localScale = Vector3.one;
        }
        else if (_visualRoot.parent != trackingSpace.transform)
        {
            _visualRoot.SetParent(trackingSpace.transform, false);
            _visualRoot.localPosition = Vector3.zero;
            _visualRoot.localRotation = Quaternion.identity;
            _visualRoot.localScale = Vector3.one;
        }
    }

    void EnsurePointMaterial()
    {
        if (_pointMaterial != null)
        {
            return;
        }

        Shader shader = Shader.Find("Standard");
        if (shader == null)
        {
            Debug.LogError("FeatherPointStream: Standard shader not found.");
            return;
        }

        _pointMaterial = new Material(shader);
        _pointMaterial.name = "FeatherPointMaterial";
        _pointMaterial.SetFloat("_Mode", 3f);
        _pointMaterial.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
        _pointMaterial.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
        _pointMaterial.SetInt("_ZWrite", 0);
        _pointMaterial.DisableKeyword("_ALPHATEST_ON");
        _pointMaterial.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        _pointMaterial.EnableKeyword("_ALPHABLEND_ON");
        _pointMaterial.renderQueue = 3000;
        _pointMaterial.color = new Color(1f, 0.75f, 0.25f, 0.85f);
    }

    void DestroyPointMaterial()
    {
        if (_pointMaterial == null)
        {
            return;
        }

        Destroy(_pointMaterial);
        _pointMaterial = null;
    }

    void StartReceiver(string address)
    {
        if (_connectionEstablished || string.IsNullOrEmpty(address) || address == "tcp://:")
        {
            return;
        }

        try
        {
            _socket = new SubscriberSocket();
            _socket.Options.ReceiveHighWatermark = 64;
            _socket.Options.Linger = TimeSpan.Zero;
            _socket.Connect(address);
            _socket.Subscribe("");

            _communicationAddress = address;
            _threadShouldRun = true;
            _receiverThread = new Thread(ReceiveLoop)
            {
                IsBackground = true,
                Name = "FeatherPointReceiver"
            };
            _receiverThread.Start();
            _connectionEstablished = true;

            Debug.Log("FeatherPointStream: Connected to feather stream at " + address);
        }
        catch (Exception exception)
        {
            Debug.LogError("FeatherPointStream: Failed to connect feather stream - " + exception.Message);
            DisconnectNetMQ();
        }
    }

    void ReceiveLoop()
    {
        while (_threadShouldRun && _socket != null)
        {
            try
            {
                if (_socket.TryReceiveFrameBytes(TimeSpan.FromMilliseconds(100), out byte[] payloadBytes))
                {
                    string json = Encoding.UTF8.GetString(payloadBytes);

                    lock (_messageLock)
                    {
                        _pendingPayloadJson = json;
                        _hasPendingPayload = true;
                    }
                }
            }
            catch (Exception exception)
            {
                if (_threadShouldRun)
                {
                    Debug.LogError("FeatherPointStream: Feather receive loop failed - " + exception.Message);
                }

                break;
            }
        }
    }

    void DrainPendingPayload()
    {
        string json = null;

        lock (_messageLock)
        {
            if (_hasPendingPayload)
            {
                json = _pendingPayloadJson;
                _pendingPayloadJson = null;
                _hasPendingPayload = false;
            }
        }

        if (string.IsNullOrEmpty(json))
        {
            return;
        }

        FeatherPayload payload;
        try
        {
            payload = JsonUtility.FromJson<FeatherPayload>(json);
        }
        catch (Exception exception)
        {
            MaybeLogParseError("FeatherPointStream: Invalid feather JSON - " + exception.Message);
            return;
        }

        if (payload == null)
        {
            MaybeLogParseError("FeatherPointStream: Feather payload deserialized to null.");
            return;
        }

        if (!string.IsNullOrEmpty(payload.frame) && !string.Equals(payload.frame, "tracking_space", StringComparison.OrdinalIgnoreCase))
        {
            MaybeLogParseError("FeatherPointStream: Unsupported feather frame '" + payload.frame + "'. Expected 'tracking_space'.");
            return;
        }

        if (enablePayloadLogging && payload.sequence != _lastSequence)
        {
            int pointCount = payload.points != null ? payload.points.Length : 0;
            Debug.Log($"FeatherPointStream: Applying payload sequence={payload.sequence} points={pointCount}");
        }

        ApplyPayload(payload);
        _lastSequence = payload.sequence;
        _lastPayloadReceivedAt = Time.unscaledTime;
    }

    void MaybeLogParseError(string message)
    {
        if (Time.unscaledTime - _lastParseErrorAt < 1f)
        {
            return;
        }

        _lastParseErrorAt = Time.unscaledTime;
        Debug.LogError(message);
    }

    void ApplyPayload(FeatherPayload payload)
    {
        if (_visualRoot == null || _pointMaterial == null)
        {
            return;
        }

        int requestedCount = payload.points != null ? payload.points.Length : 0;
        int clampedCount = Mathf.Min(requestedCount, MaxSupportedPoints);
        EnsurePoolSize(clampedCount);

        for (int index = 0; index < clampedCount; index++)
        {
            FeatherPointDefinition point = payload.points[index];
            FeatherPointVisual visual = _pointPool[index];
            Vector3 position = point.ResolvePosition();
            Color color = point.ResolveColor();
            float size = point.ResolveSize(defaultPointSize);

            visual.Transform.SetLocalPositionAndRotation(position, Quaternion.identity);
            visual.Transform.localScale = Vector3.one * size;
            visual.Renderer.enabled = true;
            visual.GameObject.SetActive(true);

            _propertyBlock.Clear();
            _propertyBlock.SetColor("_Color", color);
            if (visual.Renderer.sharedMaterial != null && visual.Renderer.sharedMaterial.HasProperty("_BaseColor"))
            {
                _propertyBlock.SetColor("_BaseColor", color);
            }

            visual.Renderer.SetPropertyBlock(_propertyBlock);
        }

        for (int index = clampedCount; index < _pointPool.Count; index++)
        {
            FeatherPointVisual visual = _pointPool[index];
            visual.Renderer.SetPropertyBlock(null);
            visual.GameObject.SetActive(false);
        }

        if (requestedCount > MaxSupportedPoints)
        {
            Debug.LogWarning($"FeatherPointStream: Received {requestedCount} feather points, clamped to {MaxSupportedPoints}.");
        }
    }

    void EnsurePoolSize(int targetCount)
    {
        while (_pointPool.Count < targetCount)
        {
            _pointPool.Add(CreatePointVisual(_pointPool.Count));
        }
    }

    FeatherPointVisual CreatePointVisual(int index)
    {
        GameObject pointObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        pointObject.name = $"FeatherPoint_{index:00}";
        pointObject.transform.SetParent(_visualRoot, false);
        pointObject.SetActive(false);

        Collider collider = pointObject.GetComponent<Collider>();
        if (collider != null)
        {
            Destroy(collider);
        }

        var renderer = pointObject.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = _pointMaterial;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
        renderer.lightProbeUsage = LightProbeUsage.Off;
        renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;

        return new FeatherPointVisual(pointObject, pointObject.transform, renderer);
    }

    void HideAllPoints()
    {
        for (int index = 0; index < _pointPool.Count; index++)
        {
            FeatherPointVisual visual = _pointPool[index];
            if (visual.GameObject.activeSelf)
            {
                visual.Renderer.SetPropertyBlock(null);
                visual.GameObject.SetActive(false);
            }
        }
    }

    sealed class FeatherPointVisual
    {
        public FeatherPointVisual(GameObject gameObject, Transform transform, MeshRenderer renderer)
        {
            GameObject = gameObject;
            Transform = transform;
            Renderer = renderer;
        }

        public GameObject GameObject { get; }
        public Transform Transform { get; }
        public MeshRenderer Renderer { get; }
    }

    [Serializable]
    sealed class FeatherPayload
    {
        public string frame = "tracking_space";
        public int sequence;
        public FeatherPointDefinition[] points;
    }

    [Serializable]
    sealed class FeatherPointDefinition
    {
        public FeatherVector3 position;
        public FeatherColor color;
        public float size;

        public Vector3 ResolvePosition()
        {
            if (position == null)
            {
                return Vector3.zero;
            }

            return new Vector3(position.x, position.y, position.z);
        }

        public Color ResolveColor()
        {
            if (color == null)
            {
                return new Color(1f, 0.75f, 0.25f, 0.85f);
            }

            return color.ToUnityColor();
        }

        public float ResolveSize(float fallbackSize)
        {
            float resolved = size > 0f ? size : fallbackSize;
            return Mathf.Max(0.001f, resolved);
        }
    }

    [Serializable]
    sealed class FeatherVector3
    {
        public float x;
        public float y;
        public float z;
    }

    [Serializable]
    sealed class FeatherColor
    {
        public float r = 1f;
        public float g = 1f;
        public float b = 1f;
        public float a = 1f;

        public Color ToUnityColor()
        {
            float maxComponent = Mathf.Max(r, g, b, a);
            if (maxComponent > 1f)
            {
                return new Color(r / 255f, g / 255f, b / 255f, a / 255f);
            }

            return new Color(r, g, b, a);
        }
    }
}
