using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

/// <summary>
/// Client réseau côté PC (Soignant).
/// Écoute les beacons UDP émis par les casques pour les découvrir,
/// puis se connecte via TCP au casque choisi.
/// Envoie les commandes (exercices, passthrough, etc.) au casque.
/// </summary>
public class SoignantClient : MonoBehaviour
{
    [Header("Configuration")]
    [SerializeField] private bool autoDiscover = true;
    [SerializeField] private float discoveryRefreshInterval = 2f;

    [Header("État (lecture seule)")]
    [SerializeField] private bool isConnected = false;
    [SerializeField] private string connectedHeadsetIP = "";
    [SerializeField] private string connectionStatus = "Déconnecté";

    public bool IsConnected => isConnected;
    public string ConnectedHeadsetIP => connectedHeadsetIP;
    public string ConnectionStatus => connectionStatus;

    /// <summary>
    /// Info d'un casque découvert sur le réseau.
    /// </summary>
    [Serializable]
    public class DiscoveredHeadset
    {
        public string ip;
        public int port;
        public float lastSeen;    // Time.realtimeSinceStartup
    }

    /// <summary>
    /// Liste des casques actuellement visibles sur le réseau.
    /// </summary>
    public IReadOnlyList<DiscoveredHeadset> DiscoveredHeadsets => discoveredHeadsets;
    private List<DiscoveredHeadset> discoveredHeadsets = new List<DiscoveredHeadset>();

    // Events
    public event Action OnConnected;
    public event Action OnDisconnected;
    public event Action<NetworkMessage> OnMessageReceived;
    public event Action<string> OnStatusChanged;
    public event Action OnHeadsetListChanged;

    private TcpClient tcpClient;
    private NetworkStream stream;
    private Thread receiveThread;
    private Thread discoveryListenerThread;
    private UdpClient udpListener;
    private bool shouldStop = false;

    private Queue<Action> mainThreadActions = new Queue<Action>();

    // Debug log circulaire
    private static readonly List<string> debugLog = new List<string>();
    private const int MAX_LOG_LINES = 20;

    /// <summary>
    /// Derniers logs réseau (pour affichage debug côté PC).
    /// </summary>
    public static IReadOnlyList<string> DebugLog => debugLog;

    private static void AddDebugLog(string msg)
    {
        string line = $"[{DateTime.Now:HH:mm:ss}] {msg}";
        Debug.Log($"[SoignantClient] {msg}");
        lock (debugLog)
        {
            debugLog.Add(line);
            if (debugLog.Count > MAX_LOG_LINES)
                debugLog.RemoveAt(0);
        }
    }

    // ─────────────────────────── LIFECYCLE ───────────────────────────

    private void Start()
    {
        LogFirewallWarning();

        if (autoDiscover)
            StartDiscovery();
    }

    private void Update()
    {
        lock (mainThreadActions)
        {
            while (mainThreadActions.Count > 0)
                mainThreadActions.Dequeue()?.Invoke();
        }

        // Retirer les casques qui n'ont pas émis de beacon depuis 6s
        PruneStaleHeadsets();
    }

    private void OnDestroy()
    {
        shouldStop = true;
        StopDiscovery();
        Disconnect();
    }

    // ─────────────────────────── DISCOVERY ───────────────────────────

    /// <summary>
    /// Démarre l'écoute des beacons UDP émis par les casques.
    /// </summary>
    public void StartDiscovery()
    {
        StopDiscovery();
        shouldStop = false;

        discoveryListenerThread = new Thread(ListenForBeacons) { IsBackground = true };
        discoveryListenerThread.Start();

        SetStatus("🔍 Recherche de casques...");
        AddDebugLog("🔍 Écoute des beacons casques...");
        AddDebugLog($"Platform: {Application.platform}");

        // Afficher les IPs locales pour debug
        try
        {
            foreach (var iface in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (iface.OperationalStatus != OperationalStatus.Up) continue;
                if (iface.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                foreach (var addr in iface.GetIPProperties().UnicastAddresses)
                {
                    if (addr.Address.AddressFamily == AddressFamily.InterNetwork)
                        AddDebugLog($"IP locale: {addr.Address} [{iface.Name}]");
                }
            }
        }
        catch { }
    }

    /// <summary>
    /// Arrête l'écoute des beacons.
    /// </summary>
    public void StopDiscovery()
    {
        try { udpListener?.Close(); } catch { }
        udpListener = null;
    }

    private void ListenForBeacons()
    {
        // Essayer plusieurs ports pour éviter les conflits
        // (en dev, HeadsetServer et SoignantClient tournent sur la même machine)
        int[] portsToTry = new int[]
        {
            NetworkConfig.DISCOVERY_PORT,
            NetworkConfig.DISCOVERY_PORT_ALT,
            NetworkConfig.DISCOVERY_PORT + 10,
            NetworkConfig.DISCOVERY_PORT_ALT + 10
        };

        foreach (int port in portsToTry)
        {
            try
            {
                udpListener = new UdpClient();
                udpListener.Client.SetSocketOption(
                    SocketOptionLevel.Socket, SocketOptionName.ExclusiveAddressUse, false);
                udpListener.Client.SetSocketOption(
                    SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                udpListener.Client.Bind(new IPEndPoint(IPAddress.Any, port));
                udpListener.Client.ReceiveTimeout = 3000;

                RunOnMainThread(() =>
                    AddDebugLog($"📡 Écoute UDP sur port {port}"));
                break; // Succès
            }
            catch (Exception ex)
            {
                int p = port;
                RunOnMainThread(() =>
                    AddDebugLog($"⚠️ Port {p} échoué: {ex.Message}"));
                try { udpListener?.Close(); } catch { }
                udpListener = null;
            }
        }

        if (udpListener == null)
        {
            RunOnMainThread(() =>
            {
                AddDebugLog("❌ Impossible d'ouvrir un port UDP pour la découverte.");
                SetStatus("❌ Erreur écoute UDP");
            });
            return;
        }

        RunOnMainThread(() => AddDebugLog("🔄 Boucle d'écoute beacon démarrée..."));
        int receivedCount = 0;

        while (!shouldStop)
        {
            try
            {
                IPEndPoint remoteEP = new IPEndPoint(IPAddress.Any, 0);
                byte[] data = udpListener.Receive(ref remoteEP);
                string message = Encoding.UTF8.GetString(data);

                receivedCount++;
                string fromIP = remoteEP.Address.ToString();
                int count = receivedCount;
                RunOnMainThread(() =>
                    AddDebugLog($"📨 UDP reçu #{count} de {fromIP}: {message}"));

                // Format attendu : REVERSO_HEADSET|<ip>|<port>
                if (message.StartsWith(NetworkConfig.BEACON_HEADSET))
                {
                    string[] parts = message.Split('|');
                    if (parts.Length >= 3)
                    {
                        string ip = parts[1];
                        int port = int.Parse(parts[2]);

                        RunOnMainThread(() => RegisterHeadset(ip, port));
                    }
                }
            }
            catch (SocketException)
            {
                // Timeout, c'est normal, on boucle
            }
            catch (ObjectDisposedException)
            {
                break; // Socket fermé
            }
            catch { }
        }
    }

    private void RegisterHeadset(string ip, int port)
    {
        bool changed = false;

        var existing = discoveredHeadsets.Find(h => h.ip == ip && h.port == port);
        if (existing != null)
        {
            existing.lastSeen = Time.realtimeSinceStartup;
        }
        else
        {
            discoveredHeadsets.Add(new DiscoveredHeadset
            {
                ip = ip,
                port = port,
                lastSeen = Time.realtimeSinceStartup
            });
            changed = true;
            AddDebugLog($"📡 Casque découvert: {ip}:{port}");
        }

        if (changed)
            OnHeadsetListChanged?.Invoke();

        if (!isConnected && discoveredHeadsets.Count > 0)
            SetStatus($"🔍 {discoveredHeadsets.Count} casque(s) trouvé(s)");
    }

    private void PruneStaleHeadsets()
    {
        float now = Time.realtimeSinceStartup;
        int removed = discoveredHeadsets.RemoveAll(
            h => (now - h.lastSeen) > NetworkConfig.HEADSET_TIMEOUT_S);

        if (removed > 0)
        {
            OnHeadsetListChanged?.Invoke();
            if (!isConnected)
                SetStatus(discoveredHeadsets.Count > 0
                    ? $"🔍 {discoveredHeadsets.Count} casque(s) trouvé(s)"
                    : "🔍 Recherche de casques...");
        }
    }

    // ─────────────────────────── CONNECTION ───────────────────────────

    /// <summary>
    /// Connecte le PC au casque à l'adresse donnée.
    /// </summary>
    public void ConnectToHeadset(string ip, int port)
    {
        if (isConnected)
            Disconnect();

        shouldStop = false;

        try
        {
            SetStatus($"Connexion à {ip}:{port}...");

            tcpClient = new TcpClient();
            tcpClient.Connect(ip, port);
            stream = tcpClient.GetStream();

            connectedHeadsetIP = ip;
            isConnected = true;

            receiveThread = new Thread(ReceiveMessages) { IsBackground = true };
            receiveThread.Start();

            SetStatus($"✅ Connecté au casque: {ip}");
            OnConnected?.Invoke();
            AddDebugLog($"✅ Connecté au casque {ip}:{port}");

            // Demander le statut initial
            SendCommand(NetworkMessageType.RequestStatus);
        }
        catch (Exception e)
        {
            SetStatus($"❌ Échec connexion: {e.Message}");
            AddDebugLog($"❌ Erreur connexion: {e.GetType().Name}: {e.Message}");
            isConnected = false;
        }
    }

    /// <summary>
    /// Connecte au premier casque découvert (raccourci).
    /// </summary>
    public void ConnectToFirstAvailable()
    {
        if (discoveredHeadsets.Count > 0)
        {
            var h = discoveredHeadsets[0];
            ConnectToHeadset(h.ip, h.port);
        }
        else
        {
            SetStatus("❌ Aucun casque trouvé");
        }
    }

    /// <summary>
    /// Déconnecte du casque.
    /// </summary>
    public void Disconnect()
    {
        bool wasConnected = isConnected;
        isConnected = false;

        try { stream?.Close(); } catch { }
        try { tcpClient?.Close(); } catch { }

        connectedHeadsetIP = "";

        if (wasConnected)
        {
            SetStatus("Déconnecté du casque");
            OnDisconnected?.Invoke();
            AddDebugLog("🔌 Déconnecté du casque");
        }
    }

    // ─────────────────────────── SEND ───────────────────────────

    /// <summary>
    /// Envoie une commande au casque.
    /// </summary>
    public void SendCommand(NetworkMessageType type, string data = "")
    {
        SendMessage(new NetworkMessage(type, data));
        Debug.Log($"[SoignantClient] 📤 Envoyé: {type} {data}");
    }

    /// <summary>
    /// Envoie un message au casque.
    /// </summary>
    public void SendMessage(NetworkMessage message)
    {
        if (!isConnected || stream == null) return;

        try
        {
            string json = message.ToJson() + "\n";
            byte[] data = Encoding.UTF8.GetBytes(json);
            stream.Write(data, 0, data.Length);
        }
        catch (Exception e)
        {
            Debug.LogError($"[SoignantClient] Erreur envoi: {e.Message}");
            Disconnect();
        }
    }

    // ─────────────────────────── RECEIVE ───────────────────────────

    private void ReceiveMessages()
    {
        byte[] buffer = new byte[4096];
        StringBuilder messageBuilder = new StringBuilder();

        while (!shouldStop && tcpClient != null && tcpClient.Connected)
        {
            try
            {
                if (stream.DataAvailable)
                {
                    int bytesRead = stream.Read(buffer, 0, buffer.Length);
                    if (bytesRead > 0)
                    {
                        messageBuilder.Append(Encoding.UTF8.GetString(buffer, 0, bytesRead));

                        string allData = messageBuilder.ToString();
                        string[] messages = allData.Split('\n');

                        for (int i = 0; i < messages.Length - 1; i++)
                        {
                            if (!string.IsNullOrEmpty(messages[i]))
                                ProcessMessage(messages[i]);
                        }

                        messageBuilder.Clear();
                        messageBuilder.Append(messages[messages.Length - 1]);
                    }
                }
                Thread.Sleep(10);
            }
            catch { break; }
        }

        RunOnMainThread(() =>
        {
            if (isConnected)
            {
                isConnected = false;
                connectedHeadsetIP = "";
                SetStatus("Connexion perdue");
                OnDisconnected?.Invoke();
            }
        });
    }

    private void ProcessMessage(string json)
    {
        try
        {
            NetworkMessage message = NetworkMessage.FromJson(json);
            RunOnMainThread(() =>
            {
                OnMessageReceived?.Invoke(message);
                Debug.Log($"[SoignantClient] 📥 Reçu: {message.type}");
            });
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[SoignantClient] Message invalide: {e.Message}");
        }
    }

    // ─────────────────────────── HELPERS ───────────────────────────

    private void SetStatus(string status)
    {
        if (status == connectionStatus) return;
        connectionStatus = status;
        OnStatusChanged?.Invoke(connectionStatus);
    }

    private void RunOnMainThread(Action action)
    {
        lock (mainThreadActions)
        {
            mainThreadActions.Enqueue(action);
        }
    }

    /// <summary>
    /// Affiche un avertissement firewall au démarrage (builds Windows).
    /// Le pare-feu Windows bloque souvent les connexions UDP en build.
    /// </summary>
    private void LogFirewallWarning()
    {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        AddDebugLog("⚠️ BUILD WINDOWS — Si le casque n'est pas détecté :");
        AddDebugLog("   1. Vérifier le pare-feu Windows (autoriser l'app)");
        AddDebugLog("   2. Vérifier que le PC et le Quest sont sur le même réseau WiFi");
        AddDebugLog($"   3. Ports requis : UDP {NetworkConfig.DISCOVERY_PORT}/{NetworkConfig.DISCOVERY_PORT_ALT}, TCP {NetworkConfig.TCP_PORT}");
#endif
    }
}
