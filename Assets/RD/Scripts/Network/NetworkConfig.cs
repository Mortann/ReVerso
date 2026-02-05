using UnityEngine;

/// <summary>
/// Configuration réseau partagée entre PC et Quest
/// </summary>
public static class NetworkConfig
{
    /// <summary>
    /// Port de communication TCP pour les commandes
    /// </summary>
    public const int TCP_PORT = 7777;
    
    /// <summary>
    /// Port UDP pour la découverte automatique
    /// </summary>
    public const int DISCOVERY_PORT = 7778;
    
    /// <summary>
    /// Message de découverte envoyé par le PC
    /// </summary>
    public const string DISCOVERY_MESSAGE = "REVERSO_DISCOVER";
    
    /// <summary>
    /// Réponse de découverte du casque
    /// </summary>
    public const string DISCOVERY_RESPONSE = "REVERSO_QUEST_HERE";
}

/// <summary>
/// Types de messages échangés entre PC et Quest
/// </summary>
public enum NetworkMessageType
{
    // PC → Quest
    StartExercise,
    StopExercise,
    LoadMovement,
    EnablePassthrough,
    DisablePassthrough,
    RequestStatus,
    
    // Quest → PC
    StatusUpdate,
    HandTrackingData,
    ExerciseCompleted,
    Error
}

/// <summary>
/// Structure de message réseau
/// </summary>
[System.Serializable]
public class NetworkMessage
{
    public NetworkMessageType type;
    public string data;
    public long timestamp;
    
    public NetworkMessage(NetworkMessageType type, string data = "")
    {
        this.type = type;
        this.data = data;
        this.timestamp = System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }
    
    public string ToJson()
    {
        return JsonUtility.ToJson(this);
    }
    
    public static NetworkMessage FromJson(string json)
    {
        return JsonUtility.FromJson<NetworkMessage>(json);
    }
}
