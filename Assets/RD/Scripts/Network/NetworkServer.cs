using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

/// <summary>
/// Serveur réseau côté PC (Soignant)
/// Envoie des commandes au Quest et reçoit les données
/// </summary>
public class NetworkServer : MonoBehaviour
{
    [Header("Configuration")]
    [SerializeField] private bool autoStartOnAwake = true;
    [SerializeField] private bool tryAlternativePortOnFail = true;
    [SerializeField] private int maxPortRetries = 5;
    
    [Header("État")]
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
    
    private TcpListener tcpListener;
    private UdpClient udpDiscovery;
    private List<TcpClient> connectedClients = new List<TcpClient>();
    private Thread listenerThread;
    private Thread discoveryThread;
    private bool shouldStop = false;
    
    private Queue<Action> mainThreadActions = new Queue<Action>();
    
    private void Awake()
    {
        if (autoStartOnAwake)
        {
            StartServer();
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
        
        connectedClientsCount = connectedClients.Count;
        
        // Mettre à jour le statut
        UpdateStatus();
    }
    
    private void UpdateStatus()
    {
        string newStatus;
        if (!isRunning)
        {
            newStatus = "❌ Serveur arrêté";
        }
        else if (connectedClientsCount > 0)
        {
            newStatus = $"✅ Casque connecté: {connectedClientIP}";
        }
        else
        {
            newStatus = $"🔍 En attente du casque... (Port {activePort})";
        }
        
        if (newStatus != serverStatus)
        {
            serverStatus = newStatus;
            OnStatusChanged?.Invoke(serverStatus);
        }
    }
    
    private void OnDestroy()
    {
        StopServer();
    }
    
    /// <summary>
    /// Démarre le serveur TCP et la découverte UDP
    /// Essaie des ports alternatifs si le port principal est occupé
    /// </summary>
    public void StartServer()
    {
        if (isRunning) return;
        
        shouldStop = false;
        
        // Essayer de démarrer sur le port principal, puis des alternatives
        int portToTry = NetworkConfig.TCP_PORT;
        bool started = false;
        
        for (int attempt = 0; attempt < maxPortRetries && !started; attempt++)
        {
            try
            {
                // Nettoyer l'ancien listener si existe
                try { tcpListener?.Stop(); } catch { }
                
                // Démarrer le listener TCP
                tcpListener = new TcpListener(IPAddress.Any, portToTry);
                tcpListener.Start();
                
                activePort = portToTry;
                started = true;
                
                listenerThread = new Thread(ListenForClients);
                listenerThread.IsBackground = true;
                listenerThread.Start();
                
                // Démarrer la découverte UDP
                discoveryThread = new Thread(HandleDiscovery);
                discoveryThread.IsBackground = true;
                discoveryThread.Start();
                
                isRunning = true;
                serverStatus = $"🔍 En attente du casque... (Port {activePort})";
                
                string localIP = GetLocalIPAddress();
                Debug.Log($"[NetworkServer] ✅ Serveur démarré sur {localIP}:{activePort}");
                Debug.Log($"[NetworkServer] 📡 Découverte UDP active sur port {NetworkConfig.DISCOVERY_PORT}");
            }
            catch (SocketException)
            {
                Debug.LogWarning($"[NetworkServer] Port {portToTry} occupé, tentative sur {portToTry + 1}...");
                portToTry++;
            }
            catch (Exception e)
            {
                Debug.LogError($"[NetworkServer] ❌ Erreur démarrage serveur: {e.Message}");
                serverStatus = $"❌ Erreur: {e.Message}";
                break;
            }
        }
        
        if (!started)
        {
            serverStatus = "❌ Impossible de démarrer le serveur (ports occupés)";
            Debug.LogError("[NetworkServer] Tous les ports sont occupés. Fermez les autres instances.");
        }
    }
    
    /// <summary>
    /// Arrête le serveur
    /// </summary>
    public void StopServer()
    {
        shouldStop = true;
        isRunning = false;
        
        // Fermer tous les clients
        foreach (var client in connectedClients)
        {
            try { client.Close(); } catch { }
        }
        connectedClients.Clear();
        
        // Fermer le listener
        try { tcpListener?.Stop(); } catch { }
        try { udpDiscovery?.Close(); } catch { }
        
        Debug.Log("[NetworkServer] Serveur arrêté");
    }
    
    /// <summary>
    /// Envoie un message à tous les clients connectés
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
                {
                    client.GetStream().Write(data, 0, data.Length);
                }
                else
                {
                    disconnected.Add(client);
                }
            }
            catch
            {
                disconnected.Add(client);
            }
        }
        
        // Nettoyer les clients déconnectés
        foreach (var client in disconnected)
        {
            connectedClients.Remove(client);
        }
    }
    
    /// <summary>
    /// Envoie une commande simple
    /// </summary>
    public void SendCommand(NetworkMessageType type, string data = "")
    {
        SendToAllClients(new NetworkMessage(type, data));
        Debug.Log($"[NetworkServer] 📤 Envoyé: {type} - {data}");
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
                    
                    // Démarrer un thread pour lire les messages de ce client
                    Thread clientThread = new Thread(() => HandleClient(client, clientIP));
                    clientThread.IsBackground = true;
                    clientThread.Start();
                    
                    RunOnMainThread(() =>
                    {
                        connectedClientIP = clientIP;
                        OnClientConnected?.Invoke(clientIP);
                        Debug.Log($"[NetworkServer] 🔗 Client connecté: {clientIP}");
                    });
                }
                
                Thread.Sleep(100);
            }
            catch (Exception e)
            {
                if (!shouldStop)
                {
                    Debug.LogError($"[NetworkServer] Erreur listener: {e.Message}");
                }
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
                        
                        // Traiter les messages complets (terminés par \n)
                        string allData = messageBuilder.ToString();
                        string[] messages = allData.Split('\n');
                        
                        for (int i = 0; i < messages.Length - 1; i++)
                        {
                            if (!string.IsNullOrEmpty(messages[i]))
                            {
                                ProcessMessage(messages[i]);
                            }
                        }
                        
                        // Garder le message incomplet
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
        
        // Client déconnecté
        connectedClients.Remove(client);
        RunOnMainThread(() =>
        {
            if (connectedClientIP == clientIP)
            {
                connectedClientIP = "";
            }
            OnClientDisconnected?.Invoke(clientIP);
            Debug.Log($"[NetworkServer] 🔌 Client déconnecté: {clientIP}");
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
                Debug.Log($"[NetworkServer] 📥 Reçu: {message.type} - {message.data}");
            });
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[NetworkServer] Message invalide: {e.Message}");
        }
    }
    
    private void HandleDiscovery()
    {
        try
        {
            udpDiscovery = new UdpClient(NetworkConfig.DISCOVERY_PORT);
            
            while (!shouldStop)
            {
                try
                {
                    IPEndPoint remoteEP = new IPEndPoint(IPAddress.Any, 0);
                    byte[] data = udpDiscovery.Receive(ref remoteEP);
                    string message = Encoding.UTF8.GetString(data);
                    
                    if (message == NetworkConfig.DISCOVERY_MESSAGE)
                    {
                        // Répondre avec notre IP
                        string response = GetLocalIPAddress();
                        byte[] responseData = Encoding.UTF8.GetBytes(response);
                        udpDiscovery.Send(responseData, responseData.Length, remoteEP);
                        
                        Debug.Log($"[NetworkServer] 📡 Découverte reçue de {remoteEP.Address}, répondu avec {response}");
                    }
                }
                catch { }
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[NetworkServer] Erreur découverte UDP: {e.Message}");
        }
    }
    
    private string GetLocalIPAddress()
    {
        var host = Dns.GetHostEntry(Dns.GetHostName());
        foreach (var ip in host.AddressList)
        {
            if (ip.AddressFamily == AddressFamily.InterNetwork)
            {
                return ip.ToString();
            }
        }
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
