using UnityEngine;
using ReVerso.Data;

/// <summary>
/// Contrôleur côté Quest qui démarre le serveur local,
/// exécute les commandes reçues du PC soignant,
/// et envoie périodiquement les données de tracking au PC.
/// </summary>
public class QuestNetworkController : MonoBehaviour
{
    [Header("Références")]
    [SerializeField] private HeadsetServer headsetServer;
    [SerializeField] private PassthroughController passthroughController;
    [SerializeField] private HandTrackingDebugger handTrackingDebugger;
    [SerializeField] private SessionMetricsCollector sessionMetricsCollector;

    [Header("Envoi des données au PC")]
    [Tooltip("Envoyer les données de tracking au PC (en temps réel)")]
    [SerializeField] private bool sendTrackingData = true;
    [Tooltip("Fréquence d'envoi des données (en frames, 10 = 1 sur 10)")]
    [SerializeField] private int sendFrequency = 10;

    [Header("Debug")]
    [SerializeField] private bool showDebugUI = true;

    private string currentStatus = "Initialisation...";
    private string lastCommand = "";
    private int frameCounter = 0;
    private bool isExerciseRunning = false;
    private CoteAffecte coteEnCours;

    private void Start()
    {
        // Auto-trouver les composants
        if (headsetServer == null)
            headsetServer = FindFirstObjectByType<HeadsetServer>();
        if (passthroughController == null)
            passthroughController = FindFirstObjectByType<PassthroughController>();
        if (handTrackingDebugger == null)
            handTrackingDebugger = FindFirstObjectByType<HandTrackingDebugger>();
        if (sessionMetricsCollector == null)
            sessionMetricsCollector = FindFirstObjectByType<SessionMetricsCollector>();

        if (headsetServer != null)
        {
            headsetServer.OnClientConnected += OnPCConnected;
            headsetServer.OnClientDisconnected += OnPCDisconnected;
            headsetServer.OnStartExercise += OnStartExercise;
            headsetServer.OnStopExercise += OnStopExercise;
            headsetServer.OnLoadMovement += OnLoadMovement;
            headsetServer.OnPassthroughToggle += OnPassthroughToggle;
        }
        else
        {
            Debug.LogWarning("[QuestNetworkController] HeadsetServer non trouvé !");
        }

        currentStatus = "En attente du PC...";
    }

    private void Update()
    {
        frameCounter++;

        // Envoyer les données de tracking au PC périodiquement
        if (sendTrackingData && headsetServer != null && headsetServer.HasConnectedClient
            && handTrackingDebugger != null && frameCounter % sendFrequency == 0)
        {
            SendHandDataToPC();
        }
    }

    private void OnDestroy()
    {
        if (headsetServer != null)
        {
            headsetServer.OnClientConnected -= OnPCConnected;
            headsetServer.OnClientDisconnected -= OnPCDisconnected;
            headsetServer.OnStartExercise -= OnStartExercise;
            headsetServer.OnStopExercise -= OnStopExercise;
            headsetServer.OnLoadMovement -= OnLoadMovement;
            headsetServer.OnPassthroughToggle -= OnPassthroughToggle;
        }
    }

    // === ENVOI DE DONNÉES AU PC ===

    /// <summary>
    /// Envoie les données de tracking des mains au PC soignant
    /// </summary>
    private void SendHandDataToPC()
    {
        var leftData = handTrackingDebugger.LeftHandData;
        var rightData = handTrackingDebugger.RightHandData;

        // Calculer l'amplitude de chaque main
        float leftAmplitude = CalculateAmplitude(leftData);
        float rightAmplitude = CalculateAmplitude(rightData);

        // Envoyer les données de la main active (ou les deux)
        if (leftData.isTracked)
        {
            var data = new HandAmplitudeData
            {
                amplitude = leftAmplitude,
                isTracked = true,
                hand = "left"
            };
            headsetServer.SendToAllClients(new NetworkMessage(
                NetworkMessageType.HandTrackingData, JsonUtility.ToJson(data)));
        }

        if (rightData.isTracked)
        {
            var data = new HandAmplitudeData
            {
                amplitude = rightAmplitude,
                isTracked = true,
                hand = "right"
            };
            headsetServer.SendToAllClients(new NetworkMessage(
                NetworkMessageType.HandTrackingData, JsonUtility.ToJson(data)));
        }
    }

    /// <summary>
    /// Calcule l'amplitude (ouverture) d'une main
    /// </summary>
    private float CalculateAmplitude(HandTrackingDebugger.HandData handData)
    {
        if (!handData.isTracked || handData.palmPosition == Vector3.zero)
            return 0f;

        float distThumb = Vector3.Distance(handData.palmPosition, handData.thumbTipPosition);
        float distIndex = Vector3.Distance(handData.palmPosition, handData.indexTipPosition);
        float distMiddle = Vector3.Distance(handData.palmPosition, handData.middleTipPosition);
        float distRing = Vector3.Distance(handData.palmPosition, handData.ringTipPosition);
        float distLittle = Vector3.Distance(handData.palmPosition, handData.littleTipPosition);

        float avgDistance = (distThumb + distIndex + distMiddle + distRing + distLittle) / 5f;
        return Mathf.Clamp01(avgDistance / 0.25f); // Normalisé: 0=fermé, 1=ouvert
    }

    // === EVENTS RÉSEAU ===

    private void OnPCConnected(string ip)
    {
        currentStatus = "PC connecté";
        Debug.Log("[QuestNetworkController] ✅ PC soignant connecté");
        headsetServer.SendStatus("Casque prêt");
    }

    private void OnPCDisconnected(string ip)
    {
        currentStatus = "En attente du PC...";
        Debug.Log("[QuestNetworkController] 🔌 PC déconnecté");
    }

    private void OnStartExercise()
    {
        lastCommand = "Démarrer exercice";
        currentStatus = "Exercice en cours";
        isExerciseRunning = true;

        // Démarrer la collecte de métriques
        if (sessionMetricsCollector != null)
        {
            sessionMetricsCollector.StartRecording(coteEnCours, 0f); // Durée illimitée
        }

        Debug.Log("[QuestNetworkController] ▶️ Démarrage exercice");
        headsetServer.SendStatus("Exercice démarré");
    }

    private void OnStopExercise()
    {
        lastCommand = "Arrêter exercice";
        currentStatus = "Exercice arrêté";
        isExerciseRunning = false;

        // Arrêter la collecte et envoyer les métriques au PC
        if (sessionMetricsCollector != null)
        {
            var metriques = sessionMetricsCollector.GetMetriques();
            string metriquesJson = JsonUtility.ToJson(metriques);
            headsetServer.SendToAllClients(new NetworkMessage(
                NetworkMessageType.ExerciseCompleted, metriquesJson));
            Debug.Log($"[QuestNetworkController] 📊 Métriques envoyées au PC");
        }

        Debug.Log("[QuestNetworkController] ⏹️ Arrêt exercice");
        headsetServer.SendStatus("Exercice arrêté");
    }

    private void OnLoadMovement(string movementData)
    {
        lastCommand = $"Charger mouvement: {movementData}";
        Debug.Log($"[QuestNetworkController] 📂 Chargement mouvement: {movementData}");
        headsetServer.SendStatus($"Mouvement chargé: {movementData}");
    }

    private void OnPassthroughToggle(bool enable)
    {
        lastCommand = enable ? "Activer Passthrough" : "Désactiver Passthrough";

        if (passthroughController != null)
        {
            passthroughController.SetPassthroughActive(enable);
            currentStatus = enable ? "Passthrough ACTIF" : "Passthrough INACTIF";
            headsetServer.SendStatus(currentStatus);
        }
        else
        {
            Debug.LogWarning("[QuestNetworkController] PassthroughController non trouvé !");
            headsetServer.SendMessage(new NetworkMessage(NetworkMessageType.Error, "PassthroughController non disponible"));
        }
    }

    // === DEBUG UI ===

    private void OnGUI()
    {
        if (!showDebugUI) return;

        GUIStyle boxStyle = new GUIStyle(GUI.skin.box) { fontSize = 24 };
        GUIStyle labelStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 20,
            normal = { textColor = Color.white }
        };

        float width = 400;
        float height = 150;
        float x = 10;
        float y = Screen.height - height - 10;

        GUI.Box(new Rect(x, y, width, height), "ReVerso Network", boxStyle);
        GUI.Label(new Rect(x + 10, y + 30, width - 20, 30),
            $"Statut: {currentStatus}", labelStyle);
        GUI.Label(new Rect(x + 10, y + 60, width - 20, 30),
            $"Serveur: {(headsetServer != null ? headsetServer.ServerStatus : "N/A")}", labelStyle);
        GUI.Label(new Rect(x + 10, y + 90, width - 20, 30),
            $"Dernière commande: {lastCommand}", labelStyle);
    }
}
