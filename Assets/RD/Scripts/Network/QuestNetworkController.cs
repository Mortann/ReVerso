using UnityEngine;

/// <summary>
/// Contrôleur côté Quest qui démarre le serveur local
/// et exécute les commandes reçues du PC soignant.
/// </summary>
public class QuestNetworkController : MonoBehaviour
{
    [Header("Références")]
    [SerializeField] private HeadsetServer headsetServer;
    [SerializeField] private PassthroughController passthroughController;

    [Header("Debug")]
    [SerializeField] private bool showDebugUI = true;

    private string currentStatus = "Initialisation...";
    private string lastCommand = "";

    private void Start()
    {
        if (headsetServer == null)
            headsetServer = FindFirstObjectByType<HeadsetServer>();

        if (passthroughController == null)
            passthroughController = FindFirstObjectByType<PassthroughController>();

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
        Debug.Log("[QuestNetworkController] ▶️ Démarrage exercice");
        headsetServer.SendStatus("Exercice démarré");
    }

    private void OnStopExercise()
    {
        lastCommand = "Arrêter exercice";
        currentStatus = "Exercice arrêté";
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
