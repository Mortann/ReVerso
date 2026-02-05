using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Gestionnaire de l'interface utilisateur côté soignant.
/// Gère la navigation entre les différents panneaux et les actions globales.
/// 
/// MODE DE FONCTIONNEMENT:
/// - Si NetworkServer est présent: envoie les commandes via réseau au Quest
/// - Sinon (Quest Link): contrôle directement le PassthroughController local
/// </summary>
public class SoignantUIManager : MonoBehaviour
{
    [Header("Panneaux UI")]
    [Tooltip("Le panneau du menu principal")]
    [SerializeField] private GameObject mainMenuPanel;
    
    [Tooltip("Le panneau Statut Casque (infos de debug)")]
    [SerializeField] private GameObject statutCasquePanel;

    [Header("Boutons Navigation")]
    [Tooltip("Bouton pour afficher le statut du casque")]
    [SerializeField] private Button btnStatutCasque;
    
    [Tooltip("Bouton pour retourner au menu principal")]
    [SerializeField] private Button btnRetourMenu;

    [Header("Boutons Actions")]
    [Tooltip("Bouton pour activer/désactiver le passthrough (pause VR)")]
    [SerializeField] private Button btnPauseVR;
    
    [Tooltip("Texte du bouton pause VR (pour changer le libellé)")]
    [SerializeField] private TMPro.TextMeshProUGUI btnPauseVRText;

    [Header("UI - Statut Connexion")]
    [Tooltip("Texte affichant le statut de connexion avec le casque")]
    [SerializeField] private TMPro.TextMeshProUGUI txtConnectionStatus;
    
    [Tooltip("Image indicateur de connexion (optionnel)")]
    [SerializeField] private UnityEngine.UI.Image imgConnectionIndicator;

    [Header("Références - Mode Local (Quest Link)")]
    [Tooltip("Référence au contrôleur de Passthrough (uniquement si même scène/Quest Link)")]
    [SerializeField] private PassthroughController passthroughController;

    [Header("Références - Mode Réseau (Build séparé)")]
    [Tooltip("Référence au serveur réseau (si builds PC/Quest séparés)")]
    [SerializeField] private NetworkServer networkServer;

    private bool isPassthroughActive = false;
    private bool useNetworkMode = false;

    private void Start()
    {
        // Détecter le mode de fonctionnement
        DetectMode();
        
        // S'abonner aux events du serveur
        if (networkServer != null)
        {
            networkServer.OnStatusChanged += OnNetworkStatusChanged;
            networkServer.OnClientConnected += OnClientConnected;
            networkServer.OnClientDisconnected += OnClientDisconnected;
            
            // Afficher le statut initial
            UpdateConnectionUI(networkServer.ServerStatus);
        }
        
        // Configurer les listeners des boutons
        if (btnStatutCasque != null)
            btnStatutCasque.onClick.AddListener(ShowStatutCasque);
        
        if (btnRetourMenu != null)
            btnRetourMenu.onClick.AddListener(ShowMainMenu);
        
        if (btnPauseVR != null)
            btnPauseVR.onClick.AddListener(TogglePauseVR);

        // Afficher le menu principal au démarrage
        ShowMainMenu();
        UpdatePauseVRButtonText();
    }

    /// <summary>
    /// Détecte automatiquement si on utilise le mode réseau ou local
    /// </summary>
    private void DetectMode()
    {
        // Chercher le NetworkServer si non assigné
        if (networkServer == null)
        {
            networkServer = FindFirstObjectByType<NetworkServer>();
        }
        
        // Chercher le PassthroughController local si non assigné
        if (passthroughController == null)
        {
            passthroughController = FindFirstObjectByType<PassthroughController>();
        }
        
        // Déterminer le mode
        if (networkServer != null)
        {
            useNetworkMode = true;
            Debug.Log("[SoignantUIManager] Mode RÉSEAU activé (PC → Quest via TCP)");
        }
        else if (passthroughController != null)
        {
            useNetworkMode = false;
            Debug.Log("[SoignantUIManager] Mode LOCAL activé (Quest Link / même scène)");
        }
        else
        {
            Debug.LogWarning("[SoignantUIManager] Aucun mode détecté ! Assignez NetworkServer (réseau) ou PassthroughController (local)");
        }
    }

    private void OnDestroy()
    {
        // Nettoyer les listeners
        if (btnStatutCasque != null)
            btnStatutCasque.onClick.RemoveListener(ShowStatutCasque);
        
        if (btnRetourMenu != null)
            btnRetourMenu.onClick.RemoveListener(ShowMainMenu);
        
        if (btnPauseVR != null)
            btnPauseVR.onClick.RemoveListener(TogglePauseVR);
        
        // Se désabonner des events réseau
        if (networkServer != null)
        {
            networkServer.OnStatusChanged -= OnNetworkStatusChanged;
            networkServer.OnClientConnected -= OnClientConnected;
            networkServer.OnClientDisconnected -= OnClientDisconnected;
        }
    }

    #region Network Events
    
    private void OnNetworkStatusChanged(string status)
    {
        UpdateConnectionUI(status);
    }
    
    private void OnClientConnected(string ip)
    {
        UpdateConnectionUI($"✅ Casque connecté: {ip}");
        
        // Activer le bouton Pause VR quand connecté
        if (btnPauseVR != null)
            btnPauseVR.interactable = true;
    }
    
    private void OnClientDisconnected(string ip)
    {
        UpdateConnectionUI("🔍 En attente du casque...");
        
        // Désactiver le bouton Pause VR quand déconnecté
        if (btnPauseVR != null)
            btnPauseVR.interactable = false;
    }
    
    private void UpdateConnectionUI(string status)
    {
        if (txtConnectionStatus != null)
        {
            txtConnectionStatus.text = status;
        }
        
        // Mettre à jour l'indicateur visuel
        if (imgConnectionIndicator != null)
        {
            if (status.Contains("✅"))
            {
                imgConnectionIndicator.color = Color.green;
            }
            else if (status.Contains("🔍"))
            {
                imgConnectionIndicator.color = Color.yellow;
            }
            else if (status.Contains("❌"))
            {
                imgConnectionIndicator.color = Color.red;
            }
        }
    }
    
    #endregion

    #region Navigation

    /// <summary>
    /// Affiche le menu principal et cache les autres panneaux
    /// </summary>
    public void ShowMainMenu()
    {
        SetActivePanel(mainMenuPanel);
    }

    /// <summary>
    /// Affiche le panneau Statut Casque
    /// </summary>
    public void ShowStatutCasque()
    {
        SetActivePanel(statutCasquePanel);
    }

    /// <summary>
    /// Active un panneau et désactive les autres
    /// </summary>
    private void SetActivePanel(GameObject panelToShow)
    {
        if (mainMenuPanel != null)
            mainMenuPanel.SetActive(panelToShow == mainMenuPanel);
        
        if (statutCasquePanel != null)
            statutCasquePanel.SetActive(panelToShow == statutCasquePanel);
    }

    #endregion

    #region Actions

    /// <summary>
    /// Active/Désactive le passthrough (pause VR = voir l'environnement IRL)
    /// </summary>
    public void TogglePauseVR()
    {
        isPassthroughActive = !isPassthroughActive;
        
        if (useNetworkMode)
        {
            // Mode réseau: envoyer la commande au Quest via TCP
            if (networkServer != null && networkServer.HasConnectedClient)
            {
                if (isPassthroughActive)
                {
                    networkServer.SendCommand(NetworkMessageType.EnablePassthrough);
                }
                else
                {
                    networkServer.SendCommand(NetworkMessageType.DisablePassthrough);
                }
            }
            else
            {
                Debug.LogWarning("[SoignantUIManager] Aucun casque connecté !");
                isPassthroughActive = !isPassthroughActive; // Annuler le changement
            }
        }
        else
        {
            // Mode local: contrôler directement le PassthroughController
            if (passthroughController != null)
            {
                passthroughController.SetPassthroughActive(isPassthroughActive);
            }
            else
            {
                Debug.LogWarning("[SoignantUIManager] PassthroughController non assigné !");
            }
        }

        UpdatePauseVRButtonText();
        
        Debug.Log($"[SoignantUIManager] Passthrough {(isPassthroughActive ? "ACTIVÉ" : "DÉSACTIVÉ")} (Mode: {(useNetworkMode ? "Réseau" : "Local")})");
    }

    private void UpdatePauseVRButtonText()
    {
        if (btnPauseVRText != null)
        {
            btnPauseVRText.text = isPassthroughActive ? "Reprendre la VR" : "Mettre en pause la VR";
        }
    }

    #endregion
}
