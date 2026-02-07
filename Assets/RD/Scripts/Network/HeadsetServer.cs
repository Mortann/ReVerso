using System;
using System.Collections.Generic;
using System.Net;
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

    private TcpListener tcpListener;
    private UdpClient udpBeacon;
    private List<TcpClient> connectedClients = new List<TcpClient>();
    private Thread listenerThread;
    private Thread beaconThread;
    private bool shouldStop = false;

    private Queue<Action> mainThreadActions = new Queue<Action>();

    private void Awake()
    {
        if (autoStartOnAwake)
            StartServer();
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
                Debug.Log($"[HeadsetServer] ✅ Serveur démarré sur {localIP}:{activePort}");
            }
            catch (SocketException)
            {
                Debug.LogWarning($"[HeadsetServer] Port {portToTry} occupé, essai {portToTry + 1}...");
                portToTry++;
            }
            catch (Exception e)
            {
                Debug.LogError($"[HeadsetServer] ❌ Erreur: {e.Message}");
                SetStatus($"❌ Erreur: {e.Message}");
                break;
            }
        }

        if (!started)
        {
            SetStatus("❌ Impossible de démarrer (ports occupés)");
            Debug.LogError("[HeadsetServer] Tous les ports sont occupés.");
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
        Debug.Log("[HeadsetServer] Serveur arrêté");
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
                }
                catch { }

                Thread.Sleep(NetworkConfig.BEACON_INTERVAL_MS);
            }
        }
        catch (Exception e)
        {
            if (!shouldStop)
                Debug.LogError($"[HeadsetServer] Erreur beacon UDP: {e.Message}");
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
                        Debug.Log($"[HeadsetServer] 🔗 PC connecté: {clientIP}");
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
            Debug.Log($"[HeadsetServer] 🔌 PC déconnecté: {clientIP}");
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

    private string GetLocalIPAddress()
    {
        try
        {
            var host = Dns.GetHostEntry(Dns.GetHostName());
            foreach (var ip in host.AddressList)
            {
                if (ip.AddressFamily == AddressFamily.InterNetwork)
                    return ip.ToString();
            }
        }
        catch { }
        return "127.0.0.1";
    }

    private void RunOnMainThread(Action action)
    {
        lock (mainThreadActions)
        {
            mainThreadActions.Enqueue(action);
        }
    }
}
