using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

/// <summary>
/// Client réseau côté Quest (Patient)
/// Se connecte au PC et reçoit des commandes
/// </summary>
public class NetworkClient : MonoBehaviour
{
    [Header("Configuration")]
    [SerializeField] private bool autoDiscoverServer = true;
    [SerializeField] private string manualServerIP = "";
    [SerializeField] private float reconnectDelay = 3f;
    
    [Header("État")]
    [SerializeField] private bool isConnected = false;
    [SerializeField] private string serverIP = "";
    [SerializeField] private string connectionStatus = "Déconnecté";
    
    public bool IsConnected => isConnected;
    public string ServerIP => serverIP;
    public string ConnectionStatus => connectionStatus;
    
    // Events
    public event Action OnConnected;
    public event Action OnDisconnected;
    public event Action<NetworkMessage> OnMessageReceived;
    
    // Events spécifiques pour les commandes courantes
    public event Action OnStartExercise;
    public event Action OnStopExercise;
    public event Action<string> OnLoadMovement;
    public event Action<bool> OnPassthroughToggle;
    
    private TcpClient tcpClient;
    private NetworkStream stream;
    private Thread receiveThread;
    private bool shouldStop = false;
    
    private Queue<Action> mainThreadActions = new Queue<Action>();
    
    private void Start()
    {
        if (autoDiscoverServer)
        {
            StartCoroutine(DiscoverAndConnect());
        }
        else if (!string.IsNullOrEmpty(manualServerIP))
        {
            ConnectToServer(manualServerIP);
        }
    }
    
    private void Update()
    {
        // Exécuter les actions sur le thread principal
        lock (mainThreadActions)
        {
            while (mainThreadActions.Count > 0)
            {
                mainThreadActions.Dequeue()?.Invoke();
            }
        }
    }
    
    private void OnDestroy()
    {
        Disconnect();
    }
    
    /// <summary>
    /// Découvre automatiquement le serveur PC sur le réseau
    /// </summary>
    private IEnumerator DiscoverAndConnect()
    {
        connectionStatus = "Recherche du PC...";
        Debug.Log("[NetworkClient] 🔍 Recherche du serveur PC...");
        
        while (!isConnected && !shouldStop)
        {
            string discoveredIP = DiscoverServer();
            
            if (!string.IsNullOrEmpty(discoveredIP))
            {
                Debug.Log($"[NetworkClient] 📡 Serveur trouvé: {discoveredIP}");
                ConnectToServer(discoveredIP);
            }
            else
            {
                connectionStatus = "PC non trouvé, nouvelle tentative...";
            }
            
            yield return new WaitForSeconds(reconnectDelay);
        }
    }
    
    /// <summary>
    /// Envoie une requête de découverte UDP
    /// </summary>
    private string DiscoverServer()
    {
        try
        {
            using (UdpClient udp = new UdpClient())
            {
                udp.EnableBroadcast = true;
                udp.Client.ReceiveTimeout = 2000;
                
                byte[] data = Encoding.UTF8.GetBytes(NetworkConfig.DISCOVERY_MESSAGE);
                udp.Send(data, data.Length, new IPEndPoint(IPAddress.Broadcast, NetworkConfig.DISCOVERY_PORT));
                
                IPEndPoint remoteEP = new IPEndPoint(IPAddress.Any, 0);
                byte[] response = udp.Receive(ref remoteEP);
                
                return Encoding.UTF8.GetString(response);
            }
        }
        catch
        {
            return null;
        }
    }
    
    /// <summary>
    /// Se connecte à un serveur spécifique
    /// </summary>
    public void ConnectToServer(string ip)
    {
        if (isConnected) Disconnect();
        
        shouldStop = false;
        
        try
        {
            connectionStatus = $"Connexion à {ip}...";
            
            tcpClient = new TcpClient();
            tcpClient.Connect(ip, NetworkConfig.TCP_PORT);
            stream = tcpClient.GetStream();
            
            serverIP = ip;
            isConnected = true;
            connectionStatus = $"Connecté à {ip}";
            
            // Démarrer le thread de réception
            receiveThread = new Thread(ReceiveMessages);
            receiveThread.IsBackground = true;
            receiveThread.Start();
            
            OnConnected?.Invoke();
            Debug.Log($"[NetworkClient] ✅ Connecté au serveur {ip}");
            
            // Envoyer un message de statut
            SendMessage(new NetworkMessage(NetworkMessageType.StatusUpdate, "Quest connecté et prêt"));
        }
        catch (Exception e)
        {
            connectionStatus = $"Échec connexion: {e.Message}";
            Debug.LogError($"[NetworkClient] ❌ Erreur connexion: {e.Message}");
            isConnected = false;
        }
    }
    
    /// <summary>
    /// Déconnecte du serveur
    /// </summary>
    public void Disconnect()
    {
        shouldStop = true;
        isConnected = false;
        connectionStatus = "Déconnecté";
        
        try { stream?.Close(); } catch { }
        try { tcpClient?.Close(); } catch { }
        
        serverIP = "";
        
        OnDisconnected?.Invoke();
        Debug.Log("[NetworkClient] Déconnecté du serveur");
    }
    
    /// <summary>
    /// Envoie un message au serveur
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
            Debug.LogError($"[NetworkClient] Erreur envoi: {e.Message}");
            Disconnect();
            StartCoroutine(DiscoverAndConnect());
        }
    }
    
    /// <summary>
    /// Envoie les données de suivi des mains
    /// </summary>
    public void SendHandTrackingData(string jsonData)
    {
        SendMessage(new NetworkMessage(NetworkMessageType.HandTrackingData, jsonData));
    }
    
    /// <summary>
    /// Envoie une mise à jour de statut
    /// </summary>
    public void SendStatus(string status)
    {
        SendMessage(new NetworkMessage(NetworkMessageType.StatusUpdate, status));
    }
    
    private void ReceiveMessages()
    {
        byte[] buffer = new byte[4096];
        StringBuilder messageBuilder = new StringBuilder();
        
        while (!shouldStop && tcpClient.Connected)
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
                            {
                                ProcessMessage(messages[i]);
                            }
                        }
                        
                        messageBuilder.Clear();
                        messageBuilder.Append(messages[messages.Length - 1]);
                    }
                }
                
                Thread.Sleep(10);
            }
            catch
            {
                break;
            }
        }
        
        // Déconnecté
        RunOnMainThread(() =>
        {
            if (isConnected)
            {
                isConnected = false;
                connectionStatus = "Connexion perdue";
                OnDisconnected?.Invoke();
                
                // Tenter de se reconnecter
                if (autoDiscoverServer)
                {
                    StartCoroutine(DiscoverAndConnect());
                }
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
                Debug.Log($"[NetworkClient] 📥 Reçu: {message.type}");
                
                // Déclencher les events spécifiques
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
                }
            });
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[NetworkClient] Message invalide: {e.Message}");
        }
    }
    
    private void RunOnMainThread(Action action)
    {
        lock (mainThreadActions)
        {
            mainThreadActions.Enqueue(action);
        }
    }
}
