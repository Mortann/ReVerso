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
    /// Port UDP sur lequel le PC écoute les beacons des casques.
    /// </summary>
    public const int DISCOVERY_PORT = 7778;

    /// <summary>
    /// Port UDP alternatif utilisé si le port principal est occupé.
    /// </summary>
    public const int DISCOVERY_PORT_ALT = 7779;
    
    /// <summary>
    /// Préfixe du beacon UDP émis par le casque pour se signaler.
    /// Format complet : REVERSO_HEADSET|<ip>|<port>
    /// </summary>
    public const string BEACON_HEADSET = "REVERSO_HEADSET";

    /// <summary>
    /// Intervalle d'émission du beacon UDP (ms).
    /// </summary>
    public const int BEACON_INTERVAL_MS = 2000;

    /// <summary>
    /// Durée sans beacon avant de considérer un casque comme disparu (s).
    /// </summary>
    public const float HEADSET_TIMEOUT_S = 6f;
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
