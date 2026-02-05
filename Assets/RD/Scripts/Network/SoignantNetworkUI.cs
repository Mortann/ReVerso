using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Interface soignant côté PC avec contrôles réseau
/// </summary>
public class SoignantNetworkUI : MonoBehaviour
{
    [Header("Références Réseau")]
    [SerializeField] private NetworkServer networkServer;
    
    [Header("UI - Statut Connexion")]
    [SerializeField] private TextMeshProUGUI txtConnectionStatus;
    [SerializeField] private Image imgConnectionIndicator;
    [SerializeField] private Color colorConnected = Color.green;
    [SerializeField] private Color colorDisconnected = Color.red;
    [SerializeField] private Color colorWaiting = Color.yellow;
    
    [Header("UI - Contrôles Exercice")]
    [SerializeField] private Button btnStartExercise;
    [SerializeField] private Button btnStopExercise;
    [SerializeField] private TMP_Dropdown dropdownExercises;
    
    [Header("UI - Contrôles Passthrough")]
    [SerializeField] private Button btnEnablePassthrough;
    [SerializeField] private Button btnDisablePassthrough;
    [SerializeField] private TextMeshProUGUI txtPassthroughStatus;
    
    [Header("UI - Statut Quest")]
    [SerializeField] private TextMeshProUGUI txtQuestStatus;
    [SerializeField] private TextMeshProUGUI txtLastMessage;
    
    private bool isPassthroughActive = false;
    
    private void Start()
    {
        // Trouver le serveur si non assigné
        if (networkServer == null)
        {
            networkServer = FindFirstObjectByType<NetworkServer>();
        }
        
        if (networkServer == null)
        {
            Debug.LogError("[SoignantNetworkUI] NetworkServer non trouvé !");
            return;
        }
        
        // S'abonner aux events
        networkServer.OnClientConnected += OnClientConnected;
        networkServer.OnClientDisconnected += OnClientDisconnected;
        networkServer.OnMessageReceived += OnMessageReceived;
        
        // Configurer les boutons
        SetupButtons();
        
        // État initial
        UpdateConnectionUI(false, "En attente du casque...");
        UpdateButtonStates(false);
    }
    
    private void OnDestroy()
    {
        if (networkServer != null)
        {
            networkServer.OnClientConnected -= OnClientConnected;
            networkServer.OnClientDisconnected -= OnClientDisconnected;
            networkServer.OnMessageReceived -= OnMessageReceived;
        }
    }
    
    private void SetupButtons()
    {
        if (btnStartExercise != null)
            btnStartExercise.onClick.AddListener(OnStartExerciseClicked);
        
        if (btnStopExercise != null)
            btnStopExercise.onClick.AddListener(OnStopExerciseClicked);
        
        if (btnEnablePassthrough != null)
            btnEnablePassthrough.onClick.AddListener(OnEnablePassthroughClicked);
        
        if (btnDisablePassthrough != null)
            btnDisablePassthrough.onClick.AddListener(OnDisablePassthroughClicked);
    }
    
    // === EVENTS RÉSEAU ===
    
    private void OnClientConnected(string ip)
    {
        UpdateConnectionUI(true, $"Casque connecté: {ip}");
        UpdateButtonStates(true);
        
        if (txtQuestStatus != null)
            txtQuestStatus.text = "Casque: Connecté";
    }
    
    private void OnClientDisconnected(string ip)
    {
        UpdateConnectionUI(false, "Casque déconnecté");
        UpdateButtonStates(false);
        
        if (txtQuestStatus != null)
            txtQuestStatus.text = "Casque: Déconnecté";
    }
    
    private void OnMessageReceived(NetworkMessage message)
    {
        if (txtLastMessage != null)
            txtLastMessage.text = $"Dernier message: {message.type}";
        
        switch (message.type)
        {
            case NetworkMessageType.StatusUpdate:
                if (txtQuestStatus != null)
                    txtQuestStatus.text = $"Casque: {message.data}";
                break;
                
            case NetworkMessageType.ExerciseCompleted:
                Debug.Log($"[SoignantNetworkUI] Exercice terminé: {message.data}");
                break;
        }
    }
    
    // === ACTIONS BOUTONS ===
    
    private void OnStartExerciseClicked()
    {
        string exerciseName = "";
        if (dropdownExercises != null && dropdownExercises.options.Count > 0)
        {
            exerciseName = dropdownExercises.options[dropdownExercises.value].text;
        }
        
        networkServer.SendCommand(NetworkMessageType.StartExercise, exerciseName);
        Debug.Log($"[SoignantNetworkUI] Démarrage exercice: {exerciseName}");
    }
    
    private void OnStopExerciseClicked()
    {
        networkServer.SendCommand(NetworkMessageType.StopExercise);
        Debug.Log("[SoignantNetworkUI] Arrêt exercice");
    }
    
    private void OnEnablePassthroughClicked()
    {
        networkServer.SendCommand(NetworkMessageType.EnablePassthrough);
        isPassthroughActive = true;
        UpdatePassthroughUI();
        Debug.Log("[SoignantNetworkUI] Passthrough activé");
    }
    
    private void OnDisablePassthroughClicked()
    {
        networkServer.SendCommand(NetworkMessageType.DisablePassthrough);
        isPassthroughActive = false;
        UpdatePassthroughUI();
        Debug.Log("[SoignantNetworkUI] Passthrough désactivé");
    }
    
    // === UI UPDATES ===
    
    private void UpdateConnectionUI(bool connected, string status)
    {
        if (txtConnectionStatus != null)
            txtConnectionStatus.text = status;
        
        if (imgConnectionIndicator != null)
            imgConnectionIndicator.color = connected ? colorConnected : colorWaiting;
    }
    
    private void UpdateButtonStates(bool enabled)
    {
        if (btnStartExercise != null)
            btnStartExercise.interactable = enabled;
        
        if (btnStopExercise != null)
            btnStopExercise.interactable = enabled;
        
        if (btnEnablePassthrough != null)
            btnEnablePassthrough.interactable = enabled;
        
        if (btnDisablePassthrough != null)
            btnDisablePassthrough.interactable = enabled;
    }
    
    private void UpdatePassthroughUI()
    {
        if (txtPassthroughStatus != null)
            txtPassthroughStatus.text = isPassthroughActive ? "Passthrough: ACTIF" : "Passthrough: INACTIF";
        
        if (btnEnablePassthrough != null)
            btnEnablePassthrough.interactable = !isPassthroughActive && networkServer.HasConnectedClient;
        
        if (btnDisablePassthrough != null)
            btnDisablePassthrough.interactable = isPassthroughActive && networkServer.HasConnectedClient;
    }
}
