using System;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

/// <summary>
/// Serveur réseau côté Quest (Patient).
/// Démarre automatiquement au lancement de l'app.
/// Émet un beacon UDP pour être découvert par le PC soignant.
/// Reçoit les commandes du PC et envoie les données de tracking.
/// 
/// Impact performance : négligeable (TCP listen + petit beacon UDP périodique).
/// </summary>
public class HeadsetServer : MonoBehaviour
{
    [Header("Configuration")]
    [SerializeField] private bool autoStartOnAwake = true;
    [SerializeField] private int maxPortRetries = 5;

    [Header("État (lecture seule)")]
    [SerializeField] private bool isRunning = false;
    [SerializeField] private string connectedClientIP = "";
    [SerializeField] private int connectedClientsCount = 0;
    [SerializeField] private string serverStatus = "Non démarré";
    [SerializeField] private int activePort = 0;

    public bool IsRunning => isRunning;
    public bool HasConnectedClient => connectedClientsCount > 0;
    public string ConnectedClientIP => connectedClientIP;
    public string ServerStatus => serverStatus;
    public int ActivePort => activePort;

    // Events
    public event Action<string> OnClientConnected;
    public event Action<string> OnClientDisconnected;
    public event Action<NetworkMessage> OnMessageReceived;
    public event Action<string> OnStatusChanged;

    // Events spécifiques pour les commandes reçues du PC
    public event Action OnStartExercise;
    public event Action OnStopExercise;
    public event Action<string> OnLoadMovement;
    public event Action<bool> OnPassthroughToggle;
    public event Action<bool> OnStreamingToggle;

    private TcpListener tcpListener;
    private UdpClient udpBeacon;
    private List<TcpClient> connectedClients = new List<TcpClient>();
    private Thread listenerThread;
    private Thread beaconThread;
    private bool shouldStop = false;

    private Queue<Action> mainThreadActions = new Queue<Action>();

    // Android MulticastLock — nécessaire pour envoyer/recevoir du UDP broadcast
#if UNITY_ANDROID && !UNITY_EDITOR
    private AndroidJavaObject multicastLock;
#endif

    // Debug log circulaire visible dans l'overlay
    private static readonly List<string> debugLog = new List<string>();
    private const int MAX_LOG_LINES = 20;

    /// <summary>
    /// Derniers logs réseau (pour affichage debug dans le casque).
    /// </summary>
    public static IReadOnlyList<string> DebugLog => debugLog;

    /// <summary>
    /// Ajoute un log reseau partage (affiche dans l'overlay casque).
    /// </summary>
    public static void AddNetworkLog(string msg)
    {
        AddDebugLog(msg);
    }

    private static void AddDebugLog(string msg)
    {
        string line = $"[{DateTime.Now:HH:mm:ss}] {msg}";
        Debug.Log($"[HeadsetServer] {msg}");
        lock (debugLog)
        {
            debugLog.Add(line);
            if (debugLog.Count > MAX_LOG_LINES)
                debugLog.RemoveAt(0);
        }
    }

    private void Awake()
    {
        try
        {
            AcquireMulticastLock();
            if (autoStartOnAwake)
                StartServer();
        }
        catch (Exception e)
        {
            Debug.LogError($"[HeadsetServer] Erreur critique au démarrage: {e.Message}");
        }
    }

    private void Update()
    {
        lock (mainThreadActions)
        {
            while (mainThreadActions.Count > 0)
                mainThreadActions.Dequeue()?.Invoke();
        }

        connectedClientsCount = connectedClients.Count;
        UpdateStatus();
    }

    private void OnDestroy()
    {
        StopServer();
        ReleaseMulticastLock();
    }

    // ─────────────────────────── PUBLIC API ───────────────────────────

    /// <summary>
    /// Démarre le serveur TCP et le beacon UDP de découverte.
    /// </summary>
    public void StartServer()
    {
        if (isRunning) return;
        shouldStop = false;

        int portToTry = NetworkConfig.TCP_PORT;
        bool started = false;

        for (int attempt = 0; attempt < maxPortRetries && !started; attempt++)
        {
            try
            {
                try { tcpListener?.Stop(); } catch { }

                tcpListener = new TcpListener(IPAddress.Any, portToTry);
                tcpListener.Start();

                activePort = portToTry;
                started = true;

                listenerThread = new Thread(ListenForClients) { IsBackground = true };
                listenerThread.Start();

                beaconThread = new Thread(BroadcastBeacon) { IsBackground = true };
                beaconThread.Start();

                isRunning = true;
                SetStatus($"🔍 En attente du PC... (Port {activePort})");

                string localIP = GetLocalIPAddress();
                AddDebugLog($"✅ Serveur démarré sur {localIP}:{activePort}");
                AddDebugLog($"Platform: {Application.platform}");
            }
            catch (SocketException se)
            {
                AddDebugLog($"⚠️ Port {portToTry} occupé ({se.SocketErrorCode}), essai {portToTry + 1}...");
                portToTry++;
            }
            catch (Exception e)
            {
                AddDebugLog($"❌ Erreur démarrage: {e.GetType().Name}: {e.Message}");
                SetStatus($"❌ Erreur: {e.Message}");
                break;
            }
        }

        if (!started)
        {
            SetStatus("❌ Impossible de démarrer (ports occupés)");
            AddDebugLog("❌ Tous les ports sont occupés.");
        }
    }

    /// <summary>
    /// Arrête le serveur.
    /// </summary>
    public void StopServer()
    {
        shouldStop = true;
        isRunning = false;

        foreach (var client in connectedClients)
        {
            try { client.Close(); } catch { }
        }
        connectedClients.Clear();

        try { tcpListener?.Stop(); } catch { }
        try { udpBeacon?.Close(); } catch { }

        SetStatus("Serveur arrêté");
        AddDebugLog("Serveur arrêté");
    }

    /// <summary>
    /// Envoie un message à tous les clients PC connectés.
    /// </summary>
    public void SendToAllClients(NetworkMessage message)
    {
        string json = message.ToJson() + "\n";
        byte[] data = Encoding.UTF8.GetBytes(json);

        List<TcpClient> disconnected = new List<TcpClient>();

        foreach (var client in connectedClients)
        {
            try
            {
                if (client.Connected)
                    client.GetStream().Write(data, 0, data.Length);
                else
                    disconnected.Add(client);
            }
            catch { disconnected.Add(client); }
        }

        foreach (var client in disconnected)
            connectedClients.Remove(client);
    }

    /// <summary>
    /// Envoie un message de type donné.
    /// </summary>
    public void SendMessage(NetworkMessage message)
    {
        SendToAllClients(message);
    }

    /// <summary>
    /// Envoie un statut au PC.
    /// </summary>
    public void SendStatus(string status)
    {
        SendToAllClients(new NetworkMessage(NetworkMessageType.StatusUpdate, status));
    }

    /// <summary>
    /// Envoie les données de tracking au PC.
    /// </summary>
    public void SendHandTrackingData(string jsonData)
    {
        SendToAllClients(new NetworkMessage(NetworkMessageType.HandTrackingData, jsonData));
    }

    // ─────────────────────────── THREADS ───────────────────────────

    /// <summary>
    /// Beacon UDP : émet périodiquement un message pour que le PC puisse
    /// découvrir le casque sur le réseau local.
    /// </summary>
    private void BroadcastBeacon()
    {
        try
        {
            udpBeacon = new UdpClient();
            udpBeacon.EnableBroadcast = true;

            string localIP = GetLocalIPAddress();
            string beaconPayload = $"{NetworkConfig.BEACON_HEADSET}|{localIP}|{activePort}";

            RunOnMainThread(() => AddDebugLog($"📡 Beacon: {beaconPayload}"));

            // Vérifier que l'IP est valide (pas loopback)
            if (localIP == "127.0.0.1" || localIP == "0.0.0.0")
            {
                RunOnMainThread(() => AddDebugLog($"⚠️ ATTENTION: IP locale = {localIP} — le PC ne pourra pas se connecter !"));
            }

            int beaconCount = 0;
            while (!shouldStop)
            {
                try
                {
                    byte[] data = Encoding.UTF8.GetBytes(beaconPayload);
                    // Envoyer sur les deux ports pour que le PC puisse écouter sur l'un ou l'autre
                    udpBeacon.Send(data, data.Length,
                        new IPEndPoint(IPAddress.Broadcast, NetworkConfig.DISCOVERY_PORT));
                    udpBeacon.Send(data, data.Length,
                        new IPEndPoint(IPAddress.Broadcast, NetworkConfig.DISCOVERY_PORT_ALT));

                    // Aussi essayer le broadcast dirigé du sous-réseau
                    try
                    {
                        IPAddress subnetBroadcast = GetSubnetBroadcast(localIP);
                        if (subnetBroadcast != null)
                        {
                            udpBeacon.Send(data, data.Length,
                                new IPEndPoint(subnetBroadcast, NetworkConfig.DISCOVERY_PORT));
                            udpBeacon.Send(data, data.Length,
                                new IPEndPoint(subnetBroadcast, NetworkConfig.DISCOVERY_PORT_ALT));
                        }
                    }
                    catch { }

                    beaconCount++;
                    if (beaconCount <= 3 || beaconCount % 30 == 0)
                    {
                        int count = beaconCount;
                        RunOnMainThread(() => AddDebugLog($"📡 Beacon #{count} envoyé ({localIP})"));
                    }
                }
                catch (Exception ex)
                {
                    RunOnMainThread(() => AddDebugLog($"⚠️ Erreur envoi beacon: {ex.Message}"));
                }

                Thread.Sleep(NetworkConfig.BEACON_INTERVAL_MS);
            }
        }
        catch (Exception e)
        {
            if (!shouldStop)
                RunOnMainThread(() => AddDebugLog($"❌ Erreur beacon UDP: {e.GetType().Name}: {e.Message}"));
        }
    }

    private void ListenForClients()
    {
        while (!shouldStop)
        {
            try
            {
                if (tcpListener.Pending())
                {
                    TcpClient client = tcpListener.AcceptTcpClient();
                    string clientIP = ((IPEndPoint)client.Client.RemoteEndPoint).Address.ToString();

                    connectedClients.Add(client);

                    Thread clientThread = new Thread(() => HandleClient(client, clientIP))
                    { IsBackground = true };
                    clientThread.Start();

                    RunOnMainThread(() =>
                    {
                        connectedClientIP = clientIP;
                        OnClientConnected?.Invoke(clientIP);
                        AddDebugLog($"🔗 PC connecté: {clientIP}");
                    });
                }

                Thread.Sleep(100);
            }
            catch (Exception e)
            {
                if (!shouldStop)
                    Debug.LogError($"[HeadsetServer] Erreur listener: {e.Message}");
            }
        }
    }

    private void HandleClient(TcpClient client, string clientIP)
    {
        NetworkStream stream = client.GetStream();
        byte[] buffer = new byte[4096];
        StringBuilder messageBuilder = new StringBuilder();

        while (!shouldStop && client.Connected)
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

        connectedClients.Remove(client);
        RunOnMainThread(() =>
        {
            if (connectedClientIP == clientIP)
                connectedClientIP = "";
            OnClientDisconnected?.Invoke(clientIP);
            AddDebugLog($"🔌 PC déconnecté: {clientIP}");
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

                // Dispatcher les commandes vers les events spécifiques
                switch (message.type)
                {
                    case NetworkMessageType.StartExercise:
                        OnStartExercise?.Invoke();
                        break;
                    case NetworkMessageType.StopExercise:
                        OnStopExercise?.Invoke();
                        break;
                    case NetworkMessageType.LoadMovement:
                        OnLoadMovement?.Invoke(message.data);
                        break;
                    case NetworkMessageType.EnablePassthrough:
                        OnPassthroughToggle?.Invoke(true);
                        break;
                    case NetworkMessageType.DisablePassthrough:
                        OnPassthroughToggle?.Invoke(false);
                        break;
                    case NetworkMessageType.StartStreaming:
                        OnStreamingToggle?.Invoke(true);
                        break;
                    case NetworkMessageType.StopStreaming:
                        OnStreamingToggle?.Invoke(false);
                        break;
                    case NetworkMessageType.RequestStatus:
                        SendStatus("Casque actif");
                        break;
                }

                Debug.Log($"[HeadsetServer] 📥 Commande reçue: {message.type}");
            });
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[HeadsetServer] Message invalide: {e.Message}");
        }
    }

    // ─────────────────────────── HELPERS ───────────────────────────

    private void SetStatus(string status)
    {
        if (status == serverStatus) return;
        serverStatus = status;
        OnStatusChanged?.Invoke(serverStatus);
    }

    private void UpdateStatus()
    {
        string newStatus;
        if (!isRunning)
            newStatus = "❌ Serveur arrêté";
        else if (connectedClientsCount > 0)
            newStatus = $"✅ PC connecté: {connectedClientIP}";
        else
            newStatus = $"🔍 En attente du PC... (Port {activePort})";

        SetStatus(newStatus);
    }

    /// <summary>
    /// Obtient l'adresse IP locale du réseau WiFi.
    /// Sur Android, Dns.GetHostEntry ne fonctionne pas — on utilise NetworkInterface.
    /// </summary>
    private string GetLocalIPAddress()
    {
        string bestIP = "127.0.0.1";

        try
        {
            // Méthode 1 : NetworkInterface (fonctionne sur Android et PC)
            foreach (var iface in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (iface.OperationalStatus != OperationalStatus.Up)
                    continue;

                // Ignorer loopback
                if (iface.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                    continue;

                foreach (var addr in iface.GetIPProperties().UnicastAddresses)
                {
                    if (addr.Address.AddressFamily == AddressFamily.InterNetwork)
                    {
                        string ip = addr.Address.ToString();
                        // Préférer les adresses WiFi/réseau local, pas loopback
                        if (ip.StartsWith("192.168.") || ip.StartsWith("10.") || ip.StartsWith("172."))
                        {
                            AddDebugLog($"IP trouvée (NetworkInterface): {ip} [{iface.Name}]");
                            return ip;
                        }
                        if (bestIP == "127.0.0.1")
                            bestIP = ip;
                    }
                }
            }
        }
        catch (Exception e)
        {
            AddDebugLog($"⚠️ NetworkInterface failed: {e.Message}");
        }

        try
        {
            // Méthode 2 : Socket connect trick (fallback robuste)
            using (var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
            {
                // On ne se connecte pas vraiment, juste pour déterminer l'IP locale
                socket.Connect("8.8.8.8", 80);
                string ip = ((IPEndPoint)socket.LocalEndPoint).Address.ToString();
                if (ip != "0.0.0.0" && ip != "127.0.0.1")
                {
                    AddDebugLog($"IP trouvée (Socket): {ip}");
                    return ip;
                }
            }
        }
        catch (Exception e)
        {
            AddDebugLog($"⚠️ Socket method failed: {e.Message}");
        }

        try
        {
            // Méthode 3 : Dns (fonctionne en Editor mais pas sur Android)
            var host = Dns.GetHostEntry(Dns.GetHostName());
            foreach (var ip in host.AddressList)
            {
                if (ip.AddressFamily == AddressFamily.InterNetwork)
                    return ip.ToString();
            }
        }
        catch { }

        AddDebugLog($"⚠️ Aucune IP réseau trouvée, utilisation de {bestIP}");
        return bestIP;
    }

    /// <summary>
    /// Calcule l'adresse de broadcast du sous-réseau (ex: 192.168.1.255).
    /// </summary>
    private IPAddress GetSubnetBroadcast(string ipStr)
    {
        try
        {
            foreach (var iface in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (iface.OperationalStatus != OperationalStatus.Up) continue;
                foreach (var addr in iface.GetIPProperties().UnicastAddresses)
                {
                    if (addr.Address.ToString() == ipStr && addr.Address.AddressFamily == AddressFamily.InterNetwork)
                    {
                        byte[] ipBytes = addr.Address.GetAddressBytes();
                        byte[] maskBytes = addr.IPv4Mask.GetAddressBytes();
                        byte[] bcast = new byte[4];
                        for (int i = 0; i < 4; i++)
                            bcast[i] = (byte)(ipBytes[i] | ~maskBytes[i]);
                        return new IPAddress(bcast);
                    }
                }
            }
        }
        catch { }
        return null;
    }

    // ─────────────────────────── ANDROID MULTICAST LOCK ───────────────────────────

    /// <summary>
    /// Acquiert le MulticastLock Android.
    /// Sans cela, Android filtre les paquets UDP broadcast pour économiser la batterie.
    /// </summary>
    private void AcquireMulticastLock()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            using (var activity = new AndroidJavaClass("com.unity3d.player.UnityPlayer")
                .GetStatic<AndroidJavaObject>("currentActivity"))
            {
                var wifiManager = activity.Call<AndroidJavaObject>("getSystemService", "wifi");
                multicastLock = wifiManager.Call<AndroidJavaObject>("createMulticastLock", "ReVersoBeacon");
                multicastLock.Call("setReferenceCounted", true);
                multicastLock.Call("acquire");
                AddDebugLog("🔓 Android MulticastLock acquis");
            }
        }
        catch (Exception e)
        {
            AddDebugLog($"⚠️ MulticastLock failed: {e.Message}");
        }
#endif
    }

    /// <summary>
    /// Libère le MulticastLock Android.
    /// </summary>
    private void ReleaseMulticastLock()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            if (multicastLock != null)
            {
                multicastLock.Call("release");
                multicastLock = null;
                AddDebugLog("🔒 Android MulticastLock libéré");
            }
        }
        catch { }
#endif
    }

    private void RunOnMainThread(Action action)
    {
        lock (mainThreadActions)
        {
            mainThreadActions.Enqueue(action);
        }
    }
}
