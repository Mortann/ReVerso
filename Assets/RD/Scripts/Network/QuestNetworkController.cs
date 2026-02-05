using UnityEngine;

/// <summary>
/// Contrôleur côté Quest qui reçoit les commandes du PC
/// et pilote le Passthrough, les exercices, etc.
/// </summary>
public class QuestNetworkController : MonoBehaviour
{
    [Header("Références")]
    [SerializeField] private NetworkClient networkClient;
    [SerializeField] private PassthroughController passthroughController;
    
    [Header("Debug")]
    [SerializeField] private bool showDebugUI = true;
    
    private string currentStatus = "Initialisation...";
    private string lastCommand = "";
    
    private void Start()
    {
        // Trouver les composants si non assignés
        if (networkClient == null)
            networkClient = FindFirstObjectByType<NetworkClient>();
        
        if (passthroughController == null)
            passthroughController = FindFirstObjectByType<PassthroughController>();
        
        if (networkClient != null)
        {
            // S'abonner aux events
            networkClient.OnConnected += OnConnectedToServer;
            networkClient.OnDisconnected += OnDisconnectedFromServer;
            networkClient.OnStartExercise += OnStartExercise;
            networkClient.OnStopExercise += OnStopExercise;
            networkClient.OnLoadMovement += OnLoadMovement;
            networkClient.OnPassthroughToggle += OnPassthroughToggle;
        }
        else
        {
            Debug.LogWarning("[QuestNetworkController] NetworkClient non trouvé !");
        }
        
        currentStatus = "Recherche du PC...";
    }
    
    private void OnDestroy()
    {
        if (networkClient != null)
        {
            networkClient.OnConnected -= OnConnectedToServer;
            networkClient.OnDisconnected -= OnDisconnectedFromServer;
            networkClient.OnStartExercise -= OnStartExercise;
            networkClient.OnStopExercise -= OnStopExercise;
            networkClient.OnLoadMovement -= OnLoadMovement;
            networkClient.OnPassthroughToggle -= OnPassthroughToggle;
        }
    }
    
    // === EVENTS RÉSEAU ===
    
    private void OnConnectedToServer()
    {
        currentStatus = "Connecté au PC";
        Debug.Log("[QuestNetworkController] ✅ Connecté au PC soignant");
        
        // Envoyer le statut initial
        networkClient.SendStatus("Casque prêt");
    }
    
    private void OnDisconnectedFromServer()
    {
        currentStatus = "Déconnecté - Recherche...";
        Debug.Log("[QuestNetworkController] 🔌 Déconnecté du PC");
    }
    
    private void OnStartExercise()
    {
        lastCommand = "Démarrer exercice";
        currentStatus = "Exercice en cours";
        Debug.Log("[QuestNetworkController] ▶️ Démarrage exercice");
        
        // TODO: Implémenter la logique de démarrage d'exercice
        networkClient.SendStatus("Exercice démarré");
    }
    
    private void OnStopExercise()
    {
        lastCommand = "Arrêter exercice";
        currentStatus = "Exercice arrêté";
        Debug.Log("[QuestNetworkController] ⏹️ Arrêt exercice");
        
        // TODO: Implémenter la logique d'arrêt d'exercice
        networkClient.SendStatus("Exercice arrêté");
    }
    
    private void OnLoadMovement(string movementData)
    {
        lastCommand = $"Charger mouvement: {movementData}";
        Debug.Log($"[QuestNetworkController] 📂 Chargement mouvement: {movementData}");
        
        // TODO: Implémenter le chargement du mouvement
        networkClient.SendStatus($"Mouvement chargé: {movementData}");
    }
    
    private void OnPassthroughToggle(bool enable)
    {
        lastCommand = enable ? "Activer Passthrough" : "Désactiver Passthrough";
        
        if (passthroughController != null)
        {
            passthroughController.SetPassthroughActive(enable);
            currentStatus = enable ? "Passthrough ACTIF" : "Passthrough INACTIF";
            networkClient.SendStatus(currentStatus);
        }
        else
        {
            Debug.LogWarning("[QuestNetworkController] PassthroughController non trouvé !");
            networkClient.SendMessage(new NetworkMessage(NetworkMessageType.Error, "PassthroughController non disponible"));
        }
    }
    
    // === DEBUG UI ===
    
    private void OnGUI()
    {
        if (!showDebugUI) return;
        
        // Afficher un petit panneau de debug dans le casque
        GUIStyle boxStyle = new GUIStyle(GUI.skin.box);
        boxStyle.fontSize = 24;
        
        GUIStyle labelStyle = new GUIStyle(GUI.skin.label);
        labelStyle.fontSize = 20;
        labelStyle.normal.textColor = Color.white;
        
        float width = 400;
        float height = 150;
        float x = 10;
        float y = Screen.height - height - 10;
        
        GUI.Box(new Rect(x, y, width, height), "ReVerso Network", boxStyle);
        
        GUI.Label(new Rect(x + 10, y + 30, width - 20, 30), 
            $"Statut: {currentStatus}", labelStyle);
        
        GUI.Label(new Rect(x + 10, y + 60, width - 20, 30), 
            $"Serveur: {(networkClient != null ? networkClient.ConnectionStatus : "N/A")}", labelStyle);
        
        GUI.Label(new Rect(x + 10, y + 90, width - 20, 30), 
            $"Dernière commande: {lastCommand}", labelStyle);
    }
}
